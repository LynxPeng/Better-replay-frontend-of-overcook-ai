# Windows 实时对局

便携联机包的房主与访客均可双击 `OvercookedLive.exe`，在首页创建或加入房间。包内已附房主服务、CPU 运行库和试玩模型。[便携版使用说明](LIVE-PORTABLE-QUICKSTART.md)

两位厨师共享一间厨房和团队得分。每秒执行两次联合动作，每局 400 步，有效游玩时长 3 分 20 秒。房主电脑运行游戏规则和 AI，访客电脑只运行 Windows 客户端。

## 房主开局

在含有 `replay_frontend` 子模块、项目 Python 3.10 环境 `.conda` 和本地素材的上游工作区根目录运行：

```powershell
.\scripts\start-live-host.ps1
```

启动器会准备本地模型目录、启动服务，再打开有房主管理权限的游戏窗口。首次加载模型或规划器需要一些时间。保持启动器窗口开启；在该窗口按 Ctrl+C 可请求房间保存记录并退出。若端口被占用，可运行 `.\scripts\start-live-host.ps1 -Port 8766`。

默认允许同一局域网的访客连接。只在本机体验人机或机机时，可加 `-Bind 127.0.0.1`。房主游戏窗口始终经本机回环地址连接。

**重开房主窗口**：关闭或意外退出游戏窗口后，保留原启动器，在原 PowerShell 控制台按 **R**。也可在同一工作区的另一个 PowerShell 执行 `.\scripts\start-live-host.ps1 -Reopen -Port 8765`。使用其他端口时填原启动器显示的实际端口。进行中的对局会在断线时暂停，须在 30 秒内恢复原席位，再由玩家确认继续。已经主动离开或超时结束的对局可从已保存的记录回看。

在大厅选择地图、种子和模式。蓝方是大肥鱼，橙方是企鹅。

| 模式 | 操作 |
|---|---|
| 人机 | 选择自己的角色，为另一位厨师选择模型或 Greedy 规划器。 |
| 人人 | 将大厅显示的局域网 IPv4 地址和端口发给另一位玩家。两人均入席且尚未准备时，任一方可点击「交换角色」。双方准备好后由房主开始。 |
| 机机 | 给两个角色分别选择模型。可选同一模型的两个实例，房主观战并管理对局。 |

配置改变后重新准备。模型预热完成、玩家就绪后进入 3 秒倒计时。房主可以在结算后重新配置下一局。

## 访客加入

把完整客户端目录（包括 `OvercookedLive.exe`、`OvercookedLive_Data`、运行库和署名文件）复制给同一局域网的 Windows 用户。访客双击 `OvercookedLive.exe`，输入房主大厅显示的地址和端口，再选择加入和准备。访客电脑无需安装 Python、conda、Unity 或模型。

可将房主给出的 `IPv4:端口` 整串粘贴到地址栏。多网卡时选与访客同网段的地址。默认 TCP 端口是 8765。若 Windows 防火墙提示网络访问，选择实际使用的专用局域网。连接中断会暂停对局，30 秒内重连可恢复原席位，重新准备后继续。

## 操作与回放

WASD 或方向键移动，按住持续移动，E 或空格交互一次。Esc 请求暂停。解锁镜头后使用滚轮缩放、中键平移、右键调整俯仰、Shift 加滚轮调整高度。机机观战时也可用 WASD 平移镜头。

每局自动保存到房主工作区的 `output/live-records/`。结算页可以查看本局，也可在离线播放器中打开 `.ocrep.json`。正常结束保存 401 个状态和 400 个步骤。提前结束的文件标记为不完整，运行过程中每 5 秒保存可播放的检查点。

## 本地 AI

SP 检查点和 PPO 权重均可选。启动器分别准备已有的文件，并沿用模型目录中已准备好的策略。仅有基础规则和规划器时，仍可开启人人房间和使用 Greedy 的对局。

- `ppo-final` 是项目原有的 SB3 图像 PPO 示例。启动器会从本地 `output/ppo-final/policy.zip` 准备它。也可把自己可信的兼容 PPO 模型放进 `output/live-models/`，文件名作为模型 ID，并重新启动房主服务。模型使用六个离散动作和 84 或 128 像素的 RGB 观察。
- `sp-cramped-room` 使用上游预训练自博弈策略。启动器校验本地 `checkpoint-550` 后准备数值权重。选择它时使用 `cramped_room` 和旧烹饪规则。想观看两名训练过的 AI 配合，可让两边都使用该模型。
- `greedy` 是显式可选的规划队友，适用于它支持的三洋葱菜谱与布局。每个 AI 单独加载和预热。超时的步执行等待，连续三次超时或推理异常会暂停，房主可重载策略或结束。

## 从源码构建

在完整上游工作区根目录运行下面的命令。构建使用已导入本地美术和角色的子仓库 Unity 工程，Unity 版本为 6000.6.8f1。

```powershell
.\replay_frontend\scripts\build-live.ps1 -OutputRoot .\output
```

独立检出播放器功能分支时，可直接用它的 `scripts/start-live-host.ps1 -Workspace <本地上游工作区>` 指定 conda、模型、客户端和记录所在工作区。运行程序位于 `output/Live/OvercookedLive.exe`。

构建完整便携包时，在本地上游工作区用 PowerShell 7 和项目 `.conda` 环境将 `pyinstaller==6.16.0` 安装到 `output/live-package-tools`。用 `scripts/build-live-host.ps1` 构建独立房主程序，再用 `scripts/package-live-complete.ps1` 将该程序、客户端、`output/live-models` 中的 PPO 与 SP 模型和署名打包。两个脚本的 `-Workspace` 均指向上游工作区。验收时将 ZIP 解压到独立路径，用 `scripts/check-live-players.py --portable` 验证实际程序的开房与对局。
