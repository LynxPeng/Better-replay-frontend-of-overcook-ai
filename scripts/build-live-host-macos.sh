#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
upstream="$(cd "${1:?Pass the pinned Overcooked-AI checkout}" && pwd)"
output="${2:?Pass a new output directory}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
data="$upstream/src/overcooked_ai_py/data"

python -m PyInstaller --onedir --console --noupx --name OvercookedHost \
  --distpath "$output" --workpath "$output/work" --specpath "$output" \
  --paths "$repo/src" --paths "$upstream/src" \
  --add-data "$data/layouts:overcooked_ai_py/data/layouts" \
  --add-data "$data/graphics:overcooked_ai_py/data/graphics" \
  --collect-data stable_baselines3 --runtime-hook "$repo/scripts/live-host-runtime-hook.py" \
  --exclude-module pytest --exclude-module notebook --exclude-module tensorboard \
  --exclude-module torchvision --exclude-module torchaudio --exclude-module tensorflow \
  --exclude-module jax "$repo/scripts/live-host-entry.py"

notices="$output/OvercookedHost/LICENSES"
mkdir "$notices"
cp "$upstream/LICENSE" "$notices/Overcooked-AI.txt"
cp "$CONDA_PREFIX/lib/python3.10/LICENSE.txt" "$notices/Python.txt"
python - "$notices" <<'PY'
import json
import shutil
import sys
import sysconfig
from pathlib import Path

notices = Path(sys.argv[1])
packages = Path(sysconfig.get_path("purelib"))
for dist in packages.glob("*.dist-info"):
    for source in dist.rglob("*"):
        if source.is_file() and (
            "licenses" in source.relative_to(dist).parts
            or source.name.upper().startswith(("LICENSE", "COPYING", "NOTICE", "AUTHORS"))
        ):
            target = notices / source.relative_to(packages)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)

for metadata in (Path(sys.prefix) / "conda-meta").glob("*.json"):
    directory = json.loads(metadata.read_text()).get("extracted_package_dir")
    if directory:
        licenses = Path(directory) / "info/licenses"
        if licenses.is_dir():
            shutil.copytree(licenses, notices / f"conda-{metadata.stem}")
PY
