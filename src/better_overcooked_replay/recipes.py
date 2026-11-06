"""Serialize/scoped access to upstream class-global recipe configuration.

Use recipe_context(mdp.recipe_config) when manipulating raw upstream states directly.
The new observation, recording and training entry points bind this automatically.
"""
import copy
import functools
import threading
from contextlib import contextmanager
from overcooked_ai_py.mdp.overcooked_mdp import Recipe

_lock = threading.RLock()


@contextmanager
def recipe_context(configuration=None):
    with _lock:
        previous = copy.deepcopy(Recipe._conf) if Recipe._configured else None
        if configuration is not None and (not Recipe._configured or Recipe._conf != configuration):
            Recipe.configure(copy.deepcopy(configuration))
        try:
            yield
        finally:
            if previous is not None and Recipe._conf != previous:
                Recipe.configure(previous)


def scoped_recipes(function):
    """Bind an env/recorder's config or a renderer's explicit mdp, and restore caller."""
    @functools.wraps(function)
    def scoped(*args, **kwargs):
        owner = args[0] if args else None
        mdp = kwargs.get('mdp', getattr(owner, 'mdp', None))
        if mdp is None and len(args)>1 and hasattr(args[1], 'recipe_config'):
            mdp = args[1]
        config = mdp.recipe_config if mdp is not None else getattr(owner, '_recipe_config', None)
        with recipe_context(config):
            return function(*args, **kwargs)
    return scoped
