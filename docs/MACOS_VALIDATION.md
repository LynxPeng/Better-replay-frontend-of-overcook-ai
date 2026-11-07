# macOS preview validation

## 0.1.2 — 2026-11-07

Windows cross-build succeeded with Unity 6000.6.8f1, Mono and Metal. Real Mac runtime and visual acceptance remain pending. The app has no Developer ID signature or Apple notarization.

- Application version verified from the built Info.plist as 0.1.2. All six native Mach-O binaries contain both arm64 and x86_64 slices.
- Includes the current cafe UI, ingredient portraits, delivery timeline, day/night themes, restaurant lighting and state-driven white cooking vapor.
- Seven embedded replay files passed protocol validation and matched their archived copies byte for byte. The six example layouts and `upstream-sp-220.ocrep.json` are present. The upstream recording is complete with 401 frames, 400 steps and a final sparse score of 220.
- Python regression suite: 49 passed. Unity checks passed for 1367 replay assertions, 1224 penguin assertions, 44 fish assertions, native dialog results, settings transactions/four-resolution layout, simmer state/seek and cafe fixtures.
- Archive CRC passed. Every native binary retains Unix 0755 permissions. The ZIP includes Mac instructions, character credits, third-party notices and the compiled-player license. No raw VRM, GLB, FBX or unitypackage source assets occur in the application.
- Archive: `BetterReplay-macOS-Universal-v0.1.2-macos-preview.zip`, 88,661,225 bytes.
- SHA256: `77d6c28add4a689f4a1e7b297b71c9cf476b87265f296b5e2e94e3ee9aeb0d08`.

## 0.1.1 — 2026-11-06

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
