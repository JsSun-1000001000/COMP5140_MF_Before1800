# Before 18:00 — E1 原型

Unity 6000.3.25f1 / Windows / C#；本地 FastAPI 模型网关。

## 团队协作

开始开发前请阅读 [项目合作规范与推进计划](COLLABORATION.md)。本次上传实现 E1 原型；岗位与预算仍由团队确认。实际工程目录为 `Unity/`，版本为 6000.3.25f1。

## 运行

- `Run-Test-Demo.cmd`：显式测试响应演示，不使用真实模型、不产生外部 API 调用。
- `Run-Prototype.cmd`：真实模型路径。先在 `service/.env` 填写 `LLM_BASE_URL`、`LLM_MODEL`、`LLM_API_KEY`。当前按用户要求留空；未配置时会明确提示，不会自动切换到模拟回复。
- 编辑工程：Unity Hub 添加本目录下的 `Unity`，打开 `Assets/_Before1800/Scenes/Prototype_E1.unity`。
- 本机也可双击 `Open-Unity-Project.cmd` 打开工程。

首次克隆：安装 Python 3.12–3.14，在 PowerShell 运行 `service/setup.ps1`；使用 Unity 6000.3.25f1 打开 `Unity/`，等待包导入，打开原型场景。运行 `service/start-test-fixture.ps1` 与 `service/start-test-gateway.ps1` 后，在场景 LlmClient 上将 Service URL 设为 `http://127.0.0.1:8001`，点击 Play 即可测试。真实模式使用 `service/start.ps1` 和默认端口 8000。

仓库不包含 Windows 可执行文件。根目录双击启动器需要先生成本地构建：关闭该工程的 Editor 后，在 PowerShell 执行下方命令。已有本地构建不需要打开 Unity Editor即可试玩。

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "$PWD\Unity" -executeMethod Before1800.Editor.PrototypeSceneBuilder.BuildWindows -logFile "$PWD\build.log"
```

构建成功后，输出位于 `Builds/Windows/Before1800.exe`。

建议使用固定安装的 Python 创建环境。若以后出现 `No Python at ...`，重新运行 `service/setup.ps1`；脚本会归档失效环境再重建，保留 `.env`。不要直接复制其他电脑的 `.venv`。

## 操作与 E1 流程

WASD 移动，鼠标看向，E 交互，T 在林附近交谈，Esc 菜单，F3 查看追踪摘要。

1. 点击 Enter the room，走向远墙上的摄像头，看向它并按 E。
2. 回到林附近按 T。点击 Invite to inspect，再 Send。
3. 阅读返回；此时世界暂停，林尚未行动。点击 Continue / act。
4. 林走到两个标记点，观察摄像头跟随自己转向。两个阶段完整完成后才增加 E1。
5. 走近林，再次交谈，使用 Discuss what happened。第二轮请求会携带 E1、真实实验结果和对话历史。

Esc 菜单中的 Cancel inspection 可中断当前实验，不授予新证据。Restart this session 新建一局，旧回包不能写入新状态。

## 当前范围与限制

这是 E1 阶段：房间、玩家、林的占位模型、跟踪镜头、暂停对话、受约束 reply/follow/inspect、状态及日志。E4/E5、完整顾虑推进和出口主线尚未接入此场景。底层已有部分完整主线规则，但不能据此声称完整游戏已完成。

真实 API 凭据留空，真实模型两轮验收尚未完成。测试演示会显示 `TEST_FIXTURE / deterministic-test-not-llm`，用于检查程序连接和动作流程，不用于评价模型智能。自然语言仍可能不准确；规则校验能拦住越权状态修改，不能证明每句对白都没有幻觉。

未导入 MiSide 逆向资源。本阶段模型、房间和 UI 由本工程构建；MiSide 仓库只做过静态参考分析。

## 文件与记录

- `Unity/Assets/_Before1800/Scripts`：状态、执行、实验、UI与API客户端。
- `Unity/Assets/_Before1800/Editor/PrototypeSceneBuilder.cs`：可重建测试场景与Windows构建入口。
- `service/README.md`：模型网关配置、契约、日志与故障说明。
- `docs/M1_ACCEPTANCE.md`：验收清单，区分 LIVE 与 TEST_FIXTURE。
- `docs/VALIDATION_2026-11-17.md`：实际已执行的验收结果与未验证项目。
- `docs/ENGINEERING_HANDOFF.md`：代码职责、完整数据流与下一阶段顺序。
- `Tests/CoreHarness/run.ps1`：纯 C# 状态规则测试，不依赖Unity编辑器窗口。
- `artifacts/`：本机编译、测试和构建记录，不提交版本控制。
- 玩家追踪日志：`%USERPROFILE%/AppData/LocalLow/COMP5140/Before 18 - E1 Prototype/traces/`，包含实际请求、原始返回、校验及状态变化。

源码提交保留 Assets 及其 .meta、Packages、ProjectSettings、service 代码和配置样例；不要提交 Library、真实 .env、密钥或本机日志。
