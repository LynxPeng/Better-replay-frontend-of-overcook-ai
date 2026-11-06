"""Trusted host-side model catalog and bounded, per-slot live inference."""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import multiprocessing as mp
from multiprocessing.connection import wait
import os
from pathlib import Path
import re
import shutil
import tempfile
import threading
import time

import numpy as np

from .live_sp import SP_ID, SelfPlayPolicy, prepare_sp
from .protocol import action_index
from .recipes import recipe_context

STAY = 4
SAFE_ID = re.compile(r"[a-zA-Z0-9_-]{1,64}")


class ModelCatalog:
    def __init__(self, models_dir=None):
        self.paths = {"greedy": None}
        if models_dir is None:
            return
        root = Path(models_dir).resolve()
        for path in sorted(root.glob("*")):
            if not path.is_file() or path.resolve().parent != root:
                continue
            if path.name == f"{SP_ID}.npz":
                self.paths[SP_ID] = path
            elif (path.suffix.lower() == ".zip" and SAFE_ID.fullmatch(path.stem)
                  and path.stem not in ("greedy", SP_ID)):
                self.paths[path.stem] = path

    def options(self):
        return list(self.paths)

    def describe(self):
        labels = {"greedy": "Greedy 规划队友", SP_ID: "上游自我对弈 · cramped_room"}
        return [{"id": model_id, "label": labels.get(model_id, f"SB3 · {model_id}")}
                for model_id in self.paths]

    def validate(self, model_id, layout, old_dynamics):
        if not isinstance(model_id, str) or model_id not in self.paths:
            raise ValueError(f"Unknown host model: {model_id!r}")
        if model_id == SP_ID and (layout != "cramped_room" or not old_dynamics):
            raise ValueError("Upstream SP requires cramped_room with old cooking rules")
        if model_id == "greedy" and (layout.startswith("forced_coordination") or layout == "cramped_room_tomato"):
            raise ValueError("Greedy requires a solvable single three-onion recipe layout")

    def prepare(self, slots, mdp, horizon=400, seed=0, startup_timeout=90):
        for index, model_id in slots.items():
            if index not in (0, 1):
                raise ValueError("Policy slot must be 0 or 1")
            self.validate(model_id, mdp.layout_name, mdp.old_dynamics)
            if model_id in ("greedy", SP_ID) and (len(mdp.start_all_orders) != 1 or
                    sorted(mdp.start_all_orders[0]["ingredients"]) != ["onion"] * 3):
                raise ValueError(f"{model_id} supports a single three-onion recipe")
        policies = LivePolicies()
        deadline = time.monotonic() + startup_timeout
        try:
            for index, model_id in slots.items():
                # Sequential warmup also prevents simultaneous upstream planner cache writes.
                policies.start(index, model_id, self.paths[model_id], mdp, horizon, seed + index, deadline)
            return policies
        except BaseException:
            policies.close()
            raise


def _sb3_policy(path, horizon):
    from gymnasium.spaces import Box, Discrete
    from stable_baselines3 import PPO
    import torch
    from .training import SB3Partner

    torch.set_num_threads(1)
    model = PPO.load(path, device="cpu")
    if not isinstance(model.action_space, Discrete) or model.action_space.n != 6 or model.action_space.start != 0:
        raise ValueError("SB3 model requires Discrete(6) actions starting at zero")
    obs = model.observation_space
    if not isinstance(obs, Box) or obs.dtype != np.uint8 or not (np.all(obs.low == 0) and np.all(obs.high == 255)):
        raise ValueError("SB3 model requires RGB uint8 observations with range 0..255")
    for size in (84, 128):
        if obs.shape in ((3, size, size), (size, size, 3)):
            return SB3Partner(model, size, horizon)
    raise ValueError(f"SB3 model requires square 84 or 128 pixel RGB observations, got {obs.shape}")


def _policy_worker(channel, model_id, path, mdp, index, seed, horizon):
    try:
        with recipe_context(mdp.recipe_config):
            if model_id == SP_ID:
                policy = SelfPlayPolicy(path, mdp, index, seed, horizon)
                policy.logits(mdp.lossless_state_encoding(mdp.get_standard_start_state(), horizon=horizon)[index])
            else:
                if model_id == "greedy":
                    from .training import UpstreamPartner
                    policy = UpstreamPartner()
                else:
                    policy = _sb3_policy(path, horizon)
                policy.reset(seed, index, mdp)
                action_index(policy.act(mdp.get_standard_start_state()))
                policy.reset(seed, index, mdp)
            channel.send(("ready", None))
            while True:
                message = channel.recv()
                if message is None:
                    return
                tick, state = message
                try:
                    channel.send((tick, action_index(policy.act(state))))
                except Exception as error:
                    channel.send((tick, str(error) or type(error).__name__))
    except (EOFError, BrokenPipeError):
        pass
    except Exception as error:
        try:
            channel.send(("error", f"{type(error).__name__}: {error}"))
        except (EOFError, OSError):
            pass
    finally:
        channel.close()


@dataclass
class _Worker:
    process: object
    channel: object
    pending: int | None = None


class LivePolicies:
    """One outstanding request per child, all slots share a single tick deadline."""
    def __init__(self):
        self.workers = {}
        self.diagnostics = {}
        self.tick = 0
        self.lock = threading.Lock()
        self.closed = False

    def start(self, index, model_id, path, mdp, horizon, seed, deadline):
        ctx = mp.get_context("spawn")
        host, child = ctx.Pipe()
        process = ctx.Process(target=_policy_worker, args=(child, model_id, path, mdp, index, seed, horizon),
                              name=f"Overcooked policy {index}", daemon=True)
        try:
            process.start()
        except BaseException:
            host.close()
            raise
        finally:
            child.close()
        self.workers[index] = _Worker(process, host)
        if not host.poll(max(0, deadline - time.monotonic())):
            raise TimeoutError(f"Model {model_id} failed to warm up within the time limit")
        status, explanation = host.recv()
        if status != "ready":
            raise ValueError(f"Could not warm up {model_id}: {explanation}")

    def decide(self, state, timeout=.45):
        with self.lock:
            if self.closed:
                raise RuntimeError("Policy group already closed")
            self.tick += 1
            deadline = time.monotonic() + max(0, timeout)
            moves = dict.fromkeys(self.workers, STAY)
            self.diagnostics = dict.fromkeys(self.workers, "timeout")
            outstanding = {}
            for index, worker in self.workers.items():
                channel = worker.channel
                try:
                    if worker.pending is not None:
                        if not channel.poll():
                            continue
                        channel.recv()  # late answer belongs to an expired tick
                        worker.pending = None
                    channel.send((self.tick, state))
                    worker.pending = self.tick
                    outstanding[channel] = index
                except (EOFError, OSError):
                    self.diagnostics[index] = "worker stopped"
            while outstanding:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    break
                for channel in wait(outstanding, remaining):
                    index = outstanding.pop(channel)
                    self.workers[index].pending = None
                    try:
                        tick, action = channel.recv()
                    except (EOFError, OSError):
                        self.diagnostics[index] = "worker stopped"
                        continue
                    if tick == self.tick and type(action) is int and action in range(6):
                        moves[index] = action
                        self.diagnostics[index] = "ok"
                    else:
                        self.diagnostics[index] = f"prediction failed: {action}"
            return moves

    def close(self):
        with self.lock:
            self.closed = True
            for worker in self.workers.values():
                worker.channel.close()
                if worker.process.is_alive():
                    worker.process.terminate()
                worker.process.join(2)
                worker.process.close()
            self.workers.clear()


def _stage_sb3(source, models, model_id):
    if not SAFE_ID.fullmatch(model_id) or model_id in ("greedy", SP_ID):
        raise ValueError("Invalid or reserved model ID")
    destination = Path(models) / (model_id + ".zip")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if Path(source).resolve() == destination.resolve():
        return destination
    with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as scratch:
        scratch_path = Path(scratch.name)
    try:
        shutil.copyfile(source, scratch_path)
        os.replace(scratch_path, destination)
    finally:
        scratch_path.unlink(missing_ok=True)
    return destination


def main():
    parser = argparse.ArgumentParser(description="Prepare locally trusted model files for live games")
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("--models", required=True, type=Path)
    prep.add_argument("--upstream", type=Path, help="Local upstream checkout containing the original SP checkpoint")
    prep.add_argument("--sb3", type=Path, help="Trusted, locally trained SB3 PPO .zip")
    prep.add_argument("--id", default="ppo-final", help="Catalog ID for --sb3")
    args = parser.parse_args()
    if args.upstream is None and args.sb3 is None:
        parser.error("specify --upstream or --sb3")
    if args.upstream is not None:
        print(prepare_sp(args.upstream, args.models))
    if args.sb3 is not None:
        print(_stage_sb3(args.sb3, args.models, args.id))


if __name__ == "__main__":
    main()
