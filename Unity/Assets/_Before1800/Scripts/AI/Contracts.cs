using System;

namespace Before1800
{
    [Serializable]
    public sealed class NpcDecision
    {
        public string dialogue;
        public string action;
        public string target_id;
        public string concern_id;
        public string concern_status;
        public string basis_turn_id;
    }

    [Serializable]
    public sealed class DialogueEntry
    {
        public string role;
        public string content;
        public DialogueEntry() { }
        public DialogueEntry(string role, string content) { this.role = role; this.content = content; }
    }

    [Serializable]
    public sealed class ConcernStates
    {
        public string C1 = "open";
        public string C2 = "open";
        public string C3 = "open";
        public string Get(string id) { return id == "C1" ? C1 : id == "C2" ? C2 : id == "C3" ? C3 : null; }
        [Newtonsoft.Json.JsonIgnore]
        public bool AllResolved { get { return C1 == "resolved" && C2 == "resolved" && C3 == "resolved"; } }
        internal void Resolve(string id) { if (id == "C1") C1 = "resolved"; else if (id == "C2") C2 = "resolved"; else if (id == "C3") C3 = "resolved"; }
        public ConcernStates Copy() { return new ConcernStates { C1 = C1, C2 = C2, C3 = C3 }; }
    }

    [Serializable]
    public sealed class DecisionContext
    {
        public string[] verified_evidence = new string[0];
        public ConcernStates concerns = new ConcernStates();
        public string cooperation_mode = "talking";
        public bool escape_agreed;
        public bool route_confirmed;
        public string[] known_targets = new string[0];
        public string[] allowed_actions = new string[0];
        public DialogueEntry[] recent_dialogue = new DialogueEntry[0];
        public ActionResult last_action_result;
        public string game_time;
        public string[] observations = new string[0];
        public string[] player_discovered = new string[0];
    }

    [Serializable]
    public sealed class DecisionRequest
    {
        public string session_id;
        public string request_id;
        public string turn_id;
        public int state_version;
        public string trigger = "user";
        public string player_input;
        public DecisionContext context;
    }

    [Serializable]
    public sealed class DecisionResponse
    {
        public string session_id;
        public string request_id;
        public string turn_id;
        public int state_version;
        public NpcDecision decision;
        public string provider;
        public string model;
        public double latency_ms;
        public int attempts;
    }

    [Serializable]
    public sealed class ActionCommand
    {
        public string session_id;
        public string request_id;
        public string action_id;
        public string action;
        public string target_id;
    }

    [Serializable]
    public sealed class ActionResult
    {
        public string session_id;
        public string action_id;
        public string event_id;
        public string action;
        public string target_id;
        public string status;
        public string reason;
    }

    [Serializable]
    public sealed class EvidenceVerification
    {
        public string session_id;
        public string event_id;
        public string evidence_id;
        public bool lin_present;
        public bool experiment_completed;
        public bool prediction_registered_before_start;
        public bool prediction_matched;
        public bool tracking_matched;
    }
}
