import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description='Overcooked replay tools')
    commands = parser.add_subparsers(dest='command', required=True)
    samples = commands.add_parser('samples')
    samples.add_argument('--out', type=Path, default=Path('output/samples'))
    partition = commands.add_parser('partition-samples', help='Record both real v3 scripted kitchens')
    partition.add_argument('--out', type=Path, default=Path('output/partition-samples'))
    partition.add_argument('--seed', type=int, default=0)
    partition.add_argument('--horizon', type=int, default=300)
    partition.add_argument('--traces', type=Path, help='Also convert v3 seed/actions delivery traces')
    convert = commands.add_parser('convert')
    convert.add_argument('input', type=Path)
    convert.add_argument('--out', type=Path, required=True)
    convert.add_argument('--dynamics', choices=['old','new'])
    convert.add_argument('--partition-config', type=Path, help='JSON overrides for v3 trace configuration')
    validate = commands.add_parser('validate')
    validate.add_argument('input', type=Path)
    smoke = commands.add_parser('train-smoke')
    smoke.add_argument('--out', type=Path, default=Path('output/ppo-smoke'))
    smoke.add_argument('--timesteps', type=int, default=4096)
    args = parser.parse_args()
    if args.command == 'samples':
        from .samples import generate_samples
        for path in generate_samples(args.out): print(path)
    elif args.command == 'partition-samples':
        from .partition_samples import generate_partition_samples
        for path in generate_partition_samples(args.out, seed=args.seed, horizon=args.horizon, traces=args.traces): print(path)
    elif args.command == 'convert':
        from .importers import convert_file
        config = json.loads(args.partition_config.read_text(encoding='utf-8-sig')) if args.partition_config else None
        for path in convert_file(args.input,args.out,args.dynamics,partition_config=config): print(path)
    elif args.command == 'validate':
        from .protocol import load_replay
        data = load_replay(args.input)
        print(json.dumps({'valid':True,'frames':len(data['frames']), 'complete':data['header']['complete'],
                          'diagnostics':data['diagnostics']},ensure_ascii=False))
    elif args.command == 'train-smoke':
        from .smoke import train_smoke
        print(json.dumps(train_smoke(args.out,args.timesteps),indent=2))


if __name__ == '__main__':
    main()
