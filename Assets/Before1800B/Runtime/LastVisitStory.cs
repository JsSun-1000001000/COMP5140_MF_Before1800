using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    // Authored things, perceptions and consequences. Interpretation stays with the model.
    // Nothing in this component turns a keyword or a document into emotional consent.
    public sealed class LastVisitStory : MonoBehaviour
    {
        sealed class Paper
        {
            public string Id, Title, Text;
            public Transform Prop;
            public bool PlayerRead, LinRead;
        }
        readonly List<Paper> papers = new List<Paper>();
        readonly List<Material> materials = new List<Material>();
        readonly HashSet<string> dwellReported = new HashSet<string>();
        PartBScene scene;
        Guid run;
        PauseToken readingPause;
        string readingId, queuedObservation, dwellId;
        float dwellTime, inspectTime;
        ActionTicket readingTicket;
        bool initialized;
        Transform exitFocus, exitGoal;
        public string ReadableTitle { get; private set; }
        public string ReadableText { get; private set; }
        public bool ReadingOpen => readingPause != null;
        public bool IsReading => ReadingOpen;
        public bool LettersCompared { get; private set; }
        public bool FirstAnswerSent { get; set; }
        public bool LetterComparison => ReadingOpen && readingId == "letter_comparison";
        public Transform PaperFocus(string id) => Find(id)?.Prop;
        public bool ReadingExit => ReadingOpen && readingId == "exit_door";
        public bool IsEnding => scene && scene.State != null && scene.State.IsTerminal;
        public bool CanLeaveTogether => scene && scene.State.EscapeAgreed && AtExit(scene.World.Player) && AtExit(scene.World.Lin);
        public bool CanRespectStay => scene && scene.State.E5Verified && !scene.State.EscapeAgreed;
        public bool CanShareReading => ReadingOpen && !ReadingExit && Find(readingId) != null &&
            Find(readingId).PlayerRead && !Find(readingId).LinRead && Vector3.Distance(scene.World.Player.position, scene.World.Lin.position) < 3.2f &&
            InteractionSight.Clear(scene.World.ViewCamera.transform.position,
                scene.World.Lin.position + Vector3.up * 1.3f - scene.World.ViewCamera.transform.position, scene.World.Player, scene.World.Lin);
        public string[] KnownStoryTargets => new[] { "sealed_letter", "parent_reply", "parents_diary", "friend_postcard", "exit_door" };
        public string Objective
        {
            get
            {
                if (!scene || scene.State == null) return "";
                if (IsEnding) return "这次拜访已经结束。";
                if (!LettersCompared) return "① 看懂两封信 · 沿标记走到茶几，对准信封按 E。";
                if (!FirstAnswerSent) return "② 回应林 · 走近她按 E。回信是你写的，你准备怎么解释？";
                if (scene.State.EscapeAgreed) return "林已选择同行 · 带她走到前门，面对出口牌按 E。";
                if (!scene.State.E1Verified) return "可以照常告别，也可以调查 · 与林谈谈信中只有她知道的细节。";
                if (!scene.State.E4Verified) return "镜头为什么在跟着她？· 试着用电视预测检验另一种解释。";
                if (!scene.State.E5Verified) return "前门旁的墙板有个黄铜卡扣 · 打开它，邀请林亲眼看支架和制作编号。";
                if (!scene.State.RouteConfirmed) return "确认出路 · 站到前门地面的金色线，面对出口牌按 E 检查。";
                if (!scene.State.IsConcernResolved("C2")) return "证据已经在眼前 · 林仍需要听你解释那些代写的信。";
                if (!scene.State.IsConcernResolved("C1") || !scene.State.IsConcernResolved("C3")) return "最后一场对话 · 回应她对事实与未来的疑问，走不走仍由她决定。";
                return "把选择交还给林 · 与她讨论是否现在同行。";
            }
        }
        public string EndingTitle => !scene ? "" : scene.State.Ending == "together" ? "一起走出镜头" :
            scene.State.Ending == "ordinary_departure" ? "最后一封回信" : scene.State.Ending == "respected_stay" ? "把决定留给她" : "18 点，灯亮了";
        public string EndingText
        {
            get
            {
                if (!scene) return "";
                switch (scene.State.Ending)
                {
                    case "together": return "门外没有预先写好的下一幕。林带上了那封还没有寄出的信。\n\n她已看过镜头、电视与布景，也听过你对那些回信的解释。你们没有保证外面一定更好；她选择与你一起面对。\n\n离场时间：" + scene.State.TimeHms + "\n这段关系曾被安排，但接下来的一步属于你们。";
                    case "ordinary_departure": return scene.State.VerifiedEvidence.Length == 0
                        ? "你完成了信使的最后一场戏。疑问还留在房间里，她等的回信，也许还会继续由别人写下去。\n\n你照常说了再见。没有人阻止你。那句没有说出口的话，跟着你走出了门。"
                        : "你最终独自走出了门。林已经亲眼看见的事实并没有因此消失；今天揭开的裂缝，也不会因为一句再见重新合上。\n\n你完成了最后一次拜访，却没有与她一起面对门外。她会记得你带她看过什么，以及最后选择了怎样告别。";
                    case "respected_stay": return "你没有把已经揭开的事实变成另一条命令。林知道墙后是什么，今天仍没有选择与你离开。\n\n留下不等于什么都没发生。那封信、被验证的事实和这次谈话，都属于她。你走了；下一步仍由她决定。";
                    default: return "导演回岗，前门的离场窗口关闭。林掌握的事实没有被抹去，你们却没能在今天一起离开。\n\n她会记得你让她亲眼看见的事，也会记得你回答或回避的问题。重开会开始一段新的试玩，不是故事中的时间倒流。";
                }
            }
        }

        void Start() { Bind(GetComponent<PartBScene>()); }
        public void Bind(PartBScene owner)
        {
            if (initialized || !owner || owner.State == null || !owner.World.UsesSceneBindings) return;
            initialized = true;
            scene = owner;
            var cream = Material("Last visit paper", new Color(.89f, .79f, .61f));
            var dark = Material("Last visit ink", new Color(.12f, .19f, .22f));
            var gold = Material("Last visit brass", new Color(.65f, .37f, .12f));
            var red = Material("Last visit seal", new Color(.44f, .12f, .11f));
            AddPaper("sealed_letter", "那封没有寄出的信", "爸、妈：\n\n厨房的钟最近总在第三拍迟一下。我昨晚把窗边那只蓝杯子的缺口转向墙，假装它还是完整的。你们上次说，人不能总躲着缺口。可我还没想好怎么做。\n\n我没有跟别人说过蓝杯子的事。信写完就封上了，等今天信使来取。\n——林", new Vector3(.24f, .705f, -1.16f), new Vector3(.4f, .02f, -.45f), cream, "未寄出", red);
            AddPaper("parent_reply", "今天送来的回信", "林：\n\n把蓝杯子的缺口转向墙，并不等于它不存在。等天气好了，就出去走走吧。\n——爸、妈\n\n信封下压着信使的送达签：今天，最后一场。\n\n玩家知道：父母的扮演者已经永久离场。剧组通过隐藏镜头看见她写信，你照着画面替父母写了回信。这封回信回应的细节，来自尚未寄出的原信。", new Vector3(.79f, .71f, -1.14f), new Vector3(1f, .02f, -.45f), cream, "回信", dark);
            AddPaper("parents_diary", "父亲扮演者留下的笔记", "撤场前夜\n\n今天她又把杯子的缺口转向墙。我差点伸手替她转回来——那个动作不在台词里。\n\n合约结束了，剧组说我们不能再见她。我不知道她会不会原谅这许多年的戏。但我希望有一天能用自己的名字再陪她吃一顿饭。\n\n我想见她，是真的。我不敢保证那一天会来。\n\n（这页只能证明作者当时写下的心意；没有地址、联系方式，也没有对未来安全的保证。）", new Vector3(3.75f, .88f, 1.76f), new Vector3(2.95f, .02f, 1.5f), dark, "私人笔记", gold);
            AddPaper("friend_postcard", "朋友没有寄走的明信片", "林：\n\n你说不敢走到地图的边缘。我也怕。我能保证的只有：如果哪天你想试，我会先听你说完。\n\n你不欠任何人一个勇敢的决定。以后见面时，也可以告诉我今天不想走。\n——阿禾\n\n背面写着一行小字：不要替我选择。", new Vector3(-.14f, .71f, -1.55f), new Vector3(-.65f, .02f, -.9f), cream, "阿禾", dark);
            // A small writing desk makes the crew diary physically inspectable without moving existing furniture.
            Box("Writing stand", new Vector3(3.75f, .43f, 1.76f), new Vector3(.54f, .85f, .42f), cream, true);
            if (!scene.World.Station)
                scene.World.RegisterSetBackStation(SetBackStation.BuildResidence(transform, Node("E5 return anchor", scene.World.Lin.position), cream, dark, gold));
            exitFocus = scene.World.Station.RouteSign;
            exitGoal = Node("Departure waiting point", new Vector3(.5f, .02f, 3.45f));
            scene.World.RegisterStoryTarget("exit_door", exitGoal, exitFocus);
            ResetStory();
        }
        void ResetStory()
        {
            run = scene.State.RunId;
            readingPause = null;
            readingId = ReadableTitle = ReadableText = queuedObservation = dwellId = null;
            readingTicket = null; dwellTime = inspectTime = 0; LettersCompared=FirstAnswerSent=false;
            dwellReported.Clear();
            foreach (var paper in papers) paper.PlayerRead = paper.LinRead = false;
        }
        void Update()
        {
            if (!initialized) return;
            if (run != scene.State.RunId) ResetStory();
            if (scene.State.IsPaused) return;
            var ticket = scene.State.Active;
            if (ticket != null && ticket.Action == "inspect" && SessionState.IsStoryTarget(ticket.TargetId) && !ResidenceMemories.IsTarget(ticket.TargetId) && scene.State.Stage == ActionStage.Arrived)
            {
                if (ticket != readingTicket) { readingTicket = ticket; inspectTime = 0; }
                inspectTime += Time.deltaTime;
                if (inspectTime > 1.5f && scene.World.AtObservation(ticket.TargetId))
                {
                    var paper = Find(ticket.TargetId);
                    if (paper != null) paper.LinRead = true;
                    if (scene.State.TryCompleteStoryInspection(ticket))
                    {
                        scene.World.ResetTravel();
                        queuedObservation = paper == null ? "林亲自走到前门，观察了出口位置。" : "林刚刚亲自读了「" + paper.Title + "」，可以根据内容向朋友追问。";
                    }
                }
            }
            // Dwell is a real scene observation, and only reaches Lin if she can see the player and object.
            var focus = FocusPaper();
            if (focus == null || !LinCanWitness(focus)) { dwellTime = 0; dwellId = null; return; }
            if (dwellId != focus.Id) { dwellId = focus.Id; dwellTime = 0; }
            dwellTime += Time.deltaTime;
            if (dwellTime >= 7f && dwellReported.Add(focus.Id))
                queuedObservation = "你亲眼看见朋友在「" + focus.Title + "」前停留了至少七秒。你不知道她/他在想什么，可以联系自己的经历主动靠近、提问或阅读；不要杜撰玩家心声。";
        }
        bool LinCanWitness(Paper paper)
        {
            var lin = scene.World.Lin;
            if (Vector3.Distance(lin.position, scene.World.Player.position) > 4.5f) return false;
            var eye = lin.position + Vector3.up * 1.5f;
            return InteractionSight.Clear(eye, scene.World.Player.position + Vector3.up - eye, lin, scene.World.Player) &&
                InteractionSight.Clear(eye, paper.Prop.position - eye, lin, paper.Prop);
        }
        public string ConsumeProactiveObservation()
        {
            var message = queuedObservation;
            queuedObservation = null;
            return message;
        }
        public string Hint
        {
            get
            {
                if (!initialized || scene.State.IsPaused) return null;
                var paper = FocusPaper();
                if (paper != null) return "[E] " + (paper.Id == "sealed_letter" && !scene.State.IsConcernResolved("C2") ? "查看封口 · 私人信件由林亲自读" : "阅读 · " + paper.Title);
                if (FacingExit() && (scene.State.RouteConfirmed || !scene.World.Station.DoorOpen)) return "[E] 前门 · 告别 / 同行";
                return null;
            }
        }
        public bool TryInteract()
        {
            if (!initialized || scene.State.IsPaused) return false;
            var paper = FocusPaper();
            if (paper != null)
            {
                if(!LettersCompared && (paper.Id=="sealed_letter" || paper.Id=="parent_reply")) { OpenLetterComparison(); return true; }
                readingId = paper.Id;
                ReadableTitle = paper.Title;
                if (!CanPlayerRead(paper.Id))
                    ReadableText = "封口完好，邮戳的位置还是空白。\n\n林说：「这封信我还没交给你，可回信怎么会知道我昨晚写了什么？」\n\n她把手放在信封上：「先别拆。我想自己读。」\n\n可以邀请林亲自检查原信。她愿意重新信任你之后，你才可以代她阅读。";
                else { paper.PlayerRead = true; ReadableText = paper.Text; }
                readingPause = scene.State.AcquirePause(PauseReason.Journal, "story-reading");
                return true;
            }
            if (!FacingExit() || (!scene.State.RouteConfirmed && scene.World.Station.DoorOpen)) return false;
            readingId = "exit_door";
            ReadableTitle = "今天，怎样说再见";
            ReadableText = scene.State.EscapeAgreed
                ? (CanLeaveTogether ? "林已经走到门边。准备好了，就和她一起走出去。\n\n你也可以返回房间继续交谈。"
                    : "林已经选择与你同行。收起这个页面，站到门前的金色线边，给她留一点位置。她走过来后，再面对出口牌按 E。")
                : "这是你最后一次以信使身份来访。你可以照常告别，完成这场戏；也可以回去听林把话说完。\n\n出口就在这里。她是否与你一起离开，不能由你替她决定。";
            readingPause = scene.State.AcquirePause(PauseReason.Journal, "departure-choice");
            return true;
        }
        public void OpenHelp()
        {
            if(!initialized || ReadingOpen || scene.State.IsTerminal)return;
            readingId="help";ReadableTitle="这一局要做什么？";
            ReadableText="你是信使，也是林的朋友。你替剧组冒充她父母写回信。今天是最后一次送信，她发现了破绽。\n\n目标可以选：照常告别，或帮她查清真相，再让她自己决定是否离开。\n\n调查顺序：茶几上的两封信 → 电视上方的镜头 → 电视 → 前门墙板 → 出口 → 最后的谈话。屏幕左上会提示你当前这一步。\n\nWASD 移动，鼠标或方向键环顾，对准物品或林按 E。林可以自己结束谈话，或说完后带你去看。按 Esc 可以先离开。J 查看手记；H 随时回看玩法。\n\n聊天时直接打你想说的话。最终要回答她对真相、你的欺骗和门外生活的顾虑；收集东西不会自动替她作决定。";
            readingPause=scene.State.AcquirePause(PauseReason.Journal,"play-help");
        }
        public void OpenLetterComparison()
        {
            if(!initialized || ReadingOpen)return;
            LettersCompared=true; Find("parent_reply").PlayerRead=true;
            readingId="letter_comparison";
            ReadableTitle="一封还没寄出的信，怎么有了回信？";
            ReadableText="林说：先别拆。她把回信推到你面前，只念出自己原信里的那一句。";
            readingPause=scene.State.AcquirePause(PauseReason.Journal,"letter-comparison");
        }
        public void DismissReading()
        {
            if (readingPause != null && scene) scene.State.ReleasePause(readingPause);
            readingPause = null; readingId = null; ReadableTitle = ReadableText = null;
        }
        public bool ShareCurrentDocument()
        {
            if (!CanShareReading) return false;
            var paper = Find(readingId);
            paper.LinRead = true;
            queuedObservation = "朋友在你面前把「" + paper.Title + "」交给你。你已经亲自读到内容，可以引用，也可以追问她/他为什么到现在才给你看。";
            ReadableText = paper.Text + "\n\n林已亲手读过这一页。它没有替她决定下一步。";
            return true;
        }
        public bool ChooseDeparture(bool together)
        {
            if (!ReadingExit) return false;
            bool done = scene.State.TryFinishEnding(together ? "together" : "ordinary_departure", AtExit(scene.World.Player), AtExit(scene.World.Lin));
            if (done) DismissReading();
            return done;
        }
        public bool ChooseStay()
        {
            if (!ReadingExit || !CanRespectStay) return false;
            bool done = scene.State.TryFinishEnding("respected_stay", AtExit(scene.World.Player), AtExit(scene.World.Lin));
            if (done) DismissReading();
            return done;
        }
        public void BeginVisit() { if (scene && scene.IntroOpen) scene.ToggleIntroOff(); }
        public bool FastForwardVisit() => initialized && scene.State.FinishVisitAtDeadline();
        public bool CanPlayerRead(string id) => initialized && Find(id) != null &&
            (id != "sealed_letter" || scene.State.IsConcernResolved("C2"));
        public bool HasLinRead(string id) => Find(id) != null && Find(id).LinRead;
        public bool HasPlayerRead(string id) => Find(id) != null && Find(id).PlayerRead;
        public bool CanVisit(string id) => initialized && SessionState.IsStoryTarget(id) && scene.World.CanInspect(id);
        public bool RequestVisit(string id) => CanVisit(id) && scene.State.TryQueue("inspect", id, out _);
        bool AtExit(Transform actor) => actor && exitGoal && Vector3.Distance(actor.position, exitGoal.position) <= 1.65f;
        bool FacingExit()
        {
            if (!AtExit(scene.World.Player)) return false;
            var view = scene.World.ViewCamera.transform;
            var delta = exitFocus.position - view.position;
            return Vector3.Angle(view.forward, delta) < 38f && InteractionSight.Clear(view.position, delta, scene.World.Player, exitFocus);
        }
        Paper FocusPaper()
        {
            var view = scene.World.ViewCamera.transform;
            Paper result = null; float best = 26f;
            foreach (var paper in papers)
            {
                var delta = paper.Prop.position - view.position;
                float angle = Vector3.Angle(view.forward, delta);
                if (delta.magnitude > 2.5f || angle > best || !InteractionSight.Clear(view.position, delta, scene.World.Player, paper.Prop)) continue;
                result = paper; best = angle;
            }
            return result;
        }
        internal float FocusedPaperAngle
        {
            get
            {
                var paper = FocusPaper();
                var view = scene.World.ViewCamera.transform;
                return paper == null ? float.PositiveInfinity : Vector3.Angle(view.forward, paper.Prop.position - view.position);
            }
        }
        public string[] StoryObservations
        {
            get
            {
                var observations = new List<string> {
                    "今天是信使最后一场拜访。林知道原信尚未寄出、封口完好，今天的回信却回应了只有原信里写过的蓝杯子缺口。她首先向最信任的信使朋友询问。",
                    "玩家可以照常告别。调查和同行是选择，不许把选择留下说成玩家没有通关资格。",
                    scene.State.IsConcernResolved("C2") ? "你愿意把私人原信交给朋友代读；可以请她/他帮忙再带回来。" : "你还不愿把私人原信交给朋友拆阅。若要核查，你可以亲自走去读。"
                };
                foreach (var paper in papers)
                {
                    if (paper.LinRead) observations.Add("林亲自读过「" + paper.Title + "」：" + LinReadableText(paper));
                    else observations.Add("林尚未读过「" + paper.Title + "」。" + (paper.PlayerRead ? "玩家已经独自看过，但没有展示给林；林不得引用其中内容。" : "玩家也还没有查看内容。"));
                }
                observations.Add("现场位置：玩家(" + scene.World.Player.position.x.ToString("F1") + "," + scene.World.Player.position.z.ToString("F1") + ")，林(" + scene.World.Lin.position.x.ToString("F1") + "," + scene.World.Lin.position.z.ToString("F1") + ")。物品导航目标：sealed_letter=茶几原信，parent_reply=茶几回信，parents_diary=客厅门边写字台，friend_postcard=茶几明信片，exit_door=前门。");
                return observations.ToArray();
            }
        }
        static string LinReadableText(Paper paper)
        {
            int privateNote = paper.Text.IndexOf("玩家知道：", StringComparison.Ordinal);
            return privateNote < 0 ? paper.Text : paper.Text.Substring(0, privateNote);
        }
        public string[] LinPrivateKnowledge => new[] {
            "林的自传记忆：小时候父亲教她把有缺口的杯子转过去；那天她笑了。她不知道父亲是演员。",
            "林记得旧友阿禾曾说：你不欠任何人一个勇敢的决定。她可以自然谈起这句话，不知道未读明信片的背面写了什么。",
            "林写原信时没有其他人在场。蓝杯子的细节只写在封好的原信里，从未告诉信使。"
        };
        public string[] PlayerOnlyObservations
        {
            get
            {
                var observations = new List<string> { "玩家是长期代写父母回信的信使演员；父母扮演者已永久离场。林不知道这些内幕，除非玩家当面承认或她亲自验证。" };
                foreach (var paper in papers)
                    if (paper.PlayerRead && !paper.LinRead) observations.Add("仅玩家读过「" + paper.Title + "」：" + paper.Text);
                return observations.ToArray();
            }
        }
        Paper Find(string id) => papers.Find(p => p.Id == id);
        void AddPaper(string id, string title, string content, Vector3 position, Vector3 approach, Material paperMaterial, string label, Material ink)
        {
            var prop = Box(title, position, new Vector3(.34f, .025f, .23f), paperMaterial, false);
            var trim = Box(title + " ribbon", position + Vector3.up * .016f, new Vector3(.26f, .007f, .018f), ink, false);
            trim.SetParent(prop, true);
            var target = Node(id + "_observe", Sample(approach));
            scene.World.RegisterStoryTarget(id, target, prop);
            var text = Node(label, position + new Vector3(0,.022f,0)).gameObject.AddComponent<TextMesh>();
            text.font = PlayerUIFont.Get(); text.GetComponent<Renderer>().sharedMaterial = text.font.material;
            text.text = label; text.anchor = TextAnchor.MiddleCenter; text.fontSize = 42; text.characterSize = .009f;
            text.color = ink.color; text.transform.rotation = Quaternion.Euler(90, 0, 0);
            papers.Add(new Paper { Id = id, Title = title, Text = content, Prop = prop });
        }
        Vector3 Sample(Vector3 point) => NavMesh.SamplePosition(point, out var hit, .7f, NavMesh.AllAreas) ? hit.position : point;
        Transform Node(string label, Vector3 position)
        {
            var item = new GameObject(label).transform; item.SetParent(transform, false); item.position = position; return item;
        }
        Transform Box(string label, Vector3 position, Vector3 size, Material material, bool solid)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            item.name = label; item.SetParent(transform, false); item.position = position; item.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) item.GetComponent<Collider>().isTrigger = true;
            else
            {
                var obstacle = item.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
            }
            return item;
        }
        Material Material(string label, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = label, color = color };
            materials.Add(material); return material;
        }
        void OnDestroy() { foreach (var material in materials) if (material) Destroy(material); }
    }
}
