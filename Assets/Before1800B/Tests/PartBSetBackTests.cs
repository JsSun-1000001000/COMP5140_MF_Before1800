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
