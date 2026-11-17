# 独立代码审查记录

日期：2026-11-17。范围：`Unity/Assets/_Before1800/Scripts`、`service/app`、提示词及相关合同。审查时工程持续新增，以下结论是所述代码快照的静态审查，不代表 Unity 场景、Windows 构建或真实 API 已通过。初轮仅只读审查；随后获 root 授权，只修改 `PrototypeView.cs` 的对白滚动及取消检查入口。未启动 Unity。

## 需要修复或明确限制

### R01 / P1 — 服务与 Unity 回包字段不一致

- 发现位置：`service/app/schemas.py:105–114` 的 `DecisionResponse` 包含必需 `turn_id`；`Unity/Assets/_Before1800/Scripts/AI/DecisionValidator.cs:12,31,105` 的严格 `EnvelopeFields` 没有该字段；`Contracts.cs` 的响应 DTO 也没有。
- 触发：将服务任意一个正常成功响应 JSON 交给 `TryStageResponse(rawJson, ...)`。
- 结果：`ExactFields` 拒绝额外字段，返回 `response_fields`，全部真实回复无法进入 Continue。
- 建议：两侧统一合同；Unity 加入响应 `turn_id` 并校验它等于在途请求的 `turn_id`。以服务实际序列化响应做跨语言合同测试，另外测试错误 turn 被拒绝。
- 状态：核心负责人已修复；重新读取确认响应 DTO、9 字段 schema、必需字符串校验与当前 turn 匹配均已加入。负责人报告纯 C# 测试 18 组通过；本审查者未重新运行测试，跨进程场景闭环仍待验。

### R02 / P2 — 长合法回复被截断且无滚动入口

- 发现位置：`Unity/Assets/_Before1800/Scripts/UI/PrototypeView.cs:72,158`。
- 触发：玩家输入较长文字，或收到服务允许的较长回复（最多 1200 字符）；`SetConversation` 将用户输入和 NPC 回复一起放到固定 664×260、21px 的 Text。
- 结果：Text 使用 `VerticalWrapMode.Truncate`，没有 ScrollRect、分页或自动展开。玩家可能只看见自己的输入，读不到决定依据，却能点击 Continue。
- 建议：对白容器提供滚动；同时保持按钮固定且可见。验收至少覆盖 1600 字输入加 1200 字回复，英文与中文各一次，不用缩字号代替阅读入口。
- 状态：获 root 授权后仅修改 UI 文件：原有区域内加入原生 ScrollRect、RectMask2D、按文本首选高度扩展的内容和垂直滚动条；每次新消息显示后滚回顶部。运行时拖拽/滚轮与长文本视觉验证待整合测试。

### R03 / P2 — 进行中的 inspect 没有独立取消入口

- 发现位置：`PrototypeController.OpenDialogue` 阻止动作期间对话并提示“完成或重开”；已读 UI 菜单只有 Resume / Restart / Quit。
- 触发：林到 A 后，玩家想取消本次实验而保留当前局；或正在等待导航超时。
- 结果：当前只有重开整局或等待结束，不能执行计划中的“显式取消实验后接受另一动作”，也无法在真实交互中完成 E02/E03 取消验收。
- 建议：菜单增加 Cancel inspection；停止拥有该枚举的动作协程、停止林移动、重置实验阶段，并提交当前 action 的 `cancelled` 结果。保留已验证证据，未完成实验不授予 E1。
- 状态：获 root 授权后 UI 已加入 `OnCancelInspection` 及暂停菜单按钮，菜单总体尺寸保留；root 负责绑定控制器、写取消结果并测试。

### R04 / 限制 — 结构合法不等于对白事实正确

- 发现位置：`service/app/provider.py` 的 `Decision.model_validate` 与 `validate_context_action`，以及 Unity `DecisionValidator.Validate`。
- 触发：返回六字段合法的 `reply`，但对白声称“我们已经逃出去了”或声称未验证的证据已经完成。
- 结果：当前代码校验结构、目标、动作和版本；世界状态不会因此改变，但矛盾对白仍会显示。系统提示词有约束，不过未构成可靠的输出语义验证。
- 要求：不能把 JSON 解析成功等同于角色一致性通过。使用明显矛盾的离线回包验证显示策略，并在实际模型可用后完成成对语义测试；未做前保留为未验证项。若实现检查，需避免单纯关键词过滤误拒绝“我们还没有逃出去”等合法否定句。
- 状态：已通知 root；缺 API 时实际语义效果仍 BLOCKED。

## 已审阅的有效边界与待集成观察

- `PauseService` 使用独立令牌；旧令牌不能释放新暂停，多重原因不会互相覆盖。没有发现明显的重复 Dispose 问题。
- `CameraExperiment` 在计时和转向处检查暂停；`LinMotor.MoveTo` 活动超时不累计暂停时间。正常观察检查林的位置、朝向、无遮挡射线、镜头角度、两点间距和镜头方向差，完成动作本身不直接授予 E1。
- `CameraExperiment.ResetExperiment()` 本身不会停止旧 `Run` 枚举，`LinMotor.Stop()` 也不会取消 `MoveTo` 枚举。新控制器 `RestartSession` 已显式 `StopCoroutine(actionRoutine)`，当前重开路径覆盖了该风险；新的取消/终止路径必须同样停掉拥有者协程。可再用 instance/generation 检查增强组件自保护，但这不是已确认的当前重开故障。
- 控制器发 R2 前确实根据实际 E1 构造 observation，`GameStateService.CreateRequest` 带入实际 `last_action_result`；完成控制器的 ActionResult 写入后，仍需日志证实连续闭环。
- 服务提供单进程请求去重、相同 ID 不同负载冲突拒绝、一次格式修复、统一超时、取消及脱敏日志；这些是代码观察，不代替执行测试。
- 未填写用户 API 配置，不能执行或宣布真实双轮调用完成。

## 修复复核记录

在此追加实际复核时间、变更版本、结果与证据；不要把“已通知”改写为“已解决”，除非重新读取改动或运行对应测试。
