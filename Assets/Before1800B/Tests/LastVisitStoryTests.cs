#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Before1800.PartB.Tests
{
    public class LastVisitStoryTests
    {
        static void Verify(SessionState state, string target)
        {
            Assert.IsTrue(state.TryQueue("inspect", target, out var reason), reason);
            Assert.IsTrue(state.TryStartPending(out var ticket));
            Assert.IsTrue(state.ReportArrived(ticket));
            bool complete = target == SessionState.CameraTarget ? state.TryCompleteCameraExperiment(ticket, Guid.NewGuid()) :
                target == SessionState.BroadcastTarget ? state.TryCompleteBroadcastExperiment(ticket, Guid.NewGuid()) :
                state.TryCompleteSetBackExperiment(ticket, Guid.NewGuid());
            Assert.IsTrue(complete);
            Assert.IsTrue(state.ReportCompleted(ticket));
        }
        [Test]
        public void EmotionalConsentCannotReplacePhysicalProofOrRoute()
        {
            var state = new SessionState();
            Assert.IsFalse(state.CanResolveConcern("C1"));
            Assert.IsFalse(state.TryResolveConcernFromAcceptedTurn("C3", "turn1", "Everything is safe"));
            Assert.IsFalse(state.TryQueue("agree_escape", null, out _));
            Assert.IsTrue(state.TryResolveConcernFromAcceptedTurn("C2", "turn2", "An actual accepted player turn"));
            Assert.IsFalse(state.TryResolveConcernFromAcceptedTurn("C2", "turn3", "Do it again"));
            Assert.IsFalse(state.TryResolveConcernFromAcceptedTurn("C4", "turn3", "New concern"));
            Verify(state, SessionState.CameraTarget);
            Verify(state, SessionState.BroadcastTarget);
            Assert.IsFalse(state.CanResolveConcern("C1"));
            Verify(state, SessionState.SetBackTarget);
            Assert.IsTrue(state.CanResolveConcern("C1"));
            Assert.IsFalse(state.TryResolveConcernFromAcceptedTurn("C1", "", "evidence"));
            Assert.IsFalse(state.TryResolveConcernFromAcceptedTurn("C1", "turn4", " "));
            Assert.IsTrue(state.TryResolveConcernFromAcceptedTurn("C1", "turn4", "explanation"));
            Assert.IsFalse(state.CanAgreeEscape);
            Assert.IsTrue(state.TryConfirmRoute(state.RunId, Guid.NewGuid()));
            Assert.IsTrue(state.TryResolveConcernFromAcceptedTurn("C3", "turn5", "Respect her choice and admit uncertainty"));
            Assert.IsTrue(state.CanAgreeEscape);
            Assert.IsTrue(state.TryQueue("agree_escape", null, out _));
            Assert.IsTrue(state.EscapeAgreed);
            Assert.IsFalse(state.TryFinishEnding("together", true, false), "Player alone cannot complete joint departure.");
            Assert.IsTrue(state.TryStartPending(out _));
            Assert.IsFalse(state.TryQueue("withdraw", null, out _), "Consent cannot be retracted by a random follow-up action.");
            Assert.IsTrue(state.TryFinishEnding("together", true, true));
            Assert.IsTrue(state.IsPaused);
            Assert.IsTrue(state.HasPause(PauseReason.Terminal));
            Assert.AreEqual("together", state.ReadSnapshot().ending);
            Assert.IsFalse(state.TryQueue("reply", null, out _));
        }
        [Test]
        public void EndingJustTheConversationPreservesQueuedActionAndCanReopen()
        {
            var state = new SessionState();
            var dialogue = state.AcquirePause(PauseReason.Dialogue, "conversation");
            Assert.IsTrue(state.TryResolveConcernFromAcceptedTurn("C2", "previous", "I wrote the letters."));
            Assert.IsTrue(state.TryQueue("inspect", SessionState.CameraTarget, out _));
            var queued = state.Pending;
            Assert.IsFalse(state.TryQueue("end_conversation", null, out _));
            var wait = state.AcquirePause(PauseReason.ModelWait, "model");
            var version = state.Version;
            Assert.IsFalse(state.TryQueue(state.RunId, version - 1, "end_conversation", null, out _));
            Assert.IsFalse(state.TryQueue(Guid.NewGuid(), version, "end_conversation", null, out _));
            Assert.IsFalse(state.TryQueue(state.RunId, version, "end_conversation", "player", out _));
            Assert.IsTrue(state.TryQueue(state.RunId, version, "end_conversation", null, out var reason), reason);
            Assert.AreSame(queued, state.Pending);
            Assert.IsTrue(state.IsConcernResolved("C2"));
            Assert.IsFalse(state.WithdrawUsed);
            Assert.IsFalse(state.IsTerminal);
            Assert.IsEmpty(state.VerifiedEvidence);

            state.ReleasePause(wait);
            Assert.IsTrue(state.IsPaused, "The goodbye remains on screen until its dialogue is closed.");
            state.ReleasePause(dialogue);
            Assert.IsFalse(state.CanEndConversation);
            Assert.IsTrue(state.TryStartPending(out var active));
            Assert.AreSame(queued, active);
            Assert.IsNotNull(state.AcquirePause(PauseReason.Dialogue, "next conversation"));
            Assert.IsTrue(state.CanEndConversation);
        }

        [Test]
        public void OrdinaryDepartureIsCompleteAndRestartClearsIt()
        {
            var state = new SessionState();
            Assert.IsFalse(state.TryFinishEnding("ordinary_departure", false, false));
            Assert.IsTrue(state.TryFinishEnding("ordinary_departure", true, false));
            Assert.IsTrue(state.IsTerminal);
            state.AdvanceClock(100);
            Assert.AreEqual(0, state.ElapsedGameSeconds);
            state.Restart();
            Assert.IsFalse(state.IsTerminal);
            Assert.AreEqual("none", state.Ending);
        }
        [Test]
        public void ReadingAStoryObjectNeverCountsAsEvidenceAndRequiresArrival()
        {
            var state = new SessionState();
            Assert.IsTrue(state.TryQueue("inspect", "parents_diary", out _));
            Assert.IsTrue(state.TryStartPending(out var ticket));
            Assert.IsFalse(state.TryCompleteStoryInspection(ticket));
            Assert.IsTrue(state.ReportArrived(ticket));
            Assert.IsTrue(state.TryCompleteStoryInspection(ticket));
            Assert.IsTrue(state.ReportCompleted(ticket));
            Assert.IsEmpty(state.VerifiedEvidence);
            Assert.IsFalse(state.RouteConfirmed);
            Assert.IsFalse(state.IsConcernResolved("C2"));
        }
        [Test]
        public void DeadlineWinsAndFastForwardDoesNotForgeConsent()
        {
            var state = new SessionState();
            state.AdvanceClock(360);
            Assert.AreEqual("timeout", state.Ending);
            Assert.IsFalse(state.TryFinishEnding("ordinary_departure", true, true));
            state.Restart();
            Verify(state, SessionState.CameraTarget);
            state.AcquirePause(PauseReason.Menu, "test");
            Assert.IsTrue(state.FinishVisitAtDeadline());
            Assert.AreEqual("timeout", state.Ending);
            Assert.IsFalse(state.EscapeAgreed);
        }
        static void PlaceActor(Transform actor, Vector3 position)
        {
            var controller = actor.GetComponent<CharacterController>();
            controller.enabled = false; actor.position = position; controller.enabled = true;
            Physics.SyncTransforms();
        }
        static void Face(PartBScene scene, Vector3 point)
        {
            Vector3 current = scene.World.ViewCamera.transform.forward;
            Vector3 desired = (point - scene.World.ViewCamera.transform.position).normalized;
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(current, Vector3.up), Vector3.ProjectOnPlane(desired, Vector3.up), Vector3.up);
            float elevation = (Mathf.Asin(desired.y) - Mathf.Asin(current.y)) * Mathf.Rad2Deg;
            scene.StepSimulation(.001f, Vector2.zero, new Vector2(yaw, elevation) / 2.1f);
        }
        [UnityTest]
        public IEnumerator PrivateLetterRequiresTrustAndSharingCannotPassThroughWalls()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            var story = scene.GetComponent<LastVisitStory>();
            scene.GetComponent<PartCDialogue>().SetLive(false);
            var flow = scene.GetComponent<GreyboxHUD>().Flow;
            scene.ToggleIntroOff();
            PlaceActor(scene.World.Player, new Vector3(.4f, .02f, -.45f));
            Face(scene, new Vector3(.24f, .705f, -1.16f));
            flow.Use();
            Assert.AreEqual(PlayerScreen.Reading, flow.Screen, "Aiming at the letter must select the letter, not the nearby remote.");
            Assert.IsFalse(story.HasPlayerRead("sealed_letter"));
            StringAssert.Contains("先别拆", story.ReadableText);
            story.DismissReading();
            Assert.IsTrue(scene.State.TryResolveConcernFromAcceptedTurn("C2", "accepted-turn", "full admission"));
            Assert.IsTrue(story.TryInteract());
            Assert.IsTrue(story.HasPlayerRead("sealed_letter"));
            Assert.IsFalse(story.HasLinRead("sealed_letter"));
            Assert.IsTrue(story.CanShareReading);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = Vector3.Lerp(scene.World.ViewCamera.transform.position,
                scene.World.Lin.position + Vector3.up * 1.3f, .5f);
            wall.transform.localScale = Vector3.one * .7f;
            Physics.SyncTransforms();
            Assert.IsFalse(story.CanShareReading);
            Assert.IsFalse(story.ShareCurrentDocument());
            UnityEngine.Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            Assert.IsTrue(story.ShareCurrentDocument());
            Assert.IsTrue(story.HasLinRead("sealed_letter"));
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            story.DismissReading();
        }
        [UnityTest]
        public IEnumerator ResidencePanelNeedsRealLinObservationAndSeparateRouteCheck()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            scene.GetComponent<PartCDialogue>().SetLive(false);
            var flow = scene.GetComponent<GreyboxHUD>().Flow;
            scene.ToggleIntroOff();
            var station = scene.World.Station;
            PlaceActor(scene.World.Player, new Vector3(2.8f, .02f, 3f));
            Face(scene, station.DoorControl.position);
            flow.Use();
            Assert.IsTrue(station.DoorOpen, "Panel catch must be reachable through the player's interaction.");
            Assert.IsFalse(scene.State.E5Verified);
            Assert.IsTrue(scene.State.TryQueue("inspect", SessionState.SetBackTarget, out var reason), reason);
            for (int i = 0; i < 600 && scene.State.Stage != ActionStage.Arrived && !scene.State.IsPaused; i++)
                scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
            Assert.AreEqual(ActionStage.Arrived, scene.State.Stage, scene.Feedback);
            Assert.IsTrue(station.InspectionVisible(scene.World.Lin), "Lin must see both brace and production plate behind the open panel.");
            Face(scene, station.DoorControl.position);
            flow.Use();
            Assert.IsTrue(scene.ObservingSetBack, "Player interaction must start Lin's inspection.");
            for (int i = 0; i < 75; i++) scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
            Assert.IsTrue(scene.State.E5Verified);
            Assert.IsFalse(scene.State.RouteConfirmed);
            PlaceActor(scene.World.Player, station.RouteProbe.position);
            Face(scene, station.RouteSign.position);
            Assert.IsTrue(station.CanCheckRoute(scene.World.Player, scene.World.ViewCamera.transform), "Authored foyer route must have actual clearance.");
            flow.Use();
            Assert.IsTrue(scene.CheckingRoute, "Exit storytelling must not steal the route-check interaction.");
            for (int i = 0; i < 40; i++) scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
            Assert.IsTrue(scene.State.RouteConfirmed);
        }
        [UnityTest]
        public IEnumerator ConsentingLinJoinsTheThresholdInsteadOfStoppingOutsideDepartureRange()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            scene.GetComponent<PartCDialogue>().SetLive(false);
            scene.AcceptHardwareInput = false;
            scene.ToggleIntroOff();
            var story = scene.GetComponent<LastVisitStory>();
            Verify(scene.State, SessionState.CameraTarget);
            Verify(scene.State, SessionState.BroadcastTarget);
            Verify(scene.State, SessionState.SetBackTarget);
            Assert.IsTrue(scene.State.TryConfirmRoute(scene.State.RunId, Guid.NewGuid()));
            foreach (var concern in new[] { "C1", "C2", "C3" })
                Assert.IsTrue(scene.State.TryResolveConcernFromAcceptedTurn(concern, "accepted-" + concern, "accepted conversation"));
            Assert.IsTrue(scene.State.TryQueue("agree_escape", null, out _));
            Assert.IsTrue(scene.State.TryStartPending(out _));
            // Different real, walkable approaches to the foyer, with Lin still trailing.
            foreach (var position in new[] { new Vector3(.5f, .02f, 4f), new Vector3(-.4f, .02f, 3.3f), new Vector3(1.1f, .02f, 3.7f) })
            {
                PlaceActor(scene.World.Player, position);
                PlaceActor(scene.World.Lin, new Vector3(.5f, .02f, 1.2f));
                scene.World.ResetTravel();
                Assert.IsFalse(story.CanLeaveTogether, "Lin must physically arrive; consent alone is insufficient.");
                for (int i = 0; i < 600 && !story.CanLeaveTogether; i++)
                    scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
                Assert.IsTrue(story.CanLeaveTogether, "Lin stopped outside the ending area: " + scene.World.Lin.position + " / " + scene.Feedback);
            }
            Face(scene, scene.World.Station.RouteSign.position);
            Assert.IsTrue(story.TryInteract());
            Assert.IsTrue(story.ChooseDeparture(true));
            Assert.AreEqual("together", scene.State.Ending);
        }
        [UnityTest]
        public IEnumerator ResidenceHasReachableStoryTargetsAndPhysicalSetBackPanel()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            var story = scene.GetComponent<LastVisitStory>();
            Assert.IsNotNull(story);
            story.Bind(scene);
            Assert.IsNotNull(scene.World.Station);
            Assert.IsTrue(scene.World.Station.IsAvailable);
            foreach (var id in story.KnownStoryTargets)
                Assert.IsTrue(story.CanVisit(id), "Unreachable story target: " + id);
            Assert.IsTrue(scene.World.CanInspect(SessionState.SetBackTarget));
            Assert.IsFalse(scene.State.E5Verified);
            Assert.IsFalse(scene.State.RouteConfirmed);
            Assert.IsFalse(story.IsEnding);
            foreach (var observation in story.StoryObservations)
                Assert.IsFalse(observation.Contains("合约结束了"), "Unseen diary text must not become Lin's scene knowledge.");
        }
    }
}
#endif
