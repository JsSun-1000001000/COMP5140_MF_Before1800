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
    public class PartBTvTests
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

        private void FaceTv()
        {
            var view = World.ViewCamera.transform;
            Vector3 delta = World.Tv.Screen.position - view.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < .0001f) return;
            World.Player.rotation = Quaternion.LookRotation(delta.normalized);
            scene.StepSimulation(.02f, Vector2.zero, Vector2.zero);
        }

        private void WalkNearTv()
        {
            Vector3 goal = World.Tv.ObservationPoint.position +
                (World.Player.position - World.Tv.ObservationPoint.position).normalized * .35f;
            goal.y = World.Player.position.y;
            for (int i = 0; i < 160; i++)
            {
                Vector3 remaining = goal - World.Player.position;
                remaining.y = 0;
                if (remaining.magnitude < .1f) break;
                Vector3 direction = World.Player.InverseTransformDirection(remaining.normalized);
                scene.StepSimulation(.03f, new Vector2(direction.x, direction.z), Vector2.zero);
            }
            FaceTv();
        }

        private ActionTicket QueueTvInspect()
        {
            scene.ToggleDialogue();
            Assert.IsTrue(State.TryQueue("inspect", SessionState.BroadcastTarget, out var reason), reason);
            var ticket = State.Pending;
            Assert.IsNotNull(ticket);
            scene.ToggleDialogue();
            return ticket;
        }

        [UnityTest]
        public IEnumerator Example1_NewsThenWeatherWritesE4()
        {
            Assert.IsNotNull(World.Tv);
            Assert.IsTrue(scene.TryRegisterTvPrediction(TvProgram.News, out _));
            Assert.AreEqual(TvPhase.Predicted, World.Tv.Phase);

            var ticket = QueueTvInspect();
            yield return Until(() => State.Stage == ActionStage.Arrived && State.Active == ticket, "Lin reaches TV");
            Assert.IsEmpty(State.VerifiedEvidence);

            WalkNearTv();
            scene.InteractWithBroadcast();
            Assert.AreEqual(TvPhase.Running, World.Tv.Phase, scene.Feedback);
            Assert.IsFalse(State.E4Verified);

            scene.InteractWithBroadcast();
            Assert.AreEqual(TvPhase.Step1Matched, World.Tv.Phase, scene.Feedback);
            Assert.IsFalse(State.E4Verified);

            scene.InteractWithBroadcast();
            Assert.IsTrue(State.E4Verified, scene.Feedback);
            CollectionAssert.AreEqual(new[] { "E4" }, State.VerifiedEvidence);
            Assert.AreEqual(TvProgram.Weather, World.Tv.DisplayedProgram);
        }

        [UnityTest]
        public IEnumerator Example3_WrongRequestFailsThenRetrySucceeds()
        {
            Assert.IsNotNull(World.Tv);
            Assert.IsTrue(scene.TryRegisterTvPrediction(TvProgram.Weather, out _));
            var ticket = QueueTvInspect();
            yield return Until(() => State.Stage == ActionStage.Arrived && State.Active == ticket, "Lin reaches TV");
            WalkNearTv();
            scene.InteractWithBroadcast();
            Assert.AreEqual(TvPhase.Running, World.Tv.Phase);

            Assert.IsFalse(scene.TryLinRequestTvShow(TvProgram.News, out var failReason), failReason);
            Assert.AreEqual(TvPhase.Failed, World.Tv.Phase);
            Assert.IsFalse(State.E4Verified);

            Assert.IsTrue(scene.TryRegisterTvPrediction(TvProgram.Weather, out _));
            scene.InteractWithBroadcast();
            Assert.AreEqual(TvPhase.Running, World.Tv.Phase, scene.Feedback);
            Assert.IsTrue(scene.TryLinRequestTvShow(TvProgram.Weather, out _), scene.Feedback);
            Assert.AreEqual(TvPhase.Step1Matched, World.Tv.Phase);
            scene.InteractWithBroadcast();
            Assert.IsTrue(State.E4Verified, scene.Feedback);
            CollectionAssert.Contains(State.VerifiedEvidence, "E4");
        }

        [UnityTest]
        public IEnumerator CannotBackfillPredictionAfterStart()
        {
            Assert.IsTrue(scene.TryRegisterTvPrediction(TvProgram.Cartoon, out _));
            var ticket = QueueTvInspect();
            yield return Until(() => State.Stage == ActionStage.Arrived && State.Active == ticket, "Lin reaches TV");
            WalkNearTv();
            scene.InteractWithBroadcast();
            Assert.IsFalse(scene.TryRegisterTvPrediction(TvProgram.News, out var reason), reason);
            StringAssert.Contains("不能再改预测", reason);
            Assert.IsFalse(State.E4Verified);
        }
    }
}
#endif
