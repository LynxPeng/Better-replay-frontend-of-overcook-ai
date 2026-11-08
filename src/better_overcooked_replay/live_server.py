"""Authoritative two-seat live room. Wire v1: uint32 big-endian length + UTF-8 JSON."""
from __future__ import annotations

import argparse
import asyncio
import contextlib
from dataclasses import asdict, dataclass, replace
from datetime import datetime, timezone
import ipaddress
import json
import logging
import math
import os
from pathlib import Path
import secrets
import signal
import socket
import struct
import time
import uuid

from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from .protocol import ReplayRecorder
from .samples import LAYOUTS


MAX_PACKET = 1 << 20
DEFAULT_PORT = 8765
LOG = logging.getLogger(__name__)


class ResumeBusy(ValueError):
    """A valid returning seat still has its previous transport attached."""


def packet(message):
    data = json.dumps(message, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    if not 0 < len(data) <= MAX_PACKET:
        raise ValueError("消息长度无效")
    return struct.pack(">I", len(data)) + data


async def read_message(reader):
    length, = struct.unpack(">I", await reader.readexactly(4))
    if not 0 < length <= MAX_PACKET:
        raise ValueError("消息长度无效")
    data = json.loads((await reader.readexactly(length)).decode("utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("type"), str):
        raise ValueError("消息需要 type 字段")
    return data


def integer(value, name, minimum, maximum):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError(f"{name} 必须在 {minimum} 至 {maximum} 之间")
    return value


def boolean(value, name):
    if type(value) is not bool:
        raise ValueError(f"{name} 必须是布尔值")
    return value


@dataclass(frozen=True)
class Settings:
    mode: str = "human_ai"
    layout: str = "cramped_room"
    model0: str = "greedy"
    model1: str = "greedy"
    horizon: int = 400
    tick_rate: int = 2
    seed: int = 0
    old_dynamics: bool = False
    host_slot: int = 0

    def ai_slots(self):
        if self.mode == "human_human":
            return {}
        if self.mode == "human_ai":
            partner = 1 - self.host_slot
            return {partner: (self.model0, self.model1)[partner]}
        return {0: self.model0, 1: self.model1}

    def updated(self, msg, catalog):
        cfg = replace(self, **{key: msg[key] for key in asdict(self) if key in msg})
        if cfg.mode not in ("human_ai", "human_human", "ai_ai") or cfg.layout not in LAYOUTS:
            raise ValueError("未知对局模式或地图")
        integer(cfg.horizon, "horizon", 1, 3600)
        integer(cfg.tick_rate, "tick_rate", 1, 12)
        integer(cfg.seed, "seed", 0, 2147483647)
        integer(cfg.host_slot, "host_slot", 0, 1)
        boolean(cfg.old_dynamics, "old_dynamics")
        for model in cfg.ai_slots().values():
            if not isinstance(model, str):
                raise ValueError("模型需要目录中的 ID")
            catalog.validate(model, cfg.layout, cfg.old_dynamics)
        return cfg


@dataclass
class Seat:
    name: str
    token: str
    writer: asyncio.StreamWriter | None
    ready: bool = False
    sequence: int = -1
    missing_since: float | None = None


class MatchRecord:
    """Keep a playable crash checkpoint, replacing it atomically every five seconds."""

    def __init__(self, directory, recorder):
        directory.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        self.match_id = uuid.uuid4().hex
        stem = directory / f"live-{stamp}-{self.match_id}"
        self.path = stem.with_suffix(".ocrep.json")
        self.partial = stem.with_suffix(".partial.ocrep.json")
        self.recorder = recorder
        self.recorder.header["match_id"] = self.match_id
        recorder.write(self.partial, complete=False)
        self.last_saved = time.monotonic()

    def checkpoint(self):
        now = time.monotonic()
        if now - self.last_saved >= 5:
            self.recorder.write(self.partial, complete=False)
            self.last_saved = now

    def finish(self, complete, reason):
        self.recorder.header["end_reason"] = reason
        target = self.path if complete else self.partial
        self.recorder.write(target, complete=complete)
        if complete:
            self.partial.unlink(missing_ok=True)
        return str(target.resolve())


class LiveServer:
    def __init__(self, host_token, records, catalog, *, countdown=3.0, grace=30.0, heartbeat=3.0):
        if not isinstance(host_token, str) or not host_token.isascii() or len(host_token) < 16:
            raise ValueError("Host token must contain at least 16 ASCII characters")
        self.host_token = host_token
        self.records = Path(records)
        self.catalog = catalog
        self.countdown_seconds = countdown
        self.grace = grace
        self.heartbeat = heartbeat
        self.config = Settings()
        self.phase = "lobby"
        self.seats = [None, None]
        self.pending = {}
        self.tick = 0
        self.match = self.policies = self.env = self.terminal = None
        self.policy_fault = ""
        self.reload_requested = self.warming = False
        self.timeouts = [0, 0]
        self.generation = 0
        self.countdown_until = None
        self.listener = self.runner = self.watchdog = None
        self.closing = asyncio.Event()
        self.writers = set()
        self.lan_addresses = []

    async def listen(self, bind="127.0.0.1", port=DEFAULT_PORT):
        self.listener = await asyncio.start_server(self.client, bind, port)
        port = self.listener.sockets[0].getsockname()[1]
        if bind == "0.0.0.0":
            with contextlib.suppress(OSError):
                self.lan_addresses = sorted({f"{addr[4][0]}:{port}" for addr in
                                             socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET)
                                             if not ipaddress.ip_address(addr[4][0]).is_loopback})
        elif not ipaddress.ip_address(self.listener.sockets[0].getsockname()[0]).is_loopback:
            self.lan_addresses = [f"{bind}:{port}"]
        self.watchdog = asyncio.create_task(self.watch())
        return port

    def send(self, writer, message):
        if writer is None or writer.is_closing():
            return
        if writer.transport.get_write_buffer_size() > MAX_PACKET:
            writer.close()
            return
        try:
            writer.write(packet(message))
        except (ConnectionError, OSError):
            writer.close()

    def broadcast(self, message):
        for seat in self.seats:
            if seat:
                self.send(seat.writer, message)

    def report_host_error(self, description, exc):
        for slot, seat in enumerate(self.seats):
            if seat:
                self.send(seat.writer, {"type": "error", "fatal": False,
                                        "message": f"{description}：{exc}" if slot == 0 else description})

    def lobby(self):
        result = dict(type="lobby", phase=self.phase, **asdict(self.config), warming=self.warming,
                      policy_fault=self.policy_fault,
                      countdown=max(0, (self.countdown_until or 0) - time.monotonic()))
        for i, seat in enumerate(self.seats):
            result[f"ready{i}"] = bool(seat and seat.ready)
            result[f"connected{i}"] = bool(seat and seat.writer)
        missing = [s.missing_since for s in self.seats if s and s.missing_since is not None]
        result["reconnect_remaining"] = max(0, self.grace - time.monotonic() + min(missing)) if missing else 0
        return result

    def snapshot(self):
        return dict(type="start", header=self.match.recorder.header, tick=self.tick, match_id=self.match.match_id,
                    frame=self.match.recorder.frames[-1])

    def required(self):
        return range(2) if self.config.mode == "human_human" else range(1)

    def connected(self):
        return all(self.seats[i] and self.seats[i].writer for i in self.required())

    def connect(self, hello, writer):
        if hello["type"] != "hello" or type(hello.get("version")) is not int or hello["version"] != 1:
            raise ValueError("需要 v1 hello")
        token = hello.get("resume_token") or ""
        host_key = hello.get("host_token") or ""
        if not isinstance(token, str) or not isinstance(host_key, str) or not token.isascii() or not host_key.isascii():
            raise ValueError("连接凭据无效")
        peer = ipaddress.ip_address(writer.get_extra_info("peername")[0].split("%")[0])
        if token:
            idx = next((i for i, seat in enumerate(self.seats) if seat and secrets.compare_digest(seat.token, token)), None)
            if idx is None or idx == 0 and not peer.is_loopback:
                raise ValueError("重连凭据无效")
            seat = self.seats[idx]
            if seat.writer is not None:
                raise ResumeBusy("原连接仍在关闭，请稍后重试")
            if seat.missing_since is None or time.monotonic() - seat.missing_since >= self.grace:
                raise ValueError("重连窗口已结束")
            seat.writer, seat.missing_since, seat.sequence = writer, None, -1
        else:
            if host_key:
                if not peer.is_loopback or not secrets.compare_digest(self.host_token, host_key):
                    raise ValueError("主机认证失败")
                idx = 0
            else:
                idx = 1
                if self.phase != "lobby" or not self.seats[0] or self.config.mode != "human_human":
                    raise ValueError("房间未开放第二个人类席位")
            if self.seats[idx]:
                raise ValueError("席位已占用，请使用原会话重连")
            name = hello.get("name") or ("主机" if idx == 0 else "玩家二")
            if not isinstance(name, str) or len(name) > 40:
                raise ValueError("玩家名过长")
            name.encode("utf-8")  # Reject unpaired JSON surrogates before recording this name.
            self.seats[idx] = seat = Seat(name, secrets.token_urlsafe(32), writer)

        self.send(writer, dict(type="welcome", version=1, slot=idx, host=idx == 0, resume_token=seat.token,
                               models=self.catalog.describe(), layouts=list(LAYOUTS),
                               lan_addresses=self.lan_addresses if idx == 0 else []))
        if self.phase == "reconnecting" and self.connected():
            self.phase = "paused"
        self.broadcast(self.lobby())
        if self.match:
            self.send(writer, self.snapshot())
        if self.terminal:
            self.send(writer, self.end_for(idx))
        return idx

    async def client(self, reader, writer):
        if len(self.writers) >= 12:
            writer.close()
            return
        self.writers.add(writer)
        slot = None
        try:
            hello = await asyncio.wait_for(read_message(reader), 5)
            slot = self.connect(hello, writer)
            while not writer.is_closing() and not self.closing.is_set():
                message = await asyncio.wait_for(read_message(reader), self.heartbeat)
                try:
                    self.dispatch(slot, message)
                except (ValueError, KeyError) as exc:
                    self.send(writer, {"type": "error", "message": str(exc), "fatal": False})
        except (asyncio.IncompleteReadError, ConnectionError, OSError, asyncio.TimeoutError):
            pass
        except ResumeBusy as exc:
            self.send(writer, {"type": "error", "message": str(exc), "fatal": True, "retryable": True})
        except (ValueError, KeyError, RecursionError) as exc:
            self.send(writer, {"type": "error", "message": str(exc), "fatal": True})
        finally:
            if slot is not None and self.seats[slot] and self.seats[slot].writer is writer:
                self.disconnected(slot)
            self.writers.discard(writer)
            writer.close()
            with contextlib.suppress(ConnectionError, OSError):
                await writer.wait_closed()

    def disconnected(self, slot):
        seat = self.seats[slot]
        seat.writer, seat.missing_since = None, time.monotonic()
        self.pending.clear()
        if self.phase in ("countdown", "running", "paused"):
            self.phase = "reconnecting"
            self.clear_ready()
            self.generation += 1
        self.broadcast(self.lobby())

    def dispatch(self, slot, msg):
        cmd = msg["type"]
        seat = self.seats[slot]
        if cmd == "ping":
            self.send(seat.writer, {"type": "pong"})
            return
        if cmd in ("input", "cancel_input"):
            actor = self.config.host_slot if slot == 0 else 1 - self.config.host_slot
            if self.phase != "running" or actor in self.config.ai_slots() or msg.get("match_id") != self.match.match_id:
                return
            action = 4 if cmd == "cancel_input" else integer(msg.get("action"), "action", 0, 5)
            seq = integer(msg.get("seq"), "seq", 0, 2147483647)
            tick = integer(msg.get("tick"), "tick", 1, self.config.horizon)
            if tick == self.tick + 1 and seq > seat.sequence:
                seat.sequence = seq
                if cmd == "cancel_input" or self.pending.get(actor) != 5:
                    self.pending[actor] = action
            return
        if cmd == "leave":
            if self.phase not in ("ended", "lobby"):
                self.finish(False, "player_left")
            seat.writer.close()
            self.seats[slot] = None
            if self.phase == "lobby" and self.seats[1-slot]:
                self.seats[1-slot].ready = False
        elif cmd == "swap":
            if self.phase != "lobby" or self.config.mode != "human_human" or not self.connected() or any(s.ready for s in self.seats):
                raise ValueError("两位玩家都已加入人人大厅且均未准备时才能交换角色")
            self.config = replace(self.config, host_slot=1 - self.config.host_slot)
        elif cmd == "ready" and self.phase in ("lobby", "paused"):
            seat.ready = boolean(msg.get("ready"), "ready")
        elif cmd == "pause" and self.phase in ("paused", "running"):
            if boolean(msg.get("paused"), "paused"):
                if self.phase == "running":
                    self.pause()
            else:
                if self.policy_fault or self.warming:
                    raise ValueError("请等待模型就绪，或重新加载模型")
                seat.ready = True
                if self.connected() and all(self.seats[i].ready for i in self.required()):
                    self.phase = "running"
                    self.generation += 1
        elif slot != 0:
            raise ValueError("此操作仅主机可用")
        elif cmd == "configure" and self.phase == "lobby":
            cfg = self.config.updated(msg, self.catalog)
            if cfg.mode != "human_human" and self.seats[1] is not None:
                raise ValueError("切换模式前请先退出第二个玩家")
            if self.config.mode == cfg.mode == "human_human" and cfg.host_slot != self.config.host_slot:
                raise ValueError("请使用大厅的交换角色操作")
            self.config = cfg
            self.clear_ready()
        elif cmd == "start" and self.phase == "lobby":
            if not self.connected() or not all(self.seats[i].ready for i in self.required()):
                raise ValueError("请等待所有玩家准备")
            self.config.updated({}, self.catalog)
            self.phase = "countdown"
            self.warming = bool(self.config.ai_slots())
            self.runner = asyncio.create_task(self.play(), name="live-match")
        elif cmd in ("reload", "reload_model") and self.phase == "paused" and self.policy_fault and not self.warming:
            self.reload_requested = self.warming = True
            self.clear_ready()
        elif cmd == "stop" and self.phase in ("countdown", "running", "paused", "reconnecting"):
            self.finish(False, "host_stop")
            return
        elif cmd == "reset" and self.phase == "ended":
            if self.runner and not self.runner.done():
                raise ValueError("正在关闭前一局，请稍候")
            self.match = self.terminal = self.env = None
            self.phase, self.tick = "lobby", 0
            self.policy_fault = ""
            self.warming = self.reload_requested = False
            self.timeouts = [0, 0]
            for player in self.seats:
                if player:
                    player.ready = False
                    player.sequence = -1
        elif cmd == "shutdown":
            self.closing.set()
        else:
            raise ValueError(f"当前阶段不支持 {cmd!r}")
        self.broadcast(self.lobby())

    def clear_ready(self):
        for seat in self.seats:
            if seat:
                seat.ready = False

    def pause(self):
        self.phase = "paused"
        self.generation += 1
        self.pending.clear()
        self.clear_ready()

    def fail_policy(self, reason):
        self.policy_fault = reason
        if self.phase in ("running", "countdown", "paused"):
            self.pause()
        self.broadcast(self.lobby())

    async def load_policies(self, mdp):
        cfg = self.config
        try:
            policies = await asyncio.to_thread(self.catalog.prepare, cfg.ai_slots(), mdp, cfg.horizon, cfg.seed)
            self.timeouts = [0, 0]
            self.policy_fault = ""
            return policies
        except Exception as exc:
            LOG.exception("Model startup failed")
            self.fail_policy(str(exc))
            return None
        finally:
            self.warming = False
            self.broadcast(self.lobby())

    def policy_error(self, diagnostics):
        for slot, status in diagnostics.items():
            if status == "timeout":
                self.timeouts[slot] += 1
                if self.timeouts[slot] >= 3:
                    return f"角色 {slot + 1} 的模型连续三步超时"
            elif status != "ok":
                return f"角色 {slot + 1} 的模型执行出错：{status}"
            else:
                self.timeouts[slot] = 0
        return ""

    async def play(self):
        cfg = self.config
        policies = None
        try:
            if self.phase == "ended" or self.closing.is_set():
                return
            mdp = OvercookedGridworld.from_layout_name(cfg.layout, old_dynamics=cfg.old_dynamics)
            self.env = OvercookedEnv.from_mdp(mdp, horizon=cfg.horizon, info_level=0)
            ai = cfg.ai_slots()
            identities = [dict(id=ai.get(i) or self.seats[0 if i == cfg.host_slot else 1].name,
                               is_human=i not in ai) for i in range(2)]
            recorder = ReplayRecorder(mdp, self.env.state, dict(source="live", players=identities,
                                      model_ids=[ai.get(i, "") for i in range(2)],
                                      mode=cfg.mode, seed=cfg.seed, host_slot=cfg.host_slot,
                                      tick_rate=cfg.tick_rate, horizon=cfg.horizon))
            self.match = MatchRecord(self.records, recorder)
            self.broadcast(self.snapshot())
            if ai:
                policies = await self.load_policies(mdp)
            self.policies = policies
            if self.phase == "ended" or self.closing.is_set():
                return
            self.countdown_until = time.monotonic() + self.countdown_seconds
            self.broadcast(self.lobby())
            remaining = None
            deadline = time.monotonic()
            while self.phase != "ended" and not self.closing.is_set():
                if self.reload_requested:
                    self.reload_requested = False
                    if policies:
                        await asyncio.to_thread(policies.close)
                    self.policies = policies = await self.load_policies(mdp)
                    continue
                now = time.monotonic()
                if self.phase == "countdown":
                    value = math.ceil(self.countdown_until - now)
                    if value <= 0:
                        self.phase = "running"
                        self.broadcast(self.lobby())
                        deadline = now + 1 / cfg.tick_rate
                    elif value != remaining:
                        remaining = value
                        self.broadcast(self.lobby())
                if self.phase != "running":
                    await asyncio.sleep(.025)
                    deadline = time.monotonic() + 1 / cfg.tick_rate
                    continue
                generation = self.generation
                actions = {}
                if policies:
                    try:
                        actions = await asyncio.to_thread(policies.decide, self.env.state, min(.45, .9 / cfg.tick_rate))
                        if self.phase != "running" or generation != self.generation:
                            deadline = time.monotonic() + 1 / cfg.tick_rate
                            continue
                        fault = self.policy_error(policies.diagnostics)
                    except Exception as exc:
                        fault = str(exc) or "模型执行异常"
                    if fault:
                        self.fail_policy(fault)
                        continue
                await asyncio.sleep(max(0, deadline - time.monotonic()))
                if self.phase != "running" or generation != self.generation or self.closing.is_set():
                    deadline = time.monotonic() + 1 / cfg.tick_rate
                    continue
                actions.update(self.pending)
                self.pending.clear()
                joint = tuple(Action.INDEX_TO_ACTION[actions.get(i, 4)] for i in range(2))
                state, reward, done, info = self.env.step(joint)
                recorder.append(joint, state, reward, info)
                if policies:
                    failures = [{"slot": i, "status": status} for i, status in policies.diagnostics.items()
                                if status != "ok"]
                    if failures:
                        recorder.segments[-1]["policy_diagnostics"] = failures
                self.tick += 1
                frame, segment = recorder.frames[-1], recorder.segments[-1]
                self.match.checkpoint()
                self.broadcast(dict(type="tick", match_id=self.match.match_id, tick=self.tick, frame=frame, segment=segment))
                if done:
                    self.finish(True, "horizon")
                    break
                deadline += 1 / cfg.tick_rate
                if deadline < time.monotonic():
                    deadline = time.monotonic() + 1 / cfg.tick_rate
        except Exception as exc:
            LOG.exception("Live match failed")
            self.report_host_error("对局异常，详情见房主日志", exc)
            self.finish(False, "match_error")
        finally:
            if policies:
                await asyncio.to_thread(policies.close)
            self.policies = None
            self.countdown_until = None

    def finish(self, complete, reason):
        if self.phase in ("lobby", "ended"):
            return
        path, frame = "", None
        self.phase = "ended"
        self.generation += 1
        self.pending.clear()
        if self.match:
            frame = self.match.recorder.frames[-1]
            try:
                path = self.match.finish(complete, reason)
            except (OSError, ValueError) as exc:
                LOG.exception("Could not finalize replay")
                self.report_host_error("写入回放失败，详情见房主日志", exc)
        self.terminal = dict(type="end", complete=complete, reason=reason, tick=self.tick, frame=frame, record=path,
                             match_id=self.match.match_id if self.match else "")
        for slot, seat in enumerate(self.seats):
            if seat:
                self.send(seat.writer, self.end_for(slot))
        self.broadcast(self.lobby())

    def end_for(self, slot):
        if slot == 0 or not self.terminal["record"]:
            return self.terminal
        return {**self.terminal, "record": Path(self.terminal["record"]).name}

    async def watch(self):
        while not self.closing.is_set():
            await asyncio.sleep(min(.25, self.grace))
            for i, seat in enumerate(self.seats):
                if seat and seat.missing_since is not None and time.monotonic() - seat.missing_since >= self.grace:
                    if self.phase == "reconnecting":
                        self.finish(False, "reconnect_timeout")
                    self.seats[i] = None
                    self.broadcast(self.lobby())

    async def close(self):
        self.closing.set()
        if self.listener:
            self.listener.close()
            await self.listener.wait_closed()
        self.finish(False, "server_shutdown")
        if self.runner:
            await self.runner
        if self.watchdog:
            self.watchdog.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self.watchdog
        for writer in list(self.writers):
            writer.close()
        await asyncio.sleep(0)


async def serve(args, shutdown_event=None):
    from .live_policy import ModelCatalog
    token = args.host_token_file.read_text(encoding="utf-8-sig").strip()
    room = LiveServer(token, args.records, ModelCatalog(args.models))
    port = await room.listen(args.bind, args.port)
    if args.ready_file:
        args.ready_file.parent.mkdir(parents=True, exist_ok=True)
        temp = args.ready_file.with_suffix(".tmp")
        temp.write_text(json.dumps(dict(bind=args.bind, port=port, pid=os.getpid())), encoding="utf-8")
        temp.replace(args.ready_file)
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGTERM, signal.SIGINT):
        with contextlib.suppress(ValueError, OSError):
            signal.signal(sig, lambda *_: loop.call_soon_threadsafe(room.closing.set))
    print(f"Live server listening on {args.bind}:{port}", flush=True)
    try:
        while not room.closing.is_set() and not (shutdown_event and shutdown_event.is_set()):
            if args.stop_file and args.stop_file.exists():
                break
            await asyncio.sleep(.2)
    finally:
        await room.close()


def main(argv=None, shutdown_event=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--records", type=Path, default=Path("records"))
    parser.add_argument("--models", type=Path)
    parser.add_argument("--host-token-file", type=Path)
    parser.add_argument("--ready-file", type=Path)
    parser.add_argument("--stop-file", type=Path)
    args = parser.parse_args(argv)
    if not args.host_token_file:
        parser.error("--host-token-file is required")
    asyncio.run(serve(args, shutdown_event))


if __name__ == "__main__":
    main()
