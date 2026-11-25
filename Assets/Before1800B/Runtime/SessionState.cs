using System;
using System.Collections.Generic;

namespace Before1800.PartB
{
    public enum PauseReason { Intro, Dialogue, ModelWait, Menu, Error, Terminal, Journal }
    public enum CooperationMode { Talking, Following, Withdrawn }
    public enum ActionStage { Idle, Queued, Moving, Arrived, Following, Withdrawing, Returning, Completed, Failed }
    public enum ClockAccount { Unanswered, Disclosed, Evaded }

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
    public sealed class ClockRehearsalSnapshot
    {
        public bool invited_to_watch, witnessed_three_ticks;
        public string answer_about_remote;
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
        public ClockRehearsalSnapshot clock_rehearsal;
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
        public const string BroadcastTarget = "living_tv";
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
        public string Ending { get; private set; } = "none";
        public bool IsTerminal => TimedOut || Ending != "none";
        public bool IsPaused => IsTerminal || pauses.Count != 0;
        public bool CanEndConversation => !IsTerminal && Cooperation != CooperationMode.Withdrawn &&
            HasPause(PauseReason.Dialogue);
        public bool HasRequiredEvidence => verified.Count >= 3 && E5Verified;
        public bool CanAgreeEscape => !IsTerminal && HasRequiredEvidence && RouteConfirmed &&
            IsConcernResolved("C1") && IsConcernResolved("C2") && IsConcernResolved("C3");
        public bool E1Verified => verified.Contains("E1");
        public bool E4Verified => verified.Contains("E4");
        public bool E5Verified => verified.Contains("E5");
        public bool CameraDiscovered { get; private set; }
        public bool DoorDiscovered { get; private set; }
        public bool TvDiscovered { get; private set; }
        public bool ClockRemoteTaken { get; private set; }
        public bool ClockBaselineSeen { get; private set; }
        public bool ClockSoloReplaySeen { get; private set; }
        public bool ClockInvited { get; private set; }
        public bool ClockWitnessed { get; private set; }
        public bool ClockCardRead { get; private set; }
        public ClockAccount ClockAnswer { get; private set; }
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
            Ending = "none";
            CameraDiscovered = DoorDiscovered = TvDiscovered = false;
            ClockRemoteTaken = ClockBaselineSeen = ClockSoloReplaySeen = false;
            ClockInvited = ClockWitnessed = ClockCardRead = false;
            ClockAnswer = ClockAccount.Unanswered;
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
            else if (targetId == BroadcastTarget && !TvDiscovered) TvDiscovered = true;
            else return;
            Commit("Player noticed " + targetId + ".");
        }

        internal void TakeClockRemote()
        {
            if (IsPaused || ClockRemoteTaken) return;
            ClockRemoteTaken = true;
            Commit("Player picked up the remote.");
        }

        internal void FinishClockBaseline()
        {
            if (IsPaused || ClockBaselineSeen) return;
            ClockBaselineSeen = true;
            Commit("Player saw the kitchen clock hesitate.");
        }

        internal void FinishClockReplay(bool playerWatched, bool linWatched)
        {
            if (IsPaused || !ClockRemoteTaken || !ClockBaselineSeen) return;
            if (!ClockSoloReplaySeen && playerWatched)
            {
                ClockSoloReplaySeen = true;
                Commit("Player reproduced the three ticks.");
            }
            else if (ClockSoloReplaySeen && linWatched && !ClockWitnessed)
            {
                ClockWitnessed = true;
                Commit("Lin watched the three ticks repeat.");
            }
        }

        internal void InviteLinToClock()
        {
            if (TimedOut || !HasPause(PauseReason.Dialogue) || !ClockSoloReplaySeen || ClockInvited) return;
            ClockInvited = true;
            Commit("Lin agreed to look at the kitchen clock.");
        }

        internal void AnswerClockQuestion(ClockAccount answer)
        {
            if (TimedOut || !HasPause(PauseReason.Dialogue) || !ClockWitnessed ||
                ClockAnswer != ClockAccount.Unanswered || answer == ClockAccount.Unanswered) return;
            ClockAnswer = answer;
            Commit("Lin remembers the player's answer about the remote.");
        }

        internal void ReadClockCard()
        {
            if (IsPaused || !ClockWitnessed || ClockCardRead) return;
            ClockCardRead = true;
            Commit("Player read the retake card on the living room TV.");
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
                player_discoveries = BuildDiscoveries(),
                clock_rehearsal = ClockInvited || ClockWitnessed ? new ClockRehearsalSnapshot
                {
                    invited_to_watch = ClockInvited, witnessed_three_ticks = ClockWitnessed,
                    answer_about_remote = ClockAnswer == ClockAccount.Disclosed ? "admitted_rehearsal" :
                        ClockAnswer == ClockAccount.Evaded ? "evaded" : "unanswered"
                } : null,
                concerns = new ConcernSnapshot
                {
                    C1 = IsConcernResolved("C1") ? "resolved" : "open",
                    C2 = IsConcernResolved("C2") ? "resolved" : "open",
                    C3 = IsConcernResolved("C3") ? "resolved" : "open"
                },
                cooperation_mode = Cooperation.ToString().ToLowerInvariant(),
                route_confirmed = RouteConfirmed, escape_agreed = EscapeAgreed,
                ending = Ending,
                pending_action = Pending?.Action, active_action = Active?.Action,
                action_stage = Stage.ToString().ToLowerInvariant(),
                target_id = (Pending ?? Active)?.TargetId,
                recent_action_results = history,
                last_action_result = history.Length > 0 ? history[history.Length - 1].Copy() : null
            };
        }

        internal PauseToken AcquirePause(PauseReason reason, string owner)
        {
            if (IsTerminal || reason == PauseReason.Terminal) return null;
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
            if (reason == PauseReason.Terminal) return IsTerminal;
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

        public bool CanResolveConcern(string id)
        {
            if (IsTerminal || string.IsNullOrEmpty(id) || !concerns.ContainsKey(id) || concerns[id]) return false;
            return id == "C2" || (id == "C1" && HasRequiredEvidence) || (id == "C3" && RouteConfirmed);
        }

        // The transport verifies the request identity and semantic proposal first.
        // This boundary enforces facts; no keyword can resolve an emotional concern.
        public bool TryResolveConcernFromAcceptedTurn(string id, string turnId, string playerInput)
        {
            if (!CanResolveConcern(id) || string.IsNullOrWhiteSpace(turnId) || string.IsNullOrWhiteSpace(playerInput)) return false;
            concerns[id] = true;
            Commit(id + " resolved from player turn " + turnId);
            return true;
        }

        internal void ResolveTrustFromAcceptedTurn(string turnId)
        {
            // Compatibility with the existing adapter, which has already validated a nonblank turn.
            TryResolveConcernFromAcceptedTurn("C2", turnId, "accepted player turn");
        }

        public static bool IsStoryTarget(string target) => target == "sealed_letter" || target == "parent_reply" ||
            target == "parents_diary" || target == "friend_postcard" || target == "exit_door" || ResidenceMemories.IsTarget(target);

        internal bool TryCompleteStoryInspection(ActionTicket ticket)
        {
            if (!IsCurrent(ticket) || ticket.Action != "inspect" || activeStage != ActionStage.Arrived || !IsStoryTarget(ticket.TargetId)) return false;
            activeStage = ActionStage.Returning;
            Commit("Lin personally read " + ticket.TargetId + ".");
            RecordActionResult(ticket, "observed", LastResult);
            return true;
        }

        public bool TryFinishEnding(string id, bool playerAtExit, bool linAtExit)
        {
            if (IsTerminal || !playerAtExit || (id != "ordinary_departure" && id != "respected_stay" && id != "together")) return false;
            if (id == "together" && (!CanAgreeEscape || !EscapeAgreed || !linAtExit)) return false;
            // The player can choose an ordinary farewell, or leave after respecting Lin's refusal.
            if (id == "respected_stay" && (EscapeAgreed || !E5Verified)) return false;
            Ending = id;
            pauses.Clear();
            if (Active != null) RecordActionResult(Active, "cancelled", "Visit ended.");
            Pending = Active = failedReturn = null;
            activeStage = ActionStage.Idle;
            Commit("Visit ended: " + id);
            return true;
        }

        public bool FinishVisitAtDeadline()
        {
            if (IsTerminal || !HasPause(PauseReason.Menu)) return false;
            // A deliberate time skip is a complete ordinary departure before investigation,
            // and a missed opportunity after Lin has seen proof. It is never a fake escape.
            if (verified.Count == 0) return TryFinishEnding("ordinary_departure", true, false);
            TimedOut = true;
            ElapsedGameSeconds = DeadlineSeconds;
            Ending = "timeout";
            pauses.Clear();
            Pending = Active = failedReturn = null;
            activeStage = ActionStage.Idle;
            Commit("The player let the final visit run to 18:00.");
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
            Ending = "timeout";
            pauses.Clear();
            if (Active != null) RecordActionResult(Active, "cancelled", "Deadline reached.");
            Pending = Active = failedReturn = null;
            activeStage = ActionStage.Idle;
            Commit("18:00. Time expired.");
        }

        // Local input may explicitly cancel the pending or active movement with stop.
        internal bool TryQueue(string action, string targetId, out string reason)
        {
            if (IsTerminal) return Reject("This visit has ended.", out reason);
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
            if (IsTerminal) return Reject("This visit has ended.", out reason);
            if (action == "reply" && targetId == null)
            {
                reason = "Reply accepted.";
                return true;
            }
            // Closing the dialogue is handled after the player has read Lin's last line.
            // Existing movement resumes when that dialogue's pause token is released.
            if (action == "end_conversation")
            {
                if (targetId != null || !CanEndConversation || !HasPause(PauseReason.ModelWait))
                    return Reject("No current dialogue to end, or unexpected target.", out reason);
                reason = "Lin finished speaking.";
                return true;
            }
            if (Pending != null) return Reject("A command is already queued.", out reason);
            if (Cooperation == CooperationMode.Withdrawn) return Reject("Restore conversation first.", out reason);
            if (Active != null && Active.Action != "follow") return Reject("Finish or stop the current action first.", out reason);

            switch (action)
            {
                case "agree_escape":
                    if (targetId != null || !CanAgreeEscape) return Reject("Evidence, route and the three concerns must be resolved first.", out reason);
                    EscapeAgreed = true;
                    action = "follow";
                    targetId = "player";
                    break;
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
                    if (evidence == null && !IsStoryTarget(targetId)) return Reject("Unknown inspection target.", out reason);
                    if (evidence != null && verified.Contains(evidence)) return Reject(evidence + " already verified.", out reason);
                    break;
                case "withdraw":
                    if (targetId != null) return Reject("Withdraw takes no target.", out reason);
                    if (EscapeAgreed || WithdrawUsed) return Reject("Withdrawal unavailable after agreement or previous withdrawal.", out reason);
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

        internal bool TryCompleteBroadcastExperiment(ActionTicket ticket, Guid eventId)
        {
            return TryCompleteInspection(BroadcastTarget, ticket, eventId);
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

        private string[] BuildDiscoveries()
        {
            var list = new List<string>(3);
            if (CameraDiscovered) list.Add(CameraTarget);
            if (TvDiscovered) list.Add(BroadcastTarget);
            if (DoorDiscovered) list.Add(SetBackTarget);
            return list.ToArray();
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
                case BroadcastTarget: return "E4";
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
