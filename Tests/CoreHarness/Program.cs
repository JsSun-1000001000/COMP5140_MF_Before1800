using System;
using System.Collections.Generic;
using System.Linq;
using Before1800;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Local deterministic rule tests. All model responses below are synthetic fixtures,
// not evidence of a real LLM call or an in-Unity scene test.
internal static class Program
{
    private static int passed;
    private static int failed;
    public static int Main()
    {
        Run("serialization contains only C1/C2/C3", Serialization);
        Run("snapshots cannot mutate authority", DetachedSnapshot);
        Run("E1 requires completed observation and grants only E1", EvidenceRules);
        Run("E3 is narrative and E4 requires both stages", SpecialEvidence);
        Run("response stays pending until Continue", PendingAndAction);
        Run("semantic changes invalidate old responses, clock ticks do not", VersionRules);
        Run("reset rejects old requests, events and actions", ResetRules);
        Run("withdraw/recovery preserve independent state", WithdrawRules);
        Run("concern hard conditions and actual player basis", ConcernRules);
        Run("agreement is separate from success and supports final concern", EscapeRules);
        Run("18:00 exact boundary rejects new success", DeadlineRules);
        Run("event IDs are single-use across event kinds", EventReplay);
        Run("proactive opportunity cannot solve concerns and occurs once", ProactiveRules);
        Run("pause tokens remain independent across reset", PauseTokens);
        Run("forged request context is rejected", ForgedContext);
        Run("strict JSON rejects extra, missing, duplicate and trailing data", StrictJson);
        Run("null/action/basis contract is enforced", NullAndTargetRules);
        Run("cancellation rejects late and reused request", CancellationRules);
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed. Synthetic pure-C# rule tests only.");
        return failed == 0 ? 0 : 1;
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Assert(bool condition, string detail = "assertion failed") { if (!condition) throw new Exception(detail); }
    private static GameStateService New() { return new GameStateService(Guid.NewGuid().ToString("N")); }
    private static DecisionRequest Request(GameStateService s, string[] actions = null, string trigger = "user")
    {
        return s.CreateRequest(trigger == "user" ? "Please check the camera with me." : "", new[] { "camera_01", "player" }, actions ?? new[] { "reply", "follow", "inspect", "withdraw", "agree_escape" }, trigger);
    }
    private static DecisionResponse Response(DecisionRequest q, string action = "reply", string target = null, string concern = null)
    {
        return new DecisionResponse { session_id = q.session_id, request_id = q.request_id, turn_id = q.turn_id, state_version = q.state_version,
            provider = "synthetic-test", model = "fixture", attempts = 1, latency_ms = 1,
            decision = new NpcDecision { dialogue = "I will consider what you said.", action = action, target_id = target,
                concern_id = concern, concern_status = concern == null ? "keep" : "resolved", basis_turn_id = concern == null ? null : q.turn_id } };
    }
    private static ActionCommand CommitDecision(GameStateService s, string action = "reply", string target = null, string concern = null)
    {
        string reason;
        DecisionRequest q = Request(s);
        Assert(s.TryBeginRequest(q, out reason), reason);
        Assert(s.TryStageResponse(Response(q, action, target, concern), out reason), reason);
        ActionCommand command;
        Assert(s.TryContinuePending(out command, out reason), reason);
        return command;
    }
    private static ActionResult Result(ActionCommand c, string status = "succeeded")
    {
        return new ActionResult { session_id = c.session_id, action_id = c.action_id, event_id = Guid.NewGuid().ToString("N"), action = c.action, target_id = c.target_id, status = status, reason = "synthetic fixture" };
    }
    private static void AddFact(GameStateService s, string id)
    {
        string reason;
        Assert(s.TryVerifyEvidence(new EvidenceVerification { session_id = s.Snapshot.session_id, event_id = Guid.NewGuid().ToString("N"), evidence_id = id,
            lin_present = true, experiment_completed = true, prediction_registered_before_start = true, prediction_matched = true, tracking_matched = true }, out reason), reason);
    }
    private static void ReadyEvidence(GameStateService s)
    {
        AddFact(s, "E1"); AddFact(s, "E4"); AddFact(s, "E5");
        string reason; Assert(s.TryConfirmRoute(s.Snapshot.session_id, "route", true, out reason), reason);
    }
    private static void MakeAgreed(GameStateService s)
    {
        ReadyEvidence(s); CommitDecision(s, concern: "C1"); CommitDecision(s, concern: "C2");
        ActionCommand c = CommitDecision(s, "agree_escape", concern: "C3");
        string reason; Assert(s.TryCompleteAction(Result(c), out reason), reason);
    }
    private static void Serialization()
    {
        JObject q = JObject.FromObject(Request(New()));
        Assert(((JObject)q["context"]["concerns"]).Properties().Select(x => x.Name).SequenceEqual(new[] { "C1", "C2", "C3" }));
        Assert(q["context"]["known_targets"].Type == JTokenType.Array);
        Assert(q["context"]["last_action_result"].Type == JTokenType.Null);
    }
    private static void DetachedSnapshot()
    {
        var s = New(); AddFact(s, "E1"); var snap = s.Snapshot;
        snap.verified_evidence[0] = "E5"; snap.concerns.C1 = "resolved"; snap.escape_agreed = true;
        Assert(s.Snapshot.verified_evidence[0] == "E1"); Assert(s.Snapshot.concerns.C1 == "open"); Assert(!s.Snapshot.escape_agreed);
    }
    private static void EvidenceRules()
    {
        var s = New(); string r; string session = s.Snapshot.session_id;
        Assert(s.TryRecordDiscovery(session, "found", "E1", out r), r); Assert(s.Snapshot.verified_evidence.Length == 0);
        Assert(!s.TryVerifyEvidence(session, "alone", "E1", false, true, out r));
        Assert(!s.TryVerifyEvidence(session, "unfinished", "E1", true, false, out r));
        Assert(s.TryVerifyEvidence(session, "done", "E1", true, true, out r), r);
        int version = s.Snapshot.state_version;
        Assert(!s.TryVerifyEvidence(session, "done", "E1", true, true, out r));
        Assert(!s.TryVerifyEvidence(session, "again", "E1", true, true, out r));
        Assert(s.Snapshot.state_version == version && s.Snapshot.verified_evidence.Length == 1);
        Assert(s.Snapshot.concerns.C1 == "open" && !s.Snapshot.route_confirmed && !s.Snapshot.escape_agreed && s.Snapshot.phase == "playing");
    }
    private static void SpecialEvidence()
    {
        var s = New(); string r; string session = s.Snapshot.session_id;
        Assert(s.TryRecordDiscovery(session, "photo", "E3", out r)); Assert(s.Snapshot.narrative_seen.Contains("E3"));
        Assert(!s.TryVerifyEvidence(session, "fakephoto", "E3", true, true, out r));
        Assert(!s.TryVerifyEvidence(session, "fake", "E99", true, true, out r));
        Assert(!s.TryVerifyEvidence(session, "half", "E4", true, true, out r));
        AddFact(s, "E4"); Assert(s.Snapshot.verified_evidence.SequenceEqual(new[] { "E4" }));
    }
    private static void PendingAndAction()
    {
        var s = New(); var q = Request(s); string r; int version = s.Snapshot.state_version;
        Assert(s.TryBeginRequest(q, out r)); Assert(s.TryBeginRequest(q, out r), "same in-flight transport retry allowed");
        Assert(s.TryStageResponse(Response(q, "inspect", "camera_01"), out r));
        Assert(!s.TryStageResponse(Response(q, "inspect", "camera_01"), out r));
        Assert(!s.HasActiveAction && s.HasPendingDecision && s.Snapshot.state_version == version);
        ActionCommand c; Assert(s.TryContinuePending(out c, out r)); Assert(c != null && s.HasActiveAction);
        ActionCommand ignored; Assert(!s.TryContinuePending(out ignored, out r));
        var result = Result(c); Assert(s.TryCompleteAction(result, out r), r);
        Assert(s.Snapshot.verified_evidence.Length == 0, "inspect completion alone must not create evidence");
        Assert(!s.TryCompleteAction(result, out r));
        AddFact(s, "E1"); var next = Request(s); Assert(next.context.verified_evidence.Contains("E1"));
        Assert(next.context.last_action_result.action_id == c.action_id);
    }
    private static void VersionRules()
    {
        var s = New(); var q = Request(s); string r;
        Assert(s.TryBeginRequest(q, out r)); s.AdvanceGameTime(1); Assert(s.Snapshot.state_version == q.state_version);
        AddFact(s, "E1"); Assert(!s.TryStageResponse(Response(q), out r) && r == "stale_state");
        Assert(s.CancelRequest(q.request_id));
        var q2 = Request(s); Assert(s.TryBeginRequest(q2, out r)); Assert(s.TryStageResponse(Response(q2, "follow", "player"), out r));
        AddFact(s, "E2"); ActionCommand c; Assert(!s.TryContinuePending(out c, out r) && r == "stale_state"); Assert(!s.HasActiveAction);
    }
    private static void ResetRules()
    {
        var s = New(); var q = Request(s); string r; Assert(s.TryBeginRequest(q, out r)); s.Reset();
        Assert(!s.TryStageResponse(Response(q), out r)); Assert(!s.TryBeginRequest(q, out r));
        Assert(!s.TryVerifyEvidence(q.session_id, "old", "E1", true, true, out r));
        ActionCommand c = CommitDecision(s, "inspect", "camera_01"); s.Reset(); Assert(!s.TryCompleteAction(Result(c), out r));
        Assert(s.Snapshot.verified_evidence.Length == 0 && !s.Snapshot.escape_agreed);
    }
    private static void WithdrawRules()
    {
        var s = New(); AddFact(s, "E1"); CommitDecision(s, concern: "C2");
        var c = CommitDecision(s, "withdraw"); string r; Assert(s.TryCompleteAction(Result(c), out r));
        Assert(s.Snapshot.cooperation_mode == "withdrawn" && s.Snapshot.concerns.C2 == "resolved" && s.Snapshot.verified_evidence.Contains("E1"));
        Assert(s.TryRecoverCooperation(s.Snapshot.session_id, "recover", out r));
        Assert(s.Snapshot.cooperation_mode == "talking" && s.Snapshot.concerns.C1 == "open");
        var q = Request(s); Assert(s.TryBeginRequest(q, out r)); Assert(!s.TryStageResponse(Response(q, "withdraw"), out r));
    }
    private static void ConcernRules()
    {
        var s = New(); AddFact(s, "E1"); var q = Request(s); string r; Assert(s.TryBeginRequest(q, out r));
        Assert(!s.TryStageResponse(Response(q, concern: "C1"), out r));
        Assert(!s.TryStageResponse(Response(q, concern: "C3"), out r));
        var wrong = Response(q, concern: "C2"); wrong.decision.basis_turn_id = "earlier"; Assert(!s.TryStageResponse(wrong, out r));
        Assert(s.TryStageResponse(Response(q, concern: "C2"), out r)); Assert(s.Snapshot.concerns.C2 == "open");
        ActionCommand c; Assert(s.TryContinuePending(out c, out r)); Assert(c == null && s.Snapshot.concerns.C2 == "resolved");
    }
    private static void EscapeRules()
    {
        var s = New(); MakeAgreed(s); Assert(s.Snapshot.escape_agreed && s.Snapshot.phase == "playing");
        string r; var q = Request(s); Assert(s.TryBeginRequest(q, out r)); Assert(!s.TryStageResponse(Response(q, "withdraw"), out r));
        Assert(!s.TryStageResponse(Response(q, "inspect", "camera_01"), out r)); s.CancelRequest(q.request_id);
        var follow = CommitDecision(s, "follow", "player"); Assert(s.TryCompleteAction(Result(follow, "failed"), out r)); Assert(s.Snapshot.escape_agreed);
        s.SetExitPresence(true, false); Assert(s.Snapshot.phase == "playing"); s.SetExitPresence(true, true); Assert(s.Snapshot.phase == "escaped");
    }
    private static void DeadlineRules()
    {
        var s = New(); MakeAgreed(s); s.AdvanceGameTime(3600); s.SetExitPresence(true, true); Assert(s.Snapshot.phase == "timed_out");
        var before = New(); MakeAgreed(before); before.AdvanceGameTime(3599.9f); before.SetExitPresence(true, true); Assert(before.Snapshot.phase == "escaped");
    }
    private static void EventReplay()
    {
        var s = New(); string r; var id = s.Snapshot.session_id;
        Assert(s.TryVerifyEvidence(id, "event", "E1", true, true, out r));
        Assert(!s.TryVerifyEvidence(id, "event", "E5", true, true, out r)); Assert(!s.TryConfirmRoute(id, "event", true, out r));
    }
    private static void ProactiveRules()
    {
        var s = New(); string r; Assert(!s.TryBeginRequest(Request(s, trigger: "proactive"), out r));
        AddFact(s, "E1"); var q = Request(s, trigger: "proactive"); Assert(s.TryBeginRequest(q, out r));
        Assert(!s.TryStageResponse(Response(q, concern: "C2"), out r)); Assert(s.TryStageResponse(Response(q), out r));
        ActionCommand c; Assert(s.TryContinuePending(out c, out r)); Assert(s.Snapshot.proactive_used);
        Assert(!s.TryBeginRequest(Request(s, trigger: "proactive"), out r));
    }
    private static void PauseTokens()
    {
        var p = new PauseService(); var dialogue = p.Acquire("dialogue"); var menu = p.Acquire("menu");
        menu.Dispose(); Assert(p.IsPaused); dialogue.Dispose(); Assert(!p.IsPaused); dialogue.Dispose();
        var old = p.Acquire("old"); p.Reset(); var fresh = p.Acquire("dialogue"); old.Dispose(); Assert(p.IsPaused); fresh.Dispose(); Assert(!p.IsPaused);
    }
    private static void ForgedContext()
    {
        var s = New(); string r; var q = Request(s); q.context.verified_evidence = new[] { "E1", "E4", "E5" }; Assert(!s.TryBeginRequest(q, out r));
    }
    private static void StrictJson()
    {
        var q = Request(New()); var json = JObject.FromObject(Response(q)); DecisionResponse response; string r;
        Assert(DecisionValidator.TryParseResponse(json.ToString(), out response, out r), r);
        var missingTurn = (JObject)json.DeepClone(); missingTurn.Remove("turn_id"); Assert(!DecisionValidator.TryParseResponse(missingTurn.ToString(), out response, out r));
        var nullTurn = (JObject)json.DeepClone(); nullTurn["turn_id"] = JValue.CreateNull(); Assert(!DecisionValidator.TryParseResponse(nullTurn.ToString(), out response, out r));
        var extra = (JObject)json.DeepClone(); extra["ending"] = "escaped"; Assert(!DecisionValidator.TryParseResponse(extra.ToString(), out response, out r));
        extra = (JObject)json.DeepClone(); extra["decision"]["verified_evidence"] = new JArray("E1"); Assert(!DecisionValidator.TryParseResponse(extra.ToString(), out response, out r));
        extra = (JObject)json.DeepClone(); ((JObject)extra["decision"]).Remove("target_id"); Assert(!DecisionValidator.TryParseResponse(extra.ToString(), out response, out r));
        extra = (JObject)json.DeepClone(); extra["state_version"] = "0"; Assert(!DecisionValidator.TryParseResponse(extra.ToString(), out response, out r));
        string duplicate = json.ToString(Formatting.None).Replace("\"state_version\":0", "\"state_version\":0,\"state_version\":0");
        Assert(!DecisionValidator.TryParseResponse(duplicate, out response, out r));
        Assert(!DecisionValidator.TryParseResponse(json.ToString() + "{}", out response, out r));
    }
    private static void NullAndTargetRules()
    {
        var s = New(); string r; var q = Request(s); Assert(s.TryBeginRequest(q, out r));
        var wrongTurn = Response(q); wrongTurn.turn_id = "another-turn"; Assert(!s.TryStageResponse(JsonConvert.SerializeObject(wrongTurn), out r) && r == "turn_mismatch");
        var response = Response(q); response.decision.concern_id = "C1"; Assert(!s.TryStageResponse(response, out r));
        Assert(!s.TryStageResponse(Response(q, "follow", "camera_01"), out r));
        Assert(!s.TryStageResponse(Response(q, "inspect", "invented"), out r));
        Assert(!s.TryStageResponse(Response(q, "reply", "camera_01"), out r));
        Assert(!s.TryStageResponse(Response(q, "agree_escape"), out r));
    }
    private static void CancellationRules()
    {
        var s = New(); string r; var q = Request(s); Assert(s.TryBeginRequest(q, out r)); Assert(s.CancelRequest(q.request_id));
        Assert(!s.TryStageResponse(Response(q), out r)); Assert(!s.TryBeginRequest(q, out r)); Assert(s.TryBeginRequest(Request(s), out r));
    }
}
