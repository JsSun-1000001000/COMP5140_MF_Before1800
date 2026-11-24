#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Before1800.PartB.Tests
{
    public sealed partial class PartBApartmentTests
    {
        [System.Serializable] sealed class ClockTurnBody { public ClockTurnContext context; }
        [System.Serializable] sealed class ClockTurnContext
        {
            public string[] observations, allowed_actions, known_targets, verified_evidence;
            public bool allow_trust_resolution;
        }

        ClockTurnContext PrepareClockTurn(PartCDialogue adapter)
        {
            scene.ToggleDialogue();
            Assert.IsTrue(scene.TryBeginModelRequest(out var ticket));
            // Read the exact JSON prepared by the production adapter without starting its HTTP coroutine.
            adapter.PrepareTurn(ticket, "刚才你看到了什么？");
            var context = JsonUtility.FromJson<ClockTurnBody>(adapter.LastPayload).context;
            Assert.IsTrue(scene.TryCancelModelRequest(ticket));
            scene.ToggleDialogue();
            return context;
        }

        [UnityTest]
        public IEnumerator ClockDialoguePayloadOnlySharesTheReplayAfterLinWitnessesIt()
        {
            var adapters = scene.GetComponents<PartCDialogue>();
            Assert.AreEqual(1, adapters.Length);
            var adapter = adapters[0];
            scene.State.TakeClockRemote();
            scene.State.FinishClockBaseline();
            scene.State.FinishClockReplay(true, false);
            Assert.IsTrue(scene.State.ClockSoloReplaySeen);
            var solo = PrepareClockTurn(adapter);
            Assert.IsFalse(solo.observations.Any(line => line.Contains("挂钟") || line.Contains("遥控器")));

            scene.State.FinishClockReplay(true, true);
            Assert.IsTrue(scene.State.ClockWitnessed);
            var shared = PrepareClockTurn(adapter);
            Assert.IsTrue(shared.observations.Any(line => line.Contains("三格") && line.Contains("遥控器")));
            CollectionAssert.AreEqual(solo.allowed_actions, shared.allowed_actions);
            CollectionAssert.Contains(shared.allowed_actions, "inspect");
            CollectionAssert.AreEqual(solo.known_targets, shared.known_targets);
            CollectionAssert.Contains(shared.known_targets, SessionState.BroadcastTarget);
            CollectionAssert.IsEmpty(shared.verified_evidence);
            Assert.IsTrue(shared.allow_trust_resolution);
            yield break;
        }

        void LookAtClue(Vector3 target)
        {
            var camera = World.ViewCamera.transform;
            var desired = (target - camera.position).normalized;
            var yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(camera.forward, Vector3.up),
                Vector3.ProjectOnPlane(desired, Vector3.up), Vector3.up);
            var pitch = (Mathf.Asin(desired.y) - Mathf.Asin(camera.forward.y)) * Mathf.Rad2Deg;
            scene.StepSimulation(Step, Vector2.zero, new Vector2(yaw, pitch) / 2.1f);
        }

        void WaitInResidence(float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int i = 0; i < frames; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
        }

        void TakeTableRemote()
        {
            Assert.IsNotNull(scene.Clock);
            WalkThroughRoom(new Vector3(3.65f, .02f, -.75f));
            WalkThroughRoom(new Vector3(1.85f, .02f, -1.12f));
            LookAtClue(scene.Clock.Props.RemoteTarget.position);
            var remote = scene.Clock.Props.RemoteTarget.GetComponentInParent<MeshRenderer>();
            Assert.IsTrue(remote && remote.enabled && remote.gameObject.activeInHierarchy);
            StringAssert.Contains("遥控", interactions.Hint);
            interactions.Use();
            Assert.IsTrue(scene.State.ClockRemoteTaken);
            Assert.IsFalse(remote.gameObject.activeInHierarchy);
        }

        Vector3 KitchenThreshold()
        {
            // Kitchen door opens to the east. Read the hinge instead of the rotating panel.
            return DoorNear(5.15f).Leaf.position + new Vector3(0f, .02f, .51f);
        }

        IEnumerator EnterKitchenForClock()
        {
            var door = DoorNear(5.15f);
            var threshold = KitchenThreshold();
            WalkThroughRoom(threshold - Vector3.right * 1.52f);
            if (!door.IsOpen) {
                UsePanel(door, true);
                yield return null;
                yield return null;
            }
            WalkStraight(threshold + Vector3.right * 1.52f);
            WalkThroughRoom(new Vector3(8.52f, .02f, 1.58f));
            LookAtClue(scene.Clock.Props.ClockTarget.position);
        }

        void StartClockFromView()
        {
            LookAtClue(scene.Clock.Props.ClockTarget.position);
            Assert.IsNotEmpty(interactions.Hint, "No clock interaction at " + World.Player.position);
            interactions.Use();
            Assert.IsTrue(scene.Clock.IsPlaying, "Facing the clock should start playback from exploration");
        }

        void CheckMainStoryUntouched()
        {
            var snapshot = scene.State.ReadSnapshot();
            CollectionAssert.IsEmpty(snapshot.verified_evidence);
            Assert.AreEqual("open", snapshot.concerns.C1);
            Assert.AreEqual("open", snapshot.concerns.C2);
            Assert.AreEqual("open", snapshot.concerns.C3);
            Assert.IsFalse(snapshot.escape_agreed);
            Assert.IsFalse(snapshot.route_confirmed);
            Assert.AreEqual("none", snapshot.ending);
        }

        void ChooseClockLine(string id)
        {
            Assert.IsTrue(interactions.Choices.Any(c => c.Id == id), "Missing choice " + id + " in " + interactions.Line);
            interactions.Choose(id);
        }

        IEnumerator LearnClockAlone()
        {
            TakeTableRemote();
            yield return EnterKitchenForClock();
            StartClockFromView();
            WaitInResidence(ClockRehearsalProps.Duration + .2f);
            Assert.IsTrue(scene.State.ClockBaselineSeen);
            Assert.IsFalse(scene.State.ClockSoloReplaySeen, "The first observation is not the player's repeat experiment");
            StartClockFromView();
            WaitInResidence(1.55f);
            var hand = scene.Clock.Props.SecondsHand;
            var firstPhrase = hand.localRotation;
            WaitInResidence(.70f);
            Assert.Greater(Quaternion.Angle(firstPhrase, hand.localRotation), 4f, "The real hand should tick forward");
            WaitInResidence(2.30f);
            Assert.Less(Quaternion.Angle(firstPhrase, hand.localRotation), .1f, "The second three-beat phrase should repeat the first");
            WaitInResidence(1.65f);
            Assert.IsTrue(scene.State.ClockSoloReplaySeen);
            Assert.IsFalse(scene.State.ClockWitnessed);
            Assert.IsFalse(scene.Clock.CueVisible);
            Assert.IsNull(scene.State.ReadSnapshot().clock_rehearsal, "Private player observations must stay out of Lin's facts");
            StringAssert.Contains("少走的一秒", interactions.JournalText);
            CheckMainStoryUntouched();
        }

        [UnityTest]
        public IEnumerator ClockRehearsalPausesThePhysicalHandAndRestartsWithTheRemoteBackOnTheTable()
        {
            TakeTableRemote();
            yield return EnterKitchenForClock();
            StartClockFromView();
            WaitInResidence(1.6f);
            var hand = scene.Clock.Props.SecondsHand;
            Assert.IsNotNull(hand);
            var pose = hand.localRotation;
            var time = scene.State.ElapsedGameSeconds;
            interactions.ToggleJournal();
            WaitInResidence(ClockRehearsalProps.Duration + .2f);
            Assert.AreEqual(time, scene.State.ElapsedGameSeconds);
            Assert.Less(Quaternion.Angle(pose, hand.localRotation), .01f);
            Assert.IsTrue(scene.Clock.IsPlaying);
            Assert.IsFalse(scene.State.ClockBaselineSeen);
            interactions.Back();
            WaitInResidence(ClockRehearsalProps.Duration + .2f);
            Assert.IsTrue(scene.State.ClockBaselineSeen);
            StartClockFromView();
            WaitInResidence(.7f);
            var previousRun = scene.State.RunId;
            scene.Restart();
            interactions = scene.GetComponent<GreyboxHUD>().Flow;
            Assert.AreNotEqual(previousRun, scene.State.RunId);
            Assert.IsFalse(scene.Clock.IsPlaying);
            Assert.IsFalse(scene.Clock.CueVisible);
            Assert.IsFalse(scene.State.ClockBaselineSeen || scene.State.ClockSoloReplaySeen || scene.State.ClockRemoteTaken ||
                scene.State.ClockInvited || scene.State.ClockWitnessed);
            Assert.IsNull(scene.State.ReadSnapshot().clock_rehearsal);
            for(int page=0;page<4;page++) interactions.Begin();
            TakeTableRemote();
            CheckMainStoryUntouched();
        }

        IEnumerator DiscussClockWithLin(string answer, string expectedSnapshot, bool interruptFirst)
        {
            yield return LearnClockAlone();
            var threshold = KitchenThreshold();
            WalkThroughRoom(threshold + Vector3.right * 1.52f);
            WalkStraight(threshold - Vector3.right * 1.52f);
            LookAtClue(World.Lin.position + Vector3.up * 1.38f);
            interactions.Use();
            Assert.AreEqual(PlayerScreen.Talk, interactions.Screen);
            yield return null;
            var buttons = Object.FindObjectsOfType<Button>().Where(b => b.name.StartsWith("DialogueChoice")).ToArray();
            CollectionAssert.AreEquivalent(new[] { "camera", "tv", "honesty", "clock", "company" },
                interactions.Choices.Select(c => c.Id).ToArray());
            Assert.AreEqual(interactions.Choices.Count, buttons.Count(b => b.isActiveAndEnabled && b.interactable));
            ChooseClockLine("clock");
            Assert.IsFalse(interactions.Choices.Any(c => c.Id == "clock_truth" || c.Id == "clock_evade"));
            ChooseClockLine("clock_invite");
            Assert.IsTrue(scene.State.ClockInvited);
            Assert.IsTrue(scene.State.ReadSnapshot().clock_rehearsal.invited_to_watch);
            Assert.IsFalse(scene.State.ReadSnapshot().clock_rehearsal.witnessed_three_ticks);
            yield return EnterKitchenForClock();
            for (int i = 0; i < 700 && (!scene.Clock.Props.CanWitness(World.Lin) ||
                 Vector3.Distance(World.Player.position, World.Lin.position) > 1.58f); i++)
                scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.AreEqual(ActionStage.Following, scene.State.Stage, scene.Feedback);
            Assert.IsTrue(scene.Clock.Props.CanWitness(World.Lin), "Lin could not see the clock from " + World.Lin.position);

            if (interruptFirst)
            {
                StartClockFromView();
                WaitInResidence(.8f);
                var partition = GameObject.CreatePrimitive(PrimitiveType.Cube);
                partition.transform.position = Vector3.Lerp(World.Lin.position + Vector3.up * 1.55f,
                    scene.Clock.Props.ClockTarget.position, .18f);
                partition.transform.localScale = new Vector3(.12f, 1.25f, .85f);
                Physics.SyncTransforms();
                try
                {
                    Assert.IsFalse(scene.Clock.Props.CanWitness(World.Lin));
                    WaitInResidence(.4f);
                }
                finally { Object.DestroyImmediate(partition); }
                Physics.SyncTransforms();
                WaitInResidence(ClockRehearsalProps.Duration + .2f);
                Assert.IsFalse(scene.State.ClockWitnessed, "Interrupted sight must not count as a shared observation");
                Assert.IsFalse(scene.Clock.CueVisible);
            }

            StartClockFromView();
            WaitInResidence(ClockRehearsalProps.Duration + .2f);
            Assert.IsTrue(scene.State.ClockWitnessed);
            Assert.IsTrue(scene.Clock.CueVisible);
            var cue = scene.Clock.Props.TvTarget.GetComponentsInChildren<Text>();
            CollectionAssert.IsSubsetOf(new[] { "重来", "从上一句接" }, cue.Select(label => label.text).ToArray());
            Assert.IsTrue(scene.State.ReadSnapshot().clock_rehearsal.witnessed_three_ticks);
            LookAtClue(World.Lin.position + Vector3.up * 1.38f);
            interactions.Use();
            Assert.AreEqual(PlayerScreen.Talk, interactions.Screen);
            ChooseClockLine(answer);
            bool disclosed = answer == "clock_truth";
            StringAssert.Contains(disclosed ? "排练" : "猜的", interactions.Line);
            ChooseClockLine("clock_after");
            StringAssert.Contains(disclosed ? "这次我来按" : "我还记得那颗键", interactions.Line);
            var snapshot = scene.State.ReadSnapshot().clock_rehearsal;
            Assert.AreEqual(expectedSnapshot, snapshot.answer_about_remote);
            Assert.IsTrue(snapshot.witnessed_three_ticks);
            ModelRequest request = null;
            scene.PlayerLineRequested += (message, _) => request = message;
            interactions.Send("你还记得我们刚才说了什么吗？");
            Assert.IsNotNull(request);
            Assert.AreEqual(expectedSnapshot, request.Snapshot.clock_rehearsal.answer_about_remote);
            Assert.IsTrue(request.Snapshot.clock_rehearsal.witnessed_three_ticks);
            interactions.Back();
            if (disclosed)
            {
                interactions.Back();
                Assert.AreEqual(PlayerScreen.Explore, interactions.Screen);
                WalkThroughRoom(new Vector3(8.45f, .02f, .55f));
                WalkThroughRoom(threshold + Vector3.right * 1.52f);
                WalkStraight(threshold - Vector3.right * 1.52f);
                WalkThroughRoom(new Vector3(3.65f, .02f, -.75f));
                WalkThroughRoom(new Vector3(1.65f, .02f, -2.35f));
                LookAtClue(scene.Clock.Props.TvTarget.position);
                Assert.Less(Vector3.Distance(World.Tv.Screen.position, scene.Clock.Props.TvTarget.position), .03f);
                Assert.IsTrue(World.Tv.PlayerCanOperate(World.ViewCamera.transform));
                Assert.IsTrue(World.Tv.TryRegisterPrediction(TvProgram.News, out _));
                WaitInResidence(25f);
                Assert.IsFalse(scene.Clock.CueVisible);
                World.Tv.ResetStation();
                WaitInResidence(.1f);
                StringAssert.Contains("重拍", interactions.Hint);
                interactions.Use();
                Assert.IsTrue(scene.State.ClockCardRead);
                Assert.IsFalse(scene.State.CameraDiscovered);
                StringAssert.Contains("客厅电视", interactions.JournalText);
                StringAssert.Contains("「重来」「从上一句接」", interactions.JournalText);
            }
            CheckMainStoryUntouched();
        }

        [UnityTest]
        public IEnumerator ClockRehearsalRequiresUninterruptedSightBeforeThePlayerCanAdmitRehearsing()
            => DiscussClockWithLin("clock_truth", "admitted_rehearsal", true);

        [UnityTest]
        public IEnumerator ClockRehearsalRecordsAnEvasiveAnswerWithoutGrantingTrustOrAnEnding()
            => DiscussClockWithLin("clock_evade", "evaded", false);
    }
}
#endif
