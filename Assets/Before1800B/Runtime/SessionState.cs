using System;
using System.Collections.Generic;

namespace Before1800.PartB
{
    public enum PauseReason { Intro, Dialogue, ModelWait, Menu, Error, Terminal, Journal }
    public enum CooperationMode { Talking, Following, Withdrawn }
    public enum ActionStage { Idle, Queued, Moving, Arrived, Following, Withdrawing, Returning, Completed, Failed }

    public sealed class PauseToken
    {
        public Guid RunId { get; }
        public PauseReason Reason { get; }
        public string Owner { get; }

        internal PauseToken(Guid runId, PauseReason reason, string owner)
        {
            RunId = runId;
            Reason = reason;
            Owner = owner;
        }
    }

    // Pass the original ticket through navigation and device callbacks. Looking up the
    // latest Active ticket in a late callback would misattribute an old observation.
    public sealed class ActionTicket
    {
        public Guid RunId { get; }
        public Guid ActionId { get; }
        public Guid ExperimentId { get; }
        public Guid RecoveryOfActionId { get; }
        public string Action { get; }
        public string TargetId { get; }

        internal ActionTicket(Guid runId, string action, string targetId, ActionTicket recoveryOf = null)
        {
            RunId = runId;
            ActionId = Guid.NewGuid();
            ExperimentId = recoveryOf != null ? recoveryOf.ExperimentId :
                action == "inspect" ? Guid.NewGuid() : Guid.Empty;
            RecoveryOfActionId = recoveryOf?.ActionId ?? Guid.Empty;
            Action = action;
            TargetId = targetId;
        }
    }

    [Serializable]
    public sealed class ConcernSnapshot
    {
        public string C1, C2, C3;
    }

    [Serializable]
    public sealed class ActionResultSnapshot
    {
        public string run_id, action_id, experiment_run_id, recovery_of_action_id, action, target_id, status, reason;
        public long game_elapsed_ms;

        internal ActionResultSnapshot Copy() => (ActionResultSnapshot)MemberwiseClone();
    }

    // Detached fields allow C to serialize a snapshot with Unity's JsonUtility.
    [Serializable]
    public sealed class SessionSnapshot
    {
        public string run_id, time_hms, cooperation_mode, ending, action_stage;
        public long state_version, game_elapsed_ms;
        public string[] verified_evidence;
        public string[] player_discoveries;
        public ConcernSnapshot concerns;
        public bool route_confirmed, escape_agreed;
        public string pending_action, active_action, target_id;
        public ActionResultSnapshot last_action_result;
        public ActionResultSnapshot[] recent_action_results;
    }

    // One instance per playing scene. Mutations are serialized on Unity's main thread.
    public sealed class SessionState
    {
        public const string CameraTarget = "camera_test";
        public const string SetBackTarget = "set_back";
        public const double DeadlineSeconds = 3600;

        private readonly HashSet<PauseToken> pauses = new HashSet<PauseToken>();
        private readonly HashSet<Guid> worldEvents = new HashSet<Guid>();
        private readonly HashSet<string> verified = new HashSet<string>();
        private readonly Dictionary<string, bool> concerns = new Dictionary<string, bool>();
        private readonly Queue<ActionResultSnapshot> recentResults = new Queue<ActionResultSnapshot>();
        private ActionStage activeStage;
        private ActionTicket failedReturn;

        public Guid RunId { get; private set; }
        public ulong Version { get; private set; }
        public double ElapsedGameSeconds { get; private set; }
        public bool TimedOut { get; private set; }
        public bool IsPaused => TimedOut || pauses.Count != 0;
        public bool E1Verified => verified.Contains("E1");
        public bool E5Verified => verified.Contains("E5");
        public bool CameraDiscovered { get; private set; }
        public bool DoorDiscovered { get; private set; }
        public bool EscapeAgreed { get; private set; }
        public bool RouteConfirmed { get; private set; }
        public CooperationMode Cooperation { get; private set; }
        public bool WithdrawUsed { get; private set; }
        public ActionTicket Pending { get; private set; }
        public ActionTicket Active { get; private set; }
        public ActionStage Stage => Pending != null ? ActionStage.Queued : activeStage;
        public string LastResult { get; private set; }
        public bool CanRetryReturn => failedReturn != null && failedReturn.RunId == RunId &&
            verified.Contains(EvidenceForTarget(failedReturn.TargetId)) && !TimedOut &&
            Pending == null && Active == null && Cooperation != CooperationMode.Withdrawn;

        public string[] VerifiedEvidence
        {
            get
            {
                var copy = new string[verified.Count];
                verified.CopyTo(copy);
                Array.Sort(copy, StringComparer.Ordinal);
                return copy;
            }
        }

        public string TimeHms
        {
            get
            {
                int seconds = 17 * 3600 + (int)ElapsedGameSeconds;
                return string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
            }
        }

        internal SessionState() { Restart(); }

        internal void Restart()
        {
            RunId = Guid.NewGuid();
            Version = 0;
            ElapsedGameSeconds = 0;
            TimedOut = EscapeAgreed = RouteConfirmed = WithdrawUsed = false;
            CameraDiscovered = DoorDiscovered = false;
            Cooperation = CooperationMode.Talking;
            Pending = Active = failedReturn = null;
            activeStage = ActionStage.Idle;
            pauses.Clear();
            worldEvents.Clear();
            verified.Clear();
            concerns.Clear();
            recentResults.Clear();
            concerns.Add("C1", false);
            concerns.Add("C2", false);
            concerns.Add("C3", false);
            LastResult = "Ready.";
        }

        public bool IsConcernResolved(string id)
        {
            return id != null && concerns.TryGetValue(id, out var resolved) && resolved;
        }

        internal void Discover(string targetId)
        {
            if (IsPaused) return;
            if (targetId == CameraTarget && !CameraDiscovered) CameraDiscovered = true;
            else if (targetId == SetBackTarget && !DoorDiscovered) DoorDiscovered = true;
            else return;
            Commit("Player noticed " + targetId + ".");
        }

        public SessionSnapshot ReadSnapshot()
        {
            var history = new ActionResultSnapshot[recentResults.Count];
            int index = 0;
            foreach (var result in recentResults) history[index++] = result.Copy();
            return new SessionSnapshot
            {
                run_id = RunId.ToString("N"), state_version = (long)Version,
                time_hms = TimeHms, game_elapsed_ms = (long)(ElapsedGameSeconds * 1000),
                verified_evidence = VerifiedEvidence,
                player_discoveries = CameraDiscovered
                    ? (DoorDiscovered ? new[] { CameraTarget, SetBackTarget } : new[] { CameraTarget })
                    : (DoorDiscovered ? new[] { SetBackTarget } : Array.Empty<string>()),
                concerns = new ConcernSnapshot
                {
                    C1 = IsConcernResolved("C1") ? "resolved" : "open",
                    C2 = IsConcernResolved("C2") ? "resolved" : "open",
                    C3 = IsConcernResolved("C3") ? "resolved" : "open"
                },
                cooperation_mode = Cooperation.ToString().ToLowerInvariant(),
                route_confirmed = RouteConfirmed, escape_agreed = EscapeAgreed,
                ending = TimedOut ? "timeout" : "none",
                pending_action = Pending?.Action, active_action = Active?.Action,
                action_stage = Stage.ToString().ToLowerInvariant(),
                target_id = (Pending ?? Active)?.TargetId,
                recent_action_results = history,
                last_action_result = history.Length > 0 ? history[history.Length - 1].Copy() : null
            };
        }

        internal PauseToken AcquirePause(PauseReason reason, string owner)
        {
            if (TimedOut || reason == PauseReason.Terminal) return null;
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A pause requires an owner.", nameof(owner));
            var token = new PauseToken(RunId, reason, owner);
            pauses.Add(token);
            return token;
        }

        internal bool ReleasePause(PauseToken token)
        {
            return token != null && token.RunId == RunId && pauses.Remove(token);
        }

        public bool HasPause(PauseReason reason)
        {
            if (reason == PauseReason.Terminal) return TimedOut;
            foreach (var token in pauses)
                if (token.Reason == reason) return true;
            return false;
        }

        // Explicit menu-only test input. Normal clock ticks always obey all pauses.
        internal bool TrySetNearDeadlineForTest()
        {
            const double nearDeadline = DeadlineSeconds - 5;
            if (!HasPause(PauseReason.Menu) || TimedOut || ElapsedGameSeconds >= nearDeadline) return false;
            ElapsedGameSeconds = nearDeadline;
            Commit("TEST: clock advanced to 17:59:55.");
            return true;
        }

        // Represents a validated C2 decision for the state-panel demo only.
        internal bool TryResolveC2ForTest()
        {
            if (TimedOut || !HasPause(PauseReason.Menu) || concerns["C2"]) return false;
            concerns["C2"] = true;
            Commit("TEST: C2 resolved.");
            return true;
        }

        // Call before dispatching any world events for the frame. Timeouts win ties.
        internal void AdvanceClock(double activeRealSeconds, double gameSecondsPerRealSecond = 10)
        {
            if (IsPaused || activeRealSeconds == 0) return;
            if (double.IsNaN(activeRealSeconds) || double.IsInfinity(activeRealSeconds) || activeRealSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(activeRealSeconds));
            if (double.IsNaN(gameSecondsPerRealSecond) || double.IsInfinity(gameSecondsPerRealSecond) || gameSecondsPerRealSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(gameSecondsPerRealSecond));

            ElapsedGameSeconds = Math.Min(DeadlineSeconds, ElapsedGameSeconds + activeRealSeconds * gameSecondsPerRealSecond);
            if (ElapsedGameSeconds < DeadlineSeconds) return;
            TimedOut = true;
            pauses.Clear();
            if (Active != null) RecordActionResult(Active, "cancelled", "Deadline reached.");
            Pending = Active = failedReturn = null;
            activeStage = ActionStage.Idle;
            Commit("18:00. Time expired.");
        }

        // Local input may explicitly cancel the pending or active movement with stop.
        internal bool TryQueue(string action, string targetId, out string reason)
        {
            if (TimedOut) return Reject("Time expired.", out reason);
            if (action == "stop")
            {
                if (targetId != null) return Reject("Stop takes no target.", out reason);
                if (Cooperation == CooperationMode.Withdrawn) return Reject("Restore conversation first.", out reason);
                failedReturn = null;
                Pending = new ActionTicket(RunId, "stop", null);
                Commit("Stop queued.");
                reason = LastResult;
                return true;
            }
            return TryQueue(RunId, Version, action, targetId, out reason);
        }

        // The C adapter calls this after validating the request identity and payload.
        internal bool TryQueue(Guid expectedRunId, ulong expectedVersion, string action, string targetId, out string reason)
        {
            if (expectedRunId != RunId || expectedVersion != Version) return Reject("Stale run or snapshot.", out reason);
            if (TimedOut) return Reject("Time expired.", out reason);
            if (action == "reply" && targetId == null)
            {
                reason = "Reply accepted.";
                return true;
            }
            if (Pending != null) return Reject("A command is already queued.", out reason);
            if (Cooperation == CooperationMode.Withdrawn) return Reject("Restore conversation first.", out reason);
            if (Active != null && Active.Action != "follow") return Reject("Finish or stop the current action first.", out reason);

            switch (action)
            {
                case "follow":
                    if (targetId != "player") return Reject("Follow requires player.", out reason);
                    if (Active != null)
                    {
                        reason = "Already following.";
                        return true;
                    }
                    break;
                case "inspect":
                    string evidence = EvidenceForTarget(targetId);
                    if (evidence == null) return Reject("Unknown inspection target.", out reason);
                    if (verified.Contains(evidence)) return Reject(evidence + " already verified.", out reason);
                    break;
                case "withdraw":
                    if (targetId != null) return Reject("Withdraw takes no target.", out reason);
                    if (WithdrawUsed) return Reject("Withdraw was already used this run.", out reason);
                    WithdrawUsed = true;
                    Cooperation = CooperationMode.Withdrawn;
                    if (Active != null) RecordActionResult(Active, "cancelled", "Replaced by withdrawal.");
                    Active = null;
                    break;
                default:
                    return Reject("Unsupported action.", out reason);
            }

            failedReturn = null;
            Pending = new ActionTicket(RunId, action, targetId);
            Commit(action + " queued.");
            reason = LastResult;
            return true;
        }

        // Only the failed return leg can be replayed. The completed experiment
        // keeps its original identity while each navigation attempt gets a fresh ticket.
        internal bool TryQueueReturnRetry()
        {
            if (!CanRetryReturn) return false;
            Pending = new ActionTicket(RunId, failedReturn.Action, failedReturn.TargetId, failedReturn);
            failedReturn = null;
            Commit(Pending.TargetId + " return queued for retry.");
            return true;
        }

        internal bool TryStartPending(out ActionTicket ticket)
        {
            ticket = null;
            if (Pending == null || IsPaused) return false;
            ticket = Pending;
            Pending = null;
            if (Active != null) RecordActionResult(Active, "cancelled", "Replaced by " + ticket.Action + ".");
            Active = ticket;
            switch (ticket.Action)
            {
                case "follow":
                    Cooperation = CooperationMode.Following;
                    activeStage = ActionStage.Following;
                    break;
                case "withdraw":
                    activeStage = ActionStage.Withdrawing;
                    break;
                default:
                    Cooperation = CooperationMode.Talking;
                    activeStage = ticket.RecoveryOfActionId == Guid.Empty ? ActionStage.Moving : ActionStage.Returning;
                    break;
            }
            Commit(activeStage == ActionStage.Returning ? "Retrying return from " + ticket.TargetId + "." : ticket.Action + " started.");
            RecordActionResult(ticket, "started", LastResult);
            return true;
        }

        // Npc executor calls this only after testing the anchor's position and facing.
        internal bool ReportArrived(ActionTicket ticket)
        {
            if (!IsCurrent(ticket) || ticket.Action != "inspect" || activeStage != ActionStage.Moving) return false;
            activeStage = ActionStage.Arrived;
            Commit("At " + ticket.TargetId + " observation point. Waiting for experiment.");
            RecordActionResult(ticket, "arrived", LastResult);
            return true;
        }

        internal bool ReportObservationLost(ActionTicket ticket)
        {
            if (!IsCurrent(ticket) || activeStage != ActionStage.Arrived) return false;
            return ReportFailure(ticket, "Observation lost. Request a new inspection.");
        }

        // A submits its independent completion, or the explicitly labelled local stub
        // simulates that event. A premature event cannot be replayed after arrival.
        internal bool TryCompleteCameraExperiment(ActionTicket ticket, Guid eventId)
        {
            return TryCompleteInspection(CameraTarget, ticket, eventId);
        }

        internal bool TryCompleteSetBackExperiment(ActionTicket ticket, Guid eventId)
        {
            return TryCompleteInspection(SetBackTarget, ticket, eventId);
        }

        private bool TryCompleteInspection(string target, ActionTicket ticket, Guid eventId)
        {
            if (ticket == null || ticket.RunId != RunId || ticket.Action != "inspect" ||
                ticket.TargetId != target || ticket.ExperimentId == Guid.Empty || TimedOut ||
                eventId == Guid.Empty || !worldEvents.Add(eventId)) return false;
            string evidence = EvidenceForTarget(target);
            if (!IsCurrent(ticket) || activeStage != ActionStage.Arrived || !verified.Add(evidence)) return false;
            activeStage = ActionStage.Returning;
            Commit(evidence + " verified. Returning.");
            RecordActionResult(ticket, "verified", LastResult);
            return true;
        }

        internal bool TryConfirmRoute(Guid runId, Guid eventId)
        {
            if (runId != RunId || IsPaused || RouteConfirmed || eventId == Guid.Empty || !worldEvents.Add(eventId))
                return false;
            RouteConfirmed = true;
            Commit("Exit route confirmed.");
            return true;
        }

        private static string EvidenceForTarget(string targetId)
        {
            switch (targetId)
            {
                case CameraTarget: return "E1";
                case SetBackTarget: return "E5";
                default: return null;
            }
        }

        internal bool ReportFailure(ActionTicket ticket, string reason)
        {
            if (!IsCurrent(ticket)) return false;
            if (ticket.Action == "follow") Cooperation = CooperationMode.Talking;
            if (ticket.Action == "inspect" && activeStage == ActionStage.Returning &&
                verified.Contains(EvidenceForTarget(ticket.TargetId)))
                failedReturn = ticket;
            Active = null;
            activeStage = ActionStage.Failed;
            Commit(string.IsNullOrWhiteSpace(reason) ? "Navigation failed." : reason);
            RecordActionResult(ticket, "failed", LastResult);
            return true;
        }

        internal bool ReportCompleted(ActionTicket ticket)
        {
            if (!IsCurrent(ticket)) return false;
            if (ticket.Action == "inspect" && activeStage != ActionStage.Returning) return false;
            if (ticket.Action == "follow") Cooperation = CooperationMode.Talking;
            Active = null;
            activeStage = ActionStage.Completed;
            Commit(ticket.Action + " completed.");
            RecordActionResult(ticket, "completed", LastResult);
            return true;
        }

        internal bool TryRestoreConversation()
        {
            if (TimedOut || Cooperation != CooperationMode.Withdrawn) return false;
            if (Active != null) RecordActionResult(Active, "cancelled", "Conversation restored.");
            Pending = Active = null;
            activeStage = ActionStage.Idle;
            Cooperation = CooperationMode.Talking;
            Commit("Conversation restored.");
            return true;
        }

        private void RecordActionResult(ActionTicket ticket, string status, string reason)
        {
            const int historyLimit = 16;
            if (recentResults.Count == historyLimit) recentResults.Dequeue();
            recentResults.Enqueue(new ActionResultSnapshot
            {
                run_id = ticket.RunId.ToString("N"), action_id = ticket.ActionId.ToString("N"),
                experiment_run_id = ticket.ExperimentId == Guid.Empty ? null : ticket.ExperimentId.ToString("N"),
                recovery_of_action_id = ticket.RecoveryOfActionId == Guid.Empty ? null : ticket.RecoveryOfActionId.ToString("N"),
                action = ticket.Action, target_id = ticket.TargetId, status = status, reason = reason,
                game_elapsed_ms = (long)(ElapsedGameSeconds * 1000)
            });
        }

        private bool IsCurrent(ActionTicket ticket)
        {
            return !IsPaused && Pending == null && ticket != null && ReferenceEquals(ticket, Active) && ticket.RunId == RunId;
        }

        private void Commit(string result)
        {
            Version++;
            LastResult = result;
        }

        private static bool Reject(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}
