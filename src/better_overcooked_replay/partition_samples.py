"""Real scripted v3 games; optional conversion of seed/actions delivery fragments."""
from pathlib import Path

from .partition import PartitionReplayRecorder, core


def generate_partition_samples(output_dir, *, seed=0, horizon=300, traces=None):
    m, native, _ = core()
    from overcooked_ai_py.cooperative.partition.controller import ScriptedChefs
    destination = Path(output_dir)
    results = []
    for layout in ('sync', 'counter'):
        env = native(m.PartitionConfig(layout=layout, horizon=horizon))
        env.reset(seed=seed)
        team = ScriptedChefs(env)
        rec = PartitionReplayRecorder(env, metadata=dict(seed=seed, source='v3-scripted-demo'))
        for _ in range(horizon):
            joint = team.actions()
            _, reward, _, _, info = env.step(joint)
            rec.append(joint, reward, info)
        results.append(rec.write(destination / f'partition_v3_{layout}.ocrep.json'))
    if traces is not None:
        import json
        from .partition import convert_partition_trace
        from .protocol import write_replay
        for path in sorted(Path(traces).glob('*.json')):
            trace = json.loads(path.read_text(encoding='utf-8-sig'))
            data = convert_partition_trace(trace, source_name=path.name)
            name = f"partition_v3_{trace['layout']}_{trace['recipe']}.ocrep.json"
            results.append(write_replay(data, destination / name))
    return results
