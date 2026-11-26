"""Read-only v3 snapshot adapter. Only the installed HuCooked core runs rules."""
from __future__ import annotations

import copy
import hashlib
import math
from dataclasses import asdict
from importlib.metadata import version
from pathlib import Path

ITEM_NAMES = ('', 'tomato', 'potato', 'egg', 'beef', 'cut_tomato', 'cut_potato',
              'meal_tomato_egg', 'meal_potato', 'meal_potato_tomato_beef')
RECIPE_NAMES = ('tomato_egg', 'potato', 'potato_tomato_beef')
RECIPE_INGREDIENTS = (('tomato', 'egg'), ('potato',), ('potato', 'tomato', 'beef'))
DIRECTIONS = ((0, -1), (0, 1), (1, 0), (-1, 0))
WIDTHS = dict(tick=1, pos=3, facing=3, held=3, board=4, cuts=4, mix=2, locked=2,
              heat=2, counter=5, deposited=5, ids=3, recipe=3, issued=3, due=3, next_id=1)


def core():
    try:
        from overcooked_ai_py.cooperative.partition import model, PartitionKitchen, TorchPartitionKitchen
    except ImportError as exc:
        raise ValueError('v3 requires HuCooked: use hucooked/.conda/python.exe and install '
                         'replay_frontend with pip install -e ./replay_frontend --no-deps; '
                         'the pinned upstream environment only supports legacy recording') from exc
    return model, PartitionKitchen, TorchPartitionKitchen


def plain(value):
    if hasattr(value, 'detach'):
        return value.detach().cpu().tolist()
    if hasattr(value, 'tolist'):
        return value.tolist()
    if isinstance(value, dict):
        return {k: plain(v) for k, v in value.items()}
    if isinstance(value, (tuple, list)):
        return [plain(v) for v in value]
    return copy.deepcopy(value)


def xy(cell):
    return [cell % 13, cell // 13]


def terrain(layout):
    # This is a facility description, not a transition or action simulator.
    rows = [list(row) for row in ('#############', '#     #     #', 'T C K H K C E',
                                 'S     #     S', 'P   C H C   B', '#     #     #', '#############')]
    for j, c in enumerate((19, 32, 45, 58, 71)):
        rows[c // 13][c % 13] = 'X' if layout == 'counter' else ('H' if j in (1, 3) else '#')
    return [''.join(row) for row in rows]


def snapshot(raw):
    def item(code, position):
        return dict(name=ITEM_NAMES[code], position=position) if code else None
    players = [dict(position=xy(p), orientation=list(DIRECTIONS[f]), held_object=item(h, xy(p)))
               for p, f, h in zip(raw['pos'], raw['facing'], raw['held'])]
    boards = (28, 56, 36, 60)
    objects = []
    for code, cut, c in zip(raw['board'], raw['cuts'], boards):
        if code:
            objects.append(item(code + 4 if cut == 2 else code, xy(c)))
    for code, c in zip(raw['counter'], (19, 32, 45, 58, 71)):
        if code:
            objects.append(item(code, xy(c)))
    orders = [dict(id=oid, recipe=r, ingredients=list(RECIPE_INGREDIENTS[r]), issued=start,
                   due=due, side=(1, 0, 0)[r])
              for oid, r, start, due in zip(raw['ids'], raw['recipe'], raw['issued'], raw['due'])]
    return dict(timestep=raw['tick'][0], players=players, objects=objects, all_orders=orders,
                partition=copy.deepcopy(raw))


def transition(before, after, actions, info, rows):
    """Explain confirmed changes; never infer success from pressing INTERACT alone."""
    old, new = before['partition'], after['partition']
    tick = after['timestep']
    events, handoffs = [], []
    targets = [[p['position'][k] + p['orientation'][k] for k in range(2)] for p in before['players']]
    def emit(kind, agent, pos, **extra):
        events.append(dict(kind=kind, agent_index=agent, position=pos, **extra))
    used = set()
    for chef, (a, p, q) in enumerate(zip(actions, before['players'], after['players'])):
        if a != 5:
            continue
        target = targets[chef]
        tile = rows[target[1]][target[0]]
        first, last = old['held'][chef], new['held'][chef]
        if first == last:
            continue
        if first and not last:
            kind = {'X': 'counter_put', 'H': 'sync_handoff_give', 'C': 'board_place',
                    'K': 'pot_deposit'}.get(tile, 'item_place')
            if tile == 'S' and first >= 7:
                kind = 'meal_delivery'
                slot = next((j for j in range(3) if info['delivered'][j] and j not in used
                             and info['recipe'][j] == first - 7), None)
                if slot is None:
                    raise ValueError('Delivery state disagrees with v3 step info')
                used.add(slot)
                emit(kind, chef, target, order_id=info['order_ids'][slot], recipe=first-7)
            else:
                emit(kind, chef, target)
        elif not first and last:
            kind = {'X': 'counter_pickup', 'H': 'sync_handoff_receive', 'C': 'board_pickup',
                    'K': 'pot_pickup'}.get(tile, 'item_pickup')
            extra = {}
            if tile == 'X':
                extra['wait_steps'] = tick - old['deposited'][target[1]-1]
            emit(kind, chef, target, **extra)
    for field, cells, kind in (('cuts', (28, 56, 36, 60), 'chop'), ('heat', (30, 34), 'tend')):
        for j, c in enumerate(cells):
            if new[field][j] > old[field][j]:
                for chef in range(3):
                    if actions[chef] == 5 and old['held'][chef] == 0 and targets[chef] == xy(c):
                        emit(kind, chef, xy(c))
            if field == 'heat' and new[field][j] == 4 and old[field][j] != 4:
                emit('pot_ready', -1, xy(c))
    for j in range(3):
        if info['expired'][j]:
            r = info['recipe'][j]
            emit('order_expired', -1, [12 if r == 0 else 0, 3],
                 order_id=info['order_ids'][j], recipe=r)
    for y in (2, 4):
        if rows[y][6] != 'H':
            continue
        station = [6, y]
        facing = [i for i, t in enumerate(targets) if t == station]
        attempting = [i for i in facing if actions[i] == 5]
        if not attempting:
            continue
        givers = [i for i in attempting if old['held'][i]]
        receivers = [i for i in attempting if not old['held'][i]]
        success = (len(givers) == len(receivers) == 1 and new['held'][givers[0]] == 0
                   and new['held'][receivers[0]] == old['held'][givers[0]])
        reason = ('success' if success else 'not_simultaneous' if len(attempting) < 2 else
                  'both_empty' if not givers else 'both_holding' if not receivers else 'unconfirmed')
        handoffs.append(dict(station=station, participants=facing, attempting_agents=attempting,
                             success=bool(success), reason=reason,
                             sender=givers[0] if success else -1, receiver=receivers[0] if success else -1))
    return events, handoffs


class PartitionReplayRecorder:
    """One episode / one lane. Append immediately after step and before any reset.

    Single environments take their unbatched step results; Torch environments take
    the ORIGINAL batched actions, rewards and info, not manually sliced values.
    Construct a NEW recorder after every reset (including masked lane resets).
    """
    def __init__(self, env, *, env_index=0, metadata=None):
        from .protocol import json_value
        m, native, batch = core()
        if not isinstance(env, (native, batch)):
            raise ValueError('Expected PartitionKitchen or TorchPartitionKitchen')
        self.batched = isinstance(env, batch)
        if type(env_index) is not int or not 0 <= env_index < (env.num_envs if self.batched else 1):
            raise ValueError('Invalid env_index')
        self.env, self.env_index = env, env_index
        config = json_value(asdict(env.config))
        meta = json_value(metadata or {})
        tick_rate = meta.pop('tick_rate', 6.0)
        if not isinstance(tick_rate, (int, float)) or not math.isfinite(tick_rate) or tick_rate <= 0:
            raise ValueError('tick_rate must be positive and finite')
        fingerprint = hashlib.sha256(Path(m.__file__).read_bytes() +
                                     Path(m.__file__).with_name('engine.py').read_bytes()).hexdigest()
        self.header = dict(meta, schema_version=2, environment='partition_v3',
                           source_version=version('overcooked_ai'), source_fingerprint=fingerprint,
                           terrain=terrain(env.config.layout), layout_name='partition_v3_' + env.config.layout,
                           partition_config=config, horizon=env.config.horizon, tick_rate=tick_rate,
                           timebase='synthetic', complete=False,
                           players=[dict(id=f'chef_{i}', is_human=False) for i in range(3)])
        self.frames = [dict(time=0.0, state=snapshot(self._state()), score=0.0)]
        self.segments, self.diagnostics = [], []

    def _state(self):
        return {k: plain(v[self.env_index] if self.batched else v) for k, v in self.env.state.items()}

    def append(self, joint_action, reward, info, time=None):
        # Copy before slicing so no Tensor/NumPy view survives the call.
        actions, reward, details = plain(joint_action), plain(reward), plain(info)
        if self.batched:
            n = self.env.num_envs
            if not isinstance(actions, list) or len(actions) != n or not isinstance(reward, list) or len(reward) != n:
                raise ValueError('Expected original batched actions and rewards')
            if any(not isinstance(v, list) or len(v) != n for v in details.values()):
                raise ValueError('Expected original batched info')
            actions, reward = actions[self.env_index], reward[self.env_index]
            details = {k: v[self.env_index] for k, v in details.items()}
        if not isinstance(actions, list) or len(actions) != 3 or any(type(a) is not int or a not in range(6) for a in actions):
            raise ValueError('Expected three integer actions from 0 to 5')
        before = self.frames[-1]
        state = snapshot(self._state())
        if before['state']['timestep'] >= self.header['horizon'] or state['timestep'] != before['state']['timestep'] + 1:
            raise ValueError('Nonconsecutive tick: append once after each step; create a new recorder after reset')
        required = ('recipe', 'order_ids', 'age', 'delivered', 'expired', 'sync_attempts',
                    'sync_successes', 'wait_steps', 'pickups')
        if any(k not in details for k in required):
            raise ValueError('Missing v3 step info')
        validate_step_info(details, before['state'], state, reward, self.header['partition_config'])
        events, handoffs = transition(before['state'], state, actions, details, self.header['terrain'])
        segment = dict(kind='step', actions=actions, sparse_reward=float(reward), reward_known=True,
                       shaped_reward_by_agent=[0, 0, 0], events=events, sync_handoffs=handoffs,
                       partition_info=details)
        t = len(self.frames)/self.header['tick_rate'] if time is None else time
        if not isinstance(t, (int, float)) or not math.isfinite(t) or t <= before['time']:
            raise ValueError('Frame time must be finite and increasing')
        if not math.isfinite(float(reward)):
            raise ValueError('Reward must be finite')
        frame = dict(time=t, state=state, score=before['score'] + float(reward))
        # Validate only this interval, before committing it. This is O(1) per step
        # and a rejected call can be corrected without losing the pending step.
        validate_partition_replay(dict(header={**self.header, 'complete': False},
                                      frames=[before, frame], segments=[segment]))
        self.segments.append(segment)
        self.frames.append(frame)

    def build(self, complete=None):
        from .protocol import validate_replay
        finished = self.frames[-1]['state']['timestep'] >= self.header['horizon']
        if complete is True and not finished:
            raise ValueError('Cannot mark an unfinished v3 episode complete')
        return validate_replay(dict(header={**self.header, 'complete': finished if complete is None else complete},
                                    frames=copy.deepcopy(self.frames), segments=copy.deepcopy(self.segments),
                                    diagnostics=list(self.diagnostics)))

    def write(self, path, complete=None):
        from .protocol import write_replay
        return write_replay(self.build(complete), path)


def recipe_rewards(cfg):
    values=cfg.get('recipe_rewards')
    if values is None:
        values=[cfg.get('delivery_reward')]*3
    if (not isinstance(values, (list, tuple)) or len(values)!=3 or
            any(type(v) not in (int,float) or not math.isfinite(v) or v<0 for v in values)):
        raise ValueError('Invalid v3 replay: expected three finite nonnegative recipe rewards or a delivery_reward')
    return values


def validate_step_info(info, before, after, reward, cfg):
    def require(condition, message):
        if not condition:
            raise ValueError('Invalid v3 step info: ' + message)
    require(isinstance(info, dict), 'dictionary required')
    for k in ('recipe', 'order_ids', 'age', 'delivered', 'expired'):
        v = info.get(k)
        require(isinstance(v, list) and len(v) == 3, k + ' shape')
        require(all(type(x) is (bool if k in ('delivered', 'expired') else int) for x in v), k + ' type')
    for k in ('sync_attempts', 'sync_successes', 'wait_steps', 'pickups'):
        require(type(info.get(k)) is int and info[k] >= 0, k)
    require(info['sync_successes'] <= info['sync_attempts'], 'sync count')
    require(not any(a and b for a, b in zip(info['delivered'], info['expired'])), 'delivery/expiry overlap')
    raw = before['partition']
    require(info['order_ids'] == raw['ids'] and info['recipe'] == raw['recipe'], 'settled order IDs/recipes')
    require(info['age'] == [after['timestep'] - t for t in raw['issued']], 'order age')
    require(type(reward) in (float, int) and math.isfinite(reward), 'finite reward')
    values=recipe_rewards(cfg)
    require(math.isclose(reward, sum(values[r] for r,done in zip(info['recipe'],info['delivered']) if done), abs_tol=1e-8), 'delivery reward')


def validate_partition_replay(data):
    """Validate schema 2 without needing the optional v3 engine or PyTorch."""
    def require(condition, message):
        if not condition:
            raise ValueError('Invalid v3 replay: ' + message)
    h = data['header']
    require(h.get('environment') == 'partition_v3', 'unknown environment')
    cfg = h.get('partition_config', {})
    require(cfg.get('layout') in ('sync', 'counter'), 'layout')
    horizon = cfg.get('horizon')
    require(type(horizon) is int and horizon > 0 and h.get('horizon') == horizon, 'horizon')
    require(len(cfg.get('deadlines', [])) == 3 and all(type(x) is int and x > 0 for x in cfg['deadlines']), 'deadlines')
    recipe_rewards(cfg)
    require(isinstance(h.get('tick_rate'), (float, int)) and math.isfinite(h['tick_rate']) and h['tick_rate'] > 0, 'tick rate')
    rows = h.get('terrain', [])
    require(len(rows) == 7 and all(isinstance(r, str) and len(r) == 13 for r in rows), '13 by 7 terrain')
    require(all(c in ' #XTPEBCKSH' for r in rows for c in r), 'terrain type')
    require(rows == terrain(cfg['layout']), 'facility positions')
    require(len(h.get('players', [])) == 3, 'three players required')
    frames, segments = data.get('frames', []), data.get('segments', [])
    require(len(frames) == len(segments)+1 and len(frames) > 0, 'T+1 frames required')
    ranges = dict(pos=(0, 90), facing=(0, 3), held=(0, 9), board=(0, 2), cuts=(0, 2),
                  mix=(0, 15), locked=(-1, 2), heat=(0, 4), counter=(0, 9), recipe=(0, 2))
    prev_time = -1
    for f in frames:
        require(isinstance(f.get('time'), (int, float)) and math.isfinite(f['time']) and f['time'] >= 0 and f['time'] > prev_time, 'frame time')
        prev_time = f['time']
        require(isinstance(f.get('score'), (int, float)) and math.isfinite(f['score']), 'score')
        s = f.get('state', {})
        raw = s.get('partition', {})
        for k, n in WIDTHS.items():
            v = raw.get(k)
            require(isinstance(v, list) and len(v) == n and all(type(x) is int for x in v), 'state field ' + k)
            lo, hi = ranges.get(k, (0, 2**63-1))
            require(all(lo <= x <= hi for x in v), 'state range ' + k)
        require(0 <= raw['tick'][0] <= horizon, 'tick')
        require(len(set(raw['pos'])) == 3 and all(rows[p//13][p%13] == ' ' for p in raw['pos']), 'player positions')
        require(s == snapshot(raw), 'snapshot projection does not match v3 state')
    require(not h.get('complete') or frames[-1]['state']['timestep'] == horizon, 'false completion')
    for i, step in enumerate(segments):
        require(step.get('kind') == 'step', 'v3 intervals must be recorded steps')
        a = step.get('actions')
        require(isinstance(a, list) and len(a) == 3 and all(type(x) is int and 0 <= x < 6 for x in a), 'actions')
        reward = step.get('sparse_reward')
        require(isinstance(reward, (int, float)) and math.isfinite(reward), 'reward')
        require(frames[i+1]['state']['timestep'] == frames[i]['state']['timestep']+1, 'tick sequence')
        require(math.isclose(frames[i+1]['score']-frames[i]['score'], reward, abs_tol=1e-8), 'score accumulation')
        info = step.get('partition_info', {})
        validate_step_info(info, frames[i]['state'], frames[i+1]['state'], reward, cfg)
        events, handoffs = transition(frames[i]['state'], frames[i+1]['state'], a, info, rows)
        require(step.get('events') == events and step.get('sync_handoffs') == handoffs, 'confirmed events')
    return data


def convert_partition_trace(trace, *, source_name='imported', config=None):
    m, native, _ = core()
    if type(trace.get('seed')) is not int:
        raise ValueError('A v3 action trace requires an integer seed')
    params = dict(trace.get('config') or {})
    params.update(config or {})
    if params.get('layout', trace['layout']) != trace['layout']:
        raise ValueError('Trace layout and configuration disagree')
    params['layout'] = trace['layout']
    try:
        cfg = m.PartitionConfig(**params)
    except TypeError as exc:
        raise ValueError('Unknown v3 configuration field') from exc
    actions = trace.get('actions')
    if not isinstance(actions, list) or len(actions) > cfg.horizon:
        raise ValueError('Trace actions must be a list no longer than the configured horizon')
    if any(not isinstance(a, list) or len(a) != 3 or any(type(v) is not int or v not in range(6) for v in a) for a in actions):
        raise ValueError('Expected three integer actions from 0 to 5 per trace step')
    env = native(cfg)
    env.reset(seed=trace['seed'])
    rec = PartitionReplayRecorder(env, metadata=dict(source=source_name, seed=trace['seed']))
    if not all(k in params for k in ('horizon', 'deadlines', 'delivery_reward')):
        rec.diagnostics.append('Missing trace configuration uses PartitionConfig defaults; replayed with the recorded source fingerprint')
    for joint in actions:
        _, reward, _, _, info = env.step(joint)
        rec.append(joint, reward, info)
    if 'recipe' in trace and 'order_id' in trace:
        if trace['recipe'] not in RECIPE_NAMES or not any(
                e['kind'] == 'meal_delivery' and e['order_id'] == trace['order_id'] and
                RECIPE_NAMES[e['recipe']] == trace['recipe'] for s in rec.segments for e in s['events']):
            raise ValueError('Historical trace did not reproduce its target delivery; check core version/configuration')
    return rec.build()
