# macOS preview validation

## 0.1.3 — 2026-11-07

Addresses Apple Silicon feedback reporting invisible Chinese UI/recipe illustrations and a black/magenta checkerboard on worktop tops. The exact failing Metal variant could not be established without a device log; Mac visual acceptance remains pending.

- Chinese UI and role names now load the embedded Noto Sans CJK SC Regular font, with font data included. All required Chinese glyphs pass the resource check. The font and SIL OFL license are shipped together.
- Recipe icons are baked from the same local models at build time and loaded as uncompressed transparent PNG textures. Runtime menu rendering no longer depends on a mesh CommandBuffer/RenderTexture path on Metal.
- The interaction overlay above each worktop and cooking steam use an explicit, resource-referenced, single-pass transparent shader. It has no URP Unlit keyword-variant dependency. Serialized blue/orange materials reference it directly.
- Unity cross-build returned exit 0. Resource checks, settings checks, simmer/fixture checks, 1367 replay assertions and both character checks passed. Same-source Windows capture verified Chinese text, both ingredients, mixed recipes and a highlighted worktop; this verifies Windows rendering only.
- Universal arm64/x86_64 verified for all six native binaries. Seven replay samples passed protocol validation and ZIP byte comparison; the upstream sample ends at 220 with 400 steps and 401 frames. ZIP CRC and Unix executable permissions passed.
- Archive: `BetterReplay-macOS-Universal-v0.1.3-macos-preview.zip`, 102,268,215 bytes.
- SHA256: `d899c9beacbce63e805a0867b0782b1ccbf802adc1490a2bb09d6834281928b9`.

No real Mac runtime, Metal appearance, signing or notarization verification is claimed for this corrective preview.

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
