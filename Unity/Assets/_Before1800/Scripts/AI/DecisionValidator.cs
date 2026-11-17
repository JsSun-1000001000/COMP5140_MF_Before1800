using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Before1800
{
    public static class DecisionValidator
    {
        private static readonly string[] EnvelopeFields = { "session_id", "request_id", "turn_id", "state_version", "decision", "provider", "model", "latency_ms", "attempts" };
        private static readonly string[] DecisionFields = { "dialogue", "action", "target_id", "concern_id", "concern_status", "basis_turn_id" };
        private static readonly HashSet<string> Actions = new HashSet<string> { "reply", "follow", "inspect", "withdraw", "agree_escape" };

        public static bool TryParseResponse(string json, out DecisionResponse response, out string reason)
        {
            response = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return Fail("response_size", out reason);
            try
            {
                JObject root;
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = 16;
                    root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
                    if (reader.Read()) return Fail("trailing_json_content", out reason);
                }
                if (!ExactFields(root, EnvelopeFields)) return Fail("response_fields", out reason);
                if (!RequiredString(root["session_id"]) || !RequiredString(root["request_id"]) || !RequiredString(root["turn_id"]) || !Integer(root["state_version"], 0) ||
                    !RequiredString(root["provider"]) || !RequiredString(root["model"]) || !Number(root["latency_ms"], 0) || !Integer(root["attempts"], 1))
                    return Fail("response_types", out reason);
                JObject d = root["decision"] as JObject;
                if (d == null || !ExactFields(d, DecisionFields)) return Fail("decision_fields", out reason);
                if (!RequiredString(d["dialogue"]) || ((string)d["dialogue"]).Length > 2000 || !RequiredString(d["action"]) ||
                    !NullableString(d["target_id"]) || !NullableString(d["concern_id"]) || !RequiredString(d["concern_status"]) || !NullableString(d["basis_turn_id"]))
                    return Fail("decision_types", out reason);
                response = root.ToObject<DecisionResponse>();
                if (!ValidateDecisionShape(response.decision, out reason)) { response = null; return false; }
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is OverflowException)
            {
                return Fail("invalid_json", out reason);
            }
        }

        public static bool Validate(DecisionResponse response, DecisionRequest request, GameSnapshot state, out string reason)
        {
            reason = null;
            if (response == null || request == null || state == null || request.context == null) return Fail("missing_envelope", out reason);
            if (state.phase != "playing" || state.elapsed_game_seconds >= GameRules.DeadlineSeconds) return Fail("game_ended", out reason);
            if (response.session_id != state.session_id || request.session_id != state.session_id) return Fail("stale_session", out reason);
            if (response.request_id != request.request_id) return Fail("request_mismatch", out reason);
            if (response.turn_id != request.turn_id) return Fail("turn_mismatch", out reason);
            if (response.state_version != request.state_version || request.state_version != state.state_version) return Fail("stale_state", out reason);
            NpcDecision d = response.decision;
            if (!ValidateDecisionShape(d, out reason)) return false;
            if (request.context.allowed_actions == null || !request.context.allowed_actions.Contains(d.action)) return Fail("action_not_offered", out reason);
            if (d.target_id != null && (request.context.known_targets == null || !request.context.known_targets.Contains(d.target_id))) return Fail("unknown_target", out reason);
            if (d.action == "follow" && d.target_id != "player") return Fail("invalid_follow_target", out reason);
            if (d.action == "inspect" && d.target_id == "player") return Fail("invalid_inspection_target", out reason);
            if (state.escape_agreed && d.action != "reply" && d.action != "follow") return Fail("agreement_cannot_be_blocked", out reason);
            if (state.cooperation_mode == "withdrawn" && d.action != "reply") return Fail("recover_cooperation_first", out reason);
            if (d.action == "withdraw" && (state.withdraw_used || state.escape_agreed)) return Fail("withdraw_unavailable", out reason);

            ConcernStates candidate = state.concerns.Copy();
            if (d.concern_status == "resolved")
            {
                if (request.trigger != "user" || string.IsNullOrWhiteSpace(request.player_input) || d.basis_turn_id != request.turn_id)
                    return Fail("invalid_concern_basis", out reason);
                if (state.concerns.Get(d.concern_id) != "open") return Fail("concern_not_open", out reason);
                if (d.concern_id == "C1" && !GameRules.HasEvidenceThreshold(state)) return Fail("C1_requires_three_facts_including_E5", out reason);
                if (d.concern_id == "C3" && !state.route_confirmed) return Fail("C3_requires_route", out reason);
                candidate.Resolve(d.concern_id);
            }
            if (request.trigger == "proactive" && d.action != "reply" && d.action != "inspect") return Fail("invalid_proactive_action", out reason);
            if (d.action == "agree_escape" && (request.trigger != "user" || !GameRules.HasEvidenceThreshold(state) || !state.route_confirmed || !candidate.AllResolved))
                return Fail("escape_requirements_missing", out reason);
            return true;
        }

        public static bool ValidateDecisionShape(NpcDecision d, out string reason)
        {
            reason = null;
            if (d == null || string.IsNullOrWhiteSpace(d.dialogue) || d.dialogue.Length > 2000 || d.action == null || !Actions.Contains(d.action)) return Fail("invalid_decision", out reason);
            if (d.action == "follow" || d.action == "inspect")
            {
                if (string.IsNullOrWhiteSpace(d.target_id)) return Fail("target_required", out reason);
            }
            else if (d.target_id != null) return Fail("unexpected_target", out reason);
            if (d.concern_status == "keep")
            {
                if (d.concern_id != null || d.basis_turn_id != null) return Fail("keep_requires_null_basis_and_concern", out reason);
            }
            else if (d.concern_status == "resolved")
            {
                if (!GameRules.IsConcernId(d.concern_id) || string.IsNullOrWhiteSpace(d.basis_turn_id)) return Fail("invalid_concern_update", out reason);
            }
            else return Fail("invalid_concern_status", out reason);
            return true;
        }

        private static bool ExactFields(JObject o, string[] fields) { return o.Properties().Count() == fields.Length && o.Properties().All(p => fields.Contains(p.Name)) && !o.Descendants().Any(t => t.Type == JTokenType.Comment); }
        private static bool RequiredString(JToken t) { return t != null && t.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t); }
        private static bool NullableString(JToken t) { return t != null && (t.Type == JTokenType.Null || RequiredString(t)); }
        private static bool Integer(JToken t, long min) { return t != null && t.Type == JTokenType.Integer && (long)t >= min && (long)t <= int.MaxValue; }
        private static bool Number(JToken t, double min) { if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return false; double v = (double)t; return !double.IsNaN(v) && !double.IsInfinity(v) && v >= min; }
        private static bool Fail(string value, out string reason) { reason = value; return false; }
    }
}
