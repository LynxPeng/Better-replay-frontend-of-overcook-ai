"""Versioned, JSON/JsonUtility-compatible snapshots. Python remains game authority."""
from __future__ import annotations

import copy
import json
import math
from pathlib import Path

from overcooked_ai_py.mdp.actions import Action
from .recipes import scoped_recipes

SCHEMA_VERSION = 1
SOURCE_VERSION = '739950a079cdaed5a44fcc662efc40244c205d06'
OBJECT_NAMES = {'onion', 'tomato', 'dish', 'soup'}


def json_value(value):
    """Convert numpy values and tuples without silently stringifying unknown objects."""
    if isinstance(value, dict):
        return {str(k): json_value(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_value(v) for v in value]
    if hasattr(value, 'tolist'):
        return json_value(value.tolist())
    if value is None or isinstance(value, (str, bool, int, float)):
        return value
    raise ValueError(f'Cannot serialize {type(value).__name__}')


def action_index(action):
    if isinstance(action, str):
        if action.lower() != Action.INTERACT:
            raise ValueError(f'Unknown action: {action!r}')
        return 5
    if isinstance(action, (int,)) or getattr(action, 'dtype', None) is not None and not hasattr(action, '__len__'):
        idx = int(action)
        if idx not in range(6) or isinstance(action, bool):
            raise ValueError(f'Invalid action index: {action}')
        return idx
    try:
        return Action.ACTION_TO_INDEX[tuple(action)]
    except (TypeError, KeyError) as exc:
        raise ValueError(f'Invalid action: {action!r}') from exc


def normalize_state(state):
    raw = copy.deepcopy(state.to_dict() if hasattr(state, 'to_dict') else state)
    if not isinstance(raw, dict) or 'players' not in raw:
        raise ValueError('State must contain players; RGB-only records cannot reconstruct 3D')

    def obj(value):
        if value is None:
            return None
        value = copy.deepcopy(value)
        if value.get('name') not in OBJECT_NAMES:
            raise ValueError(f"Unknown object: {value.get('name')}")
        value['position'] = list(value['position'])
        if value['name'] == 'soup':
            if 'state' in value:
                ingredient, count, tick = value.pop('state')
                if ingredient not in ('onion', 'tomato') or not 0 <= int(count) <= 3:
                    raise ValueError('Invalid legacy soup ingredients')
                value['_ingredients'] = [dict(name=ingredient, position=value['position']) for _ in range(int(count))]
                value['cooking_tick'] = -1 if tick == 0 else tick
                value['cook_time'] = 20
            value.setdefault('_ingredients', value.pop('ingredients', []))
            value['_ingredients'] = [dict(name=i, position=value['position']) if isinstance(i, str) else i
                                     for i in value['_ingredients']]
            tick = value.get('cooking_tick', value.get('_cooking_tick', -1))
            duration = value.get('cook_time', 20)
            value.update(cooking_tick=tick, cook_time=duration,
                         is_idle=value.get('is_idle', tick < 0),
                         is_ready=value.get('is_ready', duration > 0 and tick >= duration),
                         is_cooking=value.get('is_cooking', 0 <= tick < duration))
        return value

    for player in raw['players']:
        player['position'] = list(player['position'])
        player['orientation'] = list(player['orientation'])
        player['held_object'] = obj(player.get('held_object'))
    objects = raw.get('objects', [])
    raw['objects'] = [obj(o) for o in (objects.values() if isinstance(objects, dict) else objects)]
    raw.setdefault('timestep', 0)
    raw.setdefault('all_orders', [])
    raw.setdefault('bonus_orders', [])
    return json_value(raw)


def make_header(terrain, layout_name='custom', metadata=None, mdp=None):
    meta = copy.deepcopy(metadata or {})
    header = dict(schema_version=1, source_version=SOURCE_VERSION,
                  terrain=[''.join(' ' if str(c).isdigit() else c for c in row) for row in terrain], layout_name=layout_name,
                  tick_rate=6.0, horizon=400, complete=False, dynamics='unknown',
                  timebase='synthetic', players=[dict(id=f'player_{i}', is_human=False) for i in range(2)],
                  mdp_params_json='{}')
    if mdp is not None:
        params = {**mdp.mdp_params, **mdp.recipe_config, 'old_dynamics': mdp.old_dynamics}
        params.pop('all_orders', None)
        header['mdp_params_json'] = json.dumps(json_value(params), ensure_ascii=False)
        header['dynamics'] = 'old' if mdp.old_dynamics else 'new'
    header.update(json_value(meta))
    return header


def transition_events(before, after, actions, info):
    events = []
    for kind, flags in (info.get('event_infos') or {}).items():
        for idx, active in enumerate(flags):
            if active:
                p = before['players'][idx]
                events.append(dict(kind=kind, agent_index=idx,
                                   position=[p['position'][k] + p['orientation'][k] for k in range(2)]))
    if not events:
        for idx, (p, q) in enumerate(zip(before['players'], after['players'])):
            old, new = p.get('held_object'), q.get('held_object')
            if old != new and (old or new) and actions[idx] == 5:
                kind = f"{(new or old)['name']}_{'pickup' if new else 'drop'}"
                events.append(dict(kind=kind, agent_index=idx,
                                   position=[p['position'][k]+p['orientation'][k] for k in range(2)]))
    old_objects = {tuple(o['position']): o for o in before['objects']}
    for obj in after['objects']:
        if obj['name'] != 'soup':
            continue
        prev = old_objects.get(tuple(obj['position']), {})
        if obj.get('is_cooking') and prev.get('is_idle'):
            events.append(dict(kind='start_cooking', agent_index=-1, position=obj['position']))
        if obj.get('is_ready') and not prev.get('is_ready'):
            events.append(dict(kind='soup_ready', agent_index=-1, position=obj['position']))
    return events


def validate_replay(data):
    def fail(message):
        raise ValueError('Invalid replay: ' + message)

    if not isinstance(data, dict) or data.get('header', {}).get('schema_version') != 1:
        fail('unsupported schema_version')
    header = data['header']
    terrain = header.get('terrain', [])
    if not terrain or not terrain[0] or any(len(r) != len(terrain[0]) for r in terrain):
        fail('terrain must be rectangular string rows')
    if any(c not in ' XOTDPS' for row in terrain for c in row):
        fail('unknown terrain')
    frames, segments = data.get('frames', []), data.get('segments', [])
    if not frames or len(frames) != len(segments) + 1:
        fail('expected T+1 frames and T intervals')
    if len(header.get('players', [])) != 2:
        fail('two players required')

    def position(pos, label, walkable=False):
        if not isinstance(pos, list) or len(pos) != 2 or any(type(n) is not int for n in pos):
            fail(label + ' position must be integer [x,y]')
        x, y = pos
        if not (0 <= y < len(terrain) and 0 <= x < len(terrain[0])):
            fail(label + ' position outside terrain')
        if walkable and terrain[y][x] != ' ':
            fail(label + ' must be on floor')

    previous_time = -1
    for i, frame in enumerate(frames):
        t = frame.get('time')
        if not isinstance(t, (int, float)) or not math.isfinite(t) or t < 0 or t < previous_time:
            fail(f'frame {i} time must be finite and nondecreasing')
        previous_time = t
        score = frame.get('score')
        if not isinstance(score, (int, float)) or not math.isfinite(score):
            fail(f'frame {i} score must be finite')
        state = frame['state']
        if len(state.get('players', [])) != 2:
            fail(f'frame {i} expected two players')
        occupied = []
        for player in state['players']:
            position(player['position'], f'frame {i} player', True)
            occupied.append(player['position'])
            if tuple(player['orientation']) not in ((0, -1), (0, 1), (1, 0), (-1, 0)):
                fail(f'frame {i} invalid orientation')
        if occupied[0] == occupied[1]:
            fail(f'frame {i} players overlap')
        objects = state.get('objects', []) + [p['held_object'] for p in state['players'] if p.get('held_object')]
        for obj in objects:
            if obj['name'] not in OBJECT_NAMES:
                fail(f'frame {i} unknown object')
            position(obj['position'], f'frame {i} object')
            if obj['name'] == 'soup':
                if any(x.get('name') not in ('onion', 'tomato') for x in obj.get('_ingredients', [])):
                    fail(f'frame {i} unknown ingredient')
    for i, segment in enumerate(segments):
        if segment.get('kind') == 'gap':
            if segment.get('actions') is not None or segment.get('reward_known'):
                fail(f'gap {i} cannot claim actions or verified reward')
            continue
        if segment.get('kind') != 'step':
            fail(f'interval {i} unknown kind')
        actions = segment.get('actions')
        if not isinstance(actions, list) or len(actions) != 2 or any(type(a) is not int or a not in range(6) for a in actions):
            fail(f'step {i} invalid joint action')
        reward = segment.get('sparse_reward')
        if not isinstance(reward, (int, float)) or not math.isfinite(reward):
            fail(f'step {i} reward must be finite')
        if frames[i+1]['state']['timestep'] != frames[i]['state']['timestep'] + 1:
            fail(f'step {i} skips a timestep; use gap')
    return data


def load_replay(path):
    return validate_replay(json.loads(Path(path).read_text(encoding='utf-8-sig')))


def write_replay(data, path):
    validate_replay(data)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(json_value(data), ensure_ascii=False, allow_nan=False, separators=(',', ':')), encoding='utf-8')
    temporary.replace(path)
    return path


class ReplayRecorder:
    @scoped_recipes
    def __init__(self, mdp, initial_state, metadata=None):
        self._recipe_config = copy.deepcopy(mdp.recipe_config)
        self.header = make_header(mdp.terrain_mtx, mdp.layout_name, metadata, mdp)
        self.frames = [dict(time=0.0, state=normalize_state(initial_state), score=0.0)]
        self.segments = []

    @scoped_recipes
    def append(self, joint_action, next_state, reward, info, time=None):
        indices = [action_index(a) for a in joint_action]
        state = normalize_state(next_state)
        events = transition_events(self.frames[-1]['state'], state, indices, info)
        self.segments.append(dict(kind='step', actions=indices, sparse_reward=float(reward), reward_known=True,
                                  shaped_reward_by_agent=json_value(info.get('shaped_r_by_agent', [0, 0])), events=events))
        t = len(self.segments) / self.header['tick_rate'] if time is None else time
        self.frames.append(dict(time=t, state=state, score=self.frames[-1]['score'] + float(reward)))

    def build(self, complete=True):
        data = dict(header={**self.header, 'complete': complete}, frames=copy.deepcopy(self.frames),
                    segments=copy.deepcopy(self.segments), diagnostics=[])
        return validate_replay(data)

    def write(self, path, complete=True):
        return write_replay(self.build(complete), path)
