# 工程接手说明（E1 阶段）

本工程目标是把“对话提出行动 → 角色实际观察 → 事实写入 → 下一轮读取事实”做成可重复运行的闭环。当前使用占位模型；不包含 MiSide 的逆向资源。

## 代码与职责

| 层 | 入口 | 负责什么 |
|---|---|---|
| 场景 | `Unity/Assets/_Before1800/Scenes/Prototype_E1.unity` | 房间、玩家、林、摄像头、观察点和导航数据 |
| 编排 | `Scripts/PrototypeController.cs` | 玩家交互、请求、阅读暂停、Continue、动作、重开 |
| 权威状态 | `Scripts/Core/GameStateService.cs` | session/version、已发现/已验证事实、待执行决定、幂等和超时 |
| 模型通信 | `Scripts/AI/LlmClient.cs` | 异步本地 HTTP、超时和取消；只对回环地址禁用代理，不改 Windows 设置 |
| 决定校验 | `Scripts/AI/DecisionValidator.cs` | 精确字段、动作与目标、session/request/turn/version 匹配 |
| 行走 | `Scripts/NPC/LinMotor.cs` | NavMesh 移动、跟随、暂停、到达/失败 |
| E1 实验 | `Scripts/Evidence/CameraExperiment.cs` | 两个分离位置、林的朝向和视线、镜头实际跟踪、有效观察时长 |
| 界面/记录 | `Scripts/UI`、`Scripts/Diagnostics` | 对话滚动、状态、错误、F3 追踪、逐事件 JSONL |
| 网关 | `service/app` | OpenAI 兼容 Chat Completions、提示词、输出验证、去重、一次格式修复 |

上表的 Scripts 均在 `Unity/Assets/_Before1800/` 下。Unity 项目必须保留 `.meta`、Packages 和 ProjectSettings。不要提交 Library、.venv、真实 .env 或本机日志。

## 一次对话如何变成动作

1. 玩家发现摄像头，只写 `player_discovered`。林没有因此获得 E1。
2. 对话打开后暂停世界；输入交给服务，附带当前事实、可行动作、合法目标和最近对话。
3. 服务调用模型；返回六字段决定，加上 session/request/turn/version 与 provider/model 元数据。
4. Unity 再校验，先暂存决定。阅读回复时林仍不行动。
5. 点击 Continue 后重新验证，然后执行 inspect。林走向 A，再走向 B。
6. 每点必须位于到达范围，林看向镜头且视线无遮挡，镜头确实对准林并停留 1.3 秒；两点间距至少 1.5 米，镜头朝向变化至少 15 度。
7. 只有完整观察成功，Unity 才写入一次 E1。下一次请求携带 E1、观察说明、真实 action_result 和上一轮对话。

模型不能直接写 evidence、解决全部顾虑或结束游戏。E1 只支持“有人监控林”的观察，不证明世界的完整真相。

## 如何在没有 API 时推进

运行根目录 `Run-Test-Demo.cmd`。它启动明确标记 TEST_FIXTURE 的本地测试服务，验证与真实接口相同的传输、动作和状态流程。固定回复只适合工程演示，不用于评估模型智能。

运行根目录 `Run-Prototype.cmd` 使用正常网关。按当前要求，API key 和模型保持空；游戏明确显示配置缺失，仍允许探索、取消对话和重开。

以后填写 `service/.env` 的 Base URL、模型名、密钥，重启正常启动器即可使用真实调用路径。不需要改 Unity 源码。若供应商不支持 JSON response_format 或某种 token 参数，按 service/README.md 调整兼容参数。

## 下一阶段实施顺序

1. 有 API 后完成同局至少两轮真实调用验收：邀请检查 → 实际 E1 → 追问观察结果。检查自然语言是否夸大事实，保存 provider/model 和 trace。
2. 建立 E4/E5 独立观察事件，复用“发现不等于验证”的规则；每种证据先做失败/取消测试，再接入对白上下文。
3. 完成顾虑 C1/C2/C3 的独立解决条件、当前回合论据和共同逃离同意。同步扩展服务与 Unity 的合同，不能只改提示词开放动作。
4. 加入路线验证、双方到达出口、18:00 截止和结局。用完整场景验证后，再宣布完整 MVP。
5. 替换角色与环境美术、增加音效/动画和演示引导。保留本阶段可复现的 E1 回归测试。

## 重现检查

- 服务：在 service 下运行 `.venv/Scripts/python.exe -m pytest -q`。
- 规则：在根目录运行 `Tests/CoreHarness/run.ps1`。
- Windows 闭环：在根目录运行 `service/.venv/Scripts/python.exe tools/run_prototype.py --test --smoke`。此命令自动启动独立测试服务、跑 A/B 和两轮 HTTP，退出后清理自己启动的服务；它不会调用外部模型。
- Unity PlayMode：先启动 service 的两个测试脚本，再使用 Unity Test Runner 运行 PlayMode。不要同时用两个 Editor 打开同一个工程。

代理实现依据：[HttpClientHandler.UseProxy 官方文档](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.useproxy)。是否实际可用以本机独立程序验收记录为准。
