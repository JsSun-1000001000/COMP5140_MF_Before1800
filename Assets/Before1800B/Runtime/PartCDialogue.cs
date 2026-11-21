using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Before1800.PartB
{
    // F2 opts into the loopback gateway. Existing local topics still work with the gateway off.
    public sealed class PartCDialogue : MonoBehaviour
    {
        private const string Gateway = "http://127.0.0.1:8180/npc/";
        [Serializable] private sealed class Entry { public string role, content; }
        [Serializable] private sealed class Context
        {
            public bool allow_trust_resolution, escape_agreed, route_confirmed;
            public string cooperation_mode, game_time;
            public string[] known_targets, allowed_actions, observations, player_discovered, verified_evidence;
            public ConcernSnapshot concerns;
            public Entry[] recent_dialogue;
            public ActionResultSnapshot last_action_result;
        }
        [Serializable] private sealed class Input
        {
            public string session_id, request_id, turn_id, trigger = "user", player_input;
            public long state_version;
            public Context context;
        }
        [Serializable] private sealed class Decision { public string dialogue, action, target_id, concern_id, concern_status, basis_turn_id; }
        [Serializable] private sealed class Output
        {
            public string session_id, request_id, turn_id, model, provider;
            public long state_version;
            public Decision decision;
        }
        [Serializable] private sealed class Cancellation { public string session_id, request_id; }
        private readonly List<Entry> history = new List<Entry>();
        private PartBScene scene;
        private Guid currentRun;
        private bool live, panel;
        private string sent = "", received = "", status = "";
        private Vector2 scroll;
        private GUIStyle textStyle;
        public string Outcome => status;
        public string LastPayload => sent;
        public string LastResponse => received;

        private void Awake() { scene = GetComponent<PartBScene>(); }

        public void SetLive(bool value)
        {
            if (live == value || scene.ModelWait) return;
            live = value;
            if (live) scene.PlayerLineRequested += Request;
            else scene.PlayerLineRequested -= Request;
        }

        private void Update()
        {
            if (InputKey(KeyCode.F2)) SetLive(!live);
            if (InputKey(KeyCode.F3)) panel = !panel;
        }

        private static bool InputKey(KeyCode key) => UnityEngine.Input.GetKeyDown(key);

        private void OnDestroy()
        {
            if (scene) scene.PlayerLineRequested -= Request;
        }

        private void Request(ModelRequest ticket, string line)
        {
            if (currentRun != ticket.RunId)
            {
                history.Clear();
                currentRun = ticket.RunId;
            }
            var s = ticket.Snapshot;
            var targets = new List<string> { "player" };
            var actions = new List<string> { "reply" };
            bool canMove = scene.State.Pending == null && scene.State.Cooperation != CooperationMode.Withdrawn
                && (scene.State.Active == null || scene.State.Active.Action == "follow");
            if (canMove)
            {
                actions.Add("follow");
                if (!scene.State.E1Verified && scene.World.CanInspect(SessionState.CameraTarget)) targets.Add(SessionState.CameraTarget);
                if (!scene.State.E4Verified && scene.World.CanInspect(SessionState.BroadcastTarget)) targets.Add(SessionState.BroadcastTarget);
                if (!scene.State.E5Verified && scene.World.CanInspect(SessionState.SetBackTarget)) targets.Add(SessionState.SetBackTarget);
                if (targets.Count > 1) actions.Add("inspect");
            }

            var observations = new List<string> { "当前动作阶段：" + s.action_stage };
            if (scene.World.Tv)
            {
                var tv = scene.World.Tv;
                observations.Add(tv.HasPrediction
                    ? "电视预测已锁定：" + tv.PredictedEventId + "；阶段=" + tv.Phase
                    : "客厅电视可做 E4 预测实验；尚未锁定预测。");
                if (tv.DisplayedProgram != TvProgram.None)
                    observations.Add("电视当前画面：" + TvStation.LabelFor(tv.DisplayedProgram));
            }
            if (scene.World.Station)
                observations.Add(scene.World.Station.DoorOpen ? "维修门已打开；门开本身不能证明路线或布景。" : "现场维修门关闭。");
            var input = new Input {
                session_id = s.run_id, request_id = ticket.RequestId.ToString("N"), turn_id = ticket.RequestId.ToString("N"),
                state_version = s.state_version, player_input = line,
                context = new Context {
                    known_targets = targets.ToArray(), allowed_actions = actions.ToArray(), observations = observations.ToArray(),
                    verified_evidence = s.verified_evidence, player_discovered = s.player_discoveries, concerns = s.concerns,
                    route_confirmed = s.route_confirmed, escape_agreed = s.escape_agreed, cooperation_mode = s.cooperation_mode,
                    game_time = s.time_hms, last_action_result = s.last_action_result, recent_dialogue = history.ToArray(),
                    allow_trust_resolution = s.concerns.C2 == "open"
                }
            };
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Input)).WriteObject(buffer, input);
                sent = Encoding.UTF8.GetString(buffer.ToArray());
            }
            received = "";
            status = "正在等待 DeepSeek";
            StartCoroutine(Decide(ticket, input, sent));
        }

        private static UnityWebRequest Post(string endpoint, string json)
        {
            var http = new UnityWebRequest(Gateway + endpoint, "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(), timeout = 26
            };
            http.SetRequestHeader("Content-Type", "application/json");
            return http;
        }

        private IEnumerator Decide(ModelRequest ticket, Input input, string json)
        {
            using (var http = Post("decide", json))
            {
                var op = http.SendWebRequest();
                while (!op.isDone && scene.IsCurrentRequest(ticket)) yield return null;
                if (!scene.IsCurrentRequest(ticket))
                {
                    http.Abort();
                    using (var cancel = Post("cancel", JsonUtility.ToJson(new Cancellation {
                        session_id = input.session_id, request_id = input.request_id })))
                        yield return cancel.SendWebRequest();
                    yield break;
                }
                received = http.downloadHandler.text;
                if (http.result != UnityWebRequest.Result.Success)
                {
                    Reject(ticket, "网关 " + http.responseCode + " " + http.error);
                    yield break;
                }

                Output answer;
                try { answer = JsonUtility.FromJson<Output>(received); }
                catch (ArgumentException) { answer = null; }
                var d = answer?.decision;
                if (d == null || answer.session_id != input.session_id || answer.request_id != input.request_id
                    || answer.turn_id != input.turn_id || answer.state_version != input.state_version)
                {
                    Reject(ticket, "回复与本轮请求不匹配");
                    yield break;
                }
                string target = string.IsNullOrEmpty(d.target_id) ? null : d.target_id;
                bool resolve = d.concern_status == "resolved" && d.concern_id == "C2" && d.basis_turn_id == input.turn_id
                    && d.action == "reply" && input.context.allow_trust_resolution;
                bool keep = d.concern_status == "keep" && string.IsNullOrEmpty(d.concern_id) && string.IsNullOrEmpty(d.basis_turn_id);
                if ((!keep && !resolve) || Array.IndexOf(input.context.allowed_actions, d.action) < 0
                    || (target != null && Array.IndexOf(input.context.known_targets, target) < 0))
                {
                    Reject(ticket, "动作或顾虑提议超出本轮范围");
                    yield break;
                }
                if (!scene.TryReceivePlayerReply(ticket, d.dialogue, d.action, target, out var reason))
                {
                    Reject(ticket, reason);
                    yield break;
                }
                if (resolve) scene.State.ResolveTrustFromAcceptedTurn(input.turn_id);
                history.Add(new Entry { role = "user", content = input.player_input });
                history.Add(new Entry { role = "assistant", content = d.dialogue });
                if (history.Count > 8) history.RemoveRange(0, 2);
                status = answer.model + " 已接受 " + d.action + " " + target + (resolve ? " · C2 已解决" : "");
                Debug.Log("[Part C] " + input.request_id + " " + status);
            }
        }

        private void Reject(ModelRequest request, string reason)
        {
            scene.TryCancelModelRequest(request);
            status = "未执行：" + reason;
            Debug.LogWarning("[Part C] " + status);
        }

        private void OnGUI()
        {
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label) { font = PlayerUIFont.Get(), fontSize = 14, wordWrap = true, richText = false };
            float width = Mathf.Min(Screen.width - 20, panel ? 570 : 310);
            GUILayout.BeginArea(new Rect(Screen.width - width - 10, 6, width, panel ? Screen.height * .74f : 32), GUI.skin.box);
            GUILayout.Label(live ? "DeepSeek 已启用   F2 切换 · F3 请求记录" : "本地话题   F2 启用 DeepSeek · F3 请求记录", textStyle);
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
