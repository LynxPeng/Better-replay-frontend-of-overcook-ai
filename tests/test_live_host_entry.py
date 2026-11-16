import runpy
import subprocess
import sys
import threading
from pathlib import Path


def test_owner_exit_signals_room_shutdown():
    entry = Path(__file__).resolve().parents[1] / "scripts/live-host-entry.py"
    watch_owner = runpy.run_path(str(entry)).get("watch_owner")
    assert callable(watch_owner), "The portable host needs an owner watcher on both platforms"
    owner = subprocess.Popen([sys.executable, "-c", "import sys; sys.stdin.read()"], stdin=subprocess.PIPE)
    shutdown = threading.Event()
    thread = threading.Thread(target=watch_owner, args=(owner.pid, shutdown), daemon=True)
    try:
        thread.start()
        assert not shutdown.wait(0.2)
    finally:
        owner.communicate(timeout=10)
    assert shutdown.wait(5)
