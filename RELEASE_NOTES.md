# Better replay frontend of overcook-ai v0.1.0

Windows x64 portable release. Download **BetterReplay-Windows-x64-v0.1.0.zip**, extract the whole folder and run **OvercookedReplay.exe**. No Python, Unity Editor, conda or network is required for playback. Do not copy the EXE without its Data/Mono/DLL files.

包含：三维厨房与街景、鱼/企鹅动画、日夜、中文新UI、画质设置、镜头锁定与移动、播放/暂停/单步/时间轴/倍速、六个布局样例及上游SP模型生成的220分完整对局。

完整用法、怎样生成/转换记录、上游单行事件透传补丁及不添加时的影响见仓库README。播放离线记录不需要模型推理依赖。本版本不含实时联机功能。

源码MIT；模型、美术和运行时分别遵循随包许可。保留CharacterCredits.txt、THIRD_PARTY_NOTICES.md及LICENSES。原始VRM/GLB和商店源资源不随公开源码分发。

Validated on the build Windows machine after extracting outside the repository, with Python absent from the child process PATH, including Chinese replay paths. A separate clean-machine test has not been performed. SHA256 checksum attached.
