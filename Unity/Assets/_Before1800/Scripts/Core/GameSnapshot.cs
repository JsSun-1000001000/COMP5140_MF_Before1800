using System;

namespace Before1800
{
    // A detached read model. Changing this object never mutates the service.
    [Serializable]
    public sealed class GameSnapshot
    {
        public string session_id;
        public int state_version;
        public string[] verified_evidence = new string[0];
        public string[] player_discovered = new string[0];
        public string[] narrative_seen = new string[0];
        public ConcernStates concerns = new ConcernStates();
        public string cooperation_mode = "talking";
        public bool escape_agreed;
        public bool route_confirmed;
        public bool withdraw_used;
        public bool proactive_used;
        public float elapsed_game_seconds;
        public string phase = "playing";
        public bool player_at_exit;
        public bool lin_at_exit;
        public string active_action_id;
        public ActionResult last_action_result;
    }

    public static class GameRules
    {
        public const float DeadlineSeconds = 3600f;
        public static bool IsFactId(string id) { return id == "E1" || id == "E2" || id == "E4" || id == "E5"; }
        public static bool IsConcernId(string id) { return id == "C1" || id == "C2" || id == "C3"; }
        public static bool HasEvidenceThreshold(GameSnapshot s)
        {
            if (s == null || s.verified_evidence == null) return false;
            var facts = new System.Collections.Generic.HashSet<string>(s.verified_evidence);
            facts.RemoveWhere(id => !IsFactId(id));
            return facts.Count >= 3 && facts.Contains("E5");
        }
        public static bool CanAgreeEscape(GameSnapshot s)
        {
            return s != null && s.phase == "playing" && s.elapsed_game_seconds < DeadlineSeconds &&
                   HasEvidenceThreshold(s) && s.route_confirmed && s.concerns != null && s.concerns.AllResolved;
        }
        public static bool CanFinishEscape(GameSnapshot s)
        {
            return CanAgreeEscape(s) && s.escape_agreed && s.player_at_exit && s.lin_at_exit;
        }
    }
}
