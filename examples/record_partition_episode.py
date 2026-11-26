"""Run from replay_frontend in a project environment containing HuCooked v3."""
import argparse
from pathlib import Path

from better_overcooked_replay import PartitionReplayRecorder
from overcooked_ai_py.cooperative.partition import PartitionConfig, PartitionKitchen
from overcooked_ai_py.cooperative.partition.controller import ScriptedChefs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--layout', choices=['sync', 'counter'], default='sync')
    parser.add_argument('--seed', type=int, default=0)
    parser.add_argument('--horizon', type=int, default=300)
    parser.add_argument('--out', type=Path, default=Path('output/partition-game.ocrep.json'))
    args = parser.parse_args()
    env = PartitionKitchen(PartitionConfig(layout=args.layout, horizon=args.horizon))
    env.reset(seed=args.seed)
    chefs = ScriptedChefs(env)
    rec = PartitionReplayRecorder(env, metadata={'seed': args.seed})
    for _ in range(env.config.horizon):
        actions = chefs.actions()  # Replace with your three integer policy actions.
        _, reward, terminated, truncated, info = env.step(actions)
        rec.append(actions, reward, info)  # Before reset, even on the last step.
        if terminated or truncated:
            break
    print(rec.write(args.out))


if __name__ == '__main__':
    main()
