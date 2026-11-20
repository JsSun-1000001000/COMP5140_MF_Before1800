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
        public string Feedback { get; private set; }
        public bool IntroOpen => intro != null;
        public bool DialogueOpen => dialogue != null;
        public bool MenuOpen => menu != null;
        public bool ModelWait => State.HasPause(PauseReason.ModelWait);
        public bool TestPathBlocked => World.ObservationBlockedForTest;
        public event Action<ActionTicket> ObservationReady;

        private GreyboxHUD hud;
        private PauseToken intro, dialogue, menu, testWait, navigationError;
        private ModelRequest modelRequest;
        private ActionTicket testCompletion;
        private ActionTicket observingSetBack;
        private Guid checkingRouteRun;
        private float observationSeconds, routeSeconds;
        private const float SetBackObservationDuration = 2f;
        private const float RouteCheckDuration = 1f;
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
            EnterIntro();
        }

        public void Restart()
        {
            CancelModelRequest();
            State.Restart();
            intro = dialogue = menu = testWait = navigationError = null;
            testCompletion = null;
            ResetSetBackChecks();
            World.ResetWorld();
            EnterIntro();
        }

        private void EnterIntro()
        {
            Feedback = "";
            hud.Bind(this);
            intro = State.AcquirePause(PauseReason.Intro, "intro");
            ApplySimulationGate();
        }

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

            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            var look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            StepSimulation(Time.deltaTime, move, look);
        }

        internal void StepSimulation(float dt, Vector2 move, Vector2 look)
        {
            if (State.IsPaused || dt <= 0f) return;
            State.AdvanceClock(dt);
            if (State.TimedOut)
            {
                testCompletion = null;
                ResetSetBackChecks();
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
            StepLin(dt);
            StepSetBackChecks(dt);
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
                    if (!World.AtObservation(ticket.TargetId) && State.ReportObservationLost(ticket)) ShowNavigationError();
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
            if (State.ReportFailure(ticket, World.FailureCode)) ShowNavigationError();
        }

        private void ShowNavigationError()
        {
            Feedback = State.LastResult;
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
                !World.IsAtObservation && State.ReportObservationLost(ticket))
                ShowNavigationError();
            if (!State.TryCompleteCameraExperiment(ticket, eventId)) return false;
            World.ResetTravel();
            Feedback = State.LastResult;
            return true;
        }

        public string SetBackInteractionHint()
        {
            var station = World.Station;
            if (!station || State.IsPaused) return null;
            if (checkingRouteRun != Guid.Empty) return "Checking the passage. Stay here and face the sign.";
            if (observingSetBack != null) return "Lin is looking for the support and the production mark. Keep his view clear.";
            var view = World.ViewCamera.transform;
            if (station.CanOpenDoor(view)) return "[F] Open the maintenance door";
            if (!State.RouteConfirmed && station.CanCheckRoute(World.Player, view)) return "[F] Check whether the passage is clear";
            if (State.Stage == ActionStage.Arrived && State.Active?.TargetId == SessionState.SetBackTarget &&
                station.CanStartObservation(view)) return "[F] Ask Lin to look behind the set";
            return null;
        }

        // The same interaction and timers drive the greybox and a scene with an authored station.
        public void InteractWithSetBack()
        {
            var station = World.Station;
            if (!station || State.IsPaused) return;
            var view = World.ViewCamera.transform;
            if (station.TryOpenDoor(view))
            {
                Feedback = "Maintenance door open. Lin can inspect the set.";
                return;
            }
            if (!TryBeginRouteCheck(State.RunId)) TryBeginSetBackObservation(State.Active);
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

        private void ResetSetBackChecks()
        {
            observingSetBack = null;
            checkingRouteRun = Guid.Empty;
            observationSeconds = routeSeconds = 0f;
        }

        public bool TryBeginModelRequest(out ModelRequest request)
        {
            request = null;
            if (!DialogueOpen || MenuOpen || ModelWait || navigationError != null || State.TimedOut) return false;
            request = modelRequest = new ModelRequest(State);
            ApplySimulationGate();
            return true;
        }

        public bool TryApplyModelAction(ModelRequest request, string action, string targetId, out string reason)
        {
            if (request == null || request != modelRequest || !DialogueOpen)
            {
                reason = "Request expired.";
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
            if (action == "inspect" && (targetId == SessionState.CameraTarget || targetId == SessionState.SetBackTarget)
                && !World.CanInspect(targetId))
            {
                reason = targetId == SessionState.CameraTarget ? "Camera observation point is unreachable." :
                    "Set-back observation point is unavailable or unreachable.";
                return false;
            }
            return State.TryQueue(run, version, action, targetId, out reason);
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
            else Acquire(ref dialogue, PauseReason.Dialogue, "dialogue");
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
