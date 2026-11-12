"""Record a full 400-step episode. Replace the RNG actions with your agents."""
import argparse
from pathlib import Path
import numpy as np
from overcooked_ai_py.mdp.actions import Action
from overcooked_ai_py.mdp.overcooked_env import OvercookedEnv
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld
from better_overcooked_replay import ReplayRecorder
from better_overcooked_replay.recipes import recipe_context

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--layout", default="cramped_room")
    args = parser.parse_args()
    mdp = OvercookedGridworld.from_layout_name(args.layout)
    env = OvercookedEnv.from_mdp(mdp, horizon=400)
    rng = np.random.default_rng(0)
    recorder = ReplayRecorder(mdp, env.state, {
        "horizon": 400, "tick_rate": 6,
        "players": [{"id": f"agent_{i}", "is_human": False} for i in range(mdp.num_players)],
    })
    with recipe_context(mdp.recipe_config):
        done = False
        while not done:
            # Each agent chooses an action for the same current state; step only once.
            joint_action = tuple(Action.INDEX_TO_ACTION[int(a)] for a in rng.integers(6, size=mdp.num_players))
            state, reward, done, info = env.step(joint_action)
            recorder.append(joint_action, state, reward, info)
        path = recorder.write(Path("output/my-game.ocrep.json"), complete=True)
    print(path.resolve())

if __name__ == "__main__": main()
