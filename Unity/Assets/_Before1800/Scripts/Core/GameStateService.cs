using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Before1800
{
    // Single writer for authoritative gameplay state. Call on the game thread.
    // Networking/UI own their lifetimes; they submit results here, never mutate snapshots.
    public sealed class GameStateService
    {
        private GameSnapshot state;
        private DecisionRequest activeRequest;
        private DecisionResponse pendingResponse;
        private ActionCommand activeAction;
        private readonly HashSet<string> usedRequests = new HashSet<string>();
        private readonly HashSet<string> usedTurns = new HashSet<string>();
        private readonly HashSet<string> processedEvents = new HashSet<string>();
        private readonly HashSet<string> completedActions = new HashSet<string>();

        public event Action<GameSnapshot> Changed;
        public GameSnapshot Snapshot { get { return Copy(state); } }
        public NpcDecision PendingDecision { get { return pendingResponse == null ? null : Copy(pendingResponse.decision); } }
        public ActionCommand ActiveAction { get { return Copy(activeAction); } }
        public bool HasPendingDecision { get { return pendingResponse != null; } }
        public bool HasActiveRequest { get { return activeRequest != null; } }
        public bool HasActiveAction { get { return activeAction != null; } }

        public GameStateService(string sessionId = null) { Reset(sessionId); }

        public void Reset(string sessionId = null)
        {
            if (sessionId != null && (string.IsNullOrWhiteSpace(sessionId) || (state != null && sessionId == state.session_id)))
                throw new ArgumentException("A reset must use a fresh, non-empty session ID.", "sessionId");
            state = new GameSnapshot { session_id = sessionId ?? NewId() };
            activeRequest = null;
            pendingResponse = null;
            activeAction = null;
            usedRequests.Clear();
            usedTurns.Clear();
            processedEvents.Clear();
            completedActions.Clear();
            Publish();
        }

        public DecisionRequest CreateRequest(string playerInput, string[] knownTargets, string[] allowedActions,
            string trigger = "user", DialogueEntry[] history = null)
        {
            int seconds = (int)(17 * 3600 + state.elapsed_game_seconds);
            return new DecisionRequest
            {
                session_id = state.session_id,
                request_id = NewId(),
                turn_id = NewId(), // Proactive IDs identify a system turn, never a player explanation.
                state_version = state.state_version,
                trigger = trigger,
                player_input = playerInput ?? "",
                context = new DecisionContext
                {
                    verified_evidence = (string[])state.verified_evidence.Clone(),
                    player_discovered = (string[])state.player_discovered.Clone(),
                    concerns = state.concerns.Copy(),
                    cooperation_mode = state.cooperation_mode,
                    escape_agreed = state.escape_agreed,
                    route_confirmed = state.route_confirmed,
                    known_targets = knownTargets == null ? new string[0] : knownTargets.Distinct().ToArray(),
                    allowed_actions = allowedActions == null ? new string[0] : allowedActions.Distinct().ToArray(),
                    recent_dialogue = history == null ? new DialogueEntry[0] : Copy(history),
                    last_action_result = Copy(state.last_action_result),
                    game_time = string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60)
                }
            };
        }

        public bool TryBeginRequest(DecisionRequest request, out string reason)
        {
            reason = null;
            if (request == null || request.context == null || string.IsNullOrWhiteSpace(request.request_id) || string.IsNullOrWhiteSpace(request.turn_id))
                return Fail("invalid_request", out reason);
            if (!CheckSession(request.session_id, out reason)) return false;
            if (request.state_version != state.state_version) return Fail("stale_state", out reason);
            if (activeAction != null) return Fail("action_in_progress", out reason);
            if (activeRequest != null)
            {
                if (pendingResponse == null && activeRequest.request_id == request.request_id && JsonConvert.SerializeObject(activeRequest) == JsonConvert.SerializeObject(request)) return true;
                return Fail("request_in_progress", out reason);
            }
            if (usedRequests.Contains(request.request_id) || usedTurns.Contains(request.turn_id)) return Fail("duplicate_request_or_turn", out reason);
            if (request.trigger != "user" && request.trigger != "proactive") return Fail("invalid_trigger", out reason);
            if (request.trigger == "user" && string.IsNullOrWhiteSpace(request.player_input)) return Fail("player_input_required", out reason);
            if (request.trigger == "proactive" && (state.proactive_used || state.verified_evidence.Length == 0 || !string.IsNullOrWhiteSpace(request.player_input)))
                return Fail("proactive_unavailable", out reason);
            if (!ContextMatchesState(request.context)) return Fail("context_not_authoritative", out reason);
            if (request.context.allowed_actions == null || request.context.allowed_actions.Length == 0 || request.context.known_targets == null ||
                request.context.allowed_actions.Any(string.IsNullOrWhiteSpace) || request.context.known_targets.Any(string.IsNullOrWhiteSpace))
                return Fail("invalid_request_context", out reason);
            activeRequest = Copy(request);
            usedRequests.Add(request.request_id);
            usedTurns.Add(request.turn_id);
            return true;
        }

        // A valid response is staged only. Reading the response cannot move Lin or add facts.
        public bool TryStageResponse(DecisionResponse response, out string reason)
        {
            reason = null;
            if (activeRequest == null) return Fail("no_active_request", out reason);
            if (pendingResponse != null) return Fail("response_already_staged", out reason);
            if (!DecisionValidator.Validate(response, activeRequest, state, out reason)) return false;
            pendingResponse = Copy(response);
            return true;
        }

        public bool TryStageResponse(string rawJson, out string reason)
        {
            DecisionResponse response;
            return DecisionValidator.TryParseResponse(rawJson, out response, out reason) && TryStageResponse(response, out reason);
        }

        public bool CancelRequest(string requestId)
        {
            if (activeRequest == null || activeRequest.request_id != requestId) return false;
            activeRequest = null;
            pendingResponse = null;
            return true;
        }

        // Call while the dialogue pause is still held. Release it after this returns.
        // Null command means a pure reply; no world action needs to execute.
        public bool TryContinuePending(out ActionCommand command, out string reason)
        {
            command = null;
            reason = null;
            if (pendingResponse == null || activeRequest == null) return Fail("no_pending_decision", out reason);
            if (!DecisionValidator.Validate(pendingResponse, activeRequest, state, out reason))
            {
                activeRequest = null;
                pendingResponse = null;
                return false;
            }
            NpcDecision d = pendingResponse.decision;
            string requestId = activeRequest.request_id;
            bool changed = false;
            if (d.concern_status == "resolved") { state.concerns.Resolve(d.concern_id); changed = true; }
            if (activeRequest.trigger == "proactive") { state.proactive_used = true; changed = true; }
            if (d.action == "withdraw")
            {
                state.withdraw_used = true;
                state.cooperation_mode = "withdrawn";
                changed = true;
            }
            else if (d.action == "follow") { state.cooperation_mode = "following"; changed = true; }
            else if (d.action == "agree_escape")
            {
                state.escape_agreed = true;
                state.cooperation_mode = "following";
                changed = true;
            }
            if (d.action != "reply")
            {
                activeAction = new ActionCommand { session_id = state.session_id, request_id = requestId, action_id = NewId(), action = d.action, target_id = d.target_id };
                state.active_action_id = activeAction.action_id;
                command = Copy(activeAction);
                changed = true;
            }
            activeRequest = null;
            pendingResponse = null;
            if (GameRules.CanFinishEscape(state)) { Finish("escaped"); command = null; changed = true; }
            if (changed) Commit();
            return true;
        }

        public bool TryCompleteAction(ActionResult result, out string reason)
        {
            reason = null;
            if (result == null || !CheckSession(result.session_id, out reason)) return Fail(reason ?? "invalid_result", out reason);
            if (string.IsNullOrWhiteSpace(result.action_id) || completedActions.Contains(result.action_id)) return Fail("duplicate_or_missing_action", out reason);
            if (activeAction == null || result.action_id != activeAction.action_id) return Fail("action_mismatch", out reason);
            if (result.action != activeAction.action || result.target_id != activeAction.target_id) return Fail("action_result_mismatch", out reason);
            if (result.status != "succeeded" && result.status != "failed" && result.status != "cancelled") return Fail("invalid_action_status", out reason);
            if (!CheckEvent(result.event_id, out reason)) return false;
            processedEvents.Add(result.event_id);
            completedActions.Add(result.action_id);
            state.last_action_result = Copy(result);
            state.active_action_id = null;
            activeAction = null;
            if (result.action == "follow" && result.status != "succeeded" && !state.escape_agreed) state.cooperation_mode = "talking";
            // No action completion, including inspect, grants evidence by itself.
            Commit();
            return true;
        }

        public bool TryVerifyEvidence(string sessionId, string eventId, string evidenceId, bool linPresent, bool experimentCompleted, out string reason)
        {
            return TryVerifyEvidence(new EvidenceVerification { session_id = sessionId, event_id = eventId, evidence_id = evidenceId, lin_present = linPresent, experiment_completed = experimentCompleted }, out reason);
        }

        // Trusted scene experiment components call this; never map LLM JSON into it.
        public bool TryVerifyEvidence(EvidenceVerification proof, out string reason)
        {
            reason = null;
            if (proof == null || !CheckSession(proof.session_id, out reason)) return Fail(reason ?? "invalid_verification", out reason);
            if (!GameRules.IsFactId(proof.evidence_id)) return Fail("invalid_factual_evidence", out reason);
            if (!proof.lin_present || !proof.experiment_completed) return Fail("observation_not_completed", out reason);
            if (proof.evidence_id == "E4" && (!proof.prediction_registered_before_start || !proof.prediction_matched || !proof.tracking_matched))
                return Fail("E4_requires_prediction_and_tracking", out reason);
            if (!CheckEvent(proof.event_id, out reason)) return false;
            if (state.verified_evidence.Contains(proof.evidence_id)) return Fail("evidence_already_verified", out reason);
            processedEvents.Add(proof.event_id);
            state.verified_evidence = state.verified_evidence.Concat(new[] { proof.evidence_id }).OrderBy(x => x).ToArray();
            Commit();
            return true;
        }

        public bool TryRecordDiscovery(string sessionId, string eventId, string evidenceId, out string reason)
        {
            reason = null;
            if (!CheckSession(sessionId, out reason)) return false;
            if (!GameRules.IsFactId(evidenceId) && evidenceId != "E3") return Fail("unknown_discovery", out reason);
            if (!CheckEvent(eventId, out reason)) return false;
            if (state.player_discovered.Contains(evidenceId)) return Fail("already_discovered", out reason);
            processedEvents.Add(eventId);
            state.player_discovered = state.player_discovered.Concat(new[] { evidenceId }).OrderBy(x => x).ToArray();
            if (evidenceId == "E3") state.narrative_seen = new[] { "E3" };
            Commit();
            return true;
        }

        public bool TryConfirmRoute(string sessionId, string eventId, bool passable, out string reason)
        {
            reason = null;
            if (!CheckSession(sessionId, out reason) || !CheckEvent(eventId, out reason)) return false;
            if (!passable) return Fail("route_not_passable", out reason);
            if (state.route_confirmed) return Fail("route_already_confirmed", out reason);
            processedEvents.Add(eventId);
            state.route_confirmed = true;
            Commit();
            return true;
        }

        public bool TryRecoverCooperation(string sessionId, string eventId, out string reason)
        {
            reason = null;
            if (!CheckSession(sessionId, out reason) || !CheckEvent(eventId, out reason)) return false;
            if (state.cooperation_mode != "withdrawn") return Fail("not_withdrawn", out reason);
            if (activeAction != null) return Fail("action_in_progress", out reason);
            processedEvents.Add(eventId);
            state.cooperation_mode = "talking";
            Commit();
            return true;
        }

        // Caller advances this ONLY when PauseService.IsPaused is false.
        // Clock ticks do not invalidate semantic state versions.
        public void AdvanceGameTime(float gameSeconds)
        {
            if (float.IsNaN(gameSeconds) || float.IsInfinity(gameSeconds) || gameSeconds < 0) throw new ArgumentOutOfRangeException("gameSeconds");
            if (state.phase != "playing" || gameSeconds == 0) return;
            state.elapsed_game_seconds = Math.Min(GameRules.DeadlineSeconds, state.elapsed_game_seconds + gameSeconds);
            if (state.elapsed_game_seconds >= GameRules.DeadlineSeconds) { Finish("timed_out"); Commit(); }
        }

        public void SetExitPresence(bool player, bool lin)
        {
            if (state.phase != "playing") return;
            bool changed = state.player_at_exit != player || state.lin_at_exit != lin;
            state.player_at_exit = player;
            state.lin_at_exit = lin;
            if (GameRules.CanFinishEscape(state)) { Finish("escaped"); changed = true; }
            if (changed) Commit();
        }

        private bool ContextMatchesState(DecisionContext c)
        {
            return c.verified_evidence != null && new HashSet<string>(c.verified_evidence).SetEquals(state.verified_evidence) &&
                c.concerns != null && c.concerns.C1 == state.concerns.C1 && c.concerns.C2 == state.concerns.C2 && c.concerns.C3 == state.concerns.C3 &&
                c.route_confirmed == state.route_confirmed && c.escape_agreed == state.escape_agreed && c.cooperation_mode == state.cooperation_mode;
        }

        private bool CheckSession(string sessionId, out string reason)
        {
            reason = null;
            if (sessionId != state.session_id) return Fail("stale_session", out reason);
            if (state.phase != "playing") return Fail("game_ended", out reason);
            return true;
        }
        private bool CheckEvent(string id, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(id)) return Fail("event_id_required", out reason);
            if (processedEvents.Contains(id)) return Fail("duplicate_event", out reason);
            return true;
        }
        private void Finish(string phase)
        {
            state.phase = phase;
            state.active_action_id = null;
            activeAction = null;
            activeRequest = null;
            pendingResponse = null;
        }
        private void Commit() { state.state_version++; Publish(); }
        private void Publish() { if (Changed != null) Changed(Snapshot); }
        private static T Copy<T>(T value) { return value == null ? default(T) : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value)); }
        private static string NewId() { return Guid.NewGuid().ToString("N"); }
        private static bool Fail(string value, out string reason) { reason = value; return false; }
    }
}
