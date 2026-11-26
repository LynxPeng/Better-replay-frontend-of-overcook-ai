# v3 回放

支持 HuCooked `PartitionKitchen` 的 `sync` 与 `counter`。新版播放器同时读取旧 schema 1 和 v3 schema 2；旧版播放器不能读取 v3 文件。

## 直接打开现有实验回放

原有执行流程已经生成 `.ocrep.json` 时，继续使用它，不需要改模型、环境或另写录制代码。新版播放器直接支持实验中的 **schema 1 + `state.v3`**，以及 **`state.native_state` + `segment.native_info`** 投影文件，会读取真实持物、菜板、锅、台面和订单状态，替换旧文件中为兼容播放器而使用的洋葱、盘子等代理展示。

时间轴的金色得分标记同时检查逐帧团队分数增长和已确认交付事件。因此，导出文件的 `events` 为空时，实际得分仍会显示标记；同一步只显示一个时间点，相邻时间点也不会被直接跳过。塑形奖励不会作为团队得分标记。

从 `hucooked-project` 根目录打开已有左厨全脚本回放：

```powershell
.\replay_frontend\output\Windows\OvercookedReplay.exe --replay .\reproduction\nest_rl\mlp_partition_v3\runs\counter-400-lr001\unity-replays\script-left-chef1.ocrep.json
```

也可在程序中点击“打开本地回放”，选择原文件。九份已有实验回放不需要转换或覆盖。旧录制没有保存订单编号、派单时间、锁定菜谱、台面放置时间等字段时，界面标注未记录；不会捏造这些信息。已保存的切配、看锅进度和订单剩余步数仍能直接显示。

以下录制接口是供新增对局脚本使用的补充能力，不是查看现有文件的前置步骤。

## 录制自己的对局

从 `hucooked-project` 根目录运行。复用包含 v3 的项目环境，不安装或替换固定 upstream 依赖：

```powershell
.\hucooked\.conda\python.exe -m pip install -e .\replay_frontend --no-deps
.\hucooked\.conda\python.exe .\replay_frontend\examples\record_partition_episode.py --layout sync --out .\replay_frontend\output\my-v3.ocrep.json
```

```python
from better_overcooked_replay import PartitionReplayRecorder

# env 已 reset；一名厨师一个整数动作，编号沿用 v3 的 0～5。
rec = PartitionReplayRecorder(env, metadata={"tick_rate": 6})
actions = policy_actions
obs, reward, terminated, truncated, info = env.step(actions)
rec.append(actions, reward, info)
rec.write("game.ocrep.json")
```

在每次 step 后、任何 reset 前 append。每局新建记录器；张量环境为每个要保存的厨房指定 `env_index`，传入原始批量 actions、reward、info，不能先手动切片。记录器立即复制所选厨房的数据，既支持 46 维观测的环境，也支持 153 维观测的 `V3PolicyKitchen`，不从策略观测重建场景。记录器不会自动跟随 reset；步数校验只能发现可观察的倒退和跳步。

`build()` / `write()` 根据是否到达 horizon 自动判断整局是否结束。提前结束的片段标记为不完整，仍保留最后状态；不能将未结束的 v3 对局强行标成完整。

## 转换已有 seed/actions 轨迹

```powershell
.\hucooked\.conda\python.exe -m better_overcooked_replay convert .\hucooked\demo_outputs\partition\traces\sync-potato.json --out .\replay_frontend\output\converted
.\hucooked\.conda\python.exe -m better_overcooked_replay validate .\replay_frontend\output\converted\sync-potato-000.ocrep.json
```

输入包含 `layout`（sync/counter）、整数 `seed`、按步排列的三个整数动作 `actions`。可在 `config` 中保存 `horizon`、`deadlines`、`recipe_rewards`（按三道菜顺序），也兼容旧的统一分值 `delivery_reward`；还可用 `--partition-config config.json` 提供覆盖值。缺省采用已安装 v3 的默认配置并写入诊断，不会猜测其他训练实验的局长。配置中的 layout 必须与轨迹一致。

转换器用已安装的 v3 Python 核心从 seed 重放，保存实际奖励和状态；文件记录该核心的 SHA-256 指纹。超过 horizon 的输入会报错，短片段不会被自动续跑。现有首次交付轨迹包含目标 `recipe`、`order_id` 时，转换器还会核对目标是否复现。若轨迹缺少原始核心版本或配置，seed 与动作本身不能保证跨版本一致。

## 样例与展示

```powershell
.\hucooked\.conda\python.exe -m better_overcooked_replay partition-samples --out .\replay_frontend\output\partition-samples --traces .\hucooked\demo_outputs\partition\traces
```

生成两场 300 步完整脚本对局与六条首次交付片段。每个文件为 T 步、T+1 帧。schema 2 保存配置、人物、所有工位、台面库存、订单和逐步结算；三种成品分别使用 `meal_tomato_egg`、`meal_potato`、`meal_potato_tomato_beef`，与生料区分。

播放器显示菜板与看锅进度、订单剩余步数和交付侧。工位高亮只表示厨师朝向，不代表同时动作一定成功。新录制使用 Python 确认的事件；现有 `state.v3` 文件没有事件列表时，播放器根据已保存的真实前后状态和动作恢复可确认的加工、取放及交付事件。未知订单编号和台面等待时长仍标为未记录。Unity 不推进加工、交接或订单规则。

## Windows 构建

常规完整构建使用 `scripts/build-windows.ps1`，需要已激活的 Unity 6000.6.9f1，以及项目现有本地美术。

脚本从项目 `ProjectVersion.txt` 读取所需版本，验证实际 `Unity.exe` 版本。检测到 Microsoft Store 版 Unity Hub 时，在 Hub 的 MSIX 应用上下文中启动 Editor，并选择 Editor 自带授权客户端的版本化 IPC 通道。这样可以读取 Hub 正常持有的 Personal 许可，并避免较旧 Hub 客户端拒绝较新 Editor 的协议；无需复制许可证文件或重新激活账户。

本机已用此方式完成完整 Editor 构建：日志确认 `Unity Personal`、`Expiration: Unlimited`，构建退出码为 0。输出为 `output/Windows/OvercookedReplay.exe`，对应便携包版本 `v3-preview-20261124-editor`。

若只更新脚本与 StreamingAssets，还可用 `scripts/build-managed-update.ps1`：以现有同版本 **Mono** 播放器为基础，用 Unity 随附 Roslyn 重新编译应用程序集，生成独立 `output/Windows-v3`。该方式复用可执行宿主、场景、美术、角色程序集及运行库，不重新烘焙资源；本次新增工位和食材均使用已有资源或运行时几何。它不适用于场景、着色器、角色程序集或资源发生变化的完整重建。

程序支持实际运行验证：

```powershell
.\output\Windows\OvercookedReplay.exe -batchmode --check-replays .\output\v3-validation\all-replays --check-report .\output\v3-validation\runtime-checks.json
```

验证目录可放旧格式、v3 新格式和 `state.v3` 实验文件；程序检查读取、逐帧倒退、关键帧渲染、物品身份与交互提示，保存 JSON 报告后退出。
