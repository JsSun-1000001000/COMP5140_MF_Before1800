# E1 原型验收结果 — 2026-11-17

结论：**Windows 原型的测试服务闭环通过，真实 LLM 验收待配置。** API key 和模型按用户要求留空，未调用任何外部模型。本报告不表示完整游戏或课程项目已经完成。

## 本次交付

- Unity 6000.3.25f1 工程与可运行 Windows x64 构建。
- 单房间、玩家移动与观察、林的占位模型、可跟踪摄像头、A/B 观察点和导航。
- 文本对话、阅读/菜单暂停、Continue 后行动、reply/follow/inspect、实验取消和新局。
- 本地 GPT 兼容网关、严格合同校验、超时/取消/去重、真实动作结果回传。
- API 缺失时明确报错；测试模式必须显式启动并显示 TEST_FIXTURE，不自动伪装成真实模型。

## 已执行结果

| 检查 | 结果 | 实际依据 |
|---|---|---|
| 服务单元与接口测试 | 54/54 PASS | Python 3.14.3，`artifacts/service-final.xml`；pip check 无依赖冲突 |
| 核心 C# 规则 | 18/18 PASS | 本轮先前已运行 CoreHarness，覆盖严格协议、状态、重复/旧请求、暂停与结局门槛；不等同于完整游戏已接入 |
| Unity PlayMode | 7/7 PASS | `artifacts/playmode-final7.xml`，不是零测试退出 |
| Windows 构建 | PASS | `artifacts/build-result.txt`：Succeeded，0 errors，0 warnings |
| Windows 两轮 HTTP + 实际 E1 | PASS / TEST_FIXTURE | `artifacts/acceptance/2026-11-17-E1-final/windows-e1-trace.jsonl` 与 fixture-smoke-result.json |
| 新 Python 环境启动 | PASS | 固定安装 `C:\Python314\python.exe`；独立程序双轮测试再次通过 |
| 虚拟环境修复 | PASS | 独立损坏环境样例：归档失效环境、重建成功、原有 .env 保留 |
| 正常模式启动画面 | PASS / 界面观察 | 实际显示房间、林、介绍、API not configured；进入房间按钮可用 |
| 原生电脑操作工具发送快捷键 | NOT VERIFIED | 自动化工具按键未使游戏界面变化；不能据此声称已完成真人键鼠全流程验收 |
| 真实模型 R1 / E1 / R2 | BLOCKED（按要求留空） | 尚无 API 配置，成功真实调用 0 次 |

7 项 PlayMode 覆盖：两轮 HTTP 与实际 A/B 观察；实验取消/重开；服务不可用后的恢复；重合观察点拒绝；静止镜头拒绝；真实射线交互发现；T/Esc/F3 输入事件到界面的处理路径。

输入事件测试在无窗口批处理环境中显式允许输入送入 Game View，并在结束时恢复设置。该项证明游戏内部事件路径有效，不冒充真人键盘或电脑操作工具的端到端验证。

## Windows 同局闭环证据

最终记录 session：`914e78cd40124dd4aed72f79729bfbf4`。

1. 发现摄像头时 verified_evidence 为空。
2. 第一次 HTTP 测试返回 inspect(camera_01)，等待 Continue 才执行。
3. 林通过 NavMesh 实际到 A、B；观察完成后唯一写入 E1。
4. action_result 为 succeeded，记录“Lin observed the camera track his position at two separated points.”。
5. 第二次请求携带 E1、这次真实 action_result 和上一轮对话；收到对应观察结果的 reply。
6. C1/C2/C3 仍 open，escape_agreed 和 route_confirmed 仍 false，没有跳到逃离成功。

截图、原始 trace、服务/场景 XML、构建与包版本 SHA256 已集中保存到 `artifacts/acceptance/2026-11-17-E1-final/`。截图来自 Windows Player 的实际画面。TEST_FIXTURE 的台词只用来验证工程流程，不用于证明模型推理能力。

## 已修复问题

- 服务响应的 turn_id 与客户端严格合同对齐。
- 长对话可滚动，实验可单独取消。
- 导航代理在 NavMesh 加载后启用，消除独立程序启动时的导航初始化错误。
- 本地 HTTP 使用仅针对回环地址的直连，修复 Windows 代理导致的 502，不修改系统代理。
- 服务环境脱离已失效的应用内置 Python 路径；setup 增加可恢复重建。

## 明确保留的限制

- 尚未进行真实供应商调用、额度/延迟和角色自然语言一致性评估。
- 结构校验能阻止越权状态修改，不能保证每句话没有幻觉。
- 真人完整 WASD/鼠标/按键、长中英文滚动、另一台电脑部署仍应由成员补验；不要把本报告的自动化项目扩张为这些项目也已通过。
- E4/E5、完整顾虑推进、共同出口和最终结局尚未接入本场景。
- 美术为占位资源，没有导入 MiSide 逆向资产。

运行方法见根目录 README；代码接手与下一阶段顺序见 ENGINEERING_HANDOFF.md；完整待验协议保留在 M1_ACCEPTANCE.md。

## 仓库中可回查的测试材料

[Unity 7 项结果](test-results/playmode.xml)、[服务 54 项结果](test-results/service.xml)、[Windows 测试结果](test-results/windows-e1.json)、[Windows 实际画面](test-results/windows-e1.png)。这些材料仅来自明确的 TEST_FIXTURE，不包含真人对话或 API 凭据。其他 artifacts 路径指开发机本地记录，不随源码上传。
