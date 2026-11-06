"""Export UI expectations by probing the real Python INTERACT implementation."""
import json
from pathlib import Path
from overcooked_ai_py.mdp.actions import Action, Direction
from overcooked_ai_py.mdp.overcooked_mdp import OvercookedGridworld, ObjectState, SoupState, EVENT_TYPES
from better_overcooked_replay.protocol import make_header, normalize_state


def main():
    cases = []
    for old in (False, True):
        for capacity in (3, 4):
            if old and capacity != 3:
                continue  # Upstream old dynamics only accepts three-item recipes.
            for tile in 'XOTDPS ':
                contents = ('none', 'onion', 'dish', 'ready') if tile == 'X' else ('none', 'empty', 'one', 'full', 'cooking', 'ready') if tile == 'P' else ('none',)
                for content in contents:
                    for held in ('none', 'onion', 'tomato', 'dish', 'soup'):
                        terrain = [list(row) for row in ('XXXXX', 'X   X', 'X X X', 'X   X', 'XXXXX')]
                        terrain[2][2] = tile
                        mdp = OvercookedGridworld(terrain, ((2, 3), (1, 1)), old_dynamics=old, num_items_for_soup=capacity, max_num_ingredients=capacity,
                                                 start_all_orders=[dict(ingredients=['onion']*3), dict(ingredients=['tomato']*3)] if old else [])
                        state = mdp.get_standard_start_state()
                        state.players[0].orientation = Direction.NORTH
                        if held != 'none':
                            state.players[0].set_object(SoupState.get_soup((2, 3), finished=True) if held == 'soup' else ObjectState(held, (2, 3)))
                        if content != 'none':
                            obj = ObjectState(content, (2, 2)) if content in ('onion', 'dish') else SoupState.get_soup((2, 2), num_onions=0 if content == 'empty' else capacity if content == 'full' else 1, cooking_tick=2 if content == 'cooking' else -1, finished=content == 'ready')
                            state.add_object(obj)
                        initial = normalize_state(state)
                        expected = []
                        for agent in range(2):
                            probe = state.deepcopy()
                            actions = [Action.STAY, Action.STAY]
                            actions[agent] = Action.INTERACT
                            mdp.resolve_interacts(probe, actions, {e: [False, False] for e in EVENT_TYPES})
                            expected.append(normalize_state(probe) != initial)
                        assert normalize_state(state) == initial
                        cases.append(dict(name=f'{old}/{capacity}/{tile}/{content}/{held}', header=make_header(terrain, mdp=mdp), state=initial, expected=expected))
    path = Path('output/interaction-hint-oracle.json')
    path.parent.mkdir(exist_ok=True)
    path.write_text(json.dumps(dict(cases=cases), ensure_ascii=False), encoding='utf-8')
    print(f'{len(cases)} Python-authoritative cases written to {path}')


if __name__ == '__main__':
    main()
