"""Recover a hy demo's recorded frames using its fingerprinted upstream rules."""
import json
import sys
from hashlib import sha256
from pathlib import Path

from hy_current import SharedKitchen, SharedKitchenConfig
from hy_fixed import SharedKitchen as FixedKitchen, SharedKitchenConfig as FixedConfig


ITEM = {None: "", "raw_main": "RM", "raw_side": "RS", "chopped_side": "CS",
        "cooked_main": "CM", "plate": "PL", "meal": "MEAL"}
DIRECTIONS = {(0, -1): "N", (0, 1): "S", (1, 0): "E", (-1, 0): "W"}
ACTIONS = ("N", "S", "E", "W", "STAY", "INT")


def item(value):
    return ITEM[value.kind if value else None]


def frame(env, actions, reward, events):
    state = env.state
    cooker = state.cooker
    return dict(
        tick=state.tick, actions=actions, score=state.score, reward=reward,
        normal_completed=state.metrics["normal_completed"],
        joint_completed=state.metrics["joint_completed"],
        joint_wait_steps=state.metrics["joint_ready_steps"],
        unmatched_steps=state.metrics["unmatched_attempt_steps"],
        chefs=[dict(id=i, x=c.position[0], y=c.position[1],
                    facing=DIRECTIONS[c.orientation], held=item(c.held))
               for i, c in enumerate(state.chefs)],
        cooker=dict(item=item(cooker.item), rem=cooker.remaining,
                    kind=env.order(cooker.item.order_id).kind if cooker.item else ""),
        cutting=dict(item=item(state.cutting.item), prog=state.cutting.progress),
        asm=dict(main=item(state.assembly.main), side=item(state.assembly.side),
                 plate=item(state.assembly.plate)),
        buf=item(state.buffer),
        orders=[dict(id=t.id, kind=t.kind, status=t.status)
                for t in state.orders if t.status in ("active", "started")],
        events=events,
    )


def recover(path, summary_path):
    summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    steps = json.loads(path.read_text(encoding="utf-8-sig"))
    for kitchen, config in ((SharedKitchen, SharedKitchenConfig), (FixedKitchen, FixedConfig)):
        fingerprints = {kitchen.fingerprint()}
        if kitchen is FixedKitchen:
            # The first hy demo hashed checkout bytes before the upstream fix
            # normalized CRLF. Accept fingerprints of the same three sources.
            root = Path(sys.modules[kitchen.__module__].__file__).parent
            sources = [(root / name).read_bytes().replace(b"\r\n", b"\n")
                       for name in ("model.py", "layouts.py", "kitchen.py")]
            for newline in (b"\n", b"\r\n"):
                fingerprints.add(sha256(b"".join(s.replace(b"\n", newline) for s in sources)).hexdigest())
        if summary["fingerprint"] in fingerprints:
            env = kitchen(config(**summary["config"]))
            break
    else:
        raise ValueError("共享厨房版本指纹与此播放器支持的 hy 环境不匹配")
    env.reset(summary["seed"])
    frames = [frame(env, [""] * 3, 0, [])]
    for step in steps:
        _, reward, _, _, info = env.step(step["actions"])
        if (env.state.tick != step["tick"] or reward != step["team_reward"]
                or info["events"] != step["events"]):
            raise ValueError(f"第 {env.state.tick} 步与环境记录不一致，请检查配套的 summary 文件")
        frames.append(frame(env, [ACTIONS[a] for a in step["actions"]], reward, info["events"]))
    # Compare JSON representations, since the source uses tuples for actor IDs.
    if (not steps or summary["metrics"] != info["metrics"]
            or summary["orders"] != json.loads(json.dumps(info["orders"]))):
        raise ValueError("最终订单或得分与环境记录不一致")
    return dict(mode="hy", layout=list(env.terrain), width=len(env.terrain[0]),
                height=len(env.terrain), horizon=env.config.horizon, chop_steps=env.config.chop_steps,
                complete=env.terminated or env.truncated, metrics=info["metrics"], frames=frames)


if __name__ == "__main__":
    try:
        path, summary_path, destination = map(Path, sys.argv[1:])
        destination.write_text(json.dumps(recover(path, summary_path), separators=(",", ":")),
                               encoding="utf-8")
    except Exception as error:
        sys.stderr.reconfigure(encoding="utf-8")
        print(str(error), file=sys.stderr)
        sys.exit(1)
