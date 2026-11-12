"""Behavioral regression tests for terminal capture and portable historical replay."""
import copy
import json

import pytest

from overcooked_ai_py.mdp.actions import Action, Direction
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import ObjectState, OvercookedGridworld, SoupState
from better_overcooked_replay.protocol import ReplayRecorder, load_replay, validate_replay, normalize_state
from better_overcooked_replay.importers import convert_data


def test_legacy_uppercase_interact_import():
    from better_overcooked_replay.protocol import action_index
    assert action_index('INTERACT') == 5
    assert action_index('interact') == 5


def test_historical_layout_player_markers_become_floor():
    mdp, states, actions, rewards, _ = episode()
    data = native_data(states, actions, rewards, mdp)
    data['mdp_params'][0]['terrain'] = copy.deepcopy(mdp.terrain_mtx)
    data['mdp_params'][0]['terrain'][2][1] = '1'
    data['mdp_params'][0]['terrain'][1][3] = '2'
    result = convert_data(data)[0]
    assert result['header']['terrain'][2][1] == ' '
    assert result['header']['terrain'][1][3] == ' '


def episode(steps=2):
    mdp = OvercookedGridworld.from_layout_name('cramped_room')
    env = OvercookedEnv.from_mdp(mdp, horizon=steps)
    initial = env.state.deepcopy()
    states, actions, rewards, infos = [initial], [], [], []
    for _ in range(steps):
        state, reward, _, info = env.step((Action.STAY, Action.STAY))
        states.append(state.deepcopy())
        actions.append((Action.STAY, Action.STAY))
        rewards.append(reward)
        infos.append(info)
    return mdp, states, actions, rewards, infos


def native_data(states, actions, rewards, mdp):
    return dict(ep_states=[states], ep_actions=[actions], ep_rewards=[rewards],
                mdp_params=[mdp.mdp_params], env_params=[{'horizon': len(actions)}])


@pytest.fixture
def handoff_episode():
    if not hasattr(OvercookedGridworld, 'get_sync_handoff_locations'):
        pytest.skip('requires the HuCooked environment')
    mdp = OvercookedGridworld.from_layout_name('three_player_sync_handoff')
    env = OvercookedEnv.from_mdp(mdp, horizon=2)
    sender, third, receiver = env.state.players
    sender.update_pos_and_or((4, 3), Direction.EAST)
    sender.set_object(ObjectState('onion', (4, 3)))
    third.update_pos_and_or((9, 4), Direction.EAST)
    receiver.update_pos_and_or((6, 3), Direction.WEST)
    states = [env.state.deepcopy()]
    actions = [(Action.INTERACT, Action.STAY, Action.STAY), (Action.INTERACT,) * 3]
    rewards, infos = [], []
    for action in actions:
        state, reward, _, info = env.step(action)
        states.append(state.deepcopy())
        rewards.append(reward)
        infos.append(info)
    return mdp, states, actions, rewards, infos


def test_hucooked_replay_retains_failed_and_successful_handoffs(handoff_episode, tmp_path):
    mdp, states, actions, rewards, infos = handoff_episode
    recorder = ReplayRecorder(mdp, states[0])
    for action, state, reward, info in zip(actions, states[1:], rewards, infos):
        recorder.append(action, state, reward, info)
    replay = load_replay(recorder.write(tmp_path / 'handoff.ocrep.json'))
    assert len(replay['header']['players']) == 3
    assert replay['header']['terrain'][3][5] == 'H'
    assert len(replay['frames']) == 3
    missed, passed = replay['segments']
    assert missed['sync_handoffs'][0]['reason'] == 'not_simultaneous'
    assert missed['sync_handoffs'][0]['attempting_agents'] == [0]
    assert passed['sync_handoffs'] == [dict(station=[5, 3], participants=[0, 2],
                                           attempting_agents=[0, 2], success=True,
                                           reason='success', sender=0, receiver=2)]
    assert passed['actions'] == [5, 5, 5]
    assert len(passed['shaped_reward_by_agent']) == 3
    assert replay['frames'][-1]['score'] == 0
    players = replay['frames'][-1]['state']['players']
    assert players[0]['held_object'] is None
    assert [p['held_object']['name'] for p in players[1:]] == ['dish', 'onion']


def test_hucooked_rollout_import_retains_terminal_and_handoff_info(handoff_episode):
    mdp, states, actions, rewards, infos = handoff_episode
    data = native_data(states[:-1], actions, rewards, mdp)
    data['ep_infos'] = [infos]
    replay = convert_data(data, dynamics='new')[0]
    assert replay['header']['complete']
    assert replay['frames'][-1]['state']['timestep'] == 2
    assert replay['segments'][1]['sync_handoffs'][0]['receiver'] == 2
    assert any(e['kind'] == 'sync_handoff_receive' and e['agent_index'] == 2
               for e in replay['segments'][1]['events'])
    replay['frames'][0]['state']['players'][2]['position'] = [4, 3]
    with pytest.raises(ValueError, match='overlap'):
        validate_replay(replay)


def test_recorder_retains_terminal_and_detaches_mutable_snapshots(tmp_path):
    mdp, states, actions, rewards, infos = episode()
    rec = ReplayRecorder(mdp, states[0])
    for a, s, r, i in zip(actions, states[1:], rewards, infos):
        rec.append(a, s, r, i)
    path = tmp_path / '中文.ocrep.json'
    rec.write(path)
    states[-1].timestep = 999
    result = json.loads(path.read_text(encoding='utf-8'))
    assert len(result['frames']) == 3
    assert len(result['segments']) == 2
    assert result['frames'][-1]['state']['timestep'] == 2
    assert result['header']['complete'] is True
    assert result['segments'][0]['actions'] == [4, 4]
    validate_replay(result)


def test_last_step_delivery_is_in_terminal_snapshot_and_sparse_score():
    mdp = OvercookedGridworld.from_layout_name('cramped_room')
    env = OvercookedEnv.from_mdp(mdp, horizon=1)
    player = env.state.players[0]
    player.update_pos_and_or((3, 2), Direction.SOUTH)
    player.set_object(SoupState.get_soup((3, 2), num_onions=3, finished=True))
    rec = ReplayRecorder(mdp, env.state)
    state, reward, done, info = env.step((Action.INTERACT, Action.STAY))
    rec.append((Action.INTERACT, Action.STAY), state, reward, info)
    result = rec.build()
    assert done and reward == 20
    assert result['frames'][-1]['score'] == 20
    assert result['frames'][-1]['state']['players'][0]['held_object'] is None
    delivered = any(e['kind'] == 'soup_delivery' for e in result['segments'][0]['events'])
    assert delivered == ('event_infos' in info)


@pytest.mark.parametrize('damage', ['frame_count', 'time', 'action', 'position'])
def test_validator_rejects_corrupt_records(damage):
    mdp, states, actions, rewards, infos = episode()
    rec = ReplayRecorder(mdp, states[0])
    for a, s, r, i in zip(actions, states[1:], rewards, infos):
        rec.append(a, s, r, i)
    data = rec.build()
    if damage == 'frame_count': data['frames'].pop()
    if damage == 'time': data['frames'][1]['time'] = -1
    if damage == 'action': data['segments'][0]['actions'][0] = 9
    if damage == 'position': data['frames'][0]['state']['players'][0]['position'] = [999, 2]
    with pytest.raises(ValueError): validate_replay(data)


def test_legacy_soup_normalization_is_independent_of_recipe_globals():
    state = {'players': [{'position': [1, 1], 'orientation': [0, -1], 'held_object': None},
                         {'position': [2, 1], 'orientation': [1, 0], 'held_object': None}],
             'objects': [{'name': 'soup', 'position': [2, 0], 'state': ['onion', 3, 20]}],
             'timestep': 7}
    normalized = normalize_state(state)
    soup = normalized['objects'][0]
    assert soup['is_ready'] is True
    assert soup['cook_time'] == 20
    assert len(soup['_ingredients']) == 3
    assert 'state' in state['objects'][0]


def test_native_import_recognizes_explicit_terminal_sentinel():
    mdp, states, actions, rewards, _ = episode()
    data = native_data(states, actions + [(None, None)], rewards + [0], mdp)
    result = convert_data(data)[0]
    assert len(result['frames']) == 3 and len(result['segments']) == 2
    assert result['header']['complete'] is True


def test_missing_terminal_is_preserved_as_incomplete_without_unverified_action():
    mdp, states, actions, rewards, _ = episode()
    result = convert_data(native_data(states[:-1], actions, rewards, mdp))[0]
    assert len(result['frames']) == 2 and len(result['segments']) == 1
    assert result['header']['complete'] is False
    assert any('trailing' in x for x in result['diagnostics'])


def test_verified_explicit_dynamics_can_restore_missing_terminal():
    mdp, states, actions, rewards, _ = episode()
    result = convert_data(native_data(states[:-1], actions, rewards, mdp), dynamics='new')[0]
    assert result['frames'][-1]['state']['timestep'] == 2
    assert result['header']['complete'] is True


def test_demo_import_normalizes_post_action_score_and_gap():
    mdp, states, _, _, _ = episode(3)
    rows = []
    for idx, sparse, score in [(0, 0, 0), (3, 20, 40)]:
        rows.append(dict(state=json.dumps(states[idx].to_dict()), joint_action='[[0,0],[0,0]]',
                         reward=sparse, score=score, time_elapsed=idx / 6,
                         cur_gameloop=idx, layout=mdp.terrain_mtx,
                         layout_name='cramped_room', trial_id='one',
                         player_0_is_human=True, player_1_is_human=False))
    result = convert_data({'uid': 'demo', 'trajectory': rows})[0]
    assert result['frames'][1]['score'] == 20
    assert result['segments'][0]['kind'] == 'gap'
    assert result['segments'][0]['reward_known'] is False
    assert result['segments'][0]['actions'] is None
    assert result['header']['players'][0]['is_human'] is True


def test_dataframe_and_csv_style_rows_split_trials_and_keep_equal_timestamps():
    import pandas as pd
    mdp, states, _, _, _ = episode()
    rows = [dict(state=s.to_dict(), joint_action=[[0,0],[0,0]], reward=0, score=0,
                 time_elapsed=0, cur_gameloop=i, layout=mdp.terrain_mtx,
                 layout_name='cramped_room', trial_id=trial)
            for trial in ['a', 'b'] for i, s in enumerate(states[:2])]
    results = convert_data(pd.DataFrame(rows))
    assert len(results) == 2
    assert results[0]['frames'][0]['time'] == results[0]['frames'][1]['time'] == 0
    assert results[0]['header']['complete'] is False


def test_import_rejects_decreasing_source_time():
    mdp, states, _, _, _ = episode()
    rows = [dict(state=s.to_dict(), joint_action=[[0,0],[0,0]], reward=0, score=0,
                 time_elapsed=2-i, cur_gameloop=i, layout=mdp.terrain_mtx, trial_id='a')
            for i, s in enumerate(states[:2])]
    with pytest.raises(ValueError, match='time'): convert_data(rows)


@pytest.mark.parametrize('old', [False, True])
def test_event_instrumentation_preserves_upstream_transition(old):
    mdp = OvercookedGridworld.from_layout_name('cramped_room', old_dynamics=old)
    env = OvercookedEnv.from_mdp(mdp, horizon=8)
    plan = [(Direction.NORTH, Action.STAY), (Action.INTERACT, Action.INTERACT),
            (Direction.WEST, Direction.WEST), (Action.INTERACT, Action.STAY)]
    for joint in plan:
        expected, details = mdp.get_state_transition(env.state, joint)
        actual, reward, _, info = env.step(joint)
        assert actual.to_dict() == expected.to_dict()
        assert reward == sum(details['sparse_reward_by_agent'])
        if 'event_infos' in info:
            assert info['event_infos'] == details['event_infos']
