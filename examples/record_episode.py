"""Record a full 400-step episode. Replace the RNG actions with your agents."""
from pathlib import Path
import numpy as np
from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from better_overcooked_replay import ReplayRecorder
from better_overcooked_replay.recipes import recipe_context

def main():
    mdp = OvercookedGridworld.from_layout_name("cramped_room")
    env = OvercookedEnv.from_mdp(mdp, horizon=400)
    rng = np.random.default_rng(0)
    recorder = ReplayRecorder(mdp, env.state, {
        "horizon": 400, "tick_rate": 6,
        "players": [{"id": "agent_0", "is_human": False}, {"id": "agent_1", "is_human": False}],
    })
    with recipe_context(mdp.recipe_config):
        done = False
        while not done:
            # Both choose an action for the SAME current state; step only once.
            joint_action = tuple(Action.INDEX_TO_ACTION[int(rng.integers(6))] for _ in range(2))
            state, reward, done, info = env.step(joint_action)
            recorder.append(joint_action, state, reward, info)
        path = recorder.write(Path("output/my-game.ocrep.json"), complete=True)
    print(path.resolve())

if __name__ == "__main__": main()
