# Windows 实时对局本机验收

验收环境为 Windows x64、Unity 6000.6.8f1、项目 Python 3.10。房主规则服务与客户端使用真实 TCP 连接。测试中的真人席位由脚本输入动作，异机局域网和真人键盘手感留待实机验收。

## 对局与回放

在 `cramped_room` 以 2 Hz 各运行 400 步。为便于核对，人类席位按上游已知的 220 分轨迹逐步输入，SP 使用兼容的旧规则。

| 模式 | 房主或 AI 组合 | 终局得分 |
|---|---|---:|
| 人人 | 房主橙方，访客蓝方 | 220 |
| 人机 | 房主蓝方，SP 橙方 | 220 |
| 人机 | SP 蓝方，房主橙方 | 220 |
| 机机 | SP 蓝方，短跑 SB3 橙方 | 0 |

每局网络广播和磁盘记录均有 401 帧、400 组联合动作。将实际动作交给新建的上游环境重演，逐步比对位置、朝向、持物、厨房状态、稀疏奖励和塑形奖励，四局全部一致。两名独立 SP 模型还复现了 220 分的参考轨迹。离线 Windows 播放器实际打开了人机记录，画面末帧为 400/400 步、200.0 秒、220 分。

## Windows 客户端

- 同机运行房主和访客两份独立 Unity 程序。在第 10 步主动断开访客 TCP 连接，双方暂停、重连、重新准备，恢复后继续至第 30 步，两个窗口均收到终态。
- 从父工作区的 PowerShell 启动入口开房，完成 8 步人机对局并保存记录。单独关闭已入席的房主进程后，通过同一启动器重开，新进程在 3.89 秒后重新取得原房主席位。
- Windows 客户端分别完成新规则的人机对局和旧规则的双 SP 观战，后者在 60 步内交付一份汤，结算与磁盘记录均为 20 分。
- 将客户端压缩包解压至中文目录后，以 SB3 为搭档完成一整场 400 步人机对局，客户端收到 400 次输入确认，记录含 401 帧，有效时长 200 秒。
- Unity 批处理构建运行了回放、角色、交互及实时协议检查。服务端测试另覆盖末步交付、部分记录、迟到和重复输入、失焦取消交互、重连超时、模型故障与重载。
- 按上游 CI 的 `unittest discover` 入口运行通过 94 项，播放器子仓库的 pytest 全集通过 49 项。直接用 pytest 自动收集父仓库的 `testing/` 会把带参数的上游渲染辅助函数当成单独的测试并报缺少 fixture；该辅助函数由上游 unittest 用例传入渲染样本。

## 延迟

在本机 CPU 上用 100 个实际状态分别测量预热后的推理，耗时包含状态编码及子进程往返。SP、SB3 和两者并行的 P95 分别为 0.98、1.93 和 2.21 ms。对局的模型期限为 450 ms。

以双人房在一个逻辑步内的五个不同时刻交替发送 30 次输入，从发送到收到包含该动作的权威帧，中位数 312 ms，P95 为 512 ms。这项测量包含等待下一次 500 ms 逻辑步的时间，使用本机回环网络。

## 重跑

在上游工作区设置 `PYTHONPATH` 包含 `replay_frontend/src` 和 `src`，用项目 conda 环境运行。验收工具要求新的输出目录，避免覆盖已有报告。

```powershell
$env:PYTHONPATH = "$PWD\replay_frontend\src;$PWD\src"
$conda = "C:\Runtime\miniconda3\Scripts\conda.exe"
& $conda run --prefix .conda python replay_frontend/scripts/check-live-records.py --models output/live-models --out output/live-check-records
& $conda run --prefix .conda python replay_frontend/scripts/check-live-players.py --client output/Live/OvercookedLive.exe --models output/live-models --mode hh --steps 30 --rate 2 --drop-step 10 --out output/live-check-reconnect
```
