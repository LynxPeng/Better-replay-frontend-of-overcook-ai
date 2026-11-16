"""Keep optional upstream planner caches in a writable user directory."""
import os
from pathlib import Path

from overcooked_ai_py import static

cache_root = Path(os.environ["LOCALAPPDATA"]) if os.name == "nt" else Path.home() / "Library/Caches"
cache = cache_root / "BetterReplay" / "PlannerCache-739950a"
cache.mkdir(parents=True, exist_ok=True)
static.PLANNERS_DIR = str(cache)
