"""Modern single-chef Gymnasium adapter and replaceable, seeded partners."""
from __future__ import annotations

from contextlib import contextmanager
from pathlib import Path
import gymnasium as gym
import numpy as np

from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from .observation import PixelObserver
from .protocol import ReplayRecorder, action_index
from .recipes import scoped_recipes


class RandomPartner:
    def reset(self, seed, agent_index, mdp):
        self.rng = np.random.default_rng(seed)

    def act(self, state):
        return int(self.rng.integers(6))


class UpstreamPartner:
    """Adapters scope legacy global numpy state rather than leaking it into training."""
    def __init__(self, factory=None):
        self.factory = factory

    @contextmanager
    def _random_scope(self):
        caller = np.random.get_state()
        np.random.set_state(self.random_state)
        try:
            yield
        finally:
            self.random_state = np.random.get_state()
            np.random.set_state(caller)

    def reset(self, seed, agent_index, mdp):
        self.random_state = np.random.RandomState(seed).get_state()
        with self._random_scope():
            if self.factory is None:
                from overcooked_ai_py.agents.agent import GreedyHumanModel
                from overcooked_ai_py.planning.planners import MediumLevelActionManager, NO_COUNTERS_PARAMS
                if len(mdp.start_all_orders) != 1 or sorted(mdp.start_all_orders[0]['ingredients']) != ['onion']*3:
                    raise ValueError('Greedy partner supports only a single three-onion recipe')
                if mdp.layout_name.startswith('forced_coordination'):
                    raise ValueError('Greedy partner cannot independently solve forced_coordination')
                manager = MediumLevelActionManager.from_pickle_or_compute(mdp, NO_COUNTERS_PARAMS)
                self.agent = GreedyHumanModel(manager)
            else:
                self.agent = self.factory(mdp)
            self.agent.reset()
            self.agent.set_mdp(mdp)
            self.agent.set_agent_index(agent_index)

    def act(self, state):
        with self._random_scope():
            action, _ = self.agent.action(state)
            return action


class SB3Partner:
    def __init__(self, model, image_size=128, horizon=400):
        self.model, self.observer, self.horizon = model, PixelObserver(image_size), horizon

    def reset(self, seed, agent_index, mdp):
        self.agent_index, self.mdp = agent_index, mdp

    def act(self, state):
        obs = self.observer.render(self.mdp, state, self.agent_index, self.horizon)
        action, _ = self.model.predict(obs, deterministic=True)
        return int(action)


class SingleChefEnv(gym.Env):
    metadata = {'render_modes': ['rgb_array'], 'render_fps': 6}

    @scoped_recipes
    def __init__(self, layout_name='cramped_room', image_size=128, horizon=400,
                 partner=None, shaping_coef=0.0, record_dir=None, old_dynamics=False,
                 render_mode='rgb_array'):
        super().__init__()
        if horizon < 1:
            raise ValueError('horizon must be positive')
        self.horizon, self.shaping_coef = int(horizon), float(shaping_coef)
        self.mdp = OvercookedGridworld.from_layout_name(layout_name, old_dynamics=old_dynamics)
        self.base_env = OvercookedEnv.from_mdp(self.mdp, horizon=self.horizon)
        self.observer = PixelObserver(image_size)
        self.partner = partner if partner is not None else UpstreamPartner()
        self.action_space = gym.spaces.Discrete(6)
        self.observation_space = gym.spaces.Box(0, 255, (image_size,image_size,3), np.uint8)
        self.record_dir = Path(record_dir) if record_dir is not None else None
        self.render_mode = render_mode
        self.controlled_index = 0
        self.recorder = self.last_replay = None
        self.episode_index = 0
        self._needs_reset = True

    @scoped_recipes
    def reset(self, *, seed=None, options=None):
        if self.recorder is not None and self.recorder.segments:
            self._finish_recording(False)
        super().reset(seed=seed)
        self.base_env.reset()
        self.controlled_index = int((options or {}).get('player_index', self.np_random.integers(2)))
        if self.controlled_index not in (0,1):
            raise ValueError('player_index must be 0 or 1')
        partner_seed = int(self.np_random.integers(0,2**31-1))
        self.partner.reset(partner_seed, 1-self.controlled_index, self.mdp)
        self._needs_reset = False
        self.recorder = None
        if self.record_dir is not None:
            self.start_recording()
        return self.render(), {'controlled_player': self.controlled_index}

    @scoped_recipes
    def start_recording(self):
        self.recorder = ReplayRecorder(self.mdp, self.base_env.state,
                                       {'horizon':self.horizon, 'controlled_player':self.controlled_index,
                                        'partner':type(self.partner).__name__})

    @scoped_recipes
    def _finish_recording(self, complete):
        self.last_replay = self.recorder.build(complete)
        if self.record_dir is not None:
            self.recorder.write(self.record_dir / f'episode-{self.episode_index:04d}.ocrep.json', complete)
        self.episode_index += 1
        self.recorder = None

    @scoped_recipes
    def step(self, action):
        if self._needs_reset:
            raise RuntimeError('Call reset before step or after episode completion')
        if not self.action_space.contains(action):
            raise ValueError(f'Invalid action: {action}')
        partner_action = self.partner.act(self.base_env.state)
        joint = [None,None]
        joint[self.controlled_index] = Action.INDEX_TO_ACTION[int(action)]
        joint[1-self.controlled_index] = Action.INDEX_TO_ACTION[action_index(partner_action)]
        next_state, sparse, _, info = self.base_env.step(tuple(joint))
        terminated = bool(self.mdp.is_terminal(next_state))
        truncated = bool(not terminated and next_state.timestep >= self.horizon)
        reward = float(sparse + self.shaping_coef * sum(info['shaped_r_by_agent']))
        info.update(sparse_reward=float(sparse), controlled_player=self.controlled_index)
        if self.recorder is not None:
            self.recorder.append(joint, next_state, sparse, info)
            if terminated or truncated:
                self._finish_recording(True)
        self._needs_reset = terminated or truncated
        return self.render(), reward, terminated, truncated, info

    @scoped_recipes
    def render(self):
        return self.observer.render(self.mdp, self.base_env.state, self.controlled_index, self.horizon)

    def close(self):
        if self.recorder is not None and self.recorder.segments:
            self._finish_recording(False)
        self.recorder = None

