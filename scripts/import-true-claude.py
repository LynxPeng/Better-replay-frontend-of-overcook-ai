"""Acquire the pinned True Claude character for a local Unity build."""

import base64
import hashlib
from pathlib import Path
import tempfile
import urllib.request


SOURCE = (
    "https://raw.githubusercontent.com/a2244921700-code/blue-fat-fish-battle/"
    "3a3d84060f84a63d2f92764c56051805ce98e624/assets/characters/runtime/"
    "claude_true-model.js"
)
SHA256 = "5f8f3529e034c8b667158c979a9d0a938e4c71e5d602e134ef41365b5d9bceee"
PREFIX = b'window.FISH_MODEL_DATA=window.FISH_MODEL_DATA||{};window.FISH_MODEL_DATA["claude_true"]="'
DESTINATION = (
    Path(__file__).resolve().parents[1]
    / "unity/OvercookedViewer/Assets/LocalCharacters/TrueClaude/TrueClaude.glb"
)


def main():
    if DESTINATION.exists() and hashlib.sha256(DESTINATION.read_bytes()).hexdigest() == SHA256:
        print(f"Verified {DESTINATION}")
        return
    with urllib.request.urlopen(SOURCE, timeout=120) as response:
        wrapper = response.read().strip()
    if not wrapper.startswith(PREFIX) or not wrapper.endswith(b'";'):
        raise ValueError("Unexpected character data wrapper")
    model = base64.b64decode(wrapper[len(PREFIX):-2], validate=True)
    if hashlib.sha256(model).hexdigest() != SHA256 or model[:4] != b"glTF":
        raise ValueError("Character GLB failed its integrity check")
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=DESTINATION.parent, suffix=".tmp", delete=False) as tmp:
        temporary = Path(tmp.name)
    try:
        temporary.write_bytes(model)
        temporary.replace(DESTINATION)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Verified {DESTINATION}")


if __name__ == "__main__":
    main()
