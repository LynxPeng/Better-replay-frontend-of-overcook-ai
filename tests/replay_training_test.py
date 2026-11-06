import numpy as np
import pytest
from gymnasium.utils.env_checker import check_env

from overcooked_ai_py.mdp.actions import Action, Direction
from overcooked_ai_py.mdp.overcooked_mdp import SoupState
from better_overcooked_replay.observation import PixelObserver
from better_overcooked_replay.training import SingleChefEnv, RandomPartner, UpstreamPartner
from better_overcooked_replay.protocol import load_replay


@pytest.mark.parametrize('size', [84, 128])
def test_observation_tracks_orientation_inventory_pot_and_role(size):
    env = SingleChefEnv(image_size=size, partner=RandomPartner())
    a, _ = env.reset(seed=3, options={'player_index': 0})
    assert a.shape == (size, size, 3) and a.dtype == np.uint8
    state = env.base_env.state.deepcopy()
    state.players[0].orientation = Direction.EAST
    b = env.observer.render(env.mdp, state, 0, env.horizon)
    assert not np.array_equal(a, b)
    state.players[0].set_object(SoupState.get_soup(state.players[0].position, finished=True, num_onions=3))
    c = env.observer.render(env.mdp, state, 0, env.horizon)
    assert not np.array_equal(b, c)
    state.add_object(SoupState.get_soup(env.mdp.get_pot_locations()[0], num_onions=2, cooking_tick=4))
    d = env.observer.render(env.mdp, state, 0, env.horizon)
    assert not np.array_equal(c, d)
    assert not np.array_equal(d, env.observer.render(env.mdp, state, 1, env.horizon))
    assert state.timestep == 0
    env.close()


def test_seeded_roles_and_partner_do_not_modify_global_numpy_randomness():
    env = SingleChefEnv(partner=RandomPartner(), horizon=4)
    np.random.seed(919)
    global_before = np.random.get_state()
    a, info = env.reset(seed=42)
    role = info['controlled_player']
    rollout_a = [env.step(4)[0] for _ in range(3)]
    b, info = env.reset(seed=42)
    rollout_b = [env.step(4)[0] for _ in range(3)]
    assert role == info['controlled_player']
    assert np.array_equal(a, b)
    assert all(np.array_equal(x, y) for x, y in zip(rollout_a, rollout_b))
    after = np.random.get_state()
    assert np.array_equal(global_before[1], after[1]) and global_before[2:] == after[2:]
    env.close()


def test_gymnasium_and_sb3_contracts():
    from stable_baselines3.common.env_checker import check_env as sb3_check
    env = SingleChefEnv(partner=RandomPartner(), horizon=8)
    check_env(env, skip_render_check=True)
    sb3_check(env, warn=True)
    env.close()


def test_terminal_delivery_saved_before_reset_with_sparse_reward(tmp_path):
    class Stay:
        def reset(self, seed, agent_index, mdp): pass
        def act(self, state): return Action.STAY
    env = SingleChefEnv(partner=Stay(), horizon=1, record_dir=tmp_path)
    env.reset(seed=0, options={'player_index': 0})
    player = env.base_env.state.players[0]
    player.update_pos_and_or((3, 2), Direction.SOUTH)
    player.set_object(SoupState.get_soup((3, 2), num_onions=3, finished=True))
    # Start capture at the deliberately configured valid state, as a real caller may do.
    env.start_recording()
    _, reward, terminated, truncated, _ = env.step(5)
    assert reward == 20 and not terminated and truncated
    env.reset(seed=0)
    captured = load_replay(sorted(tmp_path.glob('*.ocrep.json'))[0])
    assert captured['frames'][-1]['score'] == 20
    assert captured['frames'][-1]['state']['players'][0]['held_object'] is None
    assert len(captured['frames']) == 2 and captured['header']['complete']
    env.close()


def test_upstream_partner_scopes_global_rng_and_rejects_unsupported_recipe():
    env = SingleChefEnv(partner=UpstreamPartner())
    np.random.seed(8)
    before = np.random.get_state()
    env.reset(seed=5)
    env.step(4)
    after = np.random.get_state()
    assert np.array_equal(before[1], after[1]) and before[2:] == after[2:]
    env.close()
    with pytest.raises(ValueError, match='three-onion'):
        other = SingleChefEnv(layout_name='cramped_room_tomato', partner=UpstreamPartner())
        other.reset(seed=0)


def test_shaping_is_explicit_and_display_score_stays_sparse(tmp_path):
    env = SingleChefEnv(partner=RandomPartner(), horizon=1, shaping_coef=2.0, record_dir=tmp_path)
    env.reset(seed=1, options={'player_index': 0})
    p = env.base_env.state.players[0]
    p.update_pos_and_or((1, 2), Direction.SOUTH)
    _, reward, _, _, info = env.step(5)
    assert reward == info['sparse_reward'] + 2 * sum(info['shaped_r_by_agent'])
    assert env.last_replay['frames'][-1]['score'] == info['sparse_reward']
    env.close()


def test_interleaved_recipe_environments_keep_ready_soup_and_delivery_reward(tmp_path):
    from overcooked_ai_py.mdp.overcooked_mdp import Recipe
    class Stay:
        def reset(self, seed, agent_index, mdp): pass
        def act(self, state): return Action.STAY
    first = SingleChefEnv(partner=Stay(), horizon=1, record_dir=tmp_path/'first')
    first.reset(seed=0, options={'player_index':0})
    Recipe.configure(first.mdp.recipe_config)
    p=first.base_env.state.players[0]
    p.update_pos_and_or((3,2),Direction.SOUTH)
    p.set_object(SoupState.get_soup((3,2),num_onions=3,finished=True))
    first.start_recording()
    second=SingleChefEnv(layout_name='cramped_room_tomato',partner=Stay(),horizon=1,record_dir=tmp_path/'second')
    second.reset(seed=0,options={'player_index':0})
    assert p.held_object.is_ready and p.held_object.cook_time==20
    q=second.base_env.state.players[0]
    # Construct a mixed-layout soup in its bound operation's configuration.
    from better_overcooked_replay.recipes import recipe_context
    with recipe_context(second.mdp.recipe_config):
        q.update_pos_and_or((3,2),Direction.SOUTH)
        q.set_object(SoupState.get_soup((3,2),num_onions=3,finished=True))
    second.start_recording()
    assert p.held_object.is_ready and p.held_object.cook_time==20
    second.render()
    assert p.held_object.is_ready and p.held_object.value==20
    _,reward,_,_,_=first.step(5)
    _,other_reward,_,_,_=second.step(5)
    assert reward==20 and other_reward==45
    assert first.last_replay['frames'][0]['state']['players'][0]['held_object']['cook_time']==20
    assert second.last_replay['frames'][0]['state']['players'][0]['held_object']['cook_time']==30
    first.close();second.close()
