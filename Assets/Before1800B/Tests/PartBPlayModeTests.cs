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
    public class PartBPlayModeTests
    {
        private const string ScenePath = "Assets/Before1800B/PartB_Greybox.unity";
        private PartBScene scene;
        private float previousCaptureDelta;
        private SessionState State => scene.State;
        private GreyboxWorld World => scene.World;

        [UnitySetUp]
        public IEnumerator LoadPlayableScene()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 0.04f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            Assert.IsNotNull(scene);
            Assert.AreEqual(ScenePath, scene.gameObject.scene.path);
            Assert.IsTrue(scene.IntroOpen);
            Assert.AreEqual("17:00:00", State.TimeHms);
            scene.ToggleIntroOff();
        }

        [TearDown]
        public void RestoreCaptureStep() => Time.captureDeltaTime = previousCaptureDelta;

        // Only observes the playing scene. Normal Update drives the NPC and the clock.
        private IEnumerator Until(Func<bool> condition, string expectation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 18.0;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(condition(), expectation + " / " + State.Stage + " / " + State.LastResult + " / " + scene.Feedback);
        }

        private ActionTicket QueueFromDialogue(string action)
        {
            scene.ToggleDialogue();
            scene.SubmitTestAction(action);
            var ticket = State.Pending;
            Assert.IsNotNull(ticket, scene.Feedback);
            Assert.AreEqual(action, ticket.Action);
            scene.ToggleDialogue();
            return ticket;
        }

        private void AssertNoFacts()
        {
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(State.RouteConfirmed);
            Assert.IsFalse(State.EscapeAgreed);
        }

        // Supply movement to the same CharacterController path used by WASD. No teleport or collider toggles.
        private void WalkPlayerTo(Vector3 goal)
        {
            for (int i = 0; i < 100; i++)
            {
                Vector3 remaining = goal - World.Player.position;
                remaining.y = 0;
                if (remaining.magnitude < .06f) return;
                Vector3 direction = World.Player.InverseTransformDirection(remaining.normalized);
                scene.StepSimulation(.03f, new Vector2(direction.x, direction.z), Vector2.zero);
            }
            Assert.Fail("Could not walk the player onto the selected floor position");
        }

        [UnityTest]
        public IEnumerator QueueAndOverlappingPauseOwnersFreezeClockCameraAndActors()
        {
            Vector3 start = World.Player.position;
            Quaternion facing = World.ViewCamera.transform.rotation;
            scene.StepSimulation(.09f, Vector2.up, new Vector2(2, 2));
            Assert.Greater(Vector3.Distance(start, World.Player.position), .04f);
            Assert.Greater(Quaternion.Angle(facing, World.ViewCamera.transform.rotation), 1f);
            scene.ToggleDialogue();
            scene.SubmitTestAction("inspect");
            Assert.AreEqual(ActionStage.Queued, State.Stage, scene.Feedback);
            var queued = State.Pending;
            scene.ToggleModelWait();
            scene.ToggleMenu();

            Vector3 player = World.Player.position;
            Vector3 lin = World.Lin.position;
            Quaternion camera = World.ViewCamera.transform.rotation;
            double clock = State.ElapsedGameSeconds;
            scene.StepSimulation(1f, Vector2.up, Vector2.one);
            yield return null;
            scene.ToggleMenu();
            Assert.IsTrue(scene.DialogueOpen);
            scene.ToggleDialogue();
            yield return null;
            Assert.IsTrue(scene.ModelWait, "The independent manual wait must survive dialogue release.");
            Assert.AreSame(queued, State.Pending);
            Assert.IsNull(State.Active);
            Assert.AreEqual(clock, State.ElapsedGameSeconds);
            Assert.AreEqual(player, World.Player.position);
            Assert.AreEqual(lin, World.Lin.position);
            Assert.AreEqual(camera, World.ViewCamera.transform.rotation);

            scene.ToggleModelWait();
            yield return Until(() => Vector3.Distance(lin, World.Lin.position) > .18f, "Lin should start the queued inspection");
            Assert.AreSame(queued, State.Active);
            Assert.Greater(State.ElapsedGameSeconds, clock);
            scene.ToggleDialogue();
            lin = World.Lin.position;
            clock = State.ElapsedGameSeconds;
            yield return null;
            yield return null;
            Assert.AreEqual(lin, World.Lin.position);
            Assert.AreEqual(clock, State.ElapsedGameSeconds);
            AssertNoFacts();
        }

        [UnityTest]
        public IEnumerator FollowMovesTowardsPlayerAndStopHoldsPosition()
        {
            float initialDistance = Vector3.Distance(World.Lin.position, World.Player.position);
            QueueFromDialogue("follow");
            yield return Until(() => Vector3.Distance(World.Lin.position, World.Player.position) < initialDistance - .2f,
                "Follow should bring Lin closer to the player");
            Assert.AreEqual(CooperationMode.Following, State.Cooperation);
            QueueFromDialogue("stop");
            yield return Until(() => State.Pending == null && State.Active == null, "Stop should release the follow");
            Assert.AreEqual(CooperationMode.Talking, State.Cooperation);
            Vector3 stopped = World.Lin.position;
            scene.StepSimulation(.09f, Vector2.down, Vector2.zero);
            yield return null;
            Assert.AreEqual(stopped, World.Lin.position);
            AssertNoFacts();
        }

        [UnityTest]
        public IEnumerator E1RequiresCurrentArrivalAndIndependentUniqueExperimentEvent()
        {
            int arrivals = 0;
            ActionTicket arrived = null;
            scene.ObservationReady += ticket => { arrivals++; arrived = ticket; };
            var cancelled = QueueFromDialogue("inspect");
            yield return Until(() => State.Active == cancelled, "First check should start");
            QueueFromDialogue("stop");
            yield return Until(() => State.Active == null && State.Pending == null, "Explicit stop should cancel first check");
            var current = QueueFromDialogue("inspect");
            Guid premature = Guid.NewGuid();
            Assert.IsFalse(scene.TryCompleteCameraExperiment(current, premature), "Queued is too early");
            yield return Until(() => State.Stage == ActionStage.Arrived, "Real navigation should report arrival");
            Assert.IsTrue(World.IsAtObservation);
            Assert.AreEqual(1, arrivals);
            Assert.AreSame(current, arrived);
            AssertNoFacts();
            yield return null;
            AssertNoFacts();
            Assert.IsFalse(scene.TryCompleteCameraExperiment(cancelled, Guid.NewGuid()), "Stale action shares camera_test only");
            Assert.IsFalse(scene.TryCompleteCameraExperiment(current, premature), "Replayed premature event");
            AssertNoFacts();
            Guid completed = Guid.NewGuid();
            Assert.IsTrue(scene.TryCompleteCameraExperiment(arrived, completed));
            Assert.IsFalse(scene.TryCompleteCameraExperiment(arrived, completed));
            Assert.IsFalse(scene.TryCompleteCameraExperiment(arrived, Guid.NewGuid()));
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
            yield return Until(() => State.Stage == ActionStage.Completed, "Lin should return after the experiment");
            scene.ToggleDialogue();
            Assert.IsTrue(scene.TryBeginModelRequest(out var nextTurn));
            var result = nextTurn.Snapshot.last_action_result;
            Assert.AreEqual(current.ActionId.ToString("N"), result.action_id);
            Assert.AreEqual(current.ExperimentId.ToString("N"), result.experiment_run_id);
            Assert.AreEqual("completed", result.status);
            Assert.IsTrue(Array.Exists(nextTurn.Snapshot.recent_action_results, r =>
                r.action_id == result.action_id && r.status == "arrived"));
            Assert.IsTrue(Array.Exists(nextTurn.Snapshot.recent_action_results, r =>
                r.action_id == result.action_id && r.status == "verified"));
            Assert.IsNull(nextTurn.Snapshot.active_action);
            scene.ToggleDialogue();
            Assert.IsFalse(State.EscapeAgreed);
            Assert.IsFalse(State.RouteConfirmed);
        }

        [UnityTest]
        public IEnumerator UnreachableCheckRejectsPreflightAndFailsIfRouteChangesWhileQueued()
        {
            scene.ToggleMenu();
            scene.ToggleTestPathBlocked();
            scene.ToggleMenu();
            Assert.IsFalse(World.CanReachObservation);
            scene.ToggleDialogue();
            scene.SubmitTestAction("inspect");
            Assert.IsNull(State.Pending);
            scene.ToggleDialogue();
            scene.ToggleMenu();
            scene.ToggleTestPathBlocked();
            scene.ToggleMenu();
            Assert.IsTrue(World.CanReachObservation);
            var ticket = QueueFromDialogue("inspect");
            // No frame elapsed since queueing. Remove the route before the first navigation step.
            scene.ToggleMenu();
            scene.ToggleTestPathBlocked();
            scene.ToggleMenu();
            yield return Until(() => State.HasPause(PauseReason.Error), "Route loss should enter recoverable error");
            Assert.AreEqual(ActionStage.Failed, State.Stage);
            var failure = State.ReadSnapshot().last_action_result;
            Assert.AreEqual("failed", failure.status);
            Assert.AreEqual(ticket.ActionId.ToString("N"), failure.action_id);
            Assert.AreEqual("RC_NAV_NO_PATH", failure.reason);
            Assert.IsFalse(scene.TryCompleteCameraExperiment(ticket, Guid.NewGuid()));
            AssertNoFacts();
            double clock = State.ElapsedGameSeconds;
            yield return null;
            Assert.AreEqual(clock, State.ElapsedGameSeconds);
            scene.ToggleMenu();
            scene.ToggleTestPathBlocked();
            scene.ToggleMenu();
            scene.RecoverNavigation();
            Assert.IsFalse(State.IsPaused);
            var retry = QueueFromDialogue("inspect");
            Assert.AreNotEqual(ticket.ActionId, retry.ActionId);
            yield return Until(() => State.Stage == ActionStage.Arrived, "New check should reach the reopened point");
            AssertNoFacts();
        }

        [UnityTest]
        public IEnumerator DisabledTargetsRejectCProposalsAndInterruptCurrentTravel()
        {
            scene.ToggleDialogue();
            foreach (var target in new[] { World.CameraTarget, World.ObservationPoint })
            {
                target.gameObject.SetActive(false);
                Assert.IsFalse(World.CanReachObservation);
                Assert.IsTrue(scene.TryBeginModelRequest(out var request));
                Assert.IsFalse(scene.TryApplyModelAction(request, "inspect", "camera_test", out _), target.name);
                Assert.IsNull(State.Pending);
                AssertNoFacts();
                target.gameObject.SetActive(true);
            }
            scene.ToggleDialogue();
            var ticket = QueueFromDialogue("inspect");
            yield return Until(() => State.Active == ticket, "Inspection should begin with targets enabled");
            World.CameraTarget.gameObject.SetActive(false);
            yield return Until(() => State.HasPause(PauseReason.Error), "Disabling the device during travel should fail");
            Assert.AreEqual(ActionStage.Failed, State.Stage);
            Assert.IsFalse(scene.TryCompleteCameraExperiment(ticket, Guid.NewGuid()));
            AssertNoFacts();
        }

        [UnityTest]
        public IEnumerator FailedReturnRetriesOnlyItsRemainingTravelAndPreservesE1()
        {
            int arrivals = 0;
            scene.ObservationReady += _ => arrivals++;
            var original = QueueFromDialogue("inspect");
            yield return Until(() => State.Stage == ActionStage.Arrived, "Inspection should reach the observation point");
            var returnPoint = World.ObservationPoint.parent.Find("e1_return");
            Assert.IsNotNull(returnPoint);
            Vector3 home = returnPoint.position;
            returnPoint.position = home + Vector3.right * 30;
            Assert.IsTrue(scene.TryCompleteCameraExperiment(original, Guid.NewGuid()));
            yield return Until(() => State.HasPause(PauseReason.Error), "Off-mesh home should fail return navigation");
            Assert.IsTrue(State.CanRetryReturn);
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
            // Exercise the alternative menu route after acknowledging so the player can move a blocker.
            scene.RecoverNavigation();
            returnPoint.position = home;
            scene.ToggleMenu();
            scene.RetryReturn();
            var retry = State.Pending;
            Assert.IsNotNull(retry);
            Assert.AreNotEqual(original.ActionId, retry.ActionId);
            Assert.IsFalse(scene.MenuOpen);
            yield return Until(() => State.Stage == ActionStage.Returning, "Fresh ticket should enter Returning directly");
            Assert.IsFalse(scene.TryCompleteCameraExperiment(retry, Guid.NewGuid()));
            yield return Until(() => State.Stage == ActionStage.Completed, "Retry should reach the restored home");
            Assert.Less(Vector3.Distance(home, World.Lin.position), .65f);
            Assert.AreEqual(1, arrivals);
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
            var history = State.ReadSnapshot().recent_action_results;
            Assert.AreEqual(1, Array.FindAll(history, x => x.status == "verified").Length);
            Assert.AreEqual(1, Array.FindAll(history, x => x.status == "arrived").Length);
            Assert.AreEqual(retry.ActionId.ToString("N"), State.ReadSnapshot().last_action_result.action_id);
        }

        [UnityTest]
        public IEnumerator PlayerBlockingReturnCausesNoProgressUntilPlayerWalksAway()
        {
            var ticket = QueueFromDialogue("inspect");
            yield return Until(() => State.Stage == ActionStage.Arrived, "Lin should reach camera_test");
            Vector3 home = World.ObservationPoint.parent.Find("e1_return").position;
            WalkPlayerTo(home);
            Assert.IsTrue(World.Player.GetComponent<CharacterController>().enabled);
            Assert.IsTrue(scene.TryCompleteCameraExperiment(ticket, Guid.NewGuid()));
            yield return Until(() => State.HasPause(PauseReason.Error), "The player's capsule should block Lin from docking");
            Assert.AreEqual("RC_NAV_NO_PROGRESS", State.ReadSnapshot().last_action_result.reason);
            Assert.IsTrue(State.CanRetryReturn);
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
            scene.RecoverNavigation();
            WalkPlayerTo(home + Vector3.back * 1.8f);
            scene.ToggleMenu();
            scene.RetryReturn();
            var recovery = State.Pending;
            Assert.IsNotNull(recovery);
            yield return Until(() => State.Stage == ActionStage.Completed, "Lin should dock once the player clears his point");
            Assert.Less(Vector3.Distance(home, World.Lin.position), .65f);
            var snapshot = State.ReadSnapshot();
            Assert.AreEqual(recovery.ActionId.ToString("N"), snapshot.last_action_result.action_id);
            Assert.AreEqual(1, Array.FindAll(snapshot.recent_action_results, x => x.status == "verified").Length);
            Assert.AreEqual(1, Array.FindAll(snapshot.recent_action_results, x => x.status == "arrived").Length);
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
        }

        [UnityTest]
        public IEnumerator TestConcernAndWithdrawPreserveIndependentStateAndRestartClearsIt()
        {
            QueueFromDialogue("inspect");
            yield return Until(() => State.Stage == ActionStage.Arrived, "Inspect should reach the point");
            scene.TestCompleteCamera();
            yield return Until(() => State.Stage == ActionStage.Completed, "Mock device completion should trigger return");
            Assert.IsTrue(State.E1Verified);
            scene.ToggleMenu();
            scene.TestResolveC2();
            scene.ToggleMenu();
            Assert.IsFalse(State.IsConcernResolved("C1"));
            Assert.IsTrue(State.IsConcernResolved("C2"));
            Assert.IsFalse(State.IsConcernResolved("C3"));
            Assert.AreEqual(CooperationMode.Talking, State.Cooperation);
            Assert.IsFalse(State.EscapeAgreed);
            Assert.IsFalse(State.RouteConfirmed);
            var beforeWithdraw = World.Lin.position;
            QueueFromDialogue("withdraw");
            yield return Until(() => State.Stage == ActionStage.Completed, "Withdraw should reach its separate standing point");
            Assert.Greater(Vector3.Distance(beforeWithdraw, World.Lin.position), .5f);
            Assert.AreEqual(CooperationMode.Withdrawn, State.Cooperation);
            scene.ToggleDialogue();
            scene.SubmitTestAction("restore");
            Assert.AreEqual(CooperationMode.Talking, State.Cooperation);
            scene.SubmitTestAction("withdraw");
            Assert.IsNull(State.Pending, "An already used withdrawal cannot be queued again");
            Assert.IsTrue(State.E1Verified);
            Assert.IsTrue(State.IsConcernResolved("C2"));
            Assert.IsFalse(State.EscapeAgreed);
            scene.Restart();
            AssertNoFacts();
            Assert.IsFalse(State.IsConcernResolved("C2"));
            Assert.IsFalse(State.WithdrawUsed);
            Assert.IsEmpty(State.ReadSnapshot().recent_action_results);
        }

        [UnityTest]
        public IEnumerator StaleModelResponseCannotReleaseNewWaitOrDispatchActionTwice()
        {
            scene.ToggleDialogue();
            Assert.IsTrue(scene.TryBeginModelRequest(out var old));
            scene.ToggleDialogue();
            scene.ToggleDialogue();
            Assert.IsTrue(scene.TryBeginModelRequest(out var current));
            Assert.IsFalse(scene.TryApplyModelAction(old, "inspect", "camera_test", out _));
            Assert.IsTrue(State.HasPause(PauseReason.ModelWait));
            Assert.IsNull(State.Pending);
            Assert.IsTrue(scene.TryApplyModelAction(current, "inspect", "camera_test", out var reason), reason);
            var pending = State.Pending;
            Assert.IsNotNull(pending);
            Assert.IsFalse(State.HasPause(PauseReason.ModelWait));
            Assert.IsTrue(State.IsPaused);
            Assert.IsFalse(scene.TryApplyModelAction(current, "inspect", "camera_test", out _));
            Assert.AreSame(pending, State.Pending);
            yield return null;
            Assert.IsNull(State.Active);
            AssertNoFacts();
        }

        [UnityTest]
        public IEnumerator DeadlineStopsSceneAndOldRunCannotCompleteNewArrival()
        {
            Vector3 playerStart = World.Player.position;
            Vector3 linStart = World.Lin.position;
            var oldRun = State.RunId;
            var previous = QueueFromDialogue("inspect");
            yield return Until(() => State.Stage == ActionStage.Arrived, "First check should reach the observation point");
            scene.ToggleMenu();
            scene.TestNearDeadline();
            Assert.AreEqual("17:59:55", State.TimeHms);
            yield return null;
            Assert.AreEqual("17:59:55", State.TimeHms);
            scene.ToggleMenu();
            yield return Until(() => State.TimedOut, "Clock should reach the deadline");
            Assert.AreEqual("18:00:00", State.TimeHms);
            Assert.IsFalse(scene.TryCompleteCameraExperiment(previous, Guid.NewGuid()));
            Vector3 playerEnd = World.Player.position;
            Vector3 linEnd = World.Lin.position;
            scene.StepSimulation(.1f, Vector2.up, Vector2.one);
            Assert.AreEqual(playerEnd, World.Player.position);
            Assert.AreEqual(linEnd, World.Lin.position);
            AssertNoFacts();
            scene.Restart();
            Assert.AreNotEqual(oldRun, State.RunId);
            Assert.IsTrue(scene.IntroOpen);
            Assert.AreEqual("17:00:00", State.TimeHms);
            Assert.AreEqual(playerStart, World.Player.position);
            Assert.AreEqual(linStart, World.Lin.position);
            scene.ToggleIntroOff();
            var current = QueueFromDialogue("inspect");
            yield return Until(() => State.Stage == ActionStage.Arrived, "New run should independently reach the point");
            Assert.IsFalse(scene.TryCompleteCameraExperiment(previous, Guid.NewGuid()));
            AssertNoFacts();
            Assert.IsTrue(scene.TryCompleteCameraExperiment(current, Guid.NewGuid()));
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
        }
    }
}
#endif
