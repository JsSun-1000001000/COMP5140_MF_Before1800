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
    public class PartBCameraTrackTests
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
            scene.ToggleIntroOff();
        }

        [TearDown]
        public void RestoreCaptureStep() => Time.captureDeltaTime = previousCaptureDelta;

        private IEnumerator Until(Func<bool> condition, string expectation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 22.0;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(condition(), expectation + " / " + State.Stage + " / " + State.LastResult + " / " + scene.Feedback);
        }

        private ActionTicket QueueInspect()
        {
            scene.ToggleDialogue();
            scene.SubmitTestAction("inspect");
            var ticket = State.Pending;
            Assert.IsNotNull(ticket, scene.Feedback);
            scene.ToggleDialogue();
            return ticket;
        }

        private void FaceCamera()
        {
            var view = World.ViewCamera.transform;
            Vector3 delta = World.CameraTarget.position - view.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < .0001f) return;
            World.Player.rotation = Quaternion.LookRotation(delta.normalized);
            scene.StepSimulation(.02f, Vector2.zero, Vector2.zero);
        }

        private void WalkNearCamera()
        {
            Vector3 goal = World.ObservationPoint.position + Vector3.back * 1.1f;
            for (int i = 0; i < 120; i++)
            {
                Vector3 remaining = goal - World.Player.position;
                remaining.y = 0;
                if (remaining.magnitude < .08f) break;
                Vector3 direction = World.Player.InverseTransformDirection(remaining.normalized);
                scene.StepSimulation(.03f, new Vector2(direction.x, direction.z), Vector2.zero);
            }
            FaceCamera();
        }

        [UnityTest]
        public IEnumerator LiveTrackingWritesE1OnlyAfterSecondMarkLock()
        {
            Assert.IsNotNull(World.Camera);
            Assert.IsTrue(World.Camera.CanReachSecondMark());
            var ticket = QueueInspect();
            yield return Until(() => State.Stage == ActionStage.Arrived && State.Active == ticket, "Lin reaches mark A");
            Assert.IsTrue(World.IsAtObservation);
            Assert.IsEmpty(State.VerifiedEvidence);

            WalkNearCamera();
            Assert.IsTrue(World.Camera.CanStartTracking(World.ViewCamera.transform, World.Lin), scene.Feedback);
            Assert.IsTrue(scene.TryBeginCameraTracking(ticket), scene.Feedback);
            Assert.IsTrue(scene.ObservingCamera);
            Assert.IsEmpty(State.VerifiedEvidence);

            Quaternion start = World.CameraTarget.Find("Camera housing")
                ? World.CameraTarget.Find("Camera housing").rotation
                : World.CameraTarget.rotation;
            yield return Until(() => State.E1Verified, "Tracking lock at mark B should write E1");
            CollectionAssert.AreEqual(new[] { "E1" }, State.VerifiedEvidence);
            Assert.IsFalse(scene.ObservingCamera);
            yield return Until(() => State.Stage == ActionStage.Completed || State.Stage == ActionStage.Returning,
                "Lin should begin returning after the live experiment");

            var housing = World.CameraTarget.Find("Camera housing");
            if (housing) Assert.Greater(Quaternion.Angle(start, housing.rotation), 1f, "Housing should have panned");
        }

        [UnityTest]
        public IEnumerator ArrivalAloneStillDoesNotCountAsE1()
        {
            QueueInspect();
            yield return Until(() => State.Stage == ActionStage.Arrived, "Arrival only");
            for (int i = 0; i < 40; i++) scene.StepSimulation(.05f, Vector2.zero, Vector2.zero);
            Assert.IsEmpty(State.VerifiedEvidence);
            Assert.IsFalse(State.E1Verified);
        }
    }
}
#endif
