using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Before1800.PartB.Tests")]

namespace Before1800.PartB
{
    // C keeps this handle with its request until the response arrives on Unity's main thread.
    public sealed class ModelRequest
    {
        public Guid RunId { get; }
        public Guid RequestId { get; } = Guid.NewGuid();
        public ulong Version { get; }
        public SessionSnapshot Snapshot { get; }
        internal PauseToken Wait { get; }
        internal string PlayerReply { get; set; }
        internal string AcceptedAction { get; set; }
        internal bool IsPlayerRequest { get; set; }

        internal ModelRequest(SessionState state)
        {
            RunId = state.RunId;
            Version = state.Version;
            Snapshot = state.ReadSnapshot();
            Wait = state.AcquirePause(PauseReason.ModelWait, RequestId.ToString("N"));
        }
    }

    [RequireComponent(typeof(GreyboxWorld), typeof(GreyboxHUD))]
    [DefaultExecutionOrder(-150)]
    public sealed class PartBScene : MonoBehaviour
    {
        public SessionState State { get; private set; }
        public GreyboxWorld World { get; private set; }
        public ClockRehearsal Clock { get; private set; }
        public LastVisitStory Story { get; private set; }
        public string Feedback { get; private set; }
        public bool IntroOpen => intro != null;
        public bool DialogueOpen => dialogue != null;
        internal int ConversationNumber { get; private set; }
        public bool MenuOpen => menu != null;
        public bool JournalOpen => journal != null;
        public bool ObservingSetBack => observingSetBack != null;
        public bool ObservingCamera => trackingCamera != null;
        public bool ObservingBroadcast => observingBroadcast != null;
        public float CameraLockProgress => trackingCamera == null || CameraTrackLockDuration <= 0f
            ? 0f : Mathf.Clamp01(cameraLockSeconds / CameraTrackLockDuration);
        public string CameraTrackHint { get; private set; }
        public string BroadcastHint { get; private set; }
        public bool CheckingRoute => checkingRouteRun != Guid.Empty;
        public bool ModelWait => State.HasPause(PauseReason.ModelWait);
        public bool TestPathBlocked => World.ObservationBlockedForTest;
        public event Action<ActionTicket> ObservationReady;
        // A dialogue service subscribes on the game's main thread. The same request
        // must come back with its response (or cancellation) even on retry.
        public event Action<ModelRequest, string> PlayerLineRequested;
        public bool HasDialogueService => PlayerLineRequested != null;

        private GreyboxHUD hud;
        private PauseToken intro, dialogue, menu, journal, testWait, navigationError;
        private ModelRequest modelRequest;
        private ActionTicket testCompletion;
        private ActionTicket observingSetBack;
        private ActionTicket trackingCamera;
        private ActionTicket observingBroadcast;
        private Guid checkingRouteRun;
        private float observationSeconds, routeSeconds, cameraLockSeconds;
        private const float SetBackObservationDuration = 2f;
        private const float RouteCheckDuration = 1f;
        private const float CameraTrackLockDuration = 1.2f;
        private float previousTimeScale;
        private CursorLockMode originalLockMode;
        private bool originalCursorVisible, frozen, skipNextInput;

        private void Awake()
        {
            originalLockMode = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            World = GetComponent<GreyboxWorld>();
            World.Initialize();
            hud = GetComponent<GreyboxHUD>();
            State = new SessionState();
            var clockProps = GetComponentInChildren<ClockRehearsalProps>();
            if (clockProps) Clock = new ClockRehearsal(State, World, clockProps);
            if (World.UsesSceneBindings) { Story = gameObject.AddComponent<LastVisitStory>(); Story.Bind(this); }
            if (World.UsesSceneBindings) gameObject.AddComponent<ResidenceMemories>().Bind(this);
            gameObject.AddComponent<ResidenceAudio>();
            gameObject.AddComponent<LinVoice>();
            gameObject.AddComponent<WorldLanguage>();
            gameObject.AddComponent<PartCDialogue>();
            EnterIntro();
        }

        public void Restart()
        {
            CancelModelRequest();
            State.Restart();
            intro = dialogue = menu = journal = testWait = navigationError = null;
            testCompletion = null;
            ResetSetBackChecks();
            ClearBroadcastObservation();
            World.ResetWorld();
            Clock?.Reset();
            EnterIntro();
        }

        private void EnterIntro()
        {
            Feedback = "";
            hud.Bind(this);
            intro = State.AcquirePause(PauseReason.Intro, "intro");
            ApplySimulationGate();
        }

        internal bool AcceptHardwareInput { get; set; } = true;
        private void Update()
        {
            hud.ReadShortcuts();
            ApplySimulationGate();
            if (State.IsPaused) return;
            if (skipNextInput)
            {
                skipNextInput = false;
                return;
            }

            if(!AcceptHardwareInput){StepSimulation(Time.deltaTime,Vector2.zero,Vector2.zero);return;}

            // Keep looking available without a mouse (trackpads, presentations and accessibility).
            // Arrow keys belong to UI navigation while paused, and to the camera while exploring.
            var move = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            var look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            look += new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0)) * (65f / 2.1f * Time.deltaTime);
            StepSimulation(Time.deltaTime, move, look);
        }

        internal void StepSimulation(float dt, Vector2 move, Vector2 look)
        {
            if (State.IsPaused || dt <= 0f) return;
            State.AdvanceClock(dt, 5);
            if (State.TimedOut)
            {
                testCompletion = null;
                ResetSetBackChecks();
                ClearBroadcastObservation();
                Clock?.EndRun();
                Feedback = State.LastResult;
                ApplySimulationGate();
                return;
            }

            if (State.TryStartPending(out _))
            {
                World.ResetTravel();
                Feedback = State.LastResult;
            }
            World.StepPlayer(move, look, dt);
            World.StepDoors(dt);
            StepLin(dt);
            Clock?.Step(dt);
            StepSetBackChecks(dt);
            StepCameraTracking(dt);
            StepBroadcastWatch(dt);
            // Surveillance runs for the whole session: the housing always faces Lin.
            if (World.Camera)
            {
                World.Camera.StepTrack(World.Lin, dt);
                World.Camera.StepSignal(dt);
            }
            if (testCompletion != null)
            {
                var ticket = testCompletion;
                testCompletion = null;
                bool accepted = TryCompleteCameraExperiment(ticket, Guid.NewGuid());
                TestFeedback(accepted ? "Mock E1 completion accepted." : "Mock E1 completion rejected.");
            }
        }

        private void StepLin(float dt)
        {
            var ticket = State.Active;
            if (ticket == null) return;
            TravelResult travel;
            switch (State.Stage)
            {
                case ActionStage.Following:
                    travel = World.StepFollow(dt, State.EscapeAgreed);
                    if (travel == TravelResult.Failed) FailTravel(ticket);
                    return;
                case ActionStage.Arrived:
                    if (trackingCamera != null && ticket == trackingCamera) return;
                    if (observingBroadcast != null && ticket == observingBroadcast) return;
                    if(World.AtObservation(ticket.TargetId))World.ClearSightWait();
                    else
                    {
                        if(World.UsesSceneBindings && World.CanInspect(ticket.TargetId)) World.StepInspect(ticket.TargetId,dt);
                        else if(State.ReportObservationLost(ticket))ShowNavigationError();
                    }
                    return;
                case ActionStage.Returning:
                    travel = World.StepReturn(ticket.TargetId, dt);
                    break;
                case ActionStage.Withdrawing:
                    travel = World.StepWithdraw(dt);
                    break;
                case ActionStage.Moving:
                    if (ticket.Action == "stop")
                    {
                        State.ReportCompleted(ticket);
                        Feedback = "Lin stopped.";
                        return;
                    }
                    travel = World.StepInspect(ticket.TargetId, dt);
                    if (travel == TravelResult.Arrived && State.ReportArrived(ticket))
                    {
                        Feedback = State.LastResult;
                        ObservationReady?.Invoke(ticket);
                        return;
                    }
                    break;
                default:
                    return;
            }
            if (travel == TravelResult.Failed) FailTravel(ticket);
            else if (travel == TravelResult.Arrived && State.ReportCompleted(ticket)) Feedback = State.LastResult;
        }

        private void FailTravel(ActionTicket ticket)
        {
            if (State.ReportFailure(ticket, World.FailureCode))
            {
                Debug.Log("[Navigation] "+ticket.TargetId+" "+World.FailureCode+" at "+World.Lin.position);
                ShowNavigationError();
            }
        }

        private void ShowNavigationError()
        {
            Feedback = State.LastResult;
            if(World.UsesSceneBindings)
            {
                // Navigation feedback belongs in the room; it must not trap the
                // player in a paused error dialog or erase already witnessed facts.
                World.ResetTravel();
                hud.Flow.NavigationNotice("这边过不去。给林让一点位置，再邀请她试一次。");
                return;
            }
            navigationError = State.AcquirePause(PauseReason.Error, "navigation");
            ApplySimulationGate();
        }

        public void RecoverNavigation()
        {
            if (navigationError == null) return;
            Release(ref navigationError);
            Feedback = State.CanRetryReturn
                ? "Move clear of Lin's route, then choose Retry return in the menu."
                : "Navigation stopped. Open dialogue to give Lin a new command.";
        }

        public void RetryReturn()
        {
            if (!State.TryQueueReturnRetry()) return;
            Release(ref navigationError);
            Release(ref menu);
            Feedback = State.LastResult;
        }

        // A sends one independent device completion using the ticket it captured for that experiment.
        public bool TryCompleteCameraExperiment(ActionTicket ticket, Guid eventId)
        {
            if (ticket != null && ticket.TargetId == SessionState.CameraTarget &&
                State.Stage == ActionStage.Arrived && !State.IsPaused && ticket == State.Active &&
                trackingCamera == null && !World.IsAtObservation && State.ReportObservationLost(ticket))
                ShowNavigationError();
            if (!State.TryCompleteCameraExperiment(ticket, eventId)) return false;
            trackingCamera = null;
            cameraLockSeconds = 0f;
            CameraTrackHint = "";
            if (World.Camera) World.Camera.SetSignal(CameraSignal.Confirmed);
            World.ResetTravel();
            Feedback = "林亲眼看见镜头外壳跟着自己转动。";
            return true;
        }

        public string SetBackInteractionHint()
        {
            if (State.IsPaused) return null;
            if (trackingCamera != null) return "Lin is changing marks. Watch the housing turn with her.";
            if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.CameraTarget &&
                World.Camera && World.Camera.CanStartTracking(World.ViewCamera.transform, World.Lin))
                return "[F] Ask Lin to change marks and watch the camera";
            string tvHint = BroadcastInteractionHint();
            if (tvHint != null) return tvHint;
            var station = World.Station;
            if (!station) return null;
            if (checkingRouteRun != Guid.Empty) return "Checking the passage. Stay here and face the sign.";
            if (observingSetBack != null) return "Lin is looking for the support and the production mark. Keep his view clear.";
            var view = World.ViewCamera.transform;
            if (station.CanOpenDoor(view)) return "[F] Open the maintenance door";
            if (!State.RouteConfirmed && station.CanCheckRoute(World.Player, view)) return "[F] Check whether the passage is clear";
            if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.SetBackTarget &&
                station.CanStartObservation(view)) return "[F] Ask Lin to look behind the set";
            return null;
        }

        public string BroadcastInteractionHint()
        {
            if (State.IsPaused || !World.Tv || State.E4Verified) return null;
            var tv = World.Tv;
            var view = World.ViewCamera.transform;
            bool atTv = State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.BroadcastTarget &&
                World.AtObservation(SessionState.BroadcastTarget);
            if (!atTv) return null;
            if (!tv.PlayerCanOperate(view)) return "对准客厅电视，继续预测实验";
            if (tv.CanStartSequence) return "[F] 启动电视序列（预测已锁定）";
            if (tv.CanConfirmPredictedShow)
                return "[F] 请林点播预测节目：" + TvStation.LabelFor(tv.PredictedProgram);
            if (tv.CanIntervene)
                return "[F] 请林改口点播：" + TvStation.LabelFor(TvStation.AlternateFor(tv.PredictedProgram));
            if (tv.Phase == TvPhase.Failed) return "预测失败。回对话重新登记后重试";
            if (!tv.HasPrediction) return "先在对话里登记预测，再启动";
            return null;
        }

        public bool TryRegisterTvPrediction(TvProgram program, out string reason)
        {
            reason = "客厅电视还没准备好。";
            if (State.E4Verified || !World.Tv) return false;
            // Dialogue pause is allowed: prediction must be locked before the live sequence.
            if (World.Tv.Phase == TvPhase.Running || World.Tv.Phase == TvPhase.Step1Matched)
                return World.Tv.TryRegisterPrediction(program, out reason);
            bool ok = World.Tv.TryRegisterPrediction(program, out reason);
            if (ok) Feedback = reason;
            return ok;
        }

        public bool TryLinRequestTvShow(TvProgram program, out string reason)
        {
            reason = "现在不能点播。";
            // Dialogue may stay open while choosing the requested show (example 3 mismatch).
            if (State.TimedOut || !World.Tv || observingBroadcast == null || observingBroadcast != State.Active)
                return false;
            if (State.Stage != ActionStage.Arrived) return false;
            if (!World.AtObservation(SessionState.BroadcastTarget))
            {
                if (!State.IsPaused && State.ReportObservationLost(observingBroadcast)) ShowNavigationError();
                ClearBroadcastObservation();
                return false;
            }
            bool matched = World.Tv.TryLinRequestShow(program, out reason);
            Feedback = reason;
            BroadcastHint = reason;
            if (!matched)
            {
                // Prediction mismatch: experiment fails; ticket stays arrived so the player can re-register.
                ClearBroadcastObservation();
                return false;
            }
            return true;
        }

        public void InteractWithBroadcast()
        {
            if (State.IsPaused || !World.Tv || State.E4Verified) return;
            var tv = World.Tv;
            var ticket = State.Active;
            if (ticket == null || ticket.TargetId != SessionState.BroadcastTarget || State.Stage != ActionStage.Arrived)
                return;
            if (!World.AtObservation(SessionState.BroadcastTarget))
            {
                if (State.ReportObservationLost(ticket)) ShowNavigationError();
                ClearBroadcastObservation();
                return;
            }
            if (!tv.PlayerCanOperate(World.ViewCamera.transform))
            {
                Feedback = tv.DescribeBlock(World.ViewCamera.transform, World.Lin, true);
                return;
            }

            if (tv.CanStartSequence)
            {
                if (!tv.TryStartSequence(out var started))
                {
                    Feedback = started;
                    return;
                }
                observingBroadcast = ticket;
                BroadcastHint = started;
                Feedback = started;
                State.Discover(SessionState.BroadcastTarget);
                return;
            }

            if (tv.CanConfirmPredictedShow)
            {
                TryLinRequestTvShow(tv.PredictedProgram, out _);
                return;
            }

            if (tv.CanIntervene)
            {
                if (!tv.TryIntervene(out var intervened))
                {
                    Feedback = intervened;
                    BroadcastHint = intervened;
                    ClearBroadcastObservation();
                    return;
                }
                Feedback = intervened;
                BroadcastHint = intervened;
                TryCompleteBroadcastExperiment(ticket, Guid.NewGuid());
            }
        }

        public bool TryBeginCameraTracking(ActionTicket ticket)
        {
            var camera = World.Camera;
            if (State.IsPaused || !camera || trackingCamera != null || ticket == null || ticket != State.Active ||
                ticket.TargetId != SessionState.CameraTarget || State.Stage != ActionStage.Arrived ||
                !camera.CanStartTracking(World.ViewCamera.transform, World.Lin)) return false;
            if (!camera.CanReachSecondMark())
            {
                Feedback = "The second observation mark is blocked. Clear the path first.";
                return false;
            }
            trackingCamera = ticket;
            cameraLockSeconds = 0f;
            CameraTrackHint = "跟踪实验开始：林正走向第二个观察点，盯着镜头外壳。";
            if (World.Camera) World.Camera.SetSignal(CameraSignal.Watching);
            World.ResetTravel();
            Feedback = CameraTrackHint;
            return true;
        }

        // The same interaction and timers drive the greybox and a scene with an authored station.
        public void InteractWithSetBack()
        {
            if (State.IsPaused) return;
            if (TryBeginCameraTracking(State.Active)) return;
            if (State.Active?.TargetId == SessionState.BroadcastTarget)
            {
                InteractWithBroadcast();
                return;
            }
            var station = World.Station;
            if (!station) return;
            var view = World.ViewCamera.transform;
            if (station.TryOpenDoor(view))
            {
                State.Discover(SessionState.SetBackTarget);
                Feedback = "Maintenance door open. Lin can inspect the set.";
                return;
            }
            if (!TryBeginRouteCheck(State.RunId)) TryBeginSetBackObservation(State.Active);
        }

        private void StepCameraTracking(float dt)
        {
            if (trackingCamera == null) return;
            if (trackingCamera != State.Active || State.Stage != ActionStage.Arrived || !World.Camera)
            {
                ClearCameraTracking(CameraSignal.Idle);
                return;
            }
            var travel = World.StepCameraTrack(dt);
            if (travel == TravelResult.Failed)
            {
                if (State.ReportFailure(trackingCamera, World.FailureCode)) ShowNavigationError();
                ClearCameraTracking(CameraSignal.Idle);
                return;
            }
            if (travel == TravelResult.Arrived)
            {
                // Once Lin is on mark B and facing the device, accept the always-on pan
                // as verification. Exact lens occlusion is only a soft warning.
                bool locked = World.Camera.AtSecondMark(World.Lin);
                bool clear = World.Camera.TrackingVisible(World.Lin);
                if (locked)
                {
                    if (cameraLockSeconds <= 0f) World.Camera.SetSignal(CameraSignal.Verifying);
                    cameraLockSeconds += dt;
                    int percent = Mathf.Clamp(Mathf.RoundToInt(CameraLockProgress * 100f), 1, 99);
                    CameraTrackHint = clear
                        ? "第二观察点锁定中 " + percent + "% — 镜头正在对准林。"
                        : "第二观察点锁定中 " + percent + "% — 外壳已转向，视线略有遮挡也可完成。";
                    Feedback = CameraTrackHint;
                    if (cameraLockSeconds >= CameraTrackLockDuration)
                        TryCompleteCameraExperiment(trackingCamera, Guid.NewGuid());
                }
                else
                {
                    cameraLockSeconds = 0f;
                    World.Camera.SetSignal(CameraSignal.Watching);
                    CameraTrackHint = "林靠近第二点了，等她转朝镜头。";
                    Feedback = CameraTrackHint;
                }
            }
            else
            {
                cameraLockSeconds = 0f;
                World.Camera.SetSignal(CameraSignal.Watching);
                CameraTrackHint = "林正换到第二个观察点，镜头外壳应持续转向她。";
                Feedback = CameraTrackHint;
            }
        }

        private void ClearCameraTracking(CameraSignal signal)
        {
            trackingCamera = null;
            cameraLockSeconds = 0f;
            CameraTrackHint = "";
            if (World.Camera) World.Camera.SetSignal(signal);
        }

        // Scene controls can call these entry points with the identities captured for this interaction.
        public bool TryBeginSetBackObservation(ActionTicket ticket)
        {
            var station = World.Station;
            if (State.IsPaused || !station || observingSetBack != null || ticket == null || ticket != State.Active ||
                ticket.TargetId != SessionState.SetBackTarget || State.Stage != ActionStage.Arrived ||
                !World.AtObservation(ticket.TargetId) || !station.CanStartObservation(World.ViewCamera.transform)) return false;
            if (!station.InspectionVisible(World.Lin))
            {
                Feedback = "Lin needs a clear view of both the support and the production mark.";
                return false;
            }
            observingSetBack = ticket;
            observationSeconds = 0f;
            Feedback = "Lin is inspecting the back of the set.";
            return true;
        }

        public bool TryBeginRouteCheck(Guid runId)
        {
            if (State.IsPaused || runId != State.RunId || checkingRouteRun != Guid.Empty || State.RouteConfirmed ||
                !World.Station || !World.Station.CanCheckRoute(World.Player, World.ViewCamera.transform)) return false;
            checkingRouteRun = runId;
            routeSeconds = 0f;
            Feedback = "Checking the passage.";
            return true;
        }

        private void StepSetBackChecks(float dt)
        {
            var station = World.Station;
            if (observingSetBack != null)
            {
                if (observingSetBack != State.Active || State.Stage != ActionStage.Arrived)
                    observingSetBack = null;
                else if (!station || !station.InspectionVisible(World.Lin))
                {
                    if (State.ReportFailure(observingSetBack, "Inspection interrupted. Clear the view and ask Lin to inspect again."))
                        ShowNavigationError();
                    observingSetBack = null;
                }
                else
                {
                    observationSeconds += dt;
                    if (observationSeconds >= SetBackObservationDuration)
                        TryCompleteSetBackExperiment(observingSetBack, Guid.NewGuid());
                }
            }
            if (checkingRouteRun == Guid.Empty || State.IsPaused) return;
            if (!station || !station.CanCheckRoute(World.Player, World.ViewCamera.transform))
            {
                checkingRouteRun = Guid.Empty;
                Feedback = "Passage check interrupted. Face the sign at the checkpoint and try again.";
                return;
            }
            routeSeconds += dt;
            if (routeSeconds >= RouteCheckDuration) TryConfirmRoute(checkingRouteRun, Guid.NewGuid());
        }

        internal bool TryCompleteSetBackExperiment(ActionTicket ticket, Guid eventId)
        {
            if (ticket == null || ticket != observingSetBack || observationSeconds < SetBackObservationDuration ||
                !World.Station || !World.AtObservation(SessionState.SetBackTarget) || !World.Station.InspectionVisible(World.Lin) ||
                !State.TryCompleteSetBackExperiment(ticket, eventId)) return false;
            observingSetBack = null;
            World.ResetTravel();
            Feedback = State.LastResult;
            return true;
        }

        internal bool TryConfirmRoute(Guid runId, Guid eventId)
        {
            if (runId != checkingRouteRun || routeSeconds < RouteCheckDuration || !World.Station ||
                !World.Station.CanCheckRoute(World.Player, World.ViewCamera.transform) ||
                !State.TryConfirmRoute(runId, eventId)) return false;
            checkingRouteRun = Guid.Empty;
            Feedback = State.LastResult;
            return true;
        }

        private void StepBroadcastWatch(float _)
        {
            if (observingBroadcast == null) return;
            if (observingBroadcast != State.Active || State.Stage != ActionStage.Arrived || !World.Tv)
            {
                ClearBroadcastObservation();
                return;
            }
            if (!World.AtObservation(SessionState.BroadcastTarget))
            {
                if (State.ReportObservationLost(observingBroadcast)) ShowNavigationError();
                if (World.Tv.Phase == TvPhase.Running || World.Tv.Phase == TvPhase.Step1Matched)
                    World.Tv.ResetStation();
                ClearBroadcastObservation();
                Feedback = "林离开了电视前，本次序列中断。可重新登记预测。";
            }
        }

        internal bool TryCompleteBroadcastExperiment(ActionTicket ticket, Guid eventId)
        {
            if (ticket == null || ticket != observingBroadcast || !World.Tv || !World.Tv.IsComplete ||
                !World.AtObservation(SessionState.BroadcastTarget) ||
                !State.TryCompleteBroadcastExperiment(ticket, eventId)) return false;
            ClearBroadcastObservation();
            World.ResetTravel();
            Feedback = "林确认电视会按她想看的节目切换。";
            BroadcastHint = Feedback;
            return true;
        }

        private void ClearBroadcastObservation()
        {
            observingBroadcast = null;
            BroadcastHint = "";
        }

        private void ResetSetBackChecks()
        {
            observingSetBack = null;
            trackingCamera = null;
            checkingRouteRun = Guid.Empty;
            observationSeconds = routeSeconds = cameraLockSeconds = 0f;
            CameraTrackHint = "";
            ClearBroadcastObservation();
            if (World && World.Camera) World.Camera.SetSignal(CameraSignal.Idle);
            if (World && World.Tv && !State.E4Verified) World.Tv.ResetStation();
        }

        public bool TryBeginModelRequest(out ModelRequest request)
        {
            request = null;
            if (!DialogueOpen || MenuOpen || JournalOpen || ModelWait || navigationError != null || State.TimedOut) return false;
            request = modelRequest = new ModelRequest(State);
            ApplySimulationGate();
            return true;
        }

        internal bool IsCurrentRequest(ModelRequest request) => request != null && modelRequest == request;

        internal bool TrySendPlayerLine(string text, out ModelRequest request)
        {
            request = null;
            if (!HasDialogueService || string.IsNullOrWhiteSpace(text) || !TryBeginModelRequest(out request)) return false;
            request.IsPlayerRequest = true;
            try { PlayerLineRequested.Invoke(request, text); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Before1800B / Dialogue] " + ex.GetType().Name);
                TryCancelModelRequest(request);
            }
            return true;
        }

        public bool TryReceivePlayerReply(ModelRequest request, string dialogueText, string action, string targetId, out string reason)
        {
            reason = "Request expired or empty reply.";
            if (request == null || request != modelRequest || !request.IsPlayerRequest) return false;
            if (string.IsNullOrWhiteSpace(dialogueText))
            {
                TryCancelModelRequest(request);
                return false;
            }
            if (!TryApplyModelAction(request, action, targetId, out reason)) return false;
            request.PlayerReply = dialogueText.Trim();
            request.AcceptedAction = action;
            return true;
        }

        public bool TryApplyModelAction(ModelRequest request, string action, string targetId, out string reason)
        {
            if (request == null || request != modelRequest || !DialogueOpen)
            {
                reason = "Request expired.";
                return false;
            }
            if (action == "end_conversation" && !request.IsPlayerRequest)
            {
                reason = "Only a conversation reply can close this conversation.";
                TryCancelModelRequest(request);
                return false;
            }
            bool accepted = TryPropose(request.RunId, request.Version, action, targetId, out reason);
            CancelModelRequest();
            Feedback = reason;
            return accepted;
        }

        public bool TryCancelModelRequest(ModelRequest request)
        {
            if (request == null || request != modelRequest) return false;
            CancelModelRequest();
            return true;
        }

        private void CancelModelRequest()
        {
            if (modelRequest == null) return;
            State.ReleasePause(modelRequest.Wait);
            modelRequest = null;
        }

        private bool TryPropose(Guid run, ulong version, string action, string targetId, out string reason)
        {
            if (action == "inspect" && (targetId == SessionState.CameraTarget ||
                targetId == SessionState.SetBackTarget || targetId == SessionState.BroadcastTarget)
                && !World.CanInspect(targetId))
            {
                reason = targetId == SessionState.CameraTarget ? "Camera observation point is unreachable." :
                    targetId == SessionState.BroadcastTarget ? "TV observation point is unavailable or unreachable." :
                    "Set-back observation point is unavailable or unreachable.";
                return false;
            }
            return State.TryQueue(run, version, action, targetId, out reason);
        }

        internal bool TryPlayerAction(string action, string targetId)
        {
            if (!DialogueOpen || MenuOpen || JournalOpen || ModelWait || navigationError != null || State.TimedOut)
                return false;
            if (action == "restore") return State.TryRestoreConversation();
            if (action == "stop") return State.TryQueue("stop", null, out _);
            return TryPropose(State.RunId, State.Version, action, targetId, out _);
        }

        internal void ToggleJournal()
        {
            if (journal != null) Release(ref journal);
            else if (!IntroOpen && !MenuOpen && !ModelWait && navigationError == null && !State.TimedOut)
                Acquire(ref journal, PauseReason.Journal, "field-notes");
        }

        public void SubmitTestAction(string action)
        {
            if (!DialogueOpen || ModelWait || MenuOpen || navigationError != null || State.TimedOut) return;
            string message;
            if (action == "restore")
                message = State.TryRestoreConversation() ? State.LastResult : "Lin is already available.";
            else if (action == "stop")
                State.TryQueue("stop", null, out message);
            else if (action == "inspect_set_back")
                TryPropose(State.RunId, State.Version, "inspect", SessionState.SetBackTarget, out message);
            else
            {
                var target = action == "follow" ? "player" : action == "inspect" ? SessionState.CameraTarget : null;
                TryPropose(State.RunId, State.Version, action, target, out message);
            }
            TestFeedback(message);
        }

        public void ToggleIntroOff() => Release(ref intro);

        public void ToggleDialogue()
        {
            if (IntroOpen || State.TimedOut || navigationError != null) return;
            if (DialogueOpen)
            {
                CancelModelRequest();
                Release(ref dialogue);
            }
            else
            {
                Acquire(ref dialogue, PauseReason.Dialogue, "dialogue");
                ConversationNumber++;
            }
        }

        public void ToggleMenu()
        {
            if (IntroOpen || State.TimedOut) return;
            if (MenuOpen) Release(ref menu);
            else Acquire(ref menu, PauseReason.Menu, "menu");
        }

        public void ToggleModelWait()
        {
            if (IntroOpen || State.TimedOut || modelRequest != null) return;
            if (testWait != null) Release(ref testWait);
            else Acquire(ref testWait, PauseReason.ModelWait, "manual-test-wait");
        }

        public void TestCompleteCamera()
        {
            if (State.IsPaused || State.Stage != ActionStage.Arrived || State.Active?.TargetId != SessionState.CameraTarget)
            {
                TestFeedback("Mock completion needs a live inspection at the observation point.");
                return;
            }
            testCompletion = State.Active;
        }

        public void TestNearDeadline()
        {
            if (State.TrySetNearDeadlineForTest()) TestFeedback(State.LastResult);
        }

        public void TestResolveC2()
        {
            if (State.TryResolveC2ForTest()) TestFeedback(State.LastResult);
        }

        public void ToggleTestPathBlocked()
        {
            if ((!MenuOpen && navigationError == null) || State.TimedOut) return;
            World.SetObservationBlockedForTest(!TestPathBlocked);
            TestFeedback(TestPathBlocked ? "Observation point moved outside the NavMesh." : "Observation point restored.");
        }

        private void TestFeedback(string message)
        {
            Feedback = message;
            Debug.Log("[Before1800B / TEST] " + message);
        }

        private void Acquire(ref PauseToken token, PauseReason reason, string owner)
        {
            token = State.AcquirePause(reason, owner);
            ApplySimulationGate();
        }

        private void Release(ref PauseToken token)
        {
            if (token == null) return;
            State.ReleasePause(token);
            token = null;
            ApplySimulationGate();
        }

        private void ApplySimulationGate()
        {
            if (State.IsPaused == frozen) return;
            frozen = State.IsPaused;
            if (frozen)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = previousTimeScale;
                skipNextInput = true;
            }
            Cursor.lockState = frozen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = frozen;
        }

        private void OnDisable()
        {
            if (frozen) Time.timeScale = previousTimeScale;
            frozen = false;
            Cursor.lockState = originalLockMode;
            Cursor.visible = originalCursorVisible;
        }
    }
}
