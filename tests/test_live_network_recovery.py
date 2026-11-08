import asyncio

from test_live_server import KEY, room_at


def test_either_player_can_swap_only_in_an_unready_two_human_lobby(tmp_path):
    async def run():
        async with room_at(tmp_path) as (_, connect, _port):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("swap")
            await host.get("error")
            await host.send("configure", mode="human_human")
            await host.get("lobby", mode="human_human", host_slot=0)
            await host.send("swap")
            await host.get("error")
            guest = await connect()
            assert (await guest.get("welcome"))["slot"] == 1
            await guest.send("swap")
            await host.get("lobby", host_slot=1)
            await guest.get("lobby", host_slot=1)
            await guest.send("configure", layout="coordination_ring")
            await guest.get("error")
            await guest.send("ready", ready=True)
            await host.get("lobby", ready1=True)
            await host.send("swap")
            await host.get("error")
            await host.send("configure", host_slot=0)
            await host.get("error")
            await guest.send("ready", ready=False)
            await host.get("lobby", ready1=False)
            await host.send("ready", ready=True)
            await guest.get("lobby", ready0=True)
            await guest.send("swap")
            await guest.get("error")
            await host.send("ready", ready=False)
            await host.get("lobby", ready0=False, ready1=False)
            await host.send("swap")
            await guest.get("lobby", host_slot=0)
    asyncio.run(run())


def test_valid_busy_seat_can_be_retried_after_old_stream_closes(tmp_path):
    async def run():
        async with room_at(tmp_path) as (_, connect, _port):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", mode="human_human")
            await host.get("lobby", mode="human_human")
            old = await connect()
            token = (await old.get("welcome"))["resume_token"]
            attempt = await connect(resume_token=token)
            assert (await attempt.get("error", fatal=True)).get("retryable") is True
            stranger = await connect(resume_token="incorrect")
            assert not (await stranger.get("error", fatal=True)).get("retryable")
            await old.close()
            await host.get("lobby", connected1=False)
            recovered = await connect(resume_token=token)
            assert (await recovered.get("welcome"))["slot"] == 1
    asyncio.run(run())


def test_guest_never_receives_host_filesystem_errors(tmp_path, monkeypatch):
    from better_overcooked_replay.live_server import MatchRecord

    private_path = str(tmp_path / "private-host-directory" / "secret.ocrep.json")

    def deny(*_args, **_kwargs):
        raise PermissionError(13, "permission denied", private_path)

    async def run():
        async with room_at(tmp_path) as (_, connect, _port):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", mode="human_human")
            await host.get("lobby", mode="human_human")
            guest = await connect()
            await guest.get("welcome")
            await guest.send("ready", ready=True)
            await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            await host.get("start")
            await guest.get("start")
            # Exercise both generic match failure and final replay write failure.
            monkeypatch.setattr(MatchRecord, "checkpoint", deny)
            monkeypatch.setattr(MatchRecord, "finish", deny)
            for _ in range(2):
                host_error = await host.get("error")
                guest_error = await guest.get("error")
                assert "private-host-directory" in host_error["message"]
                assert "private-host-directory" not in guest_error["message"]
            assert (await guest.get("end"))["record"] == ""
    asyncio.run(run())


def test_cancel_input_is_ordered_and_limited_to_current_seat_and_tick(tmp_path):
    async def run():
        async with room_at(tmp_path) as (_, connect, _port):
            host = await connect(host_token=KEY)
            await host.get("welcome")
            await host.send("configure", mode="human_human", horizon=2, tick_rate=2)
            await host.get("lobby", mode="human_human")
            guest = await connect()
            await guest.get("welcome")
            await guest.send("ready", ready=True)
            await host.get("lobby", ready1=True)
            await host.send("ready", ready=True)
            await host.send("start")
            mid = (await host.get("start"))["match_id"]
            await host.get("lobby", phase="running")
            await host.send("input", match_id=mid, tick=1, seq=4, action=5)
            await guest.send("input", match_id=mid, tick=1, seq=4, action=5)
            await host.send("cancel_input", match_id=mid, tick=1, seq=5)
            await guest.send("cancel_input", match_id=mid, tick=1, seq=3)
            await guest.send("cancel_input", match_id=mid, tick=2, seq=6)
            await guest.send("cancel_input", match_id="older-room", tick=1, seq=7)
            first = await host.get("tick", tick=1)
            assert first["segment"]["actions"] == [4, 5]
    asyncio.run(run())
