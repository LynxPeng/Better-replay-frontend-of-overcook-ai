# Better replay frontend of overcook-ai

Windows x64 离线回放器。完整解压后双击 OvercookedReplay.exe 即可运行，不需要 Python、Unity Editor、conda 或网络。

请保留本目录的全部 DLL、OvercookedReplay_Data、MonoBleedingEdge 和 D3D12 等配套文件。不要只复制 exe。

- 点「打开本地回放」可选择 .ocrep.json 或共享厨房原始 .json。例如直接打开 bc_rule_19meals.json 或 fast_bc_21meals.json。展开示例厨房可选择 upstream-sp-220，观看上游模型生成的 400 步、220 分对局。
- hy 三人共享厨房会显示普通单、联合单、订单编号和状态。锅旁显示是否需要双人出锅，装盘台分别显示主菜、配菜和餐盘。事件提示包含发生步数与参与厨师，暂停后可用左右方向键核对前后一帧。金色得分刻度对应交餐。
- 三人回放的绿方第三位厨师是真·Claude娘。打开 green-server-seed3.replay.json，调到 0.25×，可看第 12→13 步绿方装盘、第 14→15 步端菜和第 23→24 步橙绿同步出锅。
- hy 官方演示保存一对文件。将 mixed_trajectory.json 与 mixed_summary.json 放在同一目录，打开前者即可。请保留播放器旁的 SharedTrajectory 文件夹，它包含恢复动作轨迹所需的环境。逐帧原始 .json 可独立打开。
- v3 同步口与共享台面均可回放。已有实验生成的 .ocrep.json（含 state.v3）可直接打开，无需改原来的生成流程或转换文件；食材、菜板、锅与订单按真实 v3 状态显示。旧记录没有保存的订单编号等信息会标为未记录。
- 示例中“v3 · 双同步口”和“v3 · 共享台面”为完整对局，带菜名后缀的六份是首次交付片段。新格式包含订单编号、限时、交付侧和工位进度。
- 空格播放/暂停，左右方向键逐步，下方时间轴跳转，0.25×至4×倍速。
- 时间轴下方的金色刻度标记得分/交付时间点；导出文件没有事件列表时，仍根据实际团队分数增长生成标记。`native_state/native_info` 格式的 v3 投影回放可直接打开。
- 右上角切换日夜、调整画面设置。设置中支持分辨率、窗口模式、MSAA、渲染倍率、帧率和俯角。
- 解锁镜头后：滚轮缩放，Shift+滚轮升降，WASD或中键平移，右键上下调整俯仰。
- 命令行：OvercookedReplay.exe --replay "D:\对局\my-game.ocrep.json"

如何从原环境记录数据、转换原轨迹，以及可选的单行事件补丁，请阅读：
https://github.com/LynxPeng/Better-replay-frontend-of-overcook-ai#readme

适用 Windows 10/11 x64，支持 Direct3D11/12 的显卡。本版是离线回放，不是实时联机游戏。

本版使用 Unity 6000.6.9f1 构建。hy 上游演示读取器支持环境提交 de37e08（连续订单）与主项目固定的 10aba74（固定订单）。逐帧文件按原导出器默认的每秒 6 步播放。包内 runtime-checks.json 保存播放器验收结果。

请保留 CharacterCredits.txt、THIRD_PARTY_NOTICES.md、LICENSE 和 LICENSES 文件夹。资源与模型分别遵循其原许可，不受源码MIT许可覆盖；不得单独提取或分发第三方模型。大肥鱼署名：木子恶魔；资源来源 VRMPet3D/drliuhuan，编译播放器专门许可见 LICENSES/VRMPet3D-compiled-player-zh.txt。
