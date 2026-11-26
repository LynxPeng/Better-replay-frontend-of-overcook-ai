# Better replay frontend of overcook-ai — macOS 离线回放器

这是 macOS Universal / Mono 便携应用，包含 Apple Silicon（M1 及后续）和 Intel x86_64 两种架构。完整解压 ZIP 后，将 `OvercookedReplay.app` 放到“应用程序”或其他可写目录，双击打开。播放不需要 Python、Unity Editor 或网络。请保留随包附带的许可证和署名。

此版采用 ad-hoc 签名，未经 Apple 公证。macOS 如要求确认来源，可在“系统设置 → 隐私与安全性”中允许打开。

“打开本地回放”支持 hy 共享厨房的逐帧原始 JSON。打开 ZIP 内的 green-server-seed3.replay.json，可在第 13 步看绿方真·Claude娘装盘，第 24 步看橙绿同步出锅。仅含动作的上游演示（trajectory 与 summary 文件对）由 Windows 包配套的读取器支持。

如果 Finder 提示无法执行，可在终端给应用主程序补回权限（将路径替换为实际位置）：

```sh
chmod +x "/Applications/OvercookedReplay.app/Contents/MacOS/Overcooked Replay"
```

打开后展开左侧“厨房选择”，可选择六个示例厨房，或选择 `upstream-sp-220` 观看上游自博弈模型的 220 分完整对局（400 步、401 个状态帧）。所有示例均已随包附带。也可点击“打开本地回放”选择自己的 `.ocrep.json`。文件选择使用系统文件对话框，支持中文路径。菜单、回放、白天/夜晚和镜头操作与 Windows 版相同。

此版本包含更新后的餐厅界面、真实食材订单图标、交付时间轴刻度、昼夜配色、暖色灯串和白色烹饪蒸汽。默认每秒展示六个逻辑步骤，400 步样例约为 1 分 7 秒，可用播放倍速调整。


- 空格：播放/暂停；左右方向键：逐步。
- 解锁镜头后，滚轮缩放、Shift+滚轮升降、WASD/中键平移、右键上下拖动调整俯仰。
- 画面设置：分辨率、窗口模式、抗锯齿、渲染倍率和帧率。

命令行打开指定记录：

```sh
open /Applications/OvercookedReplay.app --args --replay "$HOME/对局/my-game.ocrep.json"
```

问题反馈请附 Mac 型号/芯片、macOS 版本、截图及日志。Unity 日志一般位于 `~/Library/Logs/<CompanyName>/<ProductName>/Player.log`；实际公司/产品名以应用 `Contents/Info.plist` 为准。

记录生成方法和源码见：
https://github.com/LynxPeng/Better-replay-frontend-of-overcook-ai

请保留 `LICENSE`、`LICENSES/`、`THIRD_PARTY_NOTICES.md` 和 `CharacterCredits.txt`。第三方资源仅随编译后的应用分发，不能作为素材包独立提取或再分发。
