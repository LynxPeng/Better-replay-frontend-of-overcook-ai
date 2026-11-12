"""Historical state import without human_aware_rl/TensorFlow dependencies."""
from __future__ import annotations

import ast
import copy
import json
import pickle
from pathlib import Path

from .protocol import action_index, make_header, normalize_state, step_segment, validate_replay, write_replay
from .recipes import scoped_recipes


def decoded(value):
    if not isinstance(value, str):
        return value
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        try:
            return ast.literal_eval(value)
        except (ValueError, SyntaxError) as exc:
            raise ValueError('Invalid serialized state/action/layout') from exc


@scoped_recipes
def _restore_last(params, states, actions, rewards, dynamics):
    """Reconstruct only after *every* available transition agrees with explicit rules."""
    from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld, OvercookedState
    from overcooked_ai_py.mdp.actions import Action
    params = copy.deepcopy(params)
    params['old_dynamics'] = dynamics == 'old'
    if 'terrain' not in params:
        mdp = OvercookedGridworld.from_layout_name(**params)
    else:
        mdp = OvercookedGridworld(**params)
    for i, action in enumerate(actions):
        current = OvercookedState.from_dict(states[i])
        joint = tuple(Action.INDEX_TO_ACTION[action_index(a)] for a in action)
        successor, info = mdp.get_state_transition(current, joint)
        if sum(info['sparse_reward_by_agent']) != rewards[i]:
            raise ValueError(f'reward mismatch at transition {i}')
        if i + 1 < len(states):
            if normalize_state(successor) != states[i+1]:
                raise ValueError(f'state mismatch at transition {i}')
        else:
            return normalize_state(successor)


def _episode(states, actions, rewards, params, metadata, times=None, scores=None, dynamics=None, infos=None):
    states = [normalize_state(s) for s in states]
    actions, rewards = list(actions), list(rewards)
    diagnostics = []
    # include_final_state adds a terminal sentinel, not another environment action.
    if actions and (actions[-1] is None or all(x is None for x in actions[-1])):
        actions.pop()
        rewards = rewards[:len(actions)]
    if not states:
        raise ValueError('Empty episode')
    if len(actions) < len(states)-1 or len(rewards) < min(len(actions), len(states)-1):
        raise ValueError('Source states/actions/rewards have incompatible lengths')
    complete = len(states) == len(actions)+1
    if not complete and len(states) == len(actions) and dynamics:
        try:
            final = _restore_last(params, states, actions, rewards, dynamics)
            states.append(final)
            complete = True
            if times is not None:
                times = list(times) + [times[-1] + 1/6]
            if scores is not None:
                scores = list(scores) + [scores[-1] + rewards[-1]]
            diagnostics.append('Restored terminal state using validated explicit dynamics')
        except (ValueError, AssertionError, KeyError, TypeError) as exc:
            diagnostics.append(f'Terminal restoration failed: {exc}')
    if not complete:
        diagnostics.append('Unmatched trailing source action(s) preserved: ' + json.dumps(actions[len(states)-1:], default=list))
    terrain = params.get('terrain')
    if terrain is None:
        from overcooked_ai_py.utils import read_layout_dict
        grid = read_layout_dict(params['layout_name'])['grid']
        terrain = [''.join(' ' if c.isdigit() else c for c in row.strip()) for row in grid.split('\n')]
    header = make_header(terrain, params.get('layout_name', 'imported'), metadata,
                         player_count=len(states[0]['players']))
    header.update(complete=complete, dynamics=dynamics or 'unknown', mdp_params_json=json.dumps(params, default=list))
    if times is not None:
        header['timebase'] = 'source'
        origin = float(times[0])
        times = [float(t)-origin for t in times]
    else:
        times = [i/6 for i in range(len(states))]
    frames, segments, score = [], [], 0.0
    for i, state in enumerate(states):
        if scores is not None:
            score = float(scores[i])
        elif i:
            score += float(rewards[i-1])
        frames.append(dict(time=times[i], state=state, score=score))
        if i == len(states)-1:
            break
        if states[i+1]['timestep'] != state['timestep']+1:
            segments.append(dict(kind='gap', actions=None, reward_known=False,
                                 sparse_reward=0, shaped_reward_by_agent=[], events=[]))
            diagnostics.append(f'Gap after source timestep {state["timestep"]}; action/reward unknown')
        else:
            indices = [action_index(a) for a in decoded(actions[i])]
            info = infos[i] if infos is not None and i < len(infos) else None
            segments.append(step_segment(state, states[i+1], indices, rewards[i], info or {}))
    return validate_replay(dict(header=header, frames=frames, segments=segments, diagnostics=diagnostics))


def convert_data(data, dynamics=None, source_name='imported'):
    if dynamics not in (None, 'old', 'new'):
        raise ValueError('dynamics must be old or new')
    if isinstance(data, dict) and 'header' in data:
        return [validate_replay(copy.deepcopy(data))]
    if isinstance(data, dict) and 'ep_states' in data:
        results = []
        episode_infos = data.get('ep_infos')
        for i, states in enumerate(data['ep_states']):
            params = data['mdp_params'][i]
            env_params = data.get('env_params', [{}] * len(data['ep_states']))[i]
            meta = dict(source=source_name, horizon=int(env_params.get('horizon', len(data['ep_actions'][i]))))
            infos = episode_infos[i] if episode_infos is not None else None
            results.append(_episode(states, data['ep_actions'][i], data['ep_rewards'][i], params, meta,
                                    dynamics=dynamics, infos=infos))
        return results
    if hasattr(data, 'to_dict') and not isinstance(data, dict):
        data = data.to_dict(orient='records')
    if isinstance(data, dict) and 'trajectory' in data:
        data = data['trajectory']
    if not isinstance(data, list):
        raise ValueError('Unsupported source format: expected trajectories or human/demo rows')
    groups = {}
    for row_index, row in enumerate(data):
        if not isinstance(row, dict) or 'state' not in row or 'joint_action' not in row:
            raise ValueError(f'row {row_index}: state and joint_action required')
        key = (str(row.get('trial_id', 'episode')), str(row.get('layout_name', 'custom')),
               *(str(row.get(f'player_{i}_id', i)) for i in range(3)))
        groups.setdefault(key, []).append(row)
    results = []
    for key, rows in groups.items():
        states = []
        for i, row in enumerate(rows):
            state = copy.deepcopy(decoded(row['state']))
            state['timestep'] = int(row.get('cur_gameloop', state.get('timestep', i)))
            states.append(state)
        first = rows[0]
        params = dict(layout_name=first.get('layout_name', 'custom'))
        if 'layout' in first:
            params['terrain'] = decoded(first['layout'])
            params['start_player_positions'] = [p['position'] for p in states[0]['players']]
            params['start_all_orders'] = states[0].get('all_orders', [])
        players = [dict(id=str(first.get(f'player_{i}_id', i)),
                        is_human=str(first.get(f'player_{i}_is_human', False)).lower() == 'true')
                   for i in range(len(states[0]['players']))]
        times = [float(r['time_elapsed']) for r in rows] if all('time_elapsed' in r for r in rows) else None
        rewards = [float(r.get('reward', 0)) for r in rows]
        scores = [float(r['score'])-rewards[i] for i, r in enumerate(rows)] if all('score' in r for r in rows) else None
        meta = dict(source=source_name, trial_id=key[0], players=players,
                    horizon=int(first.get('cur_gameloop_total', max(s['timestep'] for s in states)+1)))
        results.append(_episode(states, [decoded(r['joint_action']) for r in rows], rewards, params,
                                meta, times, scores, dynamics))
    return results


def convert_file(input_path, output_dir, dynamics=None):
    path, destination = Path(input_path), Path(output_dir)
    if path.suffix.lower() in ('.pickle', '.pkl'):
        # Pickles are legacy local inputs; Unity never deserializes executable Python formats.
        import pandas as pd
        data = pd.read_pickle(path)
    elif path.suffix.lower() == '.csv':
        import pandas as pd
        data = pd.read_csv(path)
    else:
        data = json.loads(path.read_text(encoding='utf-8-sig'))
    return [write_replay(item, destination / f'{path.stem}-{i:03d}.ocrep.json')
            for i, item in enumerate(convert_data(data, dynamics, path.name))]
