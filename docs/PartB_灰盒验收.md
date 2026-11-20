# Before 18:00 · Part B 灰盒试玩

用 Unity **2021.3.35f1** 打开工程，在 Project 窗口双击 `Assets/Before1800B/PartB_Greybox.unity`，点 **Play**，在 Game 视图按 **Enter**。林在前方，右前方墙上有 CAMERA TEST 标牌，地上的金色方框是观察点。

## 一分钟走通 E1

1. 用 **WASD** 移动、鼠标观察。按 **E** 打开对话，按 **3** 选择 `inspect / camera_test`。左上面板显示 **Queued**，时钟和林都停住。
2. 再按 **E** 关对话。林绕过长椅走到方框并面向摄像头。看到 **Arrived** 时，**Verified** 应为 **0**。
3. 按 **V** 模拟 A 的实验完成事件。**Verified** 变为 **1 / E1**，林返回原站位。再次按 V，数量仍为 1。需要鼠标操作时，在到场后按 **Esc**，点 **Resume + send mock E1 completion**。

## 体验维修门与出口路线（E5 灰盒）

维修门在出生点右前方，门楣写着 **E5 | MAINTENANCE**。这里可以用两种顺序试玩。

| 想确认什么 | 操作 | 能看到的结果 |
| --- | --- | --- |
| 林亲眼见到片场布景 | **E → 6 → E** 请林去维修门。靠近门框上的金色控制器，对准按 **F** 开门。等林就位，在控制器旁再按一次 **F**，让开林和布景之间的视线。 | 林看清支架和制作标记连续 2 秒后，面板出现 **E5**，林开始返程。此时 **Passage checked** 仍保留原值。 |
| 玩家亲自检查通道 | 打开维修门，自己走到门后金色地线，对准 **CHECK ROUTE** 按 **F**，保持位置和朝向 1 秒。 | 通道可走时 **Passage checked** 变成 **yes**。可以开局先做，彼时 **Verified** 仍为 0。 |

观察和探路使用游戏内活跃时间，打开对话或菜单会暂停。观察中视线被挡，可以清开遮挡后重新邀请林。检查通道时走开或转头，需要回到标记重新按 **F**。玩家独自走进测试通道时，尚未同意离开的林会留在调查区。此处演示的是一小段通道的检查，整局成功还需要正式出口与同意逃离的判定。

在 Unity 2021.3.35f1 的 Test Runner 运行 `Before1800.PartB.Tests`，本版 24 项 PlayMode 测试全部通过。新增的 5 项覆盖两种完成顺序、开门和到场时的状态、支架或标记被遮挡、通道有实体障碍、暂停与截止、重开后丢弃迟到回调，以及玩家从禁路回来后林继续跟随。

## 其他操作

| 目的 | 操作与预期 |
| --- | --- |
| 跟随、停下 | 对话中按 **1** 跟随或 **2** 停下，关对话执行。停下也可取消当前检查。 |
| 检查暂停叠加 | 在对话排好检查后，按 **M** 开模拟等待，按 **Esc** 开菜单。依次关闭菜单及对话，等待仍冻结现场，最后按 **M** 才出发。 |
| 查看四维独立状态 | **Esc → Test concern: resolve C2**。对话中按 **4** 再关对话，让林退开。重新打开对话按 **5** 恢复交流。每局只可退开一次；C2 与已有 E1 均保留，C1/C3 和逃离同意维持初值。 |
| 检查不可达 | 先排好检查，在对话仍打开时按 **Esc → Test path: OPEN / Block**。关菜单、关对话后导航失败，证据为 0。在故障面板选 **Unblock test path**，确认后重新下令。已封路时直接提案也会被拒绝。 |
| 检查返程失败 | 林到观察点后，站到林原来的位置，再按 **V**。堵住返程约 3 个活跃秒会出现导航故障。点 **Acknowledge and return**，走开，在 **Esc** 菜单点 **Retry return to Lin's starting point**，林会完成返程，E1 保持 1。 |
| 超时与重开 | 17:00 开始，每个活跃秒推进 10 个游戏秒。用 **Esc → Test clock: jump near 18:00** 跳到 17:59:55，关菜单后自然触发 18:00。超时按 **R**，或在菜单点 **Restart at 17:00**；人物、任务、四维状态和时钟全部重置。 |
| 收起状态面板 | **F1**。再按一次展开。 |

## A 与 C 接入

场景根对象上的 `Before1800.PartB.PartBScene` 提供入口，均在 Unity 主线程调用。A 或 C 使用独立 asmdef 时，添加对 `Before1800.PartB` 的引用。灰盒有 `camera_test` 和 `set_back` 两个调查目标；住宅目前只配置了摄像头。

- **A：摄像头。** 订阅 `scene.ObservationReady`，按 `ticket.TargetId` 分发。若为 `camera_test`，在本次设备实验开始时保存收到的 `ActionTicket`。实验真正完成时调用 `scene.TryCompleteCameraExperiment(ticket, eventId)`。每个结果用一个非空 `Guid`，重发沿用原 ID。回调要带回开始时保存的票据；临时读取 `State.Active` 会丢失旧回调的身份。
- **A：维修门。** 在 `GreyboxWorld` 的场景层级挂 `SetBackStation`，指定门轴、控制器、观察点、支架与制作标记的 Renderer、路线检查点、检查牌、通道终点及禁止跟随的触发区。可绑定单独的返程点和通道阻挡物。门站与检查点保留在同一场景的可走导航网格上。场景交互可用 `scene.InteractWithSetBack()`，也可以从控制器在玩家操作时调用 `scene.TryBeginSetBackObservation(ticket)` 或 `scene.TryBeginRouteCheck(runId)`。B 复核位置、视线、时间和通行状态并分别写入结果。
- **C** 在对话打开时调用 `scene.TryBeginModelRequest(out var request)`，读取 `request.Snapshot`。返回动作前由 C 校验输出，在主线程调用 `scene.TryApplyModelAction(request, action, targetId, out var reason)`。支持 `reply/null`、`follow/player`、`inspect/camera_test`、`inspect/set_back`、`withdraw/null`。C 可以用 `scene.World.CanInspect(targetId)` 查询场景可达性，B 在接收指令时会再检查。取消或出错时调用 `scene.TryCancelModelRequest(request)`。关闭对话或重开会使旧请求失效。
- **下一轮上下文** 可通过 `JsonUtility.ToJson(request.Snapshot)` 序列化。`verified_evidence` 与 `route_confirmed` 分别表示林亲见的事实和玩家已查通的路线。`last_action_result` 与 `recent_action_results` 保留最近 16 条现场结果，含动作、目标、动作/实验 ID、到达、验证、完成、失败原因及返回重试来源。正式说服、共同逃离与真实模型留给后续集成。

场景、房间材质及预烘焙 NavMesh 都在 `Assets/Before1800B/`。其中 `Runtime/` 放状态、场景协调、角色运动和面板，`Tests/` 放场景测试。复测时进入 **Window → General → Test Runner → PlayMode**，运行 `PartBPlayModeTests`。

## 实现者实测 · 2026-11-20

在当前完整工程的隔离副本，用 **Unity 2021.3.35f1** 重新编译并运行上述正式场景。副本与工作区的 39 个 Part B 目录内文件逐一比对一致。**10 项 Play Mode 用例全部通过，失败 0、跳过 0。** 测试固定模拟步长，使用场景自带的 CharacterController、NavMesh 和相机。

覆盖排队与多层暂停、跟随与停下、当前到达与独立 E1、失活目标和失效路径、返程路线失效、玩家占据返程点后的重试、C2 与退开恢复、过期或重复模型回调，以及 18:00 截止与跨局隔离。C 的输入和 A 的设备完成均由明确的测试入口提供。

另将同一场景放入同版本的最小工程，构建 Windows 播放器。通过窗口键盘消息实测 **Enter → W → E → 3 → E → V**，核对了 1280 × 720 的对话与到场画面。播放器日志依次记录了检查排队、实际到场且证据为 0、V 完成后证据为 1。

## 独立验收 · 2026-11-20

在另一份完整工程副本中运行项目自带的 10 项 Play Mode 用例及独立编写的 11 项检查，**21 项通过，失败 0，跳过 0**。独立检查覆盖了多个窗口各自持有暂停、过期请求与动作票据、快照隔离、跟随与停止、E1 去重、截止与重开。验收时还让玩家实际站在林的返程点挡路，随后移开并重试。林回到原位，下一轮对话读到的验证记录只有一次。将摄像头对象禁用后，C 的检查指令也在下发移动前得到拒绝。

使用同一套 Part B 源文件和场景构建了 Windows x64 播放器。Unity 报告 0 个构建错误、5 个警告，分别涉及无图形构建时的环境探针、Burst 查找测试程序集，以及项目 Resources 中三个已缺失脚本的组件。试玩版通过菜单和快捷键模拟 A 的设备回调和 C 的指令，正式接入可使用上文的接口。

在含完整工程依赖的 Windows 画面验收版本中，通过真实窗口键盘输入依次执行 Enter、W、E、3、E、V。1280 × 720 截图中，到场帧显示 Arrived 和 Verified 0；完成实验后的画面显示 Returning 和 Verified 1 / E1。两张图中角色、摄像头标牌、观察点和调试面板均清晰可见。发布版与画面验收版共用同一份 Part B 源文件，截图探针只加入验收版。
