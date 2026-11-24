#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Before1800.PartB.Tests
{
    public sealed partial class PartBApartmentTests
    {
        const float Step = .02f;
        PartBScene scene;
        PlayerInteraction interactions;
        GreyboxWorld World => scene.World;

        [UnitySetUp]
        public IEnumerator EnterResidence()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            scene = Object.FindObjectOfType<PartBScene>();
            scene.AcceptHardwareInput=false;
            // These geometry tests use the explicit local rehearsal, never the paid model service.
            scene.GetComponent<PartCDialogue>().SetLive(false);
            interactions = scene.GetComponent<GreyboxHUD>().Flow;
            scene.ToggleIntroOff();
        }

        ApartmentDoor DoorNear(float hingeX)
        {
            var matching = World.GetComponentsInChildren<ApartmentDoor>()
                .Where(d => d.Leaf && Mathf.Abs(d.Leaf.position.x - hingeX) < .3f).ToArray();
            Assert.AreEqual(1, matching.Length, "Expected a hinged interior door at x=" + hingeX);
            Assert.IsNotNull(matching[0].Panel);
            return matching[0];
        }

        void Move(Vector3 direction)
        {
            var local = World.Player.InverseTransformDirection(direction.normalized);
            scene.StepSimulation(Step, new Vector2(local.x, local.z), Vector2.zero);
        }

        void WalkStraight(Vector3 goal)
        {
            for (int i = 0; i < 220; i++)
            {
                var remaining = Vector3.ProjectOnPlane(goal - World.Player.position, Vector3.up);
                if (remaining.magnitude < .09f) return;
                Move(remaining);
            }
            Assert.Fail("Player stopped at " + World.Player.position + " while walking to " + goal);
        }

        void WalkThroughRoom(Vector3 goal)
        {
            Assert.IsTrue(NavMesh.SamplePosition(World.Player.position, out var from, .6f, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(goal, out var to, .5f, NavMesh.AllAreas), "No navigation at " + goal);
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path));
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "Incomplete route to " + goal);
            foreach (var corner in path.corners) WalkStraight(corner);
            WalkStraight(goal);
            Assert.That(World.Player.position.y, Is.InRange(-.15f, .35f), "Player needs a floor beneath this room.");
        }

        void FaceWithMouse(Vector3 heading)
        {
            float yaw = Vector3.SignedAngle(World.Player.forward, heading, Vector3.up);
            scene.StepSimulation(Step, Vector2.zero, new Vector2(yaw / 2.1f, 0));
        }

        void CheckDoorPrompt(string expected)
        {
            var view = scene.GetComponent<PlayerUIView>();
            var plate = view.transform.Find("PlayerUI/Exploration/WorldPrompt");
            Assert.IsNotNull(plate);
            Assert.AreEqual(expected, interactions.Hint, "Target lost at " + World.Player.position);
            Assert.AreEqual(expected != "", plate.gameObject.activeInHierarchy, "The exploration HUD hid the door action");
            if (expected == "") return;
            Canvas.ForceUpdateCanvases();
            var action = plate.Find("Action").GetComponent<Text>();
            var key = plate.Find("Use key/Key").GetComponent<Text>();
            Assert.AreEqual(expected, action.text);
            Assert.AreEqual("E", key.text);
            foreach (var label in new[] { key, action })
            {
                Assert.IsTrue(label.isActiveAndEnabled && label.canvas.isActiveAndEnabled);
                Assert.Greater(label.color.a * label.canvasRenderer.GetInheritedAlpha(), .5f);
                var canvasRect = (RectTransform)label.canvas.transform;
                var center = label.rectTransform.TransformPoint(label.rectTransform.rect.center);
                Assert.IsTrue(canvasRect.rect.Contains(canvasRect.InverseTransformPoint(center)), label.name + " is off-screen");
            }
        }

        [UnityTest]
        public IEnumerator ApartmentDoorPromptRemainsVisibleWhenStrafingStopsAtTheJamb()
        {
            var door = DoorNear(5.15f);
            var center = door.Panel.bounds.center;
            WalkThroughRoom(new Vector3(center.x - 1.45f, .02f, center.z + .39f));
            FaceWithMouse(Vector3.right);
            yield return null;
            yield return null;
            CheckDoorPrompt("开门");
            // Cross the last few centimetres of the panel without turning the camera.
            for (int i = 0; i < 3; i++) { Move(Vector3.forward); yield return null; }
            float endZ = World.Player.position.z;
            Assert.Greater(endZ, door.Panel.bounds.max.z + .02f, "The aim ray should end just beyond the door leaf");
            for (int i = 0; i < 8; i++)
            {
                scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
                yield return null;
                CheckDoorPrompt("开门");
            }
            Assert.AreEqual(endZ, World.Player.position.z, .01f);
            interactions.Use();
            Assert.IsTrue(door.WantsOpen, "E should operate the door from the same resting pose as the prompt");
        }

        [UnityTest]
        public IEnumerator ApartmentDoorPromptAndUseStayAtTheDoorwayDuringAStationaryOpenCloseCycle()
        {
            var door = DoorNear(5.15f);
            Vector3 center = door.Panel.bounds.center;
            WalkThroughRoom(new Vector3(center.x - 1.65f, .02f, center.z));
            FaceWithMouse(Vector3.right);
            for (int i = 0; i < 5; i++) { Move(Vector3.right); yield return null; }
            yield return null;
            CheckDoorPrompt("开门");
            Vector3 atRest = World.Player.position;
            var aim = World.ViewCamera.transform.rotation;
            foreach (bool open in new[] { true, false, true })
            {
                interactions.Use();
                Assert.AreEqual(open, door.WantsOpen);
                // No new mouse or movement input; the moving leaf leaves the original sight ray.
                for (int i = 0; i < 58; i++)
                {
                    scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
                    yield return null;
                    if (i > 0) CheckDoorPrompt(open ? "关门" : "开门");
                }
                Assert.AreEqual(open, door.IsOpen);
            }
            Assert.Less(Vector3.Distance(atRest, World.Player.position), .02f);
            Assert.Less(Quaternion.Angle(aim, World.ViewCamera.transform.rotation), .1f);
            FaceWithMouse(Vector3.back);
            yield return null;
            yield return null;
            CheckDoorPrompt("");
            interactions.Use();
            Assert.IsTrue(door.WantsOpen, "Turning away should release door focus");
        }

        [UnityTest]
        public IEnumerator ApartmentDoorPromptRespectsReachAndOcclusionWhenLookingWithoutWalking()
        {
            var door = DoorNear(5.15f);
            Vector3 center = door.Panel.bounds.center;
            WalkStraight(new Vector3(2.65f, .02f, 2.60f));
            var directionToDoor = Vector3.ProjectOnPlane(center - World.Player.position, Vector3.up);
            Assert.Greater(directionToDoor.magnitude, 3f);
            FaceWithMouse(directionToDoor);
            yield return null;
            yield return null;
            CheckDoorPrompt("");
            interactions.Use();
            Assert.IsFalse(door.WantsOpen, "E should not operate a distant door");

            var approach = new Vector3(center.x - 1.48f, .02f, center.z);
            WalkThroughRoom(approach);
            FaceWithMouse(Vector3.right);
            for (int i = 0; i < 30; i++) Move(Vector3.right);
            FaceWithMouse(Vector3.back);
            yield return null;
            yield return null;
            CheckDoorPrompt("");
            // No walking input: look back at eye height while resting directly against the leaf.
            FaceWithMouse(Vector3.right);
            for (int i = 0; i < 6; i++)
            {
                scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
                yield return null;
                if (i > 0) CheckDoorPrompt("开门");
            }

            WalkStraight(approach);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Opaque test partition between the player and the kitchen door";
            blocker.transform.position = new Vector3(center.x - .7f, 1.25f, center.z);
            blocker.transform.localScale = new Vector3(.15f, 2.5f, 2.3f);
            Physics.SyncTransforms();
            try
            {
                yield return null;
                yield return null;
                CheckDoorPrompt("");
                interactions.Use();
                Assert.IsFalse(door.WantsOpen, "E should not operate the door through a solid partition");
            }
            finally { Object.DestroyImmediate(blocker); }
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            CheckDoorPrompt("开门");
        }

        void PressUseOnPanel(ApartmentDoor door, bool open)
        {
            World.ViewCamera.transform.LookAt(door.Panel.transform.TransformPoint(door.Panel.center));
            Assert.AreEqual(open ? "开门" : "关门", interactions.Hint, "Door not reachable from " + World.Player.position);
            interactions.Use();
        }

        void UsePanel(ApartmentDoor door, bool open)
        {
            PressUseOnPanel(door, open);
            for (int i = 0; i < 100 && door.IsMoving; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.IsFalse(door.IsMoving, door.name + " did not finish swinging");
            Assert.AreEqual(open, door.IsOpen, door.name);
        }

        void EnterAndClose(ApartmentDoor door, Vector3 direction)
        {
            var threshold = door.Panel.transform.TransformPoint(door.Panel.center);
            threshold.y = .02f;
            var approach = threshold - direction * 1.52f;
            WalkThroughRoom(approach);
            for (int i = 0; i < 36; i++) Move(direction);
            Assert.That(Vector3.Dot(World.Player.position - threshold, direction), Is.InRange(-.72f, -.10f),
                "A closed " + door.name + " must stop the player at the leaf");
            WalkStraight(approach);
            UsePanel(door, true);
            WalkStraight(threshold + direction * 1.52f);
            UsePanel(door, false);
            for (int i = 0; i < 36; i++) Move(-direction);
            Assert.That(Vector3.Dot(World.Player.position - threshold, direction), Is.InRange(.10f, .72f),
                "The closed leaf must block the return journey too");
            WalkStraight(threshold + direction * 1.52f);
        }

        void ReturnThrough(ApartmentDoor door, Vector3 direction)
        {
            var threshold = door.Panel.transform.TransformPoint(door.Panel.center);
            threshold.y = .02f;
            WalkThroughRoom(threshold - direction * 1.52f);
            UsePanel(door, true);
            WalkStraight(threshold + direction * 1.52f);
        }

        [Test]
        public void ApartmentKitchenDoorWorksInBothDirectionsAndResetsOnRestart()
        {
            var door = DoorNear(5.15f);
            var closedPose = door.Leaf.localRotation;
            EnterAndClose(door, Vector3.right);
            WalkThroughRoom(new Vector3(7.7f, .02f, .8f));
            ReturnThrough(door, Vector3.left);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            Assert.IsFalse(scene.State.RouteConfirmed);
            PressUseOnPanel(door, false);
            for (int i = 0; i < 5; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.IsTrue(door.IsMoving, "Restart should also interrupt a partly closed leaf");
            scene.Restart();
            Assert.IsFalse(door.IsOpen);
            Assert.IsFalse(door.IsMoving);
            Assert.Less(Quaternion.Angle(closedPose, door.Leaf.localRotation), .1f);
        }

        [Test]
        public void ApartmentBedroomAndBathroomCanBeReachedAndLeftOnFoot()
        {
            var bedroom = DoorNear(-4.15f);
            var bathroom = DoorNear(-11.15f);
            EnterAndClose(bedroom, Vector3.left);
            WalkThroughRoom(new Vector3(-6f, .02f, .55f));
            EnterAndClose(bathroom, Vector3.left);
            WalkThroughRoom(new Vector3(-12.7f, .02f, .5f));
            ReturnThrough(bathroom, Vector3.right);
            ReturnThrough(bedroom, Vector3.right);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            Assert.IsFalse(scene.State.RouteConfirmed);
        }

        [Test]
        public void ApartmentFrontEntranceKeepsThePlayerInside()
        {
            WalkThroughRoom(new Vector3(.53f, .02f, 3.35f));
            World.ViewCamera.transform.LookAt(new Vector3(.53f, 1.15f, 4.71f));
            interactions.Use();
            // The final visit now offers a deliberate farewell at the real front door.
            // Reading that choice pauses movement; returning to the room preserves its collision.
            Assert.AreEqual(PlayerScreen.Reading, interactions.Screen);
            scene.Story.DismissReading();
            for (int i = 0; i < 55; i++) Move(Vector3.forward);
            Assert.That(World.Player.position.z, Is.InRange(3.6f, 4.65f), "Exterior entrance should remain a solid boundary.");
            Assert.IsFalse(scene.State.RouteConfirmed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApartmentClosingDoorWaitsForTheOccupant(bool linInDoorway)
        {
            var door = DoorNear(5.15f);
            var threshold = door.Panel.transform.TransformPoint(door.Panel.center);
            threshold.y = .02f;
            WalkThroughRoom(threshold - Vector3.right * 1.52f);
            UsePanel(door, true);
            var occupant = (linInDoorway ? World.Lin : World.Player).GetComponent<CharacterController>();
            if (linInDoorway)
            {
                WalkStraight(threshold + Vector3.right * 1.52f);
                occupant.enabled = false;
                occupant.transform.position = threshold;
                occupant.enabled = true;
                Physics.SyncTransforms();
            }
            else WalkStraight(threshold);

            PressUseOnPanel(door, false);
            for (int i = 0; i < 100; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.IsTrue(door.IsBlocked, "Door should pause before it touches the occupant");
            var center = occupant.bounds.center;
            Assert.GreaterOrEqual(Vector3.Distance(center, door.Panel.ClosestPoint(center)), occupant.radius - .025f);
            Assert.Less(Vector3.ProjectOnPlane(occupant.transform.position - threshold, Vector3.up).magnitude, .1f);
            if (linInDoorway) occupant.Move(Vector3.left * 1.3f);
            else WalkStraight(threshold - Vector3.right * 1.52f);
            for (int i = 0; i < 100; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.IsFalse(door.IsBlocked);
            Assert.IsFalse(door.IsMoving);
            Assert.IsFalse(door.IsOpen);
        }

        IEnumerator InviteLinAndEnterKitchen(ApartmentDoor door, Vector3 threshold)
        {
            World.ViewCamera.transform.LookAt(World.Lin.position + Vector3.up * 1.38f);
            interactions.Use();
            Assert.AreEqual(PlayerScreen.Talk, interactions.Screen);
            interactions.Choose("company");
            interactions.Choose("follow");
            Assert.AreEqual(PlayerScreen.Explore, interactions.Screen);
            WalkThroughRoom(threshold - Vector3.right * 1.52f);
            UsePanel(door, true);
            // Navigation publishes the moving obstacle's final cut on a later engine frame.
            yield return null;
            yield return null;
            WalkStraight(threshold + Vector3.right * 1.52f);
        }

        void WaitForLinInKitchen(Vector3 threshold)
        {
            WalkThroughRoom(new Vector3(7.7f, .02f, .8f));
            for (int i = 0; i < 750 && Vector3.Distance(World.Lin.position, World.Player.position) > 1.57f; i++)
                scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.AreEqual(ActionStage.Following, scene.State.Stage, scene.Feedback);
            Assert.Greater(World.Lin.position.x, threshold.x + .5f, "Lin must physically cross the doorway.");
            Assert.Less(Vector3.Distance(World.Lin.position, World.Player.position), 1.6f);
        }

        [UnityTest]
        public IEnumerator ApartmentLinFollowsThePlayerThroughTheOpenKitchenDoor()
        {
            var door = DoorNear(5.15f);
            var threshold = door.Panel.transform.TransformPoint(door.Panel.center);
            threshold.y = .02f;
            yield return InviteLinAndEnterKitchen(door, threshold);
            WaitForLinInKitchen(threshold);
        }

        [UnityTest]
        public IEnumerator ApartmentLinWaitsBehindAClosedKitchenDoorAndRejoinsWhenItReopens()
        {
            var door = DoorNear(5.15f);
            var threshold = door.Panel.transform.TransformPoint(door.Panel.center);
            threshold.y = .02f;
            yield return InviteLinAndEnterKitchen(door, threshold);
            UsePanel(door, false);
            yield return null;
            yield return null;
            WalkThroughRoom(new Vector3(7.7f, .02f, .8f));
            // Leave time for the normal three-second no-progress failure to trigger.
            for (int i = 0; i < 375; i++) scene.StepSimulation(Step, Vector2.zero, Vector2.zero);
            Assert.AreEqual(PlayerScreen.Explore, interactions.Screen, scene.Feedback);
            Assert.AreEqual(ActionStage.Following, scene.State.Stage, scene.Feedback);
            Assert.Less(World.Lin.position.x, threshold.x - .1f, "Lin must stay on her own side of the shut door.");
            WalkThroughRoom(threshold + Vector3.right * 1.52f);
            UsePanel(door, true);
            yield return null;
            yield return null;
            WaitForLinInKitchen(threshold);
        }
    }
}
#endif
