#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Before1800.PartB.Tests
{
    public class PartBSetBackTests
    {
        private const string ScenePath = "Assets/Before1800B/PartB_Greybox.unity";
        private PartBScene scene;
        private GreyboxWorld World => scene.World;
        private SessionState State => scene.State;

        [Test]
        public void CameraAndSetBackRequireTheirOwnArrivedTicketAndDoNotConfirmTheRoute()
        {
            var state = new SessionState();
            Assert.IsTrue(state.TryQueue("inspect", SessionState.CameraTarget, out var reason), reason);
            Assert.IsTrue(state.TryStartPending(out var camera));
            Assert.IsTrue(state.ReportArrived(camera));
            Assert.IsFalse(state.TryCompleteSetBackExperiment(camera, Guid.NewGuid()));
            Assert.IsTrue(state.TryCompleteCameraExperiment(camera, Guid.NewGuid()));
            Assert.IsTrue(state.ReportCompleted(camera));

            Assert.IsTrue(state.TryQueue("inspect", SessionState.SetBackTarget, out reason), reason);
            Assert.IsTrue(state.TryStartPending(out var set));
            var premature = Guid.NewGuid();
            Assert.IsFalse(state.TryCompleteSetBackExperiment(set, premature));
            Assert.IsTrue(state.ReportArrived(set));
            Assert.IsFalse(state.TryCompleteSetBackExperiment(set, premature));
            Assert.IsFalse(state.TryCompleteCameraExperiment(set, Guid.NewGuid()));
            Assert.IsFalse(state.TryCompleteSetBackExperiment(camera, Guid.NewGuid()));
            CollectionAssert.AreEqual(new[] { "E1" }, state.VerifiedEvidence);

            var verified = Guid.NewGuid();
            Assert.IsTrue(state.TryCompleteSetBackExperiment(set, verified));
            Assert.IsFalse(state.TryCompleteSetBackExperiment(set, verified));
            Assert.IsFalse(state.TryConfirmRoute(state.RunId, verified), "One world event cannot count for two facts.");
            Assert.IsFalse(state.RouteConfirmed);
            Assert.IsTrue(state.ReportCompleted(set));
            var route = Guid.NewGuid();
            Assert.IsTrue(state.TryConfirmRoute(state.RunId, route));
            Assert.IsFalse(state.TryConfirmRoute(state.RunId, route));
            var snapshot = state.ReadSnapshot();
            CollectionAssert.AreEqual(new[] { "E1", "E5" }, snapshot.verified_evidence);
            Assert.IsTrue(snapshot.route_confirmed);
            Assert.IsFalse(snapshot.escape_agreed);
        }

        private IEnumerator LoadGreybox()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            Assert.IsNotNull(scene);
            Assert.IsNotNull(World.Station);
            scene.ToggleIntroOff();
        }

        private sealed class UnreachableGatewayProxy : IWebProxy
        {
            private readonly Uri gateway;
            public UnreachableGatewayProxy(Uri gateway) { this.gateway = gateway; }
            public ICredentials Credentials { get; set; }
            public bool IsBypassed(Uri destination) => destination.Authority != gateway.Authority;
            public Uri GetProxy(Uri destination) => IsBypassed(destination) ? destination : new Uri("http://127.0.0.1:1/");
        }

        private static IEnumerator WaitForGateway(Func<bool> completed, string expectation)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!completed() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(completed(), expectation);
        }

        private static void Respond(HttpListenerContext exchange, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            exchange.Response.ContentType = "application/json";
            exchange.Response.ContentLength64 = bytes.Length;
            exchange.Response.Close(bytes, false);
        }

        [UnityTest]
        public IEnumerator LocalGatewayReceivesPlayerTurnWhenTheDefaultProxyRejectsLoopback()
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var address = new Uri("http://127.0.0.1:" + port + "/");
            using (var server = new HttpListener())
            {
                server.Prefixes.Add(address.AbsoluteUri);
                server.Start();
                const string setting = "BEFORE1800_LIN_GATEWAY";
                string originalGateway = Environment.GetEnvironmentVariable(setting);
                var originalProxy = WebRequest.DefaultWebProxy;
                try
                {
                    Environment.SetEnvironmentVariable(setting, address.AbsoluteUri);
                    WebRequest.DefaultWebProxy = new UnreachableGatewayProxy(address);
                    var health = server.GetContextAsync();
                    yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                        new LoadSceneParameters(LoadSceneMode.Single));
                    yield return null;
                    scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
                    var adapter = scene.GetComponent<PartCDialogue>();
                    yield return WaitForGateway(() => health.IsCompleted, "The game must contact its local health endpoint directly.");
                    var check = health.GetAwaiter().GetResult();
                    Assert.AreEqual("/health", check.Request.Url.AbsolutePath);
                    Respond(check, "{\"ready\":true}");
                    yield return WaitForGateway(() => adapter.Ready, "The editor player did not accept the gateway health reply.");

                    const string playerLine = "这封信寄出去了吗？";
                    const string linLine = "它一直放在茶几上。";
                    var incoming = server.GetContextAsync();
                    var flow = scene.GetComponent<GreyboxHUD>().Flow;
                    scene.ToggleIntroOff();
                    World.ViewCamera.transform.LookAt(World.Lin.position + Vector3.up * 1.38f);
                    flow.Use();
                    yield return null;
                    var view = scene.GetComponent<PlayerUIView>();
                    var field = view.GetComponentInChildren<InputField>();
                    Assert.IsNotNull(field, "Facing Lin should open the residence dialogue input.");
                    field.text = playerLine;
                    field.onSubmit.Invoke(field.text);
                    yield return WaitForGateway(() => incoming.IsCompleted, "The local gateway never received the player's turn.");
                    var exchange = incoming.GetAwaiter().GetResult();
                    Assert.AreEqual("POST", exchange.Request.HttpMethod);
                    Assert.AreEqual("/npc/decide", exchange.Request.Url.AbsolutePath);
                    var decoder = new DataContractJsonSerializer(typeof(PartCDialogue.Input));
                    var turn = (PartCDialogue.Input)decoder.ReadObject(exchange.Request.InputStream);
                    Assert.AreEqual(playerLine, turn.player_input);
                    Assert.AreEqual("user", turn.trigger);
                    var answer = new PartCDialogue.Output {
                        session_id = turn.session_id, request_id = turn.request_id, turn_id = turn.turn_id,
                        state_version = turn.state_version, model = "local-test", provider = "local-test",
                        decision = new PartCDialogue.Decision { dialogue = linLine, action = "reply", concern_status = "keep" }
                    };
                    Respond(exchange, JsonUtility.ToJson(answer));
                    yield return WaitForGateway(() => flow.Line == linLine, "Lin's accepted reply did not appear in the dialogue.");
                    yield return null;
                    Assert.IsTrue(view.GetComponentsInChildren<Text>().Any(label => label.text == linLine));
                    Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
                    Assert.IsTrue(flow.CanSend);
                    Assert.IsFalse(flow.Closing);
                }
                finally
                {
                    WebRequest.DefaultWebProxy = originalProxy;
                    Environment.SetEnvironmentVariable(setting, originalGateway);
                }
            }
        }

        // Opt in explicitly because this exercises the real, metered provider.
        [UnityTest]
        public IEnumerator DeepSeekDoorAndTrustDemo()
        {
            if (Environment.GetEnvironmentVariable("COMP5140_LIVE_DEMO") != "1")
                Assert.Ignore("Run with the local gateway and COMP5140_LIVE_DEMO=1.");
            yield return LoadGreybox();
            var adapter = scene.GetComponent<PartCDialogue>();
            adapter.SetLive(true);
            WalkToDoorControl();
            scene.InteractWithSetBack();
            Assert.IsTrue(World.Station.DoorOpen);
            scene.ToggleDialogue();
            var flow = scene.GetComponent<GreyboxHUD>().Flow;
            yield return SendLive(flow, adapter, "你不用相信我说的。请你自己到维修门前看看，门后的墙是实墙，还是有支架和制作标记？");
            Assert.IsNotNull(State.Pending, adapter.LastResponse);
            Assert.AreEqual(SessionState.SetBackTarget, State.Pending.TargetId);
            Assert.IsFalse(State.E5Verified);
            scene.ToggleDialogue();
            ReachStage(ActionStage.Arrived);
            WalkToDoorControl();
            scene.InteractWithSetBack();
            Step(60);
            Assert.IsTrue(State.E5Verified);
            Assert.IsFalse(State.RouteConfirmed);
            ReachStage(ActionStage.Completed);
            scene.ToggleDialogue();
            yield return SendLive(flow, adapter, "刚才在门后你亲眼看见了什么？那能证明通道一定走得通吗？先告诉我你的判断。");
            StringAssert.Contains("E5", adapter.LastPayload);
            Assert.IsFalse(State.RouteConfirmed);
            Assert.IsNull(State.Pending, adapter.LastResponse);
            yield return SendLive(flow, adapter, "对不起。以前我替导演看过广播词，还故意带你绕开这道门。我怕失去这份工作，所以一直瞒着你。今天我带你来看，是想把决定权还给你。你完全有理由不信我，可以自己核查，走不走由你。");
            Assert.IsTrue(State.IsConcernResolved("C2"), adapter.LastResponse);
            Assert.IsFalse(State.EscapeAgreed);
            Debug.Log("[Part C LIVE] E5 verified; route unchecked; C2 resolved; escape not agreed.");
        }

        private IEnumerator SendLive(PlayerInteraction flow, PartCDialogue adapter, string text)
        {
            flow.Send(text);
            float deadline = Time.realtimeSinceStartup + 31;
            while (scene.ModelWait && Time.realtimeSinceStartup < deadline) yield return null;
            flow.Tick();
            Assert.IsFalse(scene.ModelWait, "Gateway did not finish");
            StringAssert.Contains("已接受", adapter.Outcome, adapter.LastResponse);
            Assert.IsEmpty(flow.Draft);
            Debug.Log("[Part C LIVE] " + adapter.LastResponse);
        }

        private ActionTicket InviteLin()
        {
            scene.ToggleDialogue();
            scene.SubmitTestAction("inspect_set_back");
            var ticket = State.Pending;
            Assert.IsNotNull(ticket, scene.Feedback);
            Assert.AreEqual("inspect", ticket.Action);
            Assert.AreEqual(SessionState.SetBackTarget, ticket.TargetId);
            scene.ToggleDialogue();
            return ticket;
        }

        private void Step(int frames)
        {
            for (int i = 0; i < frames; i++)
                scene.StepSimulation(.04f, Vector2.zero, Vector2.zero);
        }

        private void ReachStage(ActionStage expected)
        {
            for (int i = 0; i < 450 && State.Stage != expected && !State.IsPaused; i++) Step(1);
            Assert.AreEqual(expected, State.Stage, scene.Feedback);
        }

        // Exercise the same camera and CharacterController inputs used by mouse look and WASD.
        private void LookAt(Vector3 target)
        {
            Vector3 current = World.ViewCamera.transform.forward;
            Vector3 desired = (target - World.ViewCamera.transform.position).normalized;
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(current, Vector3.up),
                Vector3.ProjectOnPlane(desired, Vector3.up), Vector3.up);
            float elevation = (Mathf.Asin(desired.y) - Mathf.Asin(current.y)) * Mathf.Rad2Deg;
            scene.StepSimulation(.02f, Vector2.zero, new Vector2(yaw, elevation) / 2.1f);
        }

        private void WalkTo(Vector3 goal)
        {
            for (int i = 0; i < 150; i++)
            {
                var remaining = Vector3.ProjectOnPlane(goal - World.Player.position, Vector3.up);
                if (remaining.magnitude < .09f) return;
                var local = World.Player.InverseTransformDirection(remaining.normalized);
                scene.StepSimulation(.04f, new Vector2(local.x, local.z), Vector2.zero);
            }
            Assert.Fail("Could not walk from " + World.Player.position + " to " + goal);
        }

        private void WalkToDoorControl()
        {
            var station = World.Station;
            WalkTo(station.DoorControl.position - station.transform.forward * .9f);
            LookAt(station.DoorControl.position);
        }

        private void EnterPassage()
        {
            var station = World.Station;
            // Start outside the doorway, then walk through the opening onto the route marker.
            WalkTo(station.ObservationPoint.position - station.transform.forward * .4f);
            WalkTo(station.RouteProbe.position);
            LookAt(station.RouteSign.position);
            Assert.IsTrue(station.CanCheckRoute(World.Player, World.ViewCamera.transform));
        }

        [UnityTest]
        public IEnumerator InspectionFirstRequiresOpeningAndUninterruptedSightThenASeparateRouteCheck()
        {
            yield return LoadGreybox();
            var station = World.Station;
            ActionTicket arrival = null;
            scene.ObservationReady += ticket => arrival = ticket;
            var inspection = InviteLin();
            ReachStage(ActionStage.Arrived);
            Assert.AreSame(inspection, arrival);
            Assert.IsFalse(station.DoorOpen);
            Assert.IsTrue(World.AtObservation(SessionState.SetBackTarget));
            Assert.IsFalse(scene.TryCompleteSetBackExperiment(inspection, Guid.NewGuid()));
            Step(60);
            Assert.IsEmpty(State.VerifiedEvidence);

            WalkToDoorControl();
            scene.InteractWithSetBack();
            Assert.IsTrue(station.DoorOpen);
            Step(60);
            Assert.IsEmpty(State.VerifiedEvidence, "Opening the door alone cannot verify the set.");
            Assert.IsTrue(station.InspectionVisible(World.Lin));
            Assert.IsFalse(scene.TryCompleteCameraExperiment(inspection, Guid.NewGuid()));
            Assert.IsFalse(scene.TryCompleteSetBackExperiment(inspection, Guid.NewGuid()));
            Assert.IsFalse(scene.TryConfirmRoute(State.RunId, Guid.NewGuid()));

            // Cover each clue independently. Clearing an obstruction must not award a partial inspection.
            foreach (var clue in new[] { station.SetBrace, station.SetMark })
            {
                scene.InteractWithSetBack();
                Step(30);
                var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    occluder.transform.position = Vector3.Lerp(clue.position, World.Lin.position + Vector3.up * 1.55f, .18f);
                    occluder.transform.localScale = Vector3.one * .45f;
                    Physics.SyncTransforms();
                    Assert.IsFalse(station.InspectionVisible(World.Lin), clue.name);
                    Step(55);
                    Assert.IsFalse(State.E5Verified);
                    Assert.AreEqual(ActionStage.Failed, State.Stage);
                    Assert.IsTrue(State.HasPause(PauseReason.Error));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(occluder);
                    Physics.SyncTransforms();
                }
                scene.RecoverNavigation();
                InviteLin();
                ReachStage(ActionStage.Arrived);
                Step(25);
                Assert.IsEmpty(State.VerifiedEvidence);
            }

            scene.InteractWithSetBack();
            Step(30);
            scene.ToggleMenu();
            Step(100);
            Assert.IsFalse(State.E5Verified, "Paused time must not complete the inspection.");
            scene.ToggleMenu();
            Step(26);
            CollectionAssert.AreEqual(new[] { "E5" }, State.VerifiedEvidence);
            Assert.IsFalse(State.RouteConfirmed);
            ReachStage(ActionStage.Completed);

            EnterPassage();
            scene.InteractWithSetBack();
            Step(12);
            Assert.IsFalse(State.RouteConfirmed);
            LookAt(World.Player.position - station.transform.forward * 3f);
            Step(30);
            Assert.IsFalse(State.RouteConfirmed, "Turning away must interrupt the route check.");
            LookAt(station.RouteSign.position);
            scene.InteractWithSetBack();
            Step(30);
            Assert.IsTrue(State.RouteConfirmed);
            Assert.IsFalse(State.EscapeAgreed);
            CollectionAssert.AreEqual(new[] { "E5" }, State.VerifiedEvidence);
        }

        [UnityTest]
        public IEnumerator RouteCanBeCheckedFirstAndRestartDiscardsDoorFactsAndOldEvents()
        {
            yield return LoadGreybox();
            var station = World.Station;
            var oldRun = State.RunId;
            scene.InteractWithSetBack();
            Assert.IsFalse(station.DoorOpen, "F at spawn must not operate the door remotely.");
            WalkToDoorControl();
            scene.InteractWithSetBack();
            Assert.IsTrue(station.DoorOpen);
            EnterPassage();
            Assert.IsFalse(scene.TryConfirmRoute(oldRun, Guid.NewGuid()), "Standing at the marker is only a precondition.");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                obstacle.transform.position = (station.RouteProbe.position + station.ExitPoint.position) * .5f + Vector3.up * .55f;
                obstacle.transform.localScale = new Vector3(.32f, .8f, .32f);
                Physics.SyncTransforms();
                Assert.IsFalse(station.CanCheckRoute(World.Player, World.ViewCamera.transform));
                scene.InteractWithSetBack();
                Step(30);
                Assert.IsFalse(State.RouteConfirmed, "An obstruction below the sign still blocks the passage.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obstacle);
                Physics.SyncTransforms();
            }
            scene.InteractWithSetBack();
            Step(30);
            Assert.IsTrue(State.RouteConfirmed);
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(scene.TryConfirmRoute(oldRun, Guid.NewGuid()));

            WalkTo(station.ObservationPoint.position - station.transform.forward * .4f);
            WalkToDoorControl();
            var inspection = InviteLin();
            ReachStage(ActionStage.Arrived);
            Assert.IsEmpty(State.VerifiedEvidence);
            scene.InteractWithSetBack();
            Step(55);
            Assert.IsTrue(State.E5Verified);
            Assert.IsTrue(State.RouteConfirmed);
            Assert.IsFalse(State.EscapeAgreed);

            scene.Restart();
            scene.ToggleIntroOff();
            Assert.AreNotEqual(oldRun, State.RunId);
            Assert.IsFalse(station.DoorOpen);
            Assert.IsFalse(State.RouteConfirmed);
            Assert.IsEmpty(State.VerifiedEvidence);
            var current = InviteLin();
            ReachStage(ActionStage.Arrived);
            Assert.AreNotEqual(inspection.ActionId, current.ActionId);
            Assert.IsFalse(scene.TryCompleteSetBackExperiment(inspection, Guid.NewGuid()));
            Assert.IsFalse(scene.TryConfirmRoute(oldRun, Guid.NewGuid()));
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(State.RouteConfirmed);
        }

        [UnityTest]
        public IEnumerator DeadlineDuringObservationLeavesTheEvidenceAndRouteUnverified()
        {
            yield return LoadGreybox();
            WalkToDoorControl();
            scene.InteractWithSetBack();
            var inspection = InviteLin();
            ReachStage(ActionStage.Arrived);
            scene.InteractWithSetBack();
            Step(15);
            scene.ToggleMenu();
            scene.TestNearDeadline();
            scene.ToggleMenu();
            Step(50);
            Assert.IsTrue(State.TimedOut);
            Assert.AreEqual("18:00:00", State.TimeHms);
            Assert.IsFalse(scene.TryCompleteSetBackExperiment(inspection, Guid.NewGuid()));
            Assert.IsFalse(scene.TryConfirmRoute(State.RunId, Guid.NewGuid()));
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(State.RouteConfirmed);
        }

        [UnityTest]
        public IEnumerator FollowingWaitsOutsideTheRestrictedPassageAndResumesInThePublicRoom()
        {
            yield return LoadGreybox();
            var station = World.Station;
            WalkToDoorControl();
            scene.InteractWithSetBack();
            EnterPassage();
            Assert.IsTrue(station.InRestrictedArea(World.Player.position));

            scene.ToggleDialogue();
            scene.SubmitTestAction("follow");
            Assert.IsNotNull(State.Pending, scene.Feedback);
            scene.ToggleDialogue();
            var start = World.Lin.position;
            for (int i = 0; i < 170; i++)
            {
                Step(1);
                Assert.IsFalse(station.InRestrictedArea(World.Lin.position), "Investigation cannot grant consent to enter.");
            }
            Assert.AreEqual(ActionStage.Following, State.Stage, scene.Feedback);
            Assert.Greater(Vector3.Distance(start, World.Lin.position), .5f);
            Assert.Less(Vector3.Distance(World.Lin.position, station.ObservationPoint.position), 1.6f);
            Vector3 waiting = World.Lin.position;

            WalkToDoorControl();
            WalkTo(station.ObservationPoint.position - station.transform.forward * 2.8f + station.transform.right * 1.6f);
            Step(150);
            Assert.IsFalse(station.InRestrictedArea(World.Player.position));
            Assert.AreEqual(ActionStage.Following, State.Stage, scene.Feedback);
            Assert.Greater(Vector3.Distance(waiting, World.Lin.position), .5f, "Lin should leave the waiting spot.");
            Assert.Less(Vector3.Distance(World.Lin.position, World.Player.position), 1.6f);
            Assert.IsFalse(State.RouteConfirmed);
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(State.EscapeAgreed);
        }
    }
}
#endif
