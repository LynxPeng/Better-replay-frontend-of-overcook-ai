# Better replay frontend of overcook-ai

Windows x64 便携版 v0.1.0。完整解压后双击 OvercookedReplay.exe 即可运行，不需要 Python、Unity Editor、conda 或网络。

请保留本目录的全部 DLL、OvercookedReplay_Data、MonoBleedingEdge 和 D3D12 等配套文件。不要只复制 exe。

- 左侧打开本地 .ocrep.json，或者展开示例厨房。示例中 upstream-sp-220 是上游模型生成的400步、220分完整对局。
- 空格播放/暂停，左右方向键逐步，下方时间轴跳转，0.25×至4×倍速。
- 右上角切换日夜、调整画面设置。设置中支持分辨率、窗口模式、MSAA、渲染倍率、帧率和俯角。
- 解锁镜头后：滚轮缩放，Shift+滚轮升降，WASD或中键平移，右键上下调整俯仰。
- 命令行：OvercookedReplay.exe --replay "D:\对局\my-game.ocrep.json"

如何从原环境记录数据、转换原轨迹，以及可选的单行事件补丁，请阅读：
https://github.com/LynxPeng/Better-replay-frontend-of-overcook-ai#readme

适用 Windows 10/11 x64，支持 Direct3D11/12 的显卡。本版是离线回放，不是实时联机游戏。

请保留 CharacterCredits.txt、THIRD_PARTY_NOTICES.md、LICENSE 和 LICENSES 文件夹。资源与模型分别遵循其原许可，不受源码MIT许可覆盖；不得单独提取或分发第三方模型。大肥鱼署名：木子恶魔；资源来源 VRMPet3D/drliuhuan，编译播放器专门许可见 LICENSES/VRMPet3D-compiled-player-zh.txt。
