# v0.1.0 validation

- Source separated from the upstream game. Python import namespace: better_overcooked_replay. Original upstream implementation is an external pinned dependency.
- 28 standalone replay/training/import tests passed against the exact unmodified upstream environment module from commit 739950a079cdaed5a44fcc662efc40244c205d06. The tests also accept full events when the optional one-line patch is installed.
- Explicit no-patch delivery probe: reward20 and terminal state recorded correctly; soup_delivery event absent, matching README limitations.
- examples/record_episode.py executed and validated: 401 frames, complete=true, no diagnostics.
- Python wheel built successfully without installing into or changing the shared environment.
- Windows release includes the previously verified UI/player (four resolution layouts, camera gesture checks, 965 replay assertions, 78 original-project Python regression tests).
- Portable ZIP was extracted outside the source repository to C:/Projects/DRLExp/BetterReplayReleaseCheck_v0_1_0. Viewer ran from that extracted directory with PATH limited to Windows system directories. It loaded a Chinese-path replay, sought terminal frame400 and rendered terminal.png; process exit0 and no runtime exceptions. No Python, Unity Editor or repository assets were needed by the child process. This validates portability on the build machine, not a separate clean Windows installation.
- Public source index checked for VRM/GLB/unitypackage, local character directories, licensed Store source directories, conda and Unity Library paths: none included. The ZIP includes compiled runtime resources and full attribution/compiled-player permission, not raw source models.
- Release ZIP SHA256 is published next to the ZIP. Keep the entire extracted directory.
