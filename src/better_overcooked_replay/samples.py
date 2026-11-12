"""Deterministic, real-core demo episodes, including a complete soup delivery."""
from pathlib import Path
import numpy as np
from overcooked_ai_py.mdp.actions import Action, Direction as D
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld, SoupState, ObjectState
from .protocol import ReplayRecorder

LAYOUTS = ['cramped_room', 'asymmetric_advantages', 'coordination_ring',
           'forced_coordination', 'counter_circuit_o_1order', 'cramped_room_tomato']


def delivery_plan():
    interact = Action.INTERACT
    plan = [D.NORTH,D.WEST,interact,D.EAST,D.NORTH,interact]
    for _ in range(2):
        plan += [D.WEST,D.WEST,interact,D.EAST,D.NORTH,interact]
    plan += [interact] + [Action.STAY]*20
    plan += [D.WEST,D.SOUTH,D.SOUTH,interact,D.NORTH,D.EAST,D.NORTH,interact,
             D.SOUTH,D.EAST,D.SOUTH,interact]
    return [(a,Action.STAY) for a in plan]


def handoff_sample(destination):
    layout = 'three_player_sync_handoff'
    mdp = OvercookedGridworld.from_layout_name(layout)
    interact, stay = Action.INTERACT, Action.STAY
    plan = [(stay, stay, stay), (interact, stay, stay), (interact, interact, interact)]
    env = OvercookedEnv.from_mdp(mdp, horizon=len(plan))
    sender, third, receiver = env.state.players
    sender.update_pos_and_or((4, 3), D.EAST)
    sender.set_object(ObjectState('onion', sender.position))
    third.update_pos_and_or((9, 4), D.EAST)
    receiver.update_pos_and_or((6, 3), D.WEST)
    recorder = ReplayRecorder(mdp, env.state, dict(horizon=len(plan), tick_rate=1,
                                                    source='scripted-demo', custom_initial_state=True))
    for joint in plan:
        state, reward, _, info = env.step(joint)
        recorder.append(joint, state, reward, info)
    return recorder.write(destination / f'{layout}.ocrep.json')


def generate_samples(output_dir):
    destination = Path(output_dir)
    outputs = []
    for index, layout in enumerate(LAYOUTS):
        mdp = OvercookedGridworld.from_layout_name(layout)
        plan = delivery_plan() if index == 0 else None
        env = OvercookedEnv.from_mdp(mdp, horizon=len(plan) if plan else 180)
        if plan is None:
            # Valid custom initial states explicitly demonstrate cooking in other maps.
            pos = mdp.get_pot_locations()[0]
            env.state.add_object(SoupState.get_soup(pos, num_onions=3, cooking_tick=0))
            player = env.state.players[0]
            player.set_object(ObjectState('dish', player.position))
        rec = ReplayRecorder(mdp, env.state, {'horizon':env.horizon, 'source':'scripted-demo',
                                            'custom_initial_state':plan is None})
        rng = np.random.default_rng(17+index)
        for tick in range(int(env.horizon)):
            joint = plan[tick] if plan else tuple(Action.INDEX_TO_ACTION[int(a)] for a in rng.integers(0,6,2))
            state,reward,_,info = env.step(joint)
            rec.append(joint,state,reward,info)
        outputs.append(rec.write(destination / f'{layout}.ocrep.json'))
        if index == 0 and rec.frames[-1]['score'] != 20:
            raise RuntimeError('Scripted demo failed to deliver exactly one soup')
    if hasattr(OvercookedGridworld, 'get_sync_handoff_locations'):
        outputs.append(handoff_sample(destination))
    return outputs
