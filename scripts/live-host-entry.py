"""Entry point for the bundled Windows room server and its policy workers."""

if __name__ == "__main__":
    from multiprocessing import freeze_support

    freeze_support()

    import argparse
    import ctypes
    from ctypes import wintypes
    from pathlib import Path
    import sys
    import threading

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--owner-pid", type=int)
    parser.add_argument("--log-file", type=Path)
    options, arguments = parser.parse_known_args()

    def watch_owner(owner_pid, shutdown):
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        kernel.WaitForSingleObject.restype = wintypes.DWORD
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        handle = kernel.OpenProcess(0x00100000, False, owner_pid)  # SYNCHRONIZE
        if handle:
            try:
                kernel.WaitForSingleObject(handle, 0xFFFFFFFF)
            finally:
                kernel.CloseHandle(handle)
        shutdown.set()

    if options.log_file:
        sys.stdout = sys.stderr = options.log_file.open("a", encoding="utf-8", buffering=1)
    shutdown = threading.Event() if options.owner_pid else None
    if shutdown:
        threading.Thread(target=watch_owner, args=(options.owner_pid, shutdown), daemon=True).start()
    from better_overcooked_replay.live_server import main

    main(arguments, shutdown)
