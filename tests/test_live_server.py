"""Exercise live rooms through real TCP clients and replay their recorded joint actions."""
import asyncio
from contextlib import asynccontextmanager
from pathlib import Path
import struct

import pytest
from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from better_overcooked_replay.live_server import LiveServer, packet, read_message, MAX_PACKET
from better_overcooked_replay.protocol import load_replay, normalize_state


KEY = "test-secret-host-only"


class SlotPolicies:
    def __init__(self, slots):
        self.slots = slots
        self.diagnostics = dict.fromkeys(slots, "ok")

    def decide(self, state, timeout):
        return {slot: (slot + state.timestep) % 4 for slot in self.slots}

    def close(self):
        pass


class Catalog:
    def describe(self):
        return [{"id": name, "label": name} for name in ("greedy", "other")]

    def validate(self, name, layout, old_dynamics):
        if name not in ("greedy", "other"):
            raise ValueError("Unknown model")

    def prepare(self, slots, mdp, horizon, seed):
        self.policies = SlotPolicies(slots)
        return self.policies


class Client:
    def __init__(self, reader, writer):
        self.reader, self.writer = reader, writer

    async def send(self, kind, **fields):
        self.writer.write(packet(dict(type=kind, **fields)))
        await self.writer.drain()

    async def get(self, expected, **subset):
        async def until():
            while True:
                msg = await read_message(self.reader)
                if msg["type"] == "error" and expected != "error":
                    pytest.fail(f"unexpected server error: {msg}")
                if msg["type"] == expected and all(msg.get(k) == v for k, v in subset.items()):
                    return msg
        return await asyncio.wait_for(until(), 5)

    async def close(self):
        self.writer.close()
        await self.writer.wait_closed()


@asynccontextmanager
async def room_at(records, **opts):
    room = LiveServer(KEY, records, Catalog(), countdown=.01, **opts)
    port = await room.listen(port=0)
    clients = []

    async def connect(**hello):
        reader, writer = await asyncio.open_connection("127.0.0.1", port)
        client = Client(reader, writer)
        clients.append(client)
        await client.send("hello", version=1, **hello)
        return client

    try:
        yield room, connect, port
    finally:
        await room.close()
        for c in clients:
            await c.close()


def test_auth_framing_and_room_authority(tmp_path):
    async def run():
        async with room_at(tmp_path) as (room, connect, port):
            bad = await connect(host_token="bad-token")
            assert (await bad.get("error"))["fatal"]
            reader, writer = await asyncio.open_connection("127.0.0.1", port)
            writer.write(struct.pack(">I", MAX_PACKET + 1))
            assert (await read_message(reader))["fatal"]
            writer.close()
            await writer.wait_closed()
            host = await connect(host_token=KEY)
            welcome = await host.get("welcome")
            assert welcome["slot"] == 0 and welcome["resume_token"] != KEY and KEY not in str(welcome)
            await host.send("configure", mode="ai_ai", model0="invalid")
            assert not (await host.get("error"))["fatal"]
            await host.send("configure", mode="human_human")
            await host.get("lobby", mode="human_human")
            guest = await connect(name="客人")
            assert (await guest.get("welcome"))["slot"] == 1
            thief = await connect(resume_token=welcome["resume_token"])
            await thief.get("error", fatal=True)
            await guest.send("shutdown")
            await guest.get("error", fatal=False)
            await host.send("start")
            await host.get("error", fatal=False)
            assert room.phase == "lobby" and not room.closing.is_set()
    asyncio.run(run())


@pytest.mark.parametrize("mode", ["human_ai", "ai_ai", "human_human"])
def test_live_modes_authoritative_tick_and_terminal_record(tmp_path, mode):
    async def run():
        async with room_at(tmp_path) as (room, connect, _):
            host = await connect(host_token=KEY, name="蓝")
            await host.get("welcome")
            await host.send("configure", mode=mode, horizon=3, tick_rate=5, host_slot=1, model0="other")
            await host.get("lobby", mode=mode, horizon=3)
            guest = None
            if mode == "human_human":
                guest = await connect(name="橙")
                await guest.get("welcome")
                await guest.send("ready", ready=True)
                await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            first = await host.get("start")
            assert first["frame"]["state"]["timestep"] == 0
            assert load_replay(next(tmp_path.glob("*.partial.ocrep.json")))["header"]["complete"] is False
            await host.get("lobby", phase="running")
            mid = first["match_id"]
            await host.send("input", match_id=mid, tick=1, seq=2, action=2)
            await host.send("input", match_id=mid, tick=1, seq=1, action=3)
            await host.send("input", match_id="stale", tick=1, seq=5, action=3)
            if guest:
                await guest.send("input", match_id=mid, tick=1, seq=2, action=1)
            tick = await host.get("tick", tick=1)
            if guest:
                assert tick == await guest.get("tick", tick=1)
            expected = {"human_ai": [0, 2], "ai_ai": [0, 1], "human_human": [1, 2]}
            assert tick["segment"]["actions"] == expected[mode]
            end = await host.get("end")
            assert end["complete"] and end["reason"] == "horizon" and end["match_id"] == mid
            data = load_replay(end["record"])
            assert len(data["frames"]) == 4 and len(data["segments"]) == 3
            assert data["frames"][-1] == end["frame"]
            assert not list(tmp_path.glob("*.partial.ocrep.json"))
            if mode != "ai_ai":
                assert data["header"]["players"][1] == {"id": "蓝", "is_human": True}
                assert data["segments"][1]["actions"][1] == 4
            env = OvercookedEnv.from_mdp(OvercookedGridworld.from_layout_name("cramped_room", old_dynamics=False), horizon=3)
            for frame, seg in zip(data["frames"][1:], data["segments"]):
                state, reward, _, _ = env.step(tuple(Action.INDEX_TO_ACTION[i] for i in seg["actions"]))
                assert normalize_state(state) == frame["state"]
                assert reward == seg["sparse_reward"]
            await host.send("reset")
            lobby = await host.get("lobby", phase="lobby")
            assert not lobby["ready0"] and not lobby["ready1"]
    asyncio.run(run())


def test_pause_disconnect_reconnect_and_partial(tmp_path):
    async def run():
        async with room_at(tmp_path, grace=.4) as (room, connect, _):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", mode="human_human", tick_rate=8)
            await host.get("lobby", mode="human_human")
            guest = await connect()
            token = (await guest.get("welcome"))["resume_token"]
            await guest.send("ready", ready=True)
            await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            start = await host.get("start")
            await host.get("tick", tick=1)
            await host.send("pause", paused=True)
            await host.get("lobby", phase="paused")
            await guest.close()
            await host.get("lobby", phase="reconnecting")
            guest = await connect(resume_token=token)
            await guest.get("welcome")
            current = await guest.get("start")
            assert current["match_id"] == start["match_id"] and current["tick"] == 1
            await host.get("lobby", phase="paused")
            await asyncio.sleep(.16)
            assert room.tick == 1
            await guest.send("ready", ready=True)
            await host.get("lobby", phase="paused", ready1=True)
            await host.send("pause", paused=False)
            await host.get("tick", tick=2)
            await guest.close()
            end = await host.get("end")
            assert end["reason"] == "reconnect_timeout" and not end["complete"]
            assert Path(end["record"]).name.endswith(".partial.ocrep.json")
            data = load_replay(end["record"])
            assert len(data["segments"]) == 2 and data["frames"][-1] == end["frame"]
    asyncio.run(run())


@pytest.mark.parametrize("scenario", ["swap", "delivery"])
def test_joint_inputs_follow_upstream_for_swaps_and_terminal_delivery(tmp_path, scenario):
    from better_overcooked_replay.samples import delivery_plan
    steps = ([(0, 3), (2, 3)] if scenario == "swap" else
             [tuple(Action.ACTION_TO_INDEX[a] for a in pair) for pair in delivery_plan()])

    async def run():
        async with room_at(tmp_path) as (_, connect, __):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", mode="human_human", tick_rate=12, horizon=len(steps))
            await host.get("lobby", mode="human_human")
            guest = await connect()
            await guest.get("welcome")
            await guest.send("ready", ready=True)
            await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            start = await host.get("start")
            await host.get("lobby", phase="running")
            env = OvercookedEnv.from_mdp(OvercookedGridworld.from_layout_name("cramped_room", old_dynamics=False),
                                         horizon=len(steps))
            for tick, (a, b) in enumerate(steps, start=1):
                fields = dict(tick=tick, match_id=start["match_id"])
                # An interact edge takes priority over a later held move in this tick.
                await host.send("input", seq=2*tick, action=a, **fields)
                if a == 5:
                    await host.send("input", seq=2*tick+1, action=1, **fields)
                await guest.send("input", seq=tick, action=b, **fields)
                received = await host.get("tick", tick=tick)
                assert received["segment"]["actions"] == [a, b]
                state, reward, _, _ = env.step((Action.INDEX_TO_ACTION[a], Action.INDEX_TO_ACTION[b]))
                assert received["frame"]["state"] == normalize_state(state)
                assert received["segment"]["sparse_reward"] == reward
            end = await host.get("end")
            guest_end = await guest.get("end")
            assert guest_end["record"] == Path(end["record"]).name
            if scenario == "delivery":
                assert end["frame"]["score"] == 20 == load_replay(end["record"])["frames"][-1]["score"]
            else:
                assert [p["position"] for p in end["frame"]["state"]["players"]] == [[1, 1], [2, 1]]
    asyncio.run(run())


@pytest.mark.parametrize("failure,last_tick", [("timeout", 2), ("worker stopped", 0)])
def test_worker_failure_pauses_and_requires_explicit_reload(tmp_path, failure, last_tick):
    async def run():
        async with room_at(tmp_path) as (room, connect, _):
            normal_prepare = room.catalog.prepare

            def fail_prepare(*args):
                policies = normal_prepare(*args)
                policies.diagnostics = {1: failure}
                return policies

            room.catalog.prepare = fail_prepare
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", tick_rate=12, horizon=5)
            await host.send("ready", ready=True)
            await host.send("start")
            paused = await host.get("lobby", phase="paused")
            assert paused["policy_fault"] and room.tick == last_tick
            await host.send("pause", paused=False)
            await host.get("error", fatal=False)
            room.catalog.prepare = normal_prepare
            await host.send("reload")
            await host.get("lobby", phase="paused", warming=False, policy_fault="")
            await host.send("pause", paused=False)
            assert (await host.get("end"))["complete"]
    asyncio.run(run())
