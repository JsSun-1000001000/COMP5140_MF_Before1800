using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Before1800.PartB
{
    // F2 opts into the loopback gateway. Existing local topics still work with the gateway off.
    public sealed class PartCDialogue : MonoBehaviour
    {
        private string gateway = "http://127.0.0.1:8180/npc/";
        private HttpClient gatewayHttp;
        private Input activeInput;
        private sealed class GatewayResponse { public string Body, Failure; }
        [Serializable] internal sealed class Entry { public string role, content; }
        [Serializable] internal sealed class Context
        {
            public bool allow_trust_resolution, escape_agreed, route_confirmed, withdraw_used, conversation_needs_break;
            public bool allow_resolved_closing, allow_break_followup;
            public string[] narrative_observations, relationship_memory, lin_private_knowledge;
            public string cooperation_mode, game_time, output_language;
            public string[] known_targets, allowed_actions, observations, player_discovered, verified_evidence;
            public ConcernSnapshot concerns;
            public Entry[] recent_dialogue;
            public ActionResultSnapshot last_action_result;
        }
        [Serializable] internal sealed class Input
        {
            public string session_id, request_id, turn_id, trigger = "user", player_input;
            public long state_version;
            public Context context;
        }
        [Serializable] internal sealed class Decision { public string dialogue, action, target_id, concern_id, concern_status, basis_turn_id; }
        [Serializable] internal sealed class Output
        {
            public string session_id, request_id, turn_id, model, provider;
            public long state_version;
            public Decision decision;
        }
        [Serializable] private sealed class Cancellation { public string session_id, request_id; }
        private readonly List<Entry> history = new List<Entry>();
        private PartBScene scene;
        private Guid currentRun;
        private int conversationNumber, plainReplyCount;
        internal const int PlainReplyLimit = 6;
        private string lastClosingLine;
        private bool live, panel, ready, proactiveRequest;
        private string proactiveObservation;
        private readonly List<string> memories = new List<string>();
        private string serviceLabel = "正在连接对话服务";
        private float nextHealth;
        public bool IsLive => live;
        public bool Ready => ready;
        public string ConnectionStatus => serviceLabel;
        [Serializable] private sealed class Health { public bool ready; public string model, provider; }
        private string sent = "", received = "", status = "";
        private Vector2 scroll;
        private GUIStyle textStyle;
        public string Outcome => status;
        public string LastPayload => sent;
        public string LastResponse => received;

        private void Awake()
        {
            scene = GetComponent<PartBScene>();
            // Player traffic is local IPC. Windows' desktop proxy can return its own 502
            // for a loopback POST without ever reaching the game's Python process.
            gatewayHttp = new HttpClient(new HttpClientHandler {
                UseProxy = false, AllowAutoRedirect = false, UseCookies = false
            }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 65536 };
            const string flag = "--lin-gateway=";
            var address = Environment.GetEnvironmentVariable("BEFORE1800_LIN_GATEWAY");
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(flag, StringComparison.Ordinal)) address = arg.Substring(flag.Length);
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback &&
                uri.Scheme == Uri.UriSchemeHttp && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" &&
                uri.Query.Length == 0 && uri.Fragment.Length == 0)
            {
                gateway = uri.GetLeftPart(UriPartial.Authority) + "/npc/";
                SetLive(true);
            }
        }

        private void Start()
        {
            SetLive(true);
            StartCoroutine(CheckHealth());
        }

        private IEnumerator CheckHealth()
        {
            nextHealth = Time.unscaledTime + 15f;
            var check = SendToGateway("/health", null, 4);
            while (!check.IsCompleted) yield return null;
            var result = check.GetAwaiter().GetResult();
            Health health = null;
            if (result.Failure == null)
                try { health = JsonUtility.FromJson<Health>(result.Body); } catch (ArgumentException) { }
            ready = health != null && health.ready;
            serviceLabel = ready ? "" : "暂时联系不上，请稍后再说。你的话会保留。";
        }

        public void SetLive(bool value)
        {
            if (live == value || scene.ModelWait) return;
            live = value;
            if (live) scene.PlayerLineRequested += Request;
            else scene.PlayerLineRequested -= Request;
        }

        private void Update()
        {
            if (Application.isEditor && InputKey(KeyCode.F2)) SetLive(!live);
            if (Application.isEditor && InputKey(KeyCode.F3)) panel = !panel;
            if (!scene.ModelWait && Time.unscaledTime >= nextHealth) StartCoroutine(CheckHealth());
            if (live && ready && scene.Story && !scene.State.IsPaused && !scene.State.IsTerminal &&
                scene.State.Pending == null && scene.State.Active == null)
            {
                var observation = scene.Story.ConsumeProactiveObservation() ?? GetComponent<ResidenceMemories>()?.Consume();
                if (!string.IsNullOrEmpty(observation))
                {
                    scene.ToggleDialogue();
                    proactiveRequest = true;
                    proactiveObservation = observation;
                    scene.TrySendPlayerLine("（现场观察触发）", out _);
                    proactiveRequest = false;
                }
            }
        }

        private static bool InputKey(KeyCode key) => UnityEngine.Input.GetKeyDown(key);

        private void OnDestroy()
        {
            if (scene) scene.PlayerLineRequested -= Request;
            if (gatewayHttp == null) return;
            gatewayHttp.CancelPendingRequests();
            if (activeInput == null) gatewayHttp.Dispose();
            else _ = CancelAndDispose(activeInput);
        }

        private async Task CancelAndDispose(Input input)
        {
            try { await CancelOnGateway(input).ConfigureAwait(false); }
            finally { gatewayHttp.Dispose(); }
        }

        private void Request(ModelRequest ticket, string line)
        {
            StartCoroutine(PrepareTurn(ticket, line));
        }

        // Freeze the request body before the transport coroutine can yield.
        internal Input BuildInput(ModelRequest ticket, string line)
        {
            if (currentRun != ticket.RunId)
            {
                history.Clear();
                memories.Clear();
                plainReplyCount = 0;
                lastClosingLine = null;
                currentRun = ticket.RunId;
            }
            if (conversationNumber != scene.ConversationNumber)
            {
                conversationNumber = scene.ConversationNumber;
                plainReplyCount = 0;
            }
            var s = ticket.Snapshot;
            var targets = new List<string> { "player" };
            var actions = new List<string> { "reply" };
            bool mayClose = !proactiveRequest && scene.State.CanEndConversation;
            if (mayClose) actions.Add("end_conversation");
            bool canMove = scene.State.Pending == null && scene.State.Cooperation != CooperationMode.Withdrawn
                && (scene.State.Active == null || scene.State.Active.Action == "follow");
            if (canMove && !scene.State.EscapeAgreed)
            {
                actions.Add("follow");
                if (!scene.State.E1Verified && scene.World.CanInspect(SessionState.CameraTarget)) targets.Add(SessionState.CameraTarget);
                if (!scene.State.E4Verified && scene.World.CanInspect(SessionState.BroadcastTarget)) targets.Add(SessionState.BroadcastTarget);
                if (!scene.State.E5Verified && scene.World.CanInspect(SessionState.SetBackTarget)) targets.Add(SessionState.SetBackTarget);
                if (scene.Story)
                    foreach (var target in scene.Story.KnownStoryTargets)
                        if (!targets.Contains(target) && scene.Story.CanVisit(target)) targets.Add(target);
                var everyday = GetComponent<ResidenceMemories>();
                if (everyday) foreach(var place in everyday.Places) if(scene.World.CanInspect(place.Id)) targets.Add(place.Id);
                if (targets.Count > 1) actions.Add("inspect");
                if (!scene.State.WithdrawUsed && !scene.State.EscapeAgreed) actions.Add("withdraw");
                if (scene.State.CanAgreeEscape) actions.Add("agree_escape");
            }

            if (canMove && scene.State.EscapeAgreed) actions.Add("follow");
            var observations = new List<string> { "当前动作阶段：" + s.action_stage };
            if (!proactiveRequest && lastClosingLine != null)
                observations.Add("这是你主动收住上次谈话后，朋友再次来找你。你收尾时原话是：“" + lastClosingLine +
                    "”。若有新发现或新问题，可以接着谈；若只是重复催促，你可以坚持刚才的界限。");
            if(scene.State.E1Verified) observations.Add("已完成且不撤销的亲见：你换位时亲眼看见镜头外壳转向自己。不要再说你没看过摄像头。");
            if(scene.State.E4Verified) observations.Add("已完成且不撤销的亲见：你点播节目命中，再改口，电视也跟着变。不要再要求重做电视实验。");
            if(scene.State.E5Verified) observations.Add("已完成且不撤销的亲见：你亲眼看清墙后的布景支架和制作标记。不要再说还没有见过布景。");
            var clock = s.clock_rehearsal;
            if (clock != null && clock.invited_to_watch)
                observations.Add("朋友邀请你去厨房看钟。");
            if (clock != null && clock.witnessed_three_ticks)
            {
                observations.Add("你亲眼看见厨房挂钟的秒针走过三格，又退回并重复相同的三格。朋友当时手里有遥控器。");
                if (clock.answer_about_remote == "admitted_rehearsal")
                    observations.Add("你问过朋友怎么知道按键，对方承认自己曾排练过。");
                else if (clock.answer_about_remote == "evaded")
                    observations.Add("你问过朋友怎么知道按键，对方岔开了话题，没有回答。");
            }
            if (scene.World.Tv && (scene.World.Tv.HasPrediction || Vector3.Distance(scene.World.Lin.position, scene.World.Tv.transform.position) < 4f || scene.State.E4Verified))
            {
                var tv = scene.World.Tv;
                observations.Add(tv.HasPrediction
                    ? "朋友当面登记的电视预测已锁定：" + TvStation.LabelFor(tv.PredictedProgram) + "（" + tv.PredictedEventId + "）；阶段=" + tv.Phase + "。你已经听见预测内容，不要重复问预测是什么；尚未完成的实验仍需到电视前亲自验证。"
                    : "客厅电视可做 E4 预测实验；尚未锁定预测。");
                if (tv.DisplayedProgram != TvProgram.None)
                    observations.Add("电视当前画面：" + TvStation.LabelFor(tv.DisplayedProgram));
            }
            if (scene.World.Station)
                observations.Add(scene.World.Station.DoorOpen ? "维修门已打开；门开本身不能证明路线或布景。" : "现场维修门关闭。");
            if (proactiveRequest && !string.IsNullOrEmpty(proactiveObservation)) observations.Add(proactiveObservation);
            observations.Add("林与朋友距离：" + Vector3.Distance(scene.World.Lin.position, scene.World.Player.position).ToString("F1") + "米。只有共同验证的事实才算林亲见，玩家自称完成不算。");
            var privateKnowledge = new List<string>(scene.Story ? scene.Story.LinPrivateKnowledge : new string[0]);
            var daily = GetComponent<ResidenceMemories>();
            if(daily) { privateKnowledge.AddRange(daily.Knowledge); if(!string.IsNullOrEmpty(daily.Observation)) observations.Add(daily.Observation); }
            return new Input {
                session_id = s.run_id, request_id = ticket.RequestId.ToString("N"), turn_id = ticket.RequestId.ToString("N"),
                state_version = s.state_version, player_input = proactiveRequest ? "" : line, trigger = proactiveRequest ? "proactive" : "user",
                context = new Context {
                    output_language = GameLanguage.Code, known_targets = targets.ToArray(), allowed_actions = actions.ToArray(), observations = observations.ToArray(),
                    verified_evidence = s.verified_evidence, player_discovered = s.player_discoveries, concerns = s.concerns,
                    route_confirmed = s.route_confirmed, escape_agreed = s.escape_agreed, cooperation_mode = s.cooperation_mode,
                    game_time = s.time_hms, last_action_result = s.last_action_result, recent_dialogue = history.ToArray(),
                    conversation_needs_break = mayClose && plainReplyCount >= PlainReplyLimit,
                    allow_break_followup = mayClose && plainReplyCount == PlainReplyLimit,
                    allow_resolved_closing = mayClose,
                    allow_trust_resolution = s.concerns.C2 == "open", withdraw_used = scene.State.WithdrawUsed,
                    narrative_observations = scene.Story ? scene.Story.StoryObservations : new string[0],
                    lin_private_knowledge = privateKnowledge.ToArray(),
                    relationship_memory = memories.ToArray()
                }
            };
        }

        internal IEnumerator PrepareTurn(ModelRequest ticket, string line)
        {
            var input = BuildInput(ticket, line);
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Input)).WriteObject(buffer, input);
                sent = Encoding.UTF8.GetString(buffer.ToArray());
            }
            received = "";
            status = "林正在考虑你的话…";
            return Decide(ticket, input, sent);
        }

        private async Task<GatewayResponse> SendToGateway(string endpoint, string json, int seconds,
            CancellationToken cancellation = default)
        {
            if (!Uri.TryCreate(gateway, UriKind.Absolute, out var root) ||
                !Uri.TryCreate(root, endpoint, out var address) || address.Scheme != Uri.UriSchemeHttp ||
                !address.IsLoopback || address.UserInfo.Length != 0)
                return new GatewayResponse { Failure = "本机对话服务地址无效" };
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var request = new HttpRequestMessage(json == null ? HttpMethod.Get : HttpMethod.Post, address))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
                    if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (var response = await gatewayHttp.SendAsync(request, deadline.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return new GatewayResponse { Failure = "HTTP " + (int)response.StatusCode };
                        return new GatewayResponse { Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false) };
                    }
                }
            }
            catch (OperationCanceledException) { return new GatewayResponse { Failure = "等待本机对话服务超时" }; }
            catch (HttpRequestException) { return new GatewayResponse { Failure = "无法连接本机对话服务" }; }
            catch (IOException) { return new GatewayResponse { Failure = "无法连接本机对话服务" }; }
            catch (ObjectDisposedException) { return new GatewayResponse { Failure = "无法连接本机对话服务" }; }
        }

        private Task<GatewayResponse> CancelOnGateway(Input input)
        {
            return SendToGateway("cancel", JsonUtility.ToJson(new Cancellation {
                session_id = input.session_id, request_id = input.request_id
            }), 4);
        }

        private IEnumerator Decide(ModelRequest ticket, Input input, string json)
        {
            activeInput = input;
            using (var cancellation = new CancellationTokenSource())
            {
                var decision = SendToGateway("decide", json, 70, cancellation.Token);
                while (!decision.IsCompleted && scene.IsCurrentRequest(ticket)) yield return null;
                if (activeInput == input) activeInput = null;
                if (!scene.IsCurrentRequest(ticket))
                {
                    cancellation.Cancel();
                    _ = CancelOnGateway(input);
                    yield break;
                }
                var result = decision.GetAwaiter().GetResult();
                if (result.Failure != null)
                {
                    ready = false;
                    serviceLabel = "连接暂时中断 · 文字已保留，可重试";
                    Reject(ticket, result.Failure);
                    if (result.Failure == "等待本机对话服务超时") _ = CancelOnGateway(input);
                    yield break;
                }

                received = result.Body;
                ApplyGatewayResponse(ticket, input, received);
            }
        }

        internal void ApplyGatewayResponse(ModelRequest ticket, Input input, string responseJson)
        {
            if (!scene.IsCurrentRequest(ticket)) return;
            Output answer;
            try { answer = JsonUtility.FromJson<Output>(responseJson); }
            catch (ArgumentException) { answer = null; }
            var d = answer?.decision;
            if (d == null || answer.session_id != input.session_id || answer.request_id != input.request_id
                || answer.turn_id != input.turn_id || answer.state_version != input.state_version)
            {
                Reject(ticket, "回复与本轮请求不匹配");
                return;
            }
            string target = string.IsNullOrEmpty(d.target_id) ? null : d.target_id;
            bool resolve = d.concern_status == "resolved" && d.basis_turn_id == input.turn_id
                && (d.action == "reply" || (input.context.allow_resolved_closing && d.action == "end_conversation"))
                && input.trigger == "user" && scene.State.CanResolveConcern(d.concern_id);
            bool keep = d.concern_status == "keep" && string.IsNullOrEmpty(d.concern_id) && string.IsNullOrEmpty(d.basis_turn_id);
            if ((!keep && !resolve) || Array.IndexOf(input.context.allowed_actions, d.action) < 0
                || (input.context.conversation_needs_break && !input.context.allow_break_followup && keep && d.action == "reply")
                || (target != null && Array.IndexOf(input.context.known_targets, target) < 0)
                || (input.trigger == "proactive" && d.action != "reply" && d.action != "inspect"))
            {
                Reject(ticket, "动作或顾虑提议超出本轮范围");
                return;
            }
            if (!scene.TryReceivePlayerReply(ticket, d.dialogue, d.action, target, out var reason))
            {
                Reject(ticket, reason);
                return;
            }
            if (resolve) scene.State.TryResolveConcernFromAcceptedTurn(d.concern_id, input.turn_id, input.player_input);
            if (input.trigger == "user") history.Add(new Entry { role = "user", content = input.player_input });
            history.Add(new Entry { role = "assistant", content = d.dialogue });
            if (history.Count > 36) history.RemoveRange(0, history.Count - 36);
            if (input.trigger == "user")
            {
                plainReplyCount = d.action == "reply" && keep ? plainReplyCount + 1 : 0;
                lastClosingLine = d.action == "end_conversation" ? d.dialogue.Substring(0, Math.Min(500, d.dialogue.Length)) : null;
                memories.Add("朋友曾原话说：" + input.player_input.Substring(0, Math.Min(500, input.player_input.Length)));
                memories.Add("你当时原话回答：" + d.dialogue.Substring(0, Math.Min(500, d.dialogue.Length)));
                // Preserve the inciting exchange and the most recent exchanges verbatim, without invented summaries.
                while (memories.Count > 24) memories.RemoveAt(6);
            }
            if (input.trigger == "proactive") scene.GetComponent<GreyboxHUD>().Flow.AcceptProactiveReply(d.dialogue, d.action);
            GetComponent<ResidenceAudio>()?.PlayCue("dialogue");
            status = answer.model + " 已接受 " + d.action + " " + target + (resolve ? " · " + d.concern_id + " 已回应" : "");
            Debug.Log("[Part C] " + input.request_id + " " + status);
        }

        private void Reject(ModelRequest request, string reason)
        {
            scene.TryCancelModelRequest(request);
            status = "未执行：" + reason;
            if (scene.DialogueOpen) scene.GetComponent<GreyboxHUD>()?.Flow?.ShowServiceError(reason);
            Debug.LogWarning("[Part C] " + status);
        }

        private void OnGUI()
        {
            if(!Application.isEditor || !panel) return;
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label) { font = PlayerUIFont.Get(), fontSize = 14, wordWrap = true, richText = false };
            float width = Mathf.Min(Screen.width - 20, panel ? 570 : 310);
            GUILayout.BeginArea(new Rect(Screen.width - width - 10, 6, width, panel ? Screen.height * .74f : 32), GUI.skin.box);
            GUILayout.Label(live ? serviceLabel : "离线探索 · F2 连接对话", textStyle);
            if (panel)
            {
                scroll = GUILayout.BeginScrollView(scroll);
                GUILayout.Label(status + "\n发送\n" + sent + "\n收到\n" + received, textStyle);
                if (scene.State != null)
                {
                    var snapshot = scene.State.ReadSnapshot();
                    GUILayout.Label("当前状态  E=" + string.Join(",", snapshot.verified_evidence)
                        + "  路线=" + snapshot.route_confirmed + "  C2=" + snapshot.concerns.C2, textStyle);
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
    }
}
