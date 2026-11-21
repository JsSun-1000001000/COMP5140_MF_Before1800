# COMP5140 · Part C 开发交接

截至 2026-11-21，Part C 已在 Unity 灰盒跑通三轮真实 DeepSeek 对话。第一轮玩家邀请林检查维修门，第二轮林依据亲眼看到的布景回答，第三轮玩家说明自己曾参与隐瞒，林可以接受解释。验收结束时可以同时看到 `E5` 已验证、`route_confirmed=false`、C2 已解决、`escape_agreed=false`。

## 在新电脑上复现

1. 在项目仓库中运行 `git fetch origin` 和 `git switch main`（Part C 已于 2026-11-21 合入 `main`）。
2. 使用 Windows、Unity **2021.3.35f1** 和 Python **3.12–3.14**。从仓库根目录在 PowerShell 运行 `.\service\setup.ps1`。脚本会建立虚拟环境，并在首次安装时从模板生成 `service/.env`。在该文件的 `LLM_API_KEY` 填自己可用的 DeepSeek 密钥，其余可使用模板中的 `deepseek-flash` 配置。密钥只保存在网关所在电脑。
3. **编辑器试玩**：运行 `.\service\start.ps1 -Port 8180` 并保留窗口。打开 `Assets/Before1800B/PartB_Greybox.unity`，点 Play。**Windows 独立试玩**：在 Unity 菜单选择 **Before 1800 → Build Part C Windows Demo**，完成后双击项目根目录的 `Run-PartC.cmd`。新克隆需要先做这次构建。
4. 开始探索后按 **F2** 启用 DeepSeek，面对林按 **E**，在自由输入框发送台词。**F3** 显示请求、实际回复、接受结果和现场状态，**J** 查看手记。三个可复制的输入、门控操作和预期状态见[本地演示与六幕剧本](PartC_本地演示.md)。

## 接手代码时从哪里看

| 接口或文件 | 当前行为与接手要点 |
|---|---|
| [Unity 适配器](../Assets/Before1800B/Runtime/PartCDialogue.cs) | 订阅 B 的 `PlayerLineRequested`，从本轮快照构造已验证证据、玩家发现、路线、最近对话及上一项动作结果。只把当下可用的动作和目标交给模型。当前调查目标为摄像头 `camera_test` 和灰盒维修门 `set_back`。 |
| [Python 网关](../service/app/main.py)、[协议校验](../service/app/schemas.py) | Unity 调用本机 `POST /npc/decide`，取消用 `POST /npc/cancel`。模型输出六个字段：`dialogue`、`action`、`target_id`、`concern_id`、`concern_status`、`basis_turn_id`。当前支持 `reply`、`follow`、`inspect`；C2 解决必须随 `reply` 返回并引用实际玩家回合。 |
| [角色提示](../service/prompts/lin_system.txt)、[模型调用](../service/app/provider.py) | 只有 Unity 的现场数据可以确立事实。模型判断玩家是否回应了林的具体疑问，可以追问、提议调查或提出信任修复。改提示后重启本机网关再测。 |
| [B 的接收入口](../Assets/Before1800B/Runtime/PartBScene.cs)、[状态](../Assets/Before1800B/Runtime/SessionState.cs) | 把原请求与回包交给 `TryReceivePlayerReply`，再次检查回合、动作与现场条件。林实际完成观察才记录 E5，通道检查单独记录路线；过期回包不能改变新局。 |

## 如何验证和排错

- **离线回归**：从仓库根目录运行 `.\service\test.ps1`。本次交接复核 57 项通过。
- **真实模型与场景**：已有记录为 31 项 PlayMode 测试通过，提示更新后单独复跑 `DeepSeekDoorAndTrustDemo` 也通过。复现此项时先启动 8180 网关，在启动 Unity 的 PowerShell 设置 `$env:COMP5140_LIVE_DEMO='1'`，在 Test Runner 的 PlayMode 页运行该用例。它会产生真实 API 调用；离线测试可独立运行。
- **点发送没有模型回复**：先看右上角是否已启用 DeepSeek，再用 `Invoke-RestMethod http://127.0.0.1:8180/health` 检查网关。健康检查只验证本地配置；实际鉴权、余额、超时与模型输出问题看 F3 和本机 `service/logs/requests.jsonl`。提交或打包前排除日志和 `.env`。
- **林到了门口但没有 E5**：确认处于灰盒、门已开、林已就位；对准金色控制器按 E 开始观察，给林留出约两秒视线。检查证据时先保留未查路线，第二轮就能观察到两种状态的区别。

## 下一位成员从哪里接

| 对接人 | 下一项工作 | 验收点 |
|---|---|---|
| A + C | 约定 E4 广播目标和事件顺序，把提前登记、实际播报、林改道和播报更新传入请求。E1 的开发模式 V 键模拟完成事件，正式演示需要 A 的设备回调。 | 玩家事后说“我猜到了”不能补写预测；预测和干预都完成后才写入 E4。 |
| B + C | 扩展 C1、C3、一次退开与恢复、`agree_escape` 的双方协议和状态转换。 | 足够事实才能解决 C1；路线确认后才能回应 C3；同意离开与两人于 18:00 前到达出口分开验收。 |
| D + C | 按剧本固定十个情境，每个跑三次，留存真实输入、模型回复和执行结果。 | 分别记录过度轻信、无故拒绝、虚构现场事实。录像建议并排展示同一句问题在 E5 验证前后的回答。 |
