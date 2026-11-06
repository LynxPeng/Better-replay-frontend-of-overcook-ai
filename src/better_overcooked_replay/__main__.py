import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description='Overcooked replay tools')
    commands = parser.add_subparsers(dest='command', required=True)
    samples = commands.add_parser('samples')
    samples.add_argument('--out', type=Path, default=Path('output/samples'))
    convert = commands.add_parser('convert')
    convert.add_argument('input', type=Path)
    convert.add_argument('--out', type=Path, required=True)
    convert.add_argument('--dynamics', choices=['old','new'])
    validate = commands.add_parser('validate')
    validate.add_argument('input', type=Path)
    smoke = commands.add_parser('train-smoke')
    smoke.add_argument('--out', type=Path, default=Path('output/ppo-smoke'))
    smoke.add_argument('--timesteps', type=int, default=4096)
    args = parser.parse_args()
    if args.command == 'samples':
        from .samples import generate_samples
        for path in generate_samples(args.out): print(path)
    elif args.command == 'convert':
        from .importers import convert_file
        for path in convert_file(args.input,args.out,args.dynamics): print(path)
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
