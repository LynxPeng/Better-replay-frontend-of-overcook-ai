"""Headless, full-length acceptance using real host processes and TCP clients."""
import argparse
import asyncio
import contextlib
import json
from pathlib import Path
import secrets
import struct
import sys
import time

import numpy as np
from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from better_overcooked_replay.protocol import load_replay, normalize_state


class Client:
    def __init__(self, reader, writer):
        self.reader, self.writer = reader, writer
        self.pings, self.rtt = [], []
        self.pinger = asyncio.create_task(self.heartbeat())

    @classmethod
    async def connect(cls, port, name, token=None):
        client = cls(*await asyncio.open_connection("127.0.0.1", port))
        await client.send("hello", version=1, name=name, host_token=token)
        msg = await client.get("welcome")
        assert msg["host"] == bool(token)
        return client

    async def send(self, kind, **fields):
        body = json.dumps(dict(type=kind, **fields), separators=(",", ":")).encode()
        self.writer.write(struct.pack(">I", len(body)) + body)
        await self.writer.drain()

    async def heartbeat(self):
        while True:
            await asyncio.sleep(2)
            self.pings.append(time.perf_counter())
            await self.send("ping")

    async def get(self, kind, **matches):
        async def read():
            while True:
                size, = struct.unpack(">I", await self.reader.readexactly(4))
                assert 0 < size <= 2**20
                msg = json.loads(await self.reader.readexactly(size))
                if msg["type"] == "error":
                    raise RuntimeError(msg["message"])
                if msg["type"] == "pong":
                    self.rtt.append(1000 * (time.perf_counter() - self.pings.pop(0)))
                if msg["type"] == kind and all(msg.get(k) == v for k,v in matches.items()):
                    return msg
        return await asyncio.wait_for(read(), 120)

    async def close(self):
        self.pinger.cancel()
        with contextlib.suppress(asyncio.CancelledError, ConnectionError):
            await self.pinger
        self.writer.close()
        with contextlib.suppress(ConnectionError):
            await self.writer.wait_closed()


def verify(record_path, frame_log, segment_log, identities, fixture, cfg):
    data = load_replay(record_path)
    head = data["header"]
    for key in ("horizon", "tick_rate", "host_slot", "mode", "seed"):
        assert head[key] == cfg[key], key
    assert head["complete"] and head["end_reason"] == "horizon" and head["source"] == "live"
    assert head["layout_name"] == "cramped_room" and head["dynamics"] == "old"
    assert head["players"] == identities and len(frame_log) == 401 and len(segment_log) == 400
    assert data["frames"] == frame_log and data["segments"] == segment_log
    mdp = OvercookedGridworld.from_layout_name("cramped_room", old_dynamics=True)
    env = OvercookedEnv.from_mdp(mdp, horizon=400, info_level=0)
    assert normalize_state(env.state) == frame_log[0]["state"]
    score = 0
    for tick, (frame, segment) in enumerate(zip(frame_log[1:], segment_log), 1):
        assert abs(frame["time"] - tick / cfg["tick_rate"]) < 1e-6
        assert segment["kind"] == "step" and segment["reward_known"]
        for slot in range(2):
            if identities[slot]["is_human"]:
                assert segment["actions"][slot] == fixture[tick-1]["actions"][slot], (tick, slot, "lost input")
        joint = tuple(Action.INDEX_TO_ACTION[i] for i in segment["actions"])
        state, reward, done, info = env.step(joint)
        score += reward
        assert frame["state"] == normalize_state(state), tick
        assert frame["score"] == score and segment["sparse_reward"] == reward, tick
        assert list(info["shaped_r_by_agent"]) == segment["shaped_reward_by_agent"], tick
        assert done == (tick == 400)
    return score


async def room(folder, models, fixture, mode, host_slot, rate):
    name = f"{mode}-host{host_slot}"
    folder = folder / name
    folder.mkdir(parents=True, exist_ok=False)
    secret = secrets.token_urlsafe(32)
    token_path, ready, stop = (folder / f for f in ("host.token", "ready.json", "stop"))
    token_path.write_text(secret, encoding="ascii")
    clients = []
    with (folder / "service.log").open("wb") as log:
        service = await asyncio.create_subprocess_exec(
            sys.executable, "-u", "-m", "better_overcooked_replay.live_server",
            "--port", "0", "--bind", "127.0.0.1", "--models", str(models),
            "--records", str(folder / "records"), "--ready-file", str(ready),
            "--host-token-file", str(token_path), "--stop-file", str(stop), stdout=log, stderr=log)
        try:
            for _ in range(300):
                if ready.exists():
                    break
                if service.returncode is not None:
                    raise RuntimeError(f"{name}: server exited, inspect {log.name}")
                await asyncio.sleep(.1)
            port = json.loads(ready.read_text(encoding="utf-8"))["port"]
            host = await Client.connect(port, "房主", secret)
            clients.append(host)
            cfg = dict(mode=mode, host_slot=host_slot, layout="cramped_room", old_dynamics=True,
                       model0="sp-cramped-room", model1="ppo-final" if mode=="ai_ai" else "sp-cramped-room",
                       horizon=400, seed=0, tick_rate=rate)
            await host.send("configure", **cfg)
            await host.get("lobby", mode=mode, host_slot=host_slot, tick_rate=rate)
            if mode == "human_human":
                guest = await Client.connect(port, "客人")
                clients.append(guest)
                await guest.send("ready", ready=True)
                await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            start = await host.get("start")
            if len(clients) == 2:
                assert await guest.get("start") == start
            for client in clients:
                await client.get("lobby", phase="running")
            frames, segments, intervals = [start["frame"]], [], []
            prev = time.perf_counter()
            for tick in range(1, 401):
                for seat, client in enumerate(clients):
                    if mode != "ai_ai":
                        slot = host_slot if seat == 0 else 1-host_slot
                        await client.send("input", match_id=start["match_id"], tick=tick, seq=tick,
                                          action=fixture[tick-1]["actions"][slot])
                update = await host.get("tick", tick=tick)
                now = time.perf_counter()
                intervals.append(1000 * (now - prev))
                prev = now
                if len(clients) == 2:
                    assert await guest.get("tick", tick=tick) == update
                frames.append(update["frame"])
                segments.append(update["segment"])
            end = await host.get("end")
            assert end["complete"] and end["frame"] == frames[-1]
            if len(clients) == 2:
                assert await guest.get("end") == {**end, "record": Path(end["record"]).name}
            ids = []
            for slot in range(2):
                human = mode == "human_human" or (mode == "human_ai" and slot == host_slot)
                identity = ("房主" if slot == host_slot else "客人") if human else cfg[f"model{slot}"]
                ids.append(dict(id=identity, is_human=human))
            score = verify(end["record"], frames, segments, ids, fixture, cfg)
            if mode != "ai_ai":
                assert score == 220, (name, score)
            samples = [value for client in clients for value in client.rtt]
            report = dict(mode=mode, host_slot=host_slot, clients=len(clients), players=ids,
                          frames=401, input_pairs=400, matched_upstream_transitions_and_rewards=400,
                          identical_tcp_and_disk_frames=401, score=score, record=end["record"],
                          tick_rate=rate, ping_samples=len(samples),
                          ping_rtt_median_ms=round(float(np.median(samples)), 2),
                          frame_interval_p95_ms=round(float(np.percentile(intervals, 95)), 2),
                          policy_fallbacks=sum(len(s.get("policy_diagnostics", [])) for s in segments))
            (folder / "result.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            print(f"{name}: 400/400 transitions verified, score={score}", flush=True)
            return report
        finally:
            stop.touch()
            for client in clients:
                await client.close()
            try:
                await asyncio.wait_for(service.wait(), 105)
            finally:
                if service.returncode is None:
                    service.terminate()
                    await service.wait()
                token_path.unlink(missing_ok=True)


async def run(options):
    folder = options.out.resolve()
    folder.mkdir(parents=True, exist_ok=False)
    fixture = load_replay(Path(__file__).resolve().parents[1] /
        "unity/OvercookedViewer/Assets/StreamingAssets/Samples/upstream-sp-220.ocrep.json")["segments"]
    cases = [("human_human", 1), ("human_ai", 0), ("human_ai", 1), ("ai_ai", 0)]
    outcomes = await asyncio.gather(*(room(folder, options.models.resolve(), fixture, mode, slot, options.rate)
                                     for mode, slot in cases), return_exceptions=True)
    for outcome in outcomes:
        if isinstance(outcome, BaseException):
            raise outcome
    (folder / "summary.json").write_text(json.dumps(outcomes, indent=2, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--models", type=Path, required=True)
    parser.add_argument("--rate", type=int, choices=range(1, 13), default=2)
    asyncio.run(run(parser.parse_args()))
