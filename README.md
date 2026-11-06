# Better replay frontend of overcook-ai

An independent Unity 3D replay viewer for [Overcooked-AI](https://github.com/HumanCompatibleAI/overcooked_ai). Python computes the game; Unity presents recorded snapshots. This is a community project, not an official Overcooked or HumanCompatibleAI product.

![Replay viewer](docs/images/main-final.png)

[查看画面设置界面](docs/images/settings-final.png)

## 下载即用 / Windows quick start

从本仓库 **Releases** 下载 `BetterReplay-Windows-x64-*.zip`，完整解压到任意可写目录，双击 `OvercookedReplay.exe`。无需安装 Python、Unity Editor、conda，也无需下载模型或美术包。**必须保留同目录的 `_Data`、`MonoBleedingEdge`、DLL 和其他文件，不能只复制 exe。**

这是 Windows x64 便携发行包，不修改系统安装目录。建议 Windows 10/11 和支持 Direct3D 11/12 的显卡。源码 ZIP 不是可运行发行包。

程序左侧“打开本地回放”选择 `.ocrep.json`，或展开“示例厨房”。下方提供重播、前后单步、播放/暂停、时间轴、0.25×–4×倍速；空格播放暂停，左右方向键逐步。右上角切换白天/夜晚和画面设置，可调分辨率、窗口/无边框、MSAA、渲染倍率、帧率、镜头俯角。Esc取消设置。

点击“镜头已锁定”解锁后，鼠标在3D区域内：滚轮缩放，Shift+滚轮升降，WASD/中键平移，右键上下拖动调整俯仰；工具栏可旋转90°及复位。中文路径受支持。命令行：

```powershell
.\OvercookedReplay.exe --replay "D:\对局\my-game.ocrep.json"
```

支持已记录的 AI–AI、人–AI、人–人对局。本版是离线回放器，不包含实时联机。角色外观与训练编号无关；画面中的装饰不参与规则。

## macOS 构建 / 预览版

新增 macOS Universal（Apple Silicon arm64 + Intel x86_64）/ Mono 构建入口，使用 Metal 渲染、macOS 系统文件选择器和中文系统字体回退。回放文件与 Windows 版通用。使用说明见 [macOS 快速开始](MACOS-QUICKSTART.md)。**Windows 交叉编译不能替代真实 Mac 验收；当前 Mac 构建未完成 Developer ID 签名和 Apple 公证。**

下载：[v0.1.1 macOS 预览版](https://github.com/LynxPeng/Better-replay-frontend-of-overcook-ai/releases/tag/v0.1.1-macos-preview)。构建和检查范围见 [验收记录](docs/MACOS_VALIDATION.md)。

已有完整本地美术与生成场景后，在 Windows 的同版本 Unity Editor 安装 **macOS Build Support (Mono)**，关闭该工程的编辑器窗口，执行：

```powershell
.\scripts\build-macos.ps1
python scripts/package-macos.py output/macOS/OvercookedReplay.app
```

输出应用 `output/macOS/OvercookedReplay.app`，以及保留 Unix 可执行权限的 `release/BetterReplay-macOS-Universal-v0.1.1-macos-preview.zip` 和 SHA256。打包脚本只需 Python 标准库，不需要安装训练依赖。在 Mac 本机也可使用同版本 Unity，通过 `-batchmode -buildTarget OSXUniversal -executeMethod MacBuild.Build -quit -projectPath <工程路径>` 构建；正式签名、公证需另行使用自己的 Apple 开发者身份完成。

## 怎样生成记录

**仅观看不需要以下环境。**生成/转换记录使用 Python 3.10；示例在仓库根目录运行。源码工具包是 `better_overcooked_replay`，不会把模块写进上游 `overcooked_ai_py`。

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e ".[import]"
```

安装会使用固定上游提交 `739950a079cdaed5a44fcc662efc40244c205d06`。原有 Overcooked-AI 环境可使用 `pip install -e . --no-deps`，自行保证依赖版本匹配。不要为了播放而安装已弃用的 RLlib/TensorFlow。

### 方式一：记录你自己的环境

完整例子在 [examples/record_episode.py](examples/record_episode.py)：

```powershell
python examples/record_episode.py
python -m better_overcooked_replay validate output/my-game.ocrep.json
```

例子使用随机动作，仅演示记录，不代表训练好的策略。把例子中的 `joint_action` 换成你的两个 Agent 对同一状态产生的动作即可。核心写法：

```python
from better_overcooked_replay import ReplayRecorder

recorder = ReplayRecorder(mdp, env.state, {"horizon": 400, "tick_rate": 6})
done = False
while not done:
    joint_action = (agent0_action, agent1_action)  # 原生动作，例如 (0,-1) 或 "interact"
    next_state, reward, done, info = env.step(joint_action)
    recorder.append(joint_action, next_state, reward, info)
recorder.write("my-game.ocrep.json", complete=True)
```

先保存初态，再在**每次 step 后、reset 前**保存新状态；T 次动作必须有 T+1 帧。提前停止用 `complete=False`。默认6逻辑步/秒仅决定展示时长，不限制训练吞吐量；按实际情况设置 `tick_rate`。得分用稀疏奖励，塑形奖励分开记录。动作顺序为上、下、右、左、等待、交互，转换器也接受原生动作向量。

### 方式二：使用上游原记录器

```python
import pickle
trajectory = env.get_rollouts(agent_pair, num_games=1, final_state=True)
with open("rollouts.pkl", "wb") as stream:
    pickle.dump(trajectory, stream)
```

```powershell
python -m better_overcooked_replay convert rollouts.pkl --out output/imported --dynamics new
```

旧烹饪规则使用 `--dynamics old`，不要为了补终态猜测规则。支持标准轨迹字典 JSON/pickle、网页 demo result.pkl、人类 CSV/ pickled DataFrame；多局会拆成多个文件。`run_agents()` 的裸数组需要先整理成标准轨迹字典。仅可信的本地 pickle 可导入；Unity 不反序列化 pickle。

**历史记录限制：**没有终态时，仅在显式规则下验证已有状态转移及奖励一致后补算；验证失败停在最后已知状态，缺失末尾动作写入诊断。缺帧直接跳转，不伪造动作或奖励。有时间戳时保留间隔，无时间戳默认6步/秒。只有视频/RGB/训练得分而没有状态，无法还原3D。历史导入目前根据前后状态推断事件，可能缺少完整交付音效和庆祝；即使原轨迹 info 中有事件，当前转换器也未完整利用它。要求完整演出时使用方式一。

### 方式三：生成内置示例

```powershell
python -m better_overcooked_replay samples --out output/samples
```

`cramped_room` 示例从标准初态完成20分交付；其他布局的演示可能使用明确的自定义有效初态。发行包另含一场上游 SP 检查点推理生成的400步/220分记录。生成样例不要求训练或启动Unity。

## 特别说明：上游源码的一行补丁

本工具包不会自动修改上游。建议在上游 `src/overcooked_ai_py/mdp/overcooked_env.py` 的 `_prepare_info_dict` 中，在奖励透传后添加：

```python
env_info["sparse_r_by_agent"] = mdp_infos["sparse_reward_by_agent"]
env_info["shaped_r_by_agent"] = mdp_infos["shaped_reward_by_agent"]
env_info["event_infos"] = mdp_infos["event_infos"]  # 仅这一行是新增
```

补丁文件在 [patches/upstream-event-info.patch](patches/upstream-event-info.patch)。在固定版本的上游仓库中：

```powershell
git apply --check path/to/upstream-event-info.patch
git apply path/to/upstream-event-info.patch
```

**它做什么：**MDP已经计算出交互事件；这一行把事件放进 `env.step()` 返回的 `info`，让记录器获取准确的拾取、入锅、交付等事件。不改变状态转移、碰撞、动作、烹饪或稀疏奖励，不是修改训练算法。

**如果不加：**环境、训练和记录仍可以运行，Unity仍能显示状态、物品、烹饪进度和得分。记录器会根据状态变化推导一部分事件，但不能完整替代原事件，例如交付可能仅被推断成放下汤；因此交付专属动作、提示音和庆祝可能缺失。状态/得分正确不等于演出事件完整。添加补丁不能补回已经丢失的历史事件，也不会让当前历史导入器自动使用所有 ep_infos。

## 源码结构和重建

```text
unity/OvercookedViewer/   Unity工程、UI、场景、材质、角色动画和导入/构建工具
src/better_overcooked_replay/  协议、记录、转换、观察及可选训练适配
examples/               接入现有环境的最小示例
tests/                  Python回归
patches/                唯一的上游事件透传补丁
```

Unity版本：6000.6.8f1、URP17.6，Windows x64/Mono 与 macOS Universal/Mono。场景/Prefab/.meta和包锁纳入版本控制；Library、环境、训练输出、商店美术源文件及原始角色模型不上传。

**源码重建与运行发行包的依赖不同。**重建完整场景需要自行取得并导入 Restaurant Bits for Unity、Cartoon Town和Synty Simple Sky，遵守各自许可；来源与目录见 [资源说明](unity/ASSET_SOURCES.md)。使用本仓库导入脚本时保留原.meta GUID。角色源模型与派生Prefab也需本地准备；没有本地角色会使用程序自制厨师。大肥鱼模型不属于MIT源码授权。

准备本地资源后，用Unity Hub打开工程，或运行：

```powershell
.\scripts\build-windows.ps1 -Initialize
```

首次Initialize会重建生成场景和预制体；已有工程保留编辑时省略该参数，执行回归与构建。输出 `output/Windows/OvercookedReplay.exe`。首次许可激活和资源商店登录由使用者完成。

测试：`python -m pip install -e ".[test]"` 后执行 `python -m pytest tests -q`。SB3/PyTorch仅可选训练适配需要，观看或基础记录不需要。训练观察在Python生成，与Unity分辨率、帧率和美术资源完全独立。

## 许可证与署名

源码采用 [MIT](LICENSE)，保留上游 Center for Human-Compatible AI 版权声明。第三方美术、模型、Unity运行时和UPM依赖分别遵循原许可，**MIT不授予这些资源的独立再分发权**。企鹅原模型 ilvoris、绑定 photon/Professor_E12，CC BY4.0，具体链接及修改说明见发行包 `CharacterCredits.txt`。商店源资源和大肥鱼原始VRM不包含在公开源码中。含第三方美术的Windows发行包须保留全部署名及授权文件。
