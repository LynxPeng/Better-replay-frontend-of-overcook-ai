from pathlib import Path
import multiprocessing as mp
import time

import pytest

from better_overcooked_replay.live_policy import ModelCatalog, prepare_sp


def _delayed_worker(channel, model_id, path, release, index, seed, horizon):
    # The first slot waits for an explicit test signal on its first decision.
    channel.send(("ready", None))
    while True:
        tick, action = channel.recv()
        if index == 0 and tick == 1:
            release.wait(10)
        channel.send((tick, action))


def test_late_prediction_cannot_replace_another_tick_or_slot(monkeypatch):
    from better_overcooked_replay import live_policy

    monkeypatch.setattr(live_policy, "_policy_worker", _delayed_worker)
    release = mp.get_context("spawn").Event()
    policies = live_policy.LivePolicies()
    try:
        for index in (0, 1):
            policies.start(index, "test", None, release, 10, index, time.monotonic() + 30)
        # The fast worker still receives and completes its prediction within the same deadline.
        assert policies.decide(2, timeout=.2) == {0: 4, 1: 2}
        assert policies.diagnostics == {0: "timeout", 1: "ok"}
        assert policies.decide(1, timeout=.2) == {0: 4, 1: 1}
        release.set()
        assert policies.workers[0].channel.poll(5)
        # A late action 2 from the first tick must never replace action 5 here.
        assert policies.decide(5, timeout=.5) == {0: 5, 1: 5}
    finally:
        release.set()
        policies.close()


def test_host_sb3_model_warms_and_predicts(tmp_path):
    from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
    from better_overcooked_replay.live_policy import _stage_sb3

    parent = Path(__file__).resolve().parents[2]
    source = parent / "output/ppo-final/policy.zip"
    if not source.is_file():
        pytest.skip("requires host's locally trained RGB PPO policy")
    _stage_sb3(source, tmp_path, "ppo-final")
    catalog = ModelCatalog(tmp_path)
    mdp = OvercookedGridworld.from_layout_name("cramped_room", old_dynamics=False)
    policies = catalog.prepare({1: "ppo-final"}, mdp)
    try:
        assert policies.decide(mdp.get_standard_start_state())[1] in range(6)
        assert policies.diagnostics == {1: "ok"}
    finally:
        policies.close()


def test_unknown_model_and_unsupported_greedy_layout(tmp_path):
    catalog = ModelCatalog(tmp_path)
    (tmp_path / "outside.zip").write_bytes(b"test")
    for invalid in ("../outside", str(tmp_path / "outside.zip"), "missing"):
        with pytest.raises(ValueError):
            catalog.validate(invalid, "cramped_room", False)
    with pytest.raises(ValueError):
        catalog.validate("greedy", "forced_coordination", False)
    with pytest.raises(ValueError):
        catalog.validate("greedy", "cramped_room_tomato", False)


def test_rejects_unrecognized_checkpoint_before_loading(tmp_path):
    source = tmp_path / "src/overcooked_demo/server/static/assets/agents/RllibCrampedRoomSP/agent/checkpoint-550"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"not the upstream checkpoint")
    with pytest.raises(ValueError, match="SHA256"):
        prepare_sp(tmp_path, tmp_path / "models")
    assert not (tmp_path / "models/sp-cramped-room.npz").exists()


def test_two_sp_slots_reproduce_upstream_game(tmp_path):
    from overcooked_ai_py.mdp.actions import Action
    from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
    from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv

    parent = Path(__file__).resolve().parents[2]
    if not (parent / "src/overcooked_demo").is_dir():
        pytest.skip("requires local upstream checkout with checkpoint-550")
    prepare_sp(parent, tmp_path)
    catalog = ModelCatalog(tmp_path)
    with pytest.raises(ValueError):
        catalog.validate("sp-cramped-room", "cramped_room", False)
    mdp = OvercookedGridworld.from_layout_name("cramped_room", old_dynamics=True)
    env = OvercookedEnv.from_mdp(mdp, horizon=400)
    policies = catalog.prepare({0:"sp-cramped-room", 1:"sp-cramped-room"}, mdp, seed=0)
    score = 0
    try:
        for _ in range(400):
            moves = policies.decide(env.state, timeout=2)
            assert all(value == "ok" for value in policies.diagnostics.values())
            _, reward, _, _ = env.step(tuple(Action.INDEX_TO_ACTION[moves[i]] for i in range(2)))
            score += reward
        assert score == 220
    finally:
        policies.close()
