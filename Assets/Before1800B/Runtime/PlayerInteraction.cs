using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before1800.PartB
{
    public enum PlayerScreen { Intro, Explore, Talk, Journal, Menu, Error, Timeout, Wait }

    public sealed class PlayerChoice
    {
        public readonly string Id;
        public readonly string Label;
        internal PlayerChoice(string id, string label) { Id = id; Label = label; }
    }

    // Player-facing dialogue and notes. Only the scene's existing action and
    // device interfaces can start navigation, verify a fact or clear a concern.
    public sealed class PlayerInteraction
    {
        private enum Topic { Opening, Camera, Tv, Honesty, Company, Door, Reply, Clock, ClockAnswerReceipt, ClockLater }
        private enum Focus { None, Lin, Camera, TrackCamera, Tv, OpenDoor, InspectDoor, Route, ApartmentDoor, Remote, KitchenClock, RetakeCard }

        private readonly PartBScene scene;
        private readonly List<PlayerChoice> choices = new List<PlayerChoice>(5);
        private Topic topic, lastChoicesTopic;
        private PlayerScreen lastChoicesScreen;
        private ulong lastChoicesVersion;
        private bool choicesDirty = true, restartConfirm, lastChoicesStation, framing;
        private Guid runId;
        private ModelRequest pendingReply;
        private float requestStarted;
        private string acceptedReply = "", prompt = "", transient = "";
        private float transientUntil;
        private string journalCache, displayedClock;
        private int displayedMinute = -1;
        private ulong journalVersion;
        private bool journalStation;
        private bool sawE1Verified;
        private bool sawE4Verified;
        private SessionState State => scene.State;
        private GreyboxWorld World => scene.World;

        public PlayerInteraction(PartBScene scene)
        {
            this.scene = scene;
            runId = State.RunId;
            sawE1Verified = State.E1Verified;
            sawE4Verified = State.E4Verified;
        }

        // Called each UI frame so success banners appear as soon as evidence is written.
        public void SyncWorldFeedback()
        {
            if (runId != State.RunId)
            {
                runId = State.RunId;
                sawE1Verified = State.E1Verified;
                sawE4Verified = State.E4Verified;
                return;
            }
            if (State.E1Verified && !sawE1Verified)
                Flash("手记已更新：林亲眼确认镜头会跟随她。按 J 查看。", 5.5f);
            if (State.E4Verified && !sawE4Verified)
                Flash("手记已更新：林确认电视会按她想看的节目切换。按 J 查看。", 5.5f);
            sawE1Verified = State.E1Verified;
            sawE4Verified = State.E4Verified;
        }

        public bool DebugOpen { get; set; }
        public bool FramingConversation => framing && scene.DialogueOpen && !DebugOpen;
        public string Draft { get; set; } = "";
        public bool HasDialogueService => scene.HasDialogueService;
        public bool CanSend => Screen == PlayerScreen.Talk && !DebugOpen && State.Pending == null;
        public string Speaker => "林";
        public string ClockText
        {
            get
            {
                int minute = (int)(State.ElapsedGameSeconds / 60);
                if (minute == displayedMinute) return displayedClock;
                displayedMinute = minute;
                displayedClock = State.TimeHms.Substring(0, 5);
                return displayedClock;
            }
        }

        public PlayerScreen Screen
        {
            get
            {
                if (State.TimedOut) return PlayerScreen.Timeout;
                if (scene.IntroOpen) return PlayerScreen.Intro;
                if (scene.MenuOpen) return PlayerScreen.Menu;
                if (scene.JournalOpen) return PlayerScreen.Journal;
                if (State.HasPause(PauseReason.Error)) return PlayerScreen.Error;
                if (scene.ModelWait) return PlayerScreen.Wait;
                return scene.DialogueOpen ? PlayerScreen.Talk : PlayerScreen.Explore;
            }
        }

        public void Tick()
        {
            if (runId != State.RunId)
            {
                runId = State.RunId;
                framing = restartConfirm = false;
                choicesDirty = true;
                topic = Topic.Opening;
                pendingReply = null;
                journalCache = null;
                Draft = transient = prompt = acceptedReply = "";
            }
            if (!scene.DialogueOpen) framing = false;
            if (pendingReply == null) return;
            if (scene.ModelWait && Time.unscaledTime - requestStarted < 30f) return;
            // The adapter can complete synchronously. Capture after returning from
            // its callback, or discard the ticket when the user leaves the dialogue.
            if (scene.ModelWait) scene.TryCancelModelRequest(pendingReply);
            if (scene.DialogueOpen && pendingReply.RunId == State.RunId)
            {
                if (pendingReply.PlayerReply != null)
                {
                    acceptedReply = pendingReply.PlayerReply;
                    topic = Topic.Reply;
                    Draft = prompt = "";
                }
                else prompt = "这句话没能送达，文字已经保留。可以再试一次。";
                choicesDirty = true;
            }
            pendingReply = null;
        }

        private Focus CurrentFocus(out ApartmentDoor door)
        {
            door = null;
            if (Screen != PlayerScreen.Explore || DebugOpen) return Focus.None;
            // Prefer the E1 experiment over journal/discover while Lin is waiting at the camera.
            if (!scene.ObservingCamera && !State.E1Verified && State.Stage == ActionStage.Arrived &&
                State.Active?.TargetId == SessionState.CameraTarget && World.Camera &&
                (World.Camera.CanStartTracking(World.ViewCamera.transform, World.Lin) || World.CanExamineCamera))
                return Focus.TrackCamera;
            if (!State.E4Verified && World.Tv &&
                State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.BroadcastTarget &&
                (World.CanExamineTv || World.Tv.PlayerCanOperate(World.ViewCamera.transform)))
                return Focus.Tv;
            var clock = scene.Clock;
            if (clock != null)
            {
                if (clock.AtRemote) return Focus.Remote;
                if (clock.AtCard) return Focus.RetakeCard;
                if (clock.AtClock) return Focus.KitchenClock;
            }
            door = World.DoorInView();
            if (door) return Focus.ApartmentDoor;
            if (World.CanExamineCamera) return Focus.Camera;
            if (World.CanExamineTv) return Focus.Tv;
            var station = World.Station;
            if (station)
            {
                var view = World.ViewCamera.transform;
                if (station.CanOpenDoor(view)) return Focus.OpenDoor;
                if (!scene.CheckingRoute && !State.RouteConfirmed && station.CanCheckRoute(World.Player, view))
                    return Focus.Route;
                if (!scene.ObservingSetBack && State.Stage == ActionStage.Arrived &&
                    State.Active?.TargetId == SessionState.SetBackTarget && station.CanStartObservation(view))
                    return Focus.InspectDoor;
            }
            return World.CanTalkToLin ? Focus.Lin : Focus.None;
        }

        public string Hint
        {
            get
            {
                switch (CurrentFocus(out var door))
                {
                    case Focus.Remote: return "拿起茶几上的遥控器";
                    case Focus.KitchenClock: return scene.Clock.ClockHint;
                    case Focus.RetakeCard: return "读电视上的重拍提示";
                    case Focus.ApartmentDoor: return door.WantsOpen ? "关门" : "开门";
                    case Focus.Lin: return State.Cooperation == CooperationMode.Withdrawn ? "重新和林说话" : "和林说话";
                    case Focus.Camera: return State.CameraDiscovered ? "查看镜头记录" : "查看镜头";
                    case Focus.TrackCamera: return "请林换到第二个观察点，看镜头是否跟随";
                    case Focus.Tv:
                        if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.BroadcastTarget)
                        {
                            if (World.Tv && World.Tv.CanStartSequence) return "启动电视预测序列";
                            if (World.Tv && World.Tv.CanConfirmPredictedShow) return "请林点播预测的节目";
                            if (World.Tv && World.Tv.CanIntervene) return "请林改口，看画面是否跟着变";
                            if (World.Tv && World.Tv.Phase == TvPhase.Failed) return "预测失败，回对话重新登记";
                        }
                        return State.TvDiscovered ? "查看电视记录" : "查看客厅电视";
                    case Focus.OpenDoor: return "打开维修门";
                    case Focus.InspectDoor: return "请林观察门后的布景";
                    case Focus.Route: return "检查通道";
                    default: return "";
                }
            }
        }

        public string Notice
        {
            get
            {
                if (Screen == PlayerScreen.Talk) return prompt;
                if (Screen != PlayerScreen.Explore) return "";
                if (Time.unscaledTime < transientUntil && transient.Length > 0) return transient;
                var clockNotice = scene.Clock?.Notice;
                if (!string.IsNullOrEmpty(clockNotice)) return clockNotice;
                var door = World.DoorInView();
                if (door && door.IsBlocked) return "门边有人挡住了，留一点空间。";
                if (State.Stage == ActionStage.Following && World.FollowingNeedsOpenDoor)
                    return "林在门的另一边，打开房门她就能跟过来。";
                if (scene.CheckingRoute) return "正在检查通道，保持面向检查牌。";
                if (scene.ObservingSetBack) return "林在辨认支架和标记，给她留出视线。";
                if (scene.ObservingCamera)
                    return string.IsNullOrEmpty(scene.CameraTrackHint)
                        ? "跟踪实验进行中：看镜头外壳是否跟着林转。"
                        : scene.CameraTrackHint;
                if (scene.ObservingBroadcast || (State.Stage == ActionStage.Arrived &&
                    State.Active?.TargetId == SessionState.BroadcastTarget))
                {
                    if (!string.IsNullOrEmpty(scene.BroadcastHint)) return scene.BroadcastHint;
                    if (World.Tv && World.Tv.CanStartSequence) return "林在电视前。对准屏幕按 E，启动已锁定的预测序列。";
                    if (World.Tv && World.Tv.CanConfirmPredictedShow)
                        return "序列已启动。按 E，请林点播你预测的节目。";
                    if (World.Tv && World.Tv.CanIntervene)
                        return "预测已命中。按 E，请林改口点另一个节目，看画面是否跟着变。";
                    if (World.Tv && World.Tv.Phase == TvPhase.Failed)
                        return "本次预测失败，不计 E4。打开对话重新登记后可重试。";
                    return "林到了电视前。先登记预测，再启动序列。";
                }
                if (State.Stage == ActionStage.Arrived)
                    return State.Active?.TargetId == SessionState.SetBackTarget
                        ? "林到了维修门边。靠近门旁的控制器，请她一起观察。"
                        : "林到了第一个观察点。对准镜头按 E，开始跟踪验证（会显示锁定进度）。";
                if (State.Stage == ActionStage.Returning)
                {
                    if (State.Active?.TargetId == SessionState.SetBackTarget)
                        return "林看清了布景支架，正在往回走。";
                    if (State.Active?.TargetId == SessionState.BroadcastTarget)
                        return "E4 已确认。电视跟着林想看的节目切换了。按 J 可回看手记。";
                    return "E1 已确认。林亲眼看见镜头跟随，正在往回走。按 J 可回看手记。";
                }
                return "";
            }
        }

        public string Line
        {
            get
            {
                switch (Screen)
                {
                    case PlayerScreen.Menu: return restartConfirm ? "从 17:00 重新开始？这一次的手记会清空。" : "";
                    case PlayerScreen.Wait: return "林正在想怎么回答。";
                    case PlayerScreen.Error: return State.CanRetryReturn
                        ? "我回去的路被挡住了。给我让一点位置，好吗？" : "我从这里过去不太方便。我们换个位置，再试一次吧。";
                    case PlayerScreen.Timeout: return "指针停在十八点。片场的出口已经关闭。";
                }
                if (State.Cooperation == CooperationMode.Withdrawn) return "先停一下吧。我还没准备好接着说。";
                switch (topic)
                {
                    case Topic.Camera: return State.E1Verified ? "我换到第二个点时，它真的跟着转了……谁在看？" :
                        "电视上面那个小黑点？我想站到两个观察点上，看它会不会跟着我转。";
                    case Topic.Tv: return State.E4Verified
                        ? "我改口以后，画面真的跟着变了……它怎么知道我想看什么？"
                        : World.Tv && World.Tv.HasPrediction
                            ? "好，先记下你的预测。等我站到电视前，你再启动。"
                            : "你是说，客厅这台电视会按我想看的节目切换？那你先说清楚会出什么。";
                    case Topic.Clock:
                        if (State.ClockWitnessed)
                            return State.ClockAnswer == ClockAccount.Unanswered
                                ? "它走了三格，退回去，又走了同样的三格。你怎么知道该按哪颗键？"
                                : ClockLaterLine();
                        return State.ClockInvited ? "我想自己听一遍。到钟前面给我留点地方，好吗？" :
                            "会不会只是电池快没电了？带我去厨房，我想听听那一拍。";
                    case Topic.ClockAnswerReceipt:
                        return State.ClockAnswer == ClockAccount.Disclosed
                            ? "你也参加过排练……谢谢你亲口说。那一秒，我想自己再看一次。"
                            : "猜的？我还没听见第三声，你的拇指就已经按上去了。";
                    case Topic.ClockLater: return ClockLaterLine();
                    case Topic.Door: return State.E5Verified ? "那些支架和编号，我都看清了。这面墙是搭起来的。" :
                        "你说的是那扇维修门？我想亲眼看一下后面有什么。";
                    case Topic.Honesty: return "那就从你知道的第一件事说起吧。我想听你亲口告诉我。";
                    case Topic.Company: return State.Cooperation == CooperationMode.Following
                        ? "要我在这里等你吗？" : "嗯，你带路。我跟着你。";
                    case Topic.Reply: return acceptedReply;
                }
                if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.CameraTarget)
                    return "我在这里了。我们一起看看，镜头到底会不会转。";
                if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.BroadcastTarget)
                    return "我在电视前面了。按你说的，先启动，再看它会不会听话。";
                if (State.Active != null && State.Active.Action == "inspect") return "怎么了？要我先停一下吗？";
                if (State.E1Verified || State.E4Verified || State.E5Verified) return "刚才看到的事，我还想再理一理。你呢？";
                if (State.Cooperation == CooperationMode.Following) return "我就在你旁边。你想去哪儿？";
                return World.UsesSceneBindings ? "你从刚才起就一直看着电视那边。怎么了？" :
                    "你一直在打量这个房间。有什么想让我一起看看？";
            }
        }

        private string ClockLaterLine() => State.ClockAnswer == ClockAccount.Disclosed
            ? "这次我来按。你告诉我，排练时还有哪些事是我不知道的？"
            : "我还记得那颗键。等你愿意说是怎么知道的，我们再聊。";

        public string JournalText
        {
            get
            {
                bool station = World.Station;
                if (journalCache != null && journalVersion == State.Version && journalStation == station) return journalCache;
                var found = new List<string>(7);
                if (State.ClockRemoteTaken) found.Add("茶几遥控器的背面写着「厨房 · 重播」。遥控器在我手里。");
                if (State.ClockBaselineSeen) found.Add("厨房挂钟的第三拍比前两拍晚了半拍。");
                if (State.ClockSoloReplaySeen) found.Add("《少走的一秒》：按下重播，秒针走三格，退回去，又走了同样的三格。");
                if (State.ClockCardRead) found.Add("客厅电视的重拍提示卡上写着「重来」「从上一句接」。");
                if (State.CameraDiscovered) found.Add(World.UsesSceneBindings ? "电视上方装着一个朝向客厅的镜头。" : "墙上的镜头朝向房间。");
                if (State.TvDiscovered) found.Add("客厅电视能按林想看的节目切换画面。");
                if (State.DoorDiscovered) found.Add("维修门后有一段窄通道。");
                var seen = new List<string>(5);
                if (State.E1Verified) seen.Add("林换到第二个观察点时，镜头外壳跟着转向她。");
                if (State.E4Verified) seen.Add("林先点播预测节目命中，再改口后电视画面跟着更新。");
                if (State.ClockWitnessed) seen.Add("林亲眼看完三拍重播，问我怎么知道该按哪颗键。");
                if (State.ClockAnswer == ClockAccount.Disclosed) seen.Add("我告诉林，我在排练时按过。她想自己试一次。");
                if (State.ClockAnswer == ClockAccount.Evaded) seen.Add("我说是猜的。林记得我提前把拇指放到了那颗键上。");
                if (State.E5Verified) seen.Add("林看清了墙后的支架和制作标记。");
                journalCache = "我留意到的\n" + (found.Count > 0 ? string.Join("\n", found) : "还没有记下发现。") +
                    "\n\n林亲眼确认的\n" + (seen.Count > 0 ? string.Join("\n", seen) : "还没有亲眼确认的线索。") +
                    "\n\n出口路线\n" + (State.RouteConfirmed ? "我亲自走进通道，确认这一段可以通行。" : "尚未确认路线。");
                journalVersion = State.Version;
                journalStation = station;
                return journalCache;
            }
        }

        public IReadOnlyList<PlayerChoice> Choices
        {
            get
            {
                var screen = Screen;
                bool station = World.Station;
                if (!choicesDirty && lastChoicesScreen == screen && lastChoicesVersion == State.Version &&
                    lastChoicesTopic == topic && lastChoicesStation == station) return choices;
                choices.Clear();
                switch (screen)
                {
                    case PlayerScreen.Menu:
                        if (restartConfirm) { Add("resume_menu", "先不重来"); Add("restart_yes", "确认重新开始"); }
                        else
                        {
                            Add("resume", "回到现场");
                            if (State.CanRetryReturn) Add("retry_return", "请林再试着回来");
                            Add("restart", "重新开始");
                        }
                        break;
                    case PlayerScreen.Timeout: Add("restart_yes", "从 17:00 再来一次"); break;
                    case PlayerScreen.Error:
                        Add("recover", "回现场看看");
                        if (State.CanRetryReturn) Add("retry_return", "请林再试一次");
                        break;
                    case PlayerScreen.Wait: Add("cancel_wait", "取消等待"); break;
                    case PlayerScreen.Talk: BuildTalkChoices(); break;
                }
                choicesDirty = false;
                lastChoicesScreen = screen;
                lastChoicesVersion = State.Version;
                lastChoicesTopic = topic;
                lastChoicesStation = station;
                return choices;
            }
        }

        private void Add(string id, string label) => choices.Add(new PlayerChoice(id, label));

        private void BuildTalkChoices()
        {
            if (State.Cooperation == CooperationMode.Withdrawn) { Add("restore", "我想继续刚才的话。"); return; }
            if (State.Pending != null) { Add("resume_action", "好，我们走吧。"); return; }
            if (State.Active != null && State.Active.Action != "follow")
            {
                Add("resume_action", "好，你继续。");
                Add("stop", "等一下，我们先停在这里。");
                return;
            }
            switch (topic)
            {
                case Topic.Opening:
                    Add("camera", State.E1Verified ? "聊聊刚才转动的镜头。" :
                        World.UsesSceneBindings ? "你看，电视上面有个镜头。" : "你看，墙上有个镜头。");
                    if (World.Tv)
                        Add("tv", State.E4Verified ? "聊聊刚才听话的电视。" : "关于客厅这台电视，我想先做个预测。");
                    Add("honesty", "有件事，我一直没敢告诉你。");
                    if (State.ClockSoloReplaySeen) Add("clock", State.ClockWitnessed ? "说说刚才的三拍。" : "我想让你听一听厨房挂钟。");
                    Add("company", "和我一起走走？");
                    break;
                case Topic.Camera:
                    if (!State.E1Verified) Add("go_camera", "一起去看看");
                    if (World.Tv) Add("tv", "还有客厅电视。");
                    if (World.Station) Add("door", "我还看见了一扇维修门。");
                    Add("again", "我还想说件事。");
                    break;
                case Topic.Tv:
                    BuildTvChoices();
                    break;
                case Topic.Door:
                    if (!State.E5Verified && World.Station) Add("go_door", "一起去维修门看看");
                    if (World.Tv) Add("tv", "先做电视预测。");
                    Add("again", "再聊一句");
                    break;
                case Topic.Company:
                    if (State.Cooperation == CooperationMode.Following) Add("stop", "在这里等我。");
                    else Add("follow", "跟我来。");
                    if (World.Tv) Add("tv", "先去电视那边。");
                    if (World.Station) Add("door", "跟我去看看维修门。");
                    Add("again", "先聊点别的。");
                    break;
                case Topic.Clock:
                    if (!State.ClockWitnessed) Add("clock_invite", State.ClockInvited ? "走，我们再看一遍。" : "跟我去厨房，第三拍时看秒针。");
                    else if (State.ClockAnswer == ClockAccount.Unanswered)
                    {
                        Add("clock_truth", "我排练时按过。我一直知道这颗键是干什么的。");
                        Add("clock_evade", "我猜的，刚好按对了。");
                    }
                    Add("again", "先聊点别的。");
                    break;
                case Topic.ClockAnswerReceipt:
                    Add("clock_after", State.ClockAnswer == ClockAccount.Disclosed ? "你还想再试一次吗？" : "你还有什么想问我的？");
                    Add("again", "先聊点别的。");
                    break;
                case Topic.ClockLater:
                    Add("honesty", "有件事，我想亲口告诉你。");
                    Add("again", "先聊点别的。");
                    break;
                default:
                    Add("camera", "先一起看看那个镜头吧。");
                    if (World.Tv) Add("tv", "先给电视做个预测。");
                    if (World.Station) Add("door", "你见过那扇维修门吗？");
                    Add("again", "回到刚才的话题。");
                    break;
            }
        }

        private void BuildTvChoices()
        {
            if (State.E4Verified)
            {
                Add("again", "回到刚才的话题。");
                return;
            }
            var tv = World.Tv;
            if (tv && tv.Phase == TvPhase.Running)
            {
                Add("tv_req_news", "请林点播新闻（例3可故意点错）");
                Add("tv_req_cartoon", "请林点播动画片");
                Add("tv_req_weather", "请林点播天气预报");
                Add("resume_action", "好，你继续对着电视。");
                return;
            }
            if (tv && tv.Phase == TvPhase.Step1Matched)
            {
                Add("tv_intervene", "请林改口，看画面是否跟着变（步骤2）");
                Add("resume_action", "好，你继续对着电视。");
                return;
            }
            if (tv && (tv.Phase == TvPhase.Idle || tv.Phase == TvPhase.Failed || tv.Phase == TvPhase.Predicted))
            {
                Add("pred_news", "例1：启动后会播新闻");
                Add("pred_cartoon", "例2：启动后会播动画片");
                Add("pred_weather", "例3：启动后会播天气预报");
            }
            if (tv && tv.HasPrediction && !State.E4Verified) Add("go_tv", "一起去客厅电视前验证");
            Add("again", "我还想说件事。");
        }

        public void Begin()
        {
            if (Screen != PlayerScreen.Intro || DebugOpen) return;
            scene.ToggleIntroOff();
            if (!World.CanTalkToLin) Flash("走近林，面向她说话。");
        }

        public void Use()
        {
            switch (CurrentFocus(out var door))
            {
                case Focus.Remote: scene.Clock.TakeRemote(); break;
                case Focus.KitchenClock: scene.Clock.UseClock(); break;
                case Focus.RetakeCard: scene.Clock.ReadCard(); break;
                case Focus.ApartmentDoor:
                    door.Toggle();
                    break;
                case Focus.Lin:
                    scene.ToggleDialogue();
                    framing = scene.DialogueOpen;
                    topic = State.ClockWitnessed && State.ClockAnswer == ClockAccount.Unanswered ? Topic.Clock : Topic.Opening;
                    prompt = "";
                    break;
                case Focus.Camera:
                    if (State.CameraDiscovered) ToggleJournal();
                    else
                    {
                        State.Discover(SessionState.CameraTarget);
                        Flash("记入手记：这里装着一个镜头。");
                    }
                    break;
                case Focus.TrackCamera:
                    if (!scene.TryBeginCameraTracking(State.Active))
                    {
                        string block = World.Camera
                            ? World.Camera.DescribeStartBlock(World.ViewCamera.transform, World.Lin)
                            : "镜头跟踪装置还没准备好。";
                        Flash(string.IsNullOrEmpty(block) ? "现在还不能开始跟踪验证。" : block, 4f);
                    }
                    else Flash("跟踪开始：林会走到第二个点，锁定进度到 100% 才会写入手记。", 4.5f);
                    break;
                case Focus.Tv:
                    if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.BroadcastTarget)
                    {
                        scene.InteractWithBroadcast();
                        if (!string.IsNullOrEmpty(scene.Feedback)) Flash(scene.Feedback, 4.5f);
                    }
                    else if (State.TvDiscovered) ToggleJournal();
                    else
                    {
                        State.Discover(SessionState.BroadcastTarget);
                        Flash("记入手记：客厅有一台会切换节目的电视。");
                    }
                    break;
                case Focus.OpenDoor:
                    scene.InteractWithSetBack();
                    if (World.Station.DoorOpen) Flash("维修门打开了。");
                    break;
                case Focus.InspectDoor:
                    if (!scene.TryBeginSetBackObservation(State.Active)) Flash("给林让开视线，让她看清两处标记。");
                    else transient = "";
                    break;
                case Focus.Route:
                    if (scene.TryBeginRouteCheck(State.RunId)) transient = "";
                    break;
            }
        }

        public void Back()
        {
            if (DebugOpen) return;
            switch (Screen)
            {
                case PlayerScreen.Journal: ToggleJournal(); break;
                case PlayerScreen.Menu:
                    if (restartConfirm) { restartConfirm = false; choicesDirty = true; }
                    else ToggleMenu();
                    break;
                case PlayerScreen.Talk: CloseTalk(); break;
                case PlayerScreen.Wait: CancelWait(); break;
                case PlayerScreen.Error: scene.RecoverNavigation(); break;
                case PlayerScreen.Explore: ToggleMenu(); break;
            }
        }

        public void ToggleJournal()
        {
            if (DebugOpen || (Screen != PlayerScreen.Explore && Screen != PlayerScreen.Talk && Screen != PlayerScreen.Journal)) return;
            scene.ToggleJournal();
        }

        public void ToggleMenu()
        {
            if (DebugOpen || (Screen != PlayerScreen.Explore && Screen != PlayerScreen.Menu)) return;
            restartConfirm = false;
            choicesDirty = true;
            scene.ToggleMenu();
        }

        public void Choose(string id)
        {
            if (DebugOpen || string.IsNullOrEmpty(id)) return;
            bool displayed = false;
            foreach (var choice in Choices) if (choice.Id == id) { displayed = true; break; }
            if (!displayed) return;
            prompt = "";
            switch (id)
            {
                case "resume": ToggleMenu(); return;
                case "resume_menu": restartConfirm = false; choicesDirty = true; return;
                case "restart": restartConfirm = true; choicesDirty = true; return;
                case "restart_yes": scene.Restart(); return;
                case "recover": scene.RecoverNavigation(); return;
                case "retry_return": scene.RetryReturn(); return;
                case "cancel_wait": CancelWait(); return;
                case "restore":
                    if (scene.TryPlayerAction("restore", null)) topic = Topic.Opening;
                    return;
                case "resume_action": CloseTalk(); return;
                case "again": topic = Topic.Opening; return;
                case "camera": topic = Topic.Camera; return;
                case "tv": topic = Topic.Tv; return;
                case "door": topic = Topic.Door; return;
                case "honesty": topic = Topic.Honesty; return;
                case "company": topic = Topic.Company; return;
                case "pred_news": RegisterPrediction(TvProgram.News); return;
                case "pred_cartoon": RegisterPrediction(TvProgram.Cartoon); return;
                case "pred_weather": RegisterPrediction(TvProgram.Weather); return;
                case "tv_req_news": RequestShow(TvProgram.News); return;
                case "tv_req_cartoon": RequestShow(TvProgram.Cartoon); return;
                case "tv_req_weather": RequestShow(TvProgram.Weather); return;
                case "tv_intervene":
                    CloseTalk();
                    scene.InteractWithBroadcast();
                    if (!string.IsNullOrEmpty(scene.Feedback)) Flash(scene.Feedback, 4.5f);
                    return;
                case "clock": topic = Topic.Clock; return;
                case "clock_after": topic = Topic.ClockLater; return;
                case "clock_truth":
                case "clock_evade":
                    State.AnswerClockQuestion(id == "clock_truth" ? ClockAccount.Disclosed : ClockAccount.Evaded);
                    topic = Topic.ClockAnswerReceipt;
                    return;
                case "clock_invite":
                    if (!scene.TryPlayerAction("follow", "player")) { prompt = "先把眼前的事做完，我们再去。"; return; }
                    State.InviteLinToClock();
                    CloseTalk();
                    Flash("林跟着你去厨房。站到钟前，给她留出看表盘的视线。");
                    return;
                case "go_camera": StartAction("inspect", SessionState.CameraTarget); return;
                case "go_tv": StartAction("inspect", SessionState.BroadcastTarget); return;
                case "go_door": StartAction("inspect", SessionState.SetBackTarget); return;
                case "follow": StartAction("follow", "player"); return;
                case "stop": StartAction("stop", null); return;
            }
        }

        private void RegisterPrediction(TvProgram program)
        {
            if (!scene.TryRegisterTvPrediction(program, out var reason))
            {
                prompt = string.IsNullOrEmpty(reason) ? "现在不能登记预测。" : reason;
                return;
            }
            prompt = reason;
            choicesDirty = true;
        }

        private void RequestShow(TvProgram program)
        {
            if (!scene.TryLinRequestTvShow(program, out var reason))
            {
                prompt = string.IsNullOrEmpty(reason) ? "点播没有生效。" : reason;
                choicesDirty = true;
                return;
            }
            prompt = reason;
            choicesDirty = true;
        }

        private void StartAction(string action, string target)
        {
            if (!scene.TryPlayerAction(action, target))
            {
                prompt = "现在还过不去。可以先结束交谈，看看前面有没有挡住。";
                return;
            }
            CloseTalk();
            Flash(action == "follow" ? "林会跟着你。" : action == "stop" ? "林会在这里等你。" :
                target == SessionState.SetBackTarget ? "林朝维修门走去。" :
                target == SessionState.BroadcastTarget ? "林朝客厅电视走去。" : "林朝镜头走去。");
        }

        private void CloseTalk()
        {
            if (scene.DialogueOpen) scene.ToggleDialogue();
            framing = false;
            prompt = "";
        }

        private void CancelWait()
        {
            if (pendingReply != null)
            {
                scene.TryCancelModelRequest(pendingReply);
                pendingReply = null;
                prompt = "已取消等待。刚才的话还在，可以重新发送。";
            }
            else if (scene.DialogueOpen) CloseTalk();
            else scene.ToggleModelWait();
        }

        public void Send(string text)
        {
            if (!CanSend || string.IsNullOrWhiteSpace(text)) return;
            Draft = text.Trim();
            if (HasDialogueService)
            {
                requestStarted = Time.unscaledTime;
                if (!scene.TrySendPlayerLine(Draft, out pendingReply)) prompt = "暂时没能送出。请再试一次。";
                Tick();
                return;
            }

            // Authored rehearsal topics. Players decide whether to act in a separate
            // choice; a fragment of text can never verify a clue or resolve a concern.
            if (State.Cooperation == CooperationMode.Withdrawn ||
                (State.Active != null && State.Active.Action != "follow"))
            {
                prompt = "先回应林眼前的事，可以选择上方的一句。";
                return;
            }
            if (State.ClockSoloReplaySeen && (Draft.Contains("挂钟") || Draft.Contains("三拍") || Draft.Contains("遥控器"))) topic = Topic.Clock;
            else if (Draft.Contains("镜头") || Draft.Contains("摄像")) topic = Topic.Camera;
            else if (World.Tv && (Draft.Contains("电视") || Draft.Contains("广播") || Draft.Contains("预测"))) topic = Topic.Tv;
            else if (World.Station && (Draft.Contains("维修门") || Draft.Contains("布景"))) topic = Topic.Door;
            else if (Draft.Contains("骗") || Draft.Contains("瞒") || Draft.Contains("告诉你")) topic = Topic.Honesty;
            else if (Draft.Contains("跟我") || Draft.Contains("一起走") || Draft.Contains("等我")) topic = Topic.Company;
            else
            {
                prompt = "当前使用预设对话。可以提到镜头、电视、隐瞒或同行，原话已保留。";
                return;
            }
            Draft = prompt = "";
        }

        private void Flash(string message, float seconds = 3.5f)
        {
            transient = message;
            transientUntil = Time.unscaledTime + seconds;
        }
    }
}
