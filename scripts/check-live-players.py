"""Exercise a built Windows player against a real localhost room using synthetic keys."""
import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time

from better_overcooked_replay.protocol import validate_replay


def wait_for(predicate, deadline, processes):
    while not predicate():
        for process in processes:
            if process.poll() not in (None, 0):
                raise RuntimeError(f"Process {process.pid} exited with {process.returncode}")
        if time.monotonic() >= deadline:
            raise TimeoutError("Timed out waiting for room or player; inspect this run's logs")
        time.sleep(.1)


def run(options):
    if options.drop_step and not (options.mode == "hh" and 0 < options.drop_step < options.steps):
        raise ValueError("--drop-step needs a two-player room and a tick before the last")
    if options.capture_tick is not None and not (0 <= options.capture_tick < options.steps):
        raise ValueError("--capture-tick must be in the match")
    client = options.client.resolve(strict=True)
    if not options.portable and options.models is None:
        raise ValueError("--models is required for a development server")
    folder = options.out.resolve()
    folder.mkdir(parents=True, exist_ok=False)
    records = folder / "records"
    token = secrets.token_urlsafe(32)
    token_path, ready, stop = (folder / name for name in ("host.token", "ready.json", "stop"))
    if not options.portable:
        token_path.write_text(token, encoding="utf-8")
    processes = []
    reports = []
    hidden = subprocess.STARTUPINFO()
    hidden.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    hidden.wShowWindow = 0
    deadline = time.monotonic() + 160 + options.steps / options.rate
    with (folder / "service.log").open("wb") as service_log:
        service = None
        if not options.portable:
            service = subprocess.Popen([
                sys.executable, "-u", "-m", "better_overcooked_replay.live_server", "--bind", "127.0.0.1",
                "--port", "0", "--records", str(records), "--models", str(options.models.resolve()),
                "--host-token-file", str(token_path), "--ready-file", str(ready), "--stop-file", str(stop),
            ], stdout=service_log, stderr=subprocess.STDOUT)
        monitored = [service] if service else []
        try:
            port = None
            if service:
                wait_for(ready.exists, deadline, monitored)
                port = json.loads(ready.read_text(encoding="utf-8"))["port"]
            for role in (["host", "guest"] if options.mode == "hh" else ["host"]):
                if role == "guest":
                    # A cold editor build may take longer to initialize the first scene.
                    host_log = folder / "host.log"
                    wait_for(lambda: host_log.exists() and b"LIVE_SMOKE_LOBBY_READY" in host_log.read_bytes(),
                             deadline, monitored)
                    if options.portable:
                        ready, = (folder / "rooms").glob("*/ready.json")
                        port = json.loads(ready.read_text(encoding="utf-8"))["port"]
                report = folder / f"{role}.json"
                reports.append(report)
                env = os.environ.copy()
                env.pop("BETTER_REPLAY_HOST_TOKEN", None)
                if options.portable:
                    env = {key: value for key, value in env.items()
                           if not key.upper().startswith(("PYTHON", "CONDA", "_CONDA"))}
                    system_root = Path(os.environ["SystemRoot"])
                    env["PATH"] = os.pathsep.join(map(str, (system_root / "System32", system_root)))
                elif role == "host":
                    env["BETTER_REPLAY_HOST_TOKEN"] = token
                if options.portable and role == "host":
                    flags = ["--portable-host", "1", "--portable-data", str(folder)]
                else:
                    flags = ["--live-address", "127.0.0.1", "--live-port", str(port)]
                flags += ["--live-name", f"local-{role}", "--live-smoke", options.mode,
                         "--live-smoke-steps", str(options.steps), "--live-smoke-rate", str(options.rate),
                         "--live-smoke-report", str(report), "-logFile", str(folder / f"{role}.log")]
                if role == "guest" and options.drop_step:
                    flags.extend(["--live-smoke-drop-step", str(options.drop_step)])
                capture_host = role == "host" and options.capture_tick is not None
                if capture_host:
                    flags.extend(["--capture-ui", "--capture-size", "1280x720", "--frame", str(options.capture_tick),
                                  "--capture", str(folder / "match.png")])
                process = subprocess.Popen([str(client), *flags], env=env, cwd=client.parent,
                                           startupinfo=None if capture_host else hidden)
                processes.append(process)
                monitored.append(process)
            wait_for(lambda: all(process.poll() is not None for process in processes), deadline, monitored)
            if any(p.returncode for p in processes):
                raise RuntimeError("Live player reported failure")
            replay_path, = records.glob("*.ocrep.json")
            replay = json.loads(replay_path.read_text(encoding="utf-8"))
            validate_replay(replay)
            if options.capture_tick is not None and not (folder / "match.png").is_file():
                raise ValueError("The player did not capture the requested HUD")
            if not replay["header"]["complete"] or len(replay["segments"]) != options.steps:
                raise ValueError("Missing complete terminal record")
            model_ids = replay["header"]["model_ids"]
            if options.portable:
                expected_models = {"hh": ["", ""], "ha": ["", "ppo-final"],
                                   "aa": ["sp-cramped-room", "sp-cramped-room"]}
                if model_ids != expected_models[options.mode]:
                    raise ValueError(f"Portable match used unexpected models: {model_ids}")
            for index, model_id in enumerate(model_ids):
                if not model_id and not any(step["actions"][index] != 4 for step in replay["segments"]):
                    raise ValueError(f"No authoritative human actions recorded for seat {index}")
            summary = {"mode": options.mode, "ticks": len(replay["segments"]), "frames": len(replay["frames"]),
                       "score": replay["frames"][-1]["score"], "record": str(replay_path), "model_ids": model_ids,
                       "clients": [json.loads(r.read_text(encoding="utf-8-sig")) for r in reports]}
            for participant in summary["clients"]:
                if not (participant["ok"] and participant["complete"] and participant["tick"] == options.steps
                        and participant["renderFrames"] > 0 and participant["score"] == summary["score"]):
                    raise ValueError(f"Client did not render the recorded terminal state: {participant}")
                if options.mode != "aa" and participant["inputSamples"] == 0:
                    raise ValueError("Human seat received no authoritative input acknowledgements")
                if options.drop_step and not participant["host"] and not participant["recovered"]:
                    raise ValueError("Guest did not resume after the requested disconnect")
            (folder / "result.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
            print(json.dumps(summary, ensure_ascii=False), flush=True)
        finally:
            if service:
                stop.touch()
            for process in processes:
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=10)
            try:
                if service:
                    service.wait(timeout=95)
            finally:
                token_path.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--models", type=Path)
    parser.add_argument("--portable", action="store_true", help="Use the packaged room launcher and bundled models")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--mode", choices=["hh", "ha", "aa"], required=True)
    parser.add_argument("--steps", type=int, default=400)
    parser.add_argument("--rate", type=int, default=2)
    parser.add_argument("--drop-step", type=int, default=0, help="Disconnect the guest once and verify recovery")
    parser.add_argument("--capture-tick", type=int, help="Show the host window and save its HUD at a confirmed tick")
    run(parser.parse_args())
