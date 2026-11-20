#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Before1800.PartB.Tests
{
    public sealed class PartBResidencePlayModeTests
    {
        const string Residence = "Assets/Before1800Home/PartB_Residence.unity";
        PartBScene scene;
        Animator animator;
        float previousStep;
        GreyboxWorld World => scene.World;

        [UnitySetUp]
        public IEnumerator OpenResidence()
        {
            previousStep = Time.captureDeltaTime;
            Time.captureDeltaTime = 0.04f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Residence, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            Assert.IsNotNull(scene);
            Assert.AreEqual(Residence, scene.gameObject.scene.path);
            Assert.IsTrue(scene.IntroOpen);
            Assert.IsTrue(World.UsesSceneBindings);
            Assert.IsNotNull(World.NavigationData);
            animator = World.Lin.GetComponentInChildren<Animator>();
            Assert.IsNotNull(animator, "Lin needs the residence character's animator");
            Assert.IsFalse(animator.applyRootMotion, "The Part B controller owns Lin's position");
            scene.ToggleIntroOff();
        }

        [TearDown]
        public void RestoreTimeStep() => Time.captureDeltaTime = previousStep;

        IEnumerator WaitFor(Func<bool> check, string expectation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (!check() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(check(), expectation + " / " + scene.State.Stage + " / " + scene.Feedback);
        }

        ActionTicket Order(string action)
        {
            scene.ToggleDialogue();
            scene.SubmitTestAction(action);
            var ticket = scene.State.Pending;
            Assert.IsNotNull(ticket, scene.Feedback);
            scene.ToggleDialogue();
            return ticket;
        }

        [UnityTest]
        public IEnumerator MitaPlaysOriginalIdleAndWalkClipsOnMovingBones()
        {
            var skin = World.Lin.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.IsTrue(Array.Exists(skin, r => r.enabled && r.sharedMesh && r.bones.Length > 5),
                "An active, rigged Mita mesh must be present on Lin");
            var arm = animator.transform.Find("Armature/Hips/Spine/Chest/Right shoulder/Right arm");
            Assert.IsNotNull(arm, "The original Mita arm must match the clip binding");

            const string idleAsset = "Assets/Before1800Home/CharacterArt/Residence Idle.anim";
            const string walkAsset = "Assets/AnimationClip/Walk With Controller.anim";
            yield return WaitFor(() => PlayingClip(idleAsset), "Lin should start in the original idle clip");
            Order("inspect");
            yield return WaitFor(() => PlayingClip(walkAsset), "The original walk clip should play as Lin moves");
            Quaternion oldPose = arm.localRotation;
            Vector3 oldPosition = World.Lin.position;
            yield return WaitFor(() => Quaternion.Angle(oldPose, arm.localRotation) > 1f &&
                Vector3.Distance(oldPosition, World.Lin.position) > .06f, "Lin must move and articulate the arm on the walk");
            yield return WaitFor(() => scene.State.Stage == ActionStage.Arrived && PlayingClip(idleAsset),
                "Lin should idle again at the actual observation point");
        }

        bool PlayingClip(string path)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            return Array.Exists(clips, c => c.clip && c.weight > .5f && AssetDatabase.GetAssetPath(c.clip) == path);
        }

        [UnityTest]
        public IEnumerator SkirtHemDeformsRelativeToHipsDuringWalking()
        {
            var skirt = animator.transform.Find("Skirt").GetComponent<SkinnedMeshRenderer>();
            var hips = animator.transform.Find("Armature/Hips");
            var mesh = new Mesh();
            try
            {
                skirt.BakeMesh(mesh);
                var rest = mesh.vertices;
                var height = new float[rest.Length];
                float bottom = float.MaxValue, top = float.MinValue;
                for (int i = 0; i < rest.Length; i++)
                {
                    Vector3 world = skirt.transform.TransformPoint(rest[i]);
                    height[i] = world.y;
                    bottom = Mathf.Min(bottom, world.y);
                    top = Mathf.Max(top, world.y);
                    rest[i] = Quaternion.Inverse(hips.rotation) * (world - hips.position);
                }
                Order("inspect");
                float maxHemTravel = 0f;
                for (int frame = 0; frame < 150 && scene.State.Stage != ActionStage.Arrived; frame++)
                {
                    yield return null;
                    skirt.BakeMesh(mesh);
                    var vertices = mesh.vertices;
                    var inverseHip = Quaternion.Inverse(hips.rotation);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        if (height[i] > Mathf.Lerp(bottom, top, .18f)) continue;
                        var point = inverseHip * (skirt.transform.TransformPoint(vertices[i]) - hips.position);
                        maxHemTravel = Mathf.Max(maxHemTravel, Vector3.Distance(point, rest[i]));
                    }
                }
                Assert.Greater(maxHemTravel, .03f, "The hem stayed rigid on the hips while the legs walked");
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }

        [UnityTest]
        public IEnumerator InspectionTurnsGraduallyAndWalksAtRoomPace()
        {
            var previousPosition = World.Lin.position;
            var previousRotation = World.Lin.rotation;
            float greatestSpeed = 0f;
            float accumulatedTurn = 0f;
            Order("inspect");
            for (int frame = 0; frame < 250 && scene.State.Stage != ActionStage.Arrived; frame++)
            {
                yield return null;
                float dt = Time.deltaTime;
                if (dt <= 0f) continue;
                var displacement = Vector3.ProjectOnPlane(World.Lin.position - previousPosition, Vector3.up);
                float yaw = Quaternion.Angle(previousRotation, World.Lin.rotation);
                Assert.LessOrEqual(yaw, 180f * dt + .1f, "Lin snapped to a new heading");
                greatestSpeed = Mathf.Max(greatestSpeed, displacement.magnitude / dt);
                accumulatedTurn += yaw;
                previousPosition = World.Lin.position;
                previousRotation = World.Lin.rotation;
            }
            Assert.AreEqual(ActionStage.Arrived, scene.State.Stage);
            Assert.Greater(accumulatedTurn, 80f, "The route must exercise the turn before walking");
            Assert.That(greatestSpeed, Is.InRange(1.04f, 1.15f));
        }

        [UnityTest]
        public IEnumerator BothRenderedShoesAlternateAndPlantDuringRealTravel()
        {
            var shoes = animator.transform.Find("Shoes").GetComponent<SkinnedMeshRenderer>();
            var baked = new Mesh();
            try
            {
                shoes.BakeMesh(baked);
                var neutral = baked.vertices;
                var onLeft = Array.ConvertAll(neutral, v =>
                    World.Lin.InverseTransformPoint(shoes.transform.TransformPoint(v)).x < 0f);
                Order("inspect");
                Vector3 lastRoot = World.Lin.position;
                Quaternion lastRotation = World.Lin.rotation;
                Vector3[] previous = null;
                int straightFrames = 0, plantedFrames = 0;
                float nearest = float.PositiveInfinity, highestSupport = float.NegativeInfinity;
                float leftLead = 0f, rightLead = 0f;
                var chest = animator.transform.Find("Armature/Hips/Spine/Chest");
                var leftWrist = chest.Find("Left shoulder/Left arm/Left elbow/Left wrist");
                var rightWrist = chest.Find("Right shoulder/Right arm/Right elbow/Right wrist");
                Assert.IsTrue(leftWrist && rightWrist);
                float leftHandLead = 0f, rightHandLead = 0f;
                for (int frame = 0; frame < 250 && scene.State.Stage != ActionStage.Arrived; frame++)
                {
                    yield return null;
                    shoes.BakeMesh(baked);
                    var verts = baked.vertices;
                    var centres = new Vector3[2];
                    var soles = new[] { float.PositiveInfinity, float.PositiveInfinity };
                    var counts = new int[2];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        int foot = onLeft[i] ? 0 : 1;
                        var point = shoes.transform.TransformPoint(verts[i]);
                        centres[foot] += point;
                        soles[foot] = Mathf.Min(soles[foot], point.y - World.Lin.position.y);
                        counts[foot]++;
                    }
                    Assert.That(Mathf.Min(counts[0], counts[1]), Is.GreaterThan(100));
                    for (int foot = 0; foot < 2; foot++) centres[foot] /= counts[foot];
                    var rootDelta = Vector3.ProjectOnPlane(World.Lin.position - lastRoot, Vector3.up);
                    if (previous != null && rootDelta.magnitude > Time.deltaTime * .98f &&
                        Quaternion.Angle(World.Lin.rotation, lastRotation) < 1f)
                    {
                        straightFrames++;
                        float separation = Vector3.Dot(centres[0] - centres[1], World.Lin.forward);
                        leftLead = Mathf.Max(leftLead, separation);
                        rightLead = Mathf.Max(rightLead, -separation);
                        float handSeparation = Vector3.Dot(leftWrist.position - rightWrist.position, World.Lin.forward);
                        leftHandLead = Mathf.Max(leftHandLead, handSeparation);
                        rightHandLead = Mathf.Max(rightHandLead, -handSeparation);
                        float low = Mathf.Min(soles[0], soles[1]);
                        nearest = Mathf.Min(nearest, low);
                        highestSupport = Mathf.Max(highestSupport, low);
                        for (int foot = 0; foot < 2; foot++)
                        {
                            float drift = Vector3.ProjectOnPlane(centres[foot] - previous[foot], Vector3.up).magnitude;
                            if (soles[foot] < .04f && drift < rootDelta.magnitude * .50f)
                            {
                                plantedFrames++;
                                break;
                            }
                        }
                    }
                    previous = centres;
                    lastRoot = World.Lin.position;
                    lastRotation = World.Lin.rotation;
                }
                Debug.Log($"[Residence gait] samples={straightFrames}, planted={plantedFrames}, " +
                    $"left/right lead={leftLead:F3}/{rightLead:F3}m, hands={leftHandLead:F3}/{rightHandLead:F3}m, " +
                    $"support height={nearest:F3}..{highestSupport:F3}m");
                Assert.Greater(straightFrames, 25);
                Assert.Greater(Mathf.Min(leftLead, rightLead), .16f, "Each shoe must pass the other");
                Assert.Greater(Mathf.Min(leftHandLead, rightHandLead), .075f, "Each arm must swing forward in turn");
                Assert.GreaterOrEqual(nearest, -.035f, "The supporting shoe penetrates the floor");
                Assert.LessOrEqual(highestSupport, .045f, "Both shoes float above the actor's ground");
                Assert.Greater(plantedFrames, straightFrames * .4f, "The shoes slide with the body");
            }
            finally { UnityEngine.Object.Destroy(baked); }
        }

        [UnityTest]
        public IEnumerator MitaFollowsStopsAndHoldsHerPoseWhileDialogueIsOpen()
        {
            var start = World.Lin.position;
            var initialDistance = Vector3.Distance(World.Player.position, start);
            Order("follow");
            yield return WaitFor(() => Vector3.Distance(World.Player.position, World.Lin.position) < initialDistance - .15f,
                "Lin should approach the player in the residence");
            scene.ToggleDialogue();
            var clock = scene.State.ElapsedGameSeconds;
            var position = World.Lin.position;
            var bone = animator.transform.Find("Armature/Hips/Spine");
            Assert.IsNotNull(bone);
            yield return null;
            var pose = bone.localRotation;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(Quaternion.Angle(pose, bone.localRotation), .1f, "The character's pose should freeze with dialogue");
            Assert.AreEqual(position, World.Lin.position);
            Assert.AreEqual(clock, scene.State.ElapsedGameSeconds);
            scene.SubmitTestAction("stop");
            scene.ToggleDialogue();
            yield return WaitFor(() => scene.State.Stage == ActionStage.Completed, "The stop order should finish");
            position = World.Lin.position;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(position, World.Lin.position);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
        }

        [UnityTest]
        public IEnumerator HomeCameraRequiresActualArrivalAndSeparateCompletionEachRun()
        {
            Assert.IsTrue(World.CanReachObservation, "The original residential floor must have a complete navigation path");
            var start = World.Lin.position;
            var ticket = Order("inspect");
            Assert.IsFalse(scene.TryCompleteCameraExperiment(ticket, Guid.NewGuid()));
            yield return WaitFor(() => scene.State.Stage == ActionStage.Arrived, "Lin should arrive in front of the camera");
            Assert.Greater(Vector3.Distance(start, World.Lin.position), .5f);
            Assert.IsTrue(World.IsAtObservation);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            var completed = Guid.NewGuid();
            Assert.IsTrue(scene.TryCompleteCameraExperiment(ticket, completed));
            Assert.IsFalse(scene.TryCompleteCameraExperiment(ticket, completed));
            yield return WaitFor(() => scene.State.Stage == ActionStage.Completed, "The actor should walk home after the device event");
            CollectionAssert.AreEqual(new[] { "E1" }, scene.State.VerifiedEvidence);
            Assert.Less(Vector3.Distance(start, World.Lin.position), .65f);
            scene.Restart();
            Assert.IsTrue(scene.IntroOpen);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            Assert.AreEqual("17:00:00", scene.State.TimeHms);
            Assert.IsFalse(scene.TryCompleteCameraExperiment(ticket, Guid.NewGuid()));
        }
    }
}
#endif
