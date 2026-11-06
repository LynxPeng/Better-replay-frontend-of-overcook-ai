# macOS preview validation — 2026-11-06

Status: **cross-build succeeded; real macOS runtime validation pending**.

- Editor: Unity 6000.6.8f1, official macOS Build Support (Mono), Windows host.
- Target: StandaloneOSX, Universal x86_64 + arm64, Mono, Metal; application version 0.1.1.
- Bundle identifier: `com.lynxpeng.betterovercookedreplay`; Info.plist minimum macOS version: 12.0. This is the configured target, not a claim of tested OS compatibility.
- Unity CLI returned exit 0. Local build log: `output/macos-build.log`.
- Both model rigs were reimported with graphics enabled. Penguin: 1224 assertions; fish actor: 44 assertions. Replay checks: 1367 assertions, including terminal frame and the upstream 220-point sample.
- Windows OPENFILENAME ABI and macOS picker result tests passed. The actual macOS chooser/AppleScript process and installed Mac fonts have not been exercised on a Mac.
- Standalone Python suite: 31 passed, including three new macOS packaging checks. Original project upstream/replay/training suites: 78 passed. No upstream dynamics or training code changed for this platform build.
- Main executable and all five other native Mach-O binaries were inspected: every one contains both arm64 and x86_64 slices, including Burst and Mono libraries.
- Seven replay examples included. App contains no raw `.vrm`, `.glb` or `.unitypackage` files.
- ZIP CRC check passed; every Mach-O entry has Unix 0755 mode; Unicode resource filename and non-executable resource mode covered by tests. Licenses, compiled-player permission, attribution and Mac quick start are included.
- App bytes: 514,647,745. ZIP bytes: 88,635,530.
- ZIP: `BetterReplay-macOS-Universal-v0.1.1-macos-preview.zip`.
- SHA256: `a8cab77f4d0fcd6a1a8cf536490422ddd8331834183726593c5482cc436ef892`.

No Developer ID signing or Apple notarization was performed. No Mac launch, UI, native file-picker, audio, GPU/Metal appearance or performance acceptance is claimed. On-device testing and any Apple distribution credentials remain separate from successful compilation.

The first import used `-nographics` and the UniGLTF material importer could not find its conversion shader. Reimporting the two local models with normal graphics resolved this; the Mac build entry performs that reimport. A pre-existing test incorrectly applied the scripted 20-point expectation to every recording on the cramped_room layout; it now checks the specific scripted fixture, allowing the legitimate 220-point upstream recording.
