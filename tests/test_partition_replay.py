"""Replay the actual v3 rules, including CPU/device lane and historical traces."""
import copy
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest

import better_overcooked_replay as replay
from better_overcooked_replay.importers import convert_data, convert_file
pytest.importorskip('overcooked_ai_py.cooperative.partition', reason='optional HuCooked v3 core')
from overcooked_ai_py.cooperative.partition import PartitionConfig, PartitionKitchen, TorchPartitionKitchen
from overcooked_ai_py.cooperative.partition.controller import ScriptedChefs
from overcooked_ai_py.cooperative.partition.model import (
    BEEF, CUT_POTATO, EAST, FIRST_MEAL, INTERACT, NORTH, SOUTH, STAY, TOMATO, WEST, cell,
)


def recorder(env, **kwargs):
    return replay.PartitionReplayRecorder(env, **kwargs)


def advance(env, rec, actions):
    _, reward, _, _, info = env.step(actions)
    rec.append(actions, reward, info)
    return info


@pytest.mark.parametrize('layout', ['sync', 'counter'])
def test_full_scripted_episode_roundtrip(layout, tmp_path):
    env = PartitionKitchen(PartitionConfig(layout=layout))
    env.reset(seed=0)
    rec = recorder(env)
    team = ScriptedChefs(env)
    for _ in range(env.config.horizon):
        advance(env, rec, team.actions())
    data = replay.load_replay(rec.write(tmp_path / 'game.ocrep.json'))
    assert data['header']['schema_version'] == 2
    assert data['header']['environment'] == 'partition_v3'
    assert data['header']['partition_config']['layout'] == layout
    assert data['header']['complete'] is True
    assert len(data['frames']) == 301 and len(data['segments']) == 300
    assert data['frames'][-1]['score'] > 0
    deliveries = [e for s in data['segments'] for e in s['events'] if e['kind'] == 'meal_delivery']
    assert {e['recipe'] for e in deliveries} == {0, 1, 2}
    assert all(e['agent_index'] in range(3) for e in deliveries)
    assert sum(s['sparse_reward'] for s in data['segments']) == data['frames'][-1]['score']
    old = copy.deepcopy(data)
    env.reset(seed=99)
    assert rec.build() == old


def test_new_format_preserves_progress_and_different_meal_identity():
    env = PartitionKitchen()
    env.reset(seed=0)
    s = env.state
    s['held'][:] = [2, FIRST_MEAL + 1, FIRST_MEAL + 2]
    s['board'][:] = [TOMATO, 2, 0, 0]
    s['cuts'][:] = [1, 2, 0, 0]
    s['mix'][:] = [2, 11]
    s['locked'][:] = [1, 2]
    s['heat'][:] = [2, 4]
    data = recorder(env).build()
    state = data['frames'][0]['state']
    assert [p['held_object']['name'] for p in state['players']] == [
        'potato', 'meal_potato', 'meal_potato_tomato_beef']
    assert state['partition']['heat'] == [2, 4]
    assert state['partition']['cuts'] == [1, 2, 0, 0]
    assert {o['name'] for o in state['objects']} == {'tomato', 'cut_potato'}
    assert all({'id', 'recipe', 'issued', 'due', 'side'} <= o.keys() for o in state['all_orders'])


def test_last_tick_delivery_uses_old_order_ids_and_separates_expiry():
    env = PartitionKitchen(PartitionConfig(horizon=1))
    env.reset(seed=0)
    s = env.state
    s['pos'][:] = [cell(1, 3), cell(11, 3), cell(11, 5)]
    s['facing'][:] = [WEST, EAST, WEST]
    s['held'][:] = [FIRST_MEAL, FIRST_MEAL, 0]
    s['recipe'][:] = [0, 1, 0]
    s['due'][:] = 1
    rec = recorder(env)
    advance(env, rec, [INTERACT, INTERACT, STAY])
    data = rec.build()
    events = data['segments'][0]['events']
    delivered = [e for e in events if e['kind'] == 'meal_delivery']
    expired = [e for e in events if e['kind'] == 'order_expired']
    assert [(e['order_id'], e['agent_index']) for e in delivered] == [(0, 1)]
    assert {e['order_id'] for e in expired} == {1, 2}
    assert [o['id'] for o in data['frames'][1]['state']['all_orders']] == [3, 4, 5]
    assert data['frames'][1]['score'] == 20
    assert data['frames'][1]['state']['players'][0]['held_object']['name'] == 'meal_tomato_egg'


@pytest.mark.parametrize('layout', ['sync', 'counter'])
def test_confirmed_handoffs_and_counter_conflict(layout):
    env = PartitionKitchen(PartitionConfig(layout=layout))
    env.reset(seed=0)
    s = env.state
    s['pos'][:] = [cell(5, 2), cell(7, 2), cell(11, 1)]
    s['facing'][:] = [EAST, WEST, NORTH]
    s['held'][:] = [BEEF, 0, 0]
    rec = recorder(env)
    advance(env, rec, [INTERACT, STAY, STAY])
    advance(env, rec, [INTERACT, INTERACT, STAY])
    if layout == 'sync':
        failed, passed = rec.build()['segments']
        assert failed['sync_handoffs'][0]['success'] is False
        assert passed['sync_handoffs'][0]['sender'] == 0
        assert passed['sync_handoffs'][0]['receiver'] == 1
    else:
        assert env.state['counter'][1] == BEEF
        advance(env, rec, [STAY, INTERACT, STAY])
        data = rec.build()
        assert data['frames'][2]['state']['partition']['counter'][1] == BEEF
        pickups = [e for e in data['segments'][2]['events'] if e['kind'] == 'counter_pickup']
        assert pickups[0]['wait_steps'] == 2


def test_chopping_tending_and_interruptions_are_snapshot_events():
    env = PartitionKitchen()
    env.reset(seed=0)
    s = env.state
    s['pos'][:] = [cell(1, 1), cell(10, 1), cell(8, 1)]
    s['facing'][:] = [NORTH, SOUTH, SOUTH]
    s['held'][:] = [0, TOMATO, CUT_POTATO]
    rec = recorder(env)
    advance(env, rec, [STAY, INTERACT, INTERACT])
    advance(env, rec, [STAY, INTERACT, INTERACT])
    assert env.state['cuts'][2] == 1 and env.state['heat'][1] == 1
    advance(env, rec, [STAY, STAY, STAY])
    data = rec.build()
    assert data['frames'][2]['state']['partition']['cuts'][2] == 1
    assert data['frames'][3]['state']['partition']['cuts'][2] == 0
    assert data['frames'][3]['state']['partition']['heat'][1] == 0
    assert {'chop', 'tend'} <= {e['kind'] for e in data['segments'][1]['events']}
    assert not any(e['kind'] in ('chop', 'tend') for e in data['segments'][2]['events'])


def test_tick_shape_time_and_metadata_guards():
    env = PartitionKitchen(PartitionConfig(horizon=2))
    rec = recorder(env, metadata={'tick_rate': 2})
    with pytest.raises(ValueError, match='tick|step'):
        rec.append([STAY]*3, 0, {})
    env.step([STAY]*3)
    env.step([STAY]*3)
    with pytest.raises(ValueError, match='tick|step'):
        rec.append([STAY]*3, 0, {})
    with pytest.raises(ValueError):
        recorder(env, env_index=1)
    env.reset(seed=0)
    rec = recorder(env)
    _, r, _, _, info = env.step([STAY]*3)
    with pytest.raises(ValueError):
        rec.append([STAY]*2, r, info)
    rec.append([STAY]*3, r, info)
    assert not rec.build()['header']['complete']
    with pytest.raises(ValueError):
        rec.build(complete=True)
    bad = rec.build()
    bad['frames'][1]['state']['partition']['heat'] = [8, 0]
    with pytest.raises(ValueError):
        replay.validate_replay(bad)


def test_all_existing_historical_traces(tmp_path):
    traces = Path(__file__).resolve().parents[1] / 'unity/OvercookedViewer/Assets/StreamingAssets/Samples'
    paths = [p for p in sorted(traces.glob('partition_v3_*.json'))
             if not json.loads(p.read_text())['header']['complete']]
    assert len(paths) == 6
    for path in paths:
        saved = json.loads(path.read_text())
        delivery = [e for s in saved['segments'] for e in s['events'] if e['kind'] == 'meal_delivery'][-1]
        source = dict(layout=saved['header']['partition_config']['layout'],seed=saved['header']['seed'],
                      actions=[s['actions'] for s in saved['segments']],order_id=delivery['order_id'],
                      recipe=('tomato_egg','potato','potato_tomato_beef')[delivery['recipe']])
        trace = tmp_path / (path.stem + '.json')
        trace.write_text(json.dumps(source))
        data = replay.load_replay(convert_file(trace, tmp_path)[0])
        assert len(data['frames']) == len(source['actions']) + 1
        assert data['header']['complete'] is False
        assert data['diagnostics']
        assert any(e.get('order_id') == source['order_id'] and e['kind'] == 'meal_delivery'
                   for s in data['segments'] for e in s['events'])


@pytest.mark.parametrize('actions', [[[0, 0]], [[True, 4, 4]], [[0.5, 4, 4]], [[6, 4, 4]]])
def test_trace_rejects_invalid_actions(actions):
    with pytest.raises(ValueError):
        convert_data(dict(layout='sync', seed=0, actions=actions))


def test_trace_config_is_preserved_and_excess_actions_rejected():
    trace = dict(layout='counter', seed=0, actions=[[4]*3]*2,
                 config={'horizon': 2, 'deadlines': [4, 5, 6], 'delivery_reward': 9})
    data = convert_data(trace)[0]
    assert data['header']['complete']
    assert data['header']['partition_config']['deadlines'] == [4, 5, 6]
    trace['actions'].append([4]*3)
    with pytest.raises(ValueError):
        convert_data(trace)


def test_cli_samples_validate_and_config_override(tmp_path):
    command = [sys.executable, '-m', 'better_overcooked_replay']
    subprocess.run(command + ['partition-samples', '--out', str(tmp_path), '--horizon', '3'], check=True)
    samples = sorted(tmp_path.glob('*.ocrep.json'))
    assert len(samples) == 2
    for path in samples:
        result = subprocess.run(command + ['validate', str(path)], capture_output=True, text=True, check=True)
        assert json.loads(result.stdout)['valid'] is True
    trace, config = tmp_path/'trace.json', tmp_path/'config.json'
    trace.write_text(json.dumps(dict(layout='sync', seed=0, actions=[[4]*3]*2)))
    config.write_text(json.dumps(dict(horizon=2)))
    subprocess.run(command + ['convert', str(trace), '--out', str(tmp_path),
                             '--partition-config', str(config)], check=True)
    assert replay.load_replay(tmp_path/'trace-000.ocrep.json')['header']['complete']


@pytest.mark.parametrize('field', ['reward', 'order_ids', 'delivered'])
def test_invalid_append_does_not_consume_the_step(field):
    env = PartitionKitchen()
    env.reset(seed=0)
    rec = recorder(env)
    _, reward, _, _, info = env.step([STAY]*3)
    bad = copy.deepcopy(info)
    if field == 'order_ids': bad['order_ids'] = np.array([99, 98, 97])
    if field == 'delivered': bad['delivered'] = np.array([True])
    with pytest.raises(ValueError):
        rec.append([STAY]*3, 99 if field == 'reward' else reward, bad)
    assert len(rec.frames) == 1 and not rec.segments
    rec.append([STAY]*3, reward, info)
    assert len(rec.build()['frames']) == 2


@pytest.mark.parametrize('rewards', [(20, 10, 50), (2.5, 3.5, 7.5)])
def test_per_recipe_rewards_are_recorded(rewards):
    env = PartitionKitchen(PartitionConfig(horizon=1, recipe_rewards=rewards))
    env.reset(seed=0)
    s = env.state
    s['pos'][:] = [cell(1, 3), cell(11, 3), cell(11, 5)]
    s['facing'][:] = [WEST, EAST, NORTH]
    s['held'][:] = [FIRST_MEAL+1, 0, 0]
    s['recipe'][:] = [1, 2, 0]
    rec = recorder(env)
    advance(env, rec, [INTERACT, STAY, STAY])
    data = rec.build()
    assert data['header']['partition_config']['recipe_rewards'] == list(rewards)
    assert data['frames'][1]['score'] == rewards[1]


@pytest.mark.parametrize('layout', ['sync', 'counter'])
def test_torch_lane_three_and_snapshot_isolation(layout):
    torch = pytest.importorskip('torch')
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    config = PartitionConfig(layout=layout, horizon=12)
    env = TorchPartitionKitchen(config, num_envs=3, device=device)
    env.reset(seed=9)
    native = PartitionKitchen(config)
    native.reset(seed=10)
    batch_rec, cpu_rec = recorder(env, env_index=1), recorder(native)
    rng = np.random.default_rng(1)
    for _ in range(12):
        a = rng.integers(0, 6, (3, 3))
        advance(env, batch_rec, torch.as_tensor(a, device=device))
        advance(native, cpu_rec, a[1])
    first, second = batch_rec.build(), cpu_rec.build()
    assert first['frames'] == second['frames']
    assert first['segments'] == second['segments']
    env.reset(mask=torch.tensor([False, True, False], device=device))
    assert batch_rec.build() == first
