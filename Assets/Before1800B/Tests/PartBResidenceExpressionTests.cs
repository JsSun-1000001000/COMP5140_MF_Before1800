#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Before1800.PartB.Tests
{
    [DefaultExecutionOrder(-1000)]
    public sealed class ResidenceBeforeLook : MonoBehaviour
    {
        public Transform head, eye;
        public Quaternion headRotation, eyeRotation;
        void LateUpdate() { headRotation = head.rotation; eyeRotation = eye.rotation; }
    }

    [DefaultExecutionOrder(1000)]
    public sealed class ResidenceAfterLook : MonoBehaviour
    {
        public ResidenceBeforeLook before;
        public Transform actor;
        public float headYaw, eyeYaw;
        void LateUpdate()
        {
            var headOffset = before.head.rotation * Quaternion.Inverse(before.headRotation);
            var eyeOffset = before.eye.rotation * Quaternion.Inverse(before.eyeRotation);
            headYaw = Vector3.SignedAngle(actor.forward, headOffset * actor.forward, actor.up);
            eyeYaw = Vector3.SignedAngle(actor.forward, eyeOffset * actor.forward, actor.up);
        }
    }

    public sealed class PartBResidenceExpressionTests
    {
        [UnityTest]
        public IEnumerator BlinkClosesTheOriginalMorphAndPausesWithLin()
        {
            float captureStep = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
                var scene = Object.FindObjectOfType<PartBScene>();
                var visual = scene.World.Lin.GetComponentInChildren<Animator>();
                var head = visual.transform.Find("Head").GetComponent<SkinnedMeshRenderer>();
                int blink = head.sharedMesh.GetBlendShapeIndex("Blink");
                Assert.GreaterOrEqual(blink, 0);
                scene.ToggleIntroOff();
                for (int i = 0; i < 240 && head.GetBlendShapeWeight(blink) < 85; i++)
                    yield return null;
                float closed = head.GetBlendShapeWeight(blink);
                Assert.GreaterOrEqual(closed, 85f, "The real head mesh must reach a closed-eye pose");

                scene.ToggleMenu();
                for (int i = 0; i < 12; i++) yield return null;
                Assert.AreEqual(closed, head.GetBlendShapeWeight(blink), .1f, "Blink should freeze with the character");
                scene.ToggleMenu();
                for (int i = 0; i < 30; i++) yield return null;
                Assert.Less(head.GetBlendShapeWeight(blink), 1f, "The authored blink should reopen both eyes");
            }
            finally
            {
                Time.captureDeltaTime = captureStep;
            }
        }

        [UnityTest]
        public IEnumerator ApproachRaisesBrowOnceAndSettlesWhileThePlayerStays()
        {
            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
                var scene = Object.FindObjectOfType<PartBScene>();
                var visual = scene.World.Lin.GetComponentInChildren<Animator>().transform;
                var face = visual.Find("Head").GetComponent<SkinnedMeshRenderer>();
                int brow = face.sharedMesh.GetBlendShapeIndex("EyeBrowRightUp");
                int mouth = face.sharedMesh.GetBlendShapeIndex("AnnoyToLeft");
                var viewer = scene.World.ViewCamera.transform;
                var actor = scene.World.Lin;
                Vector3 eyes = actor.position + Vector3.up * 1.5f;
                viewer.position = eyes + actor.forward * 6f;
                scene.ToggleIntroOff();
                for (int i = 0; i < 30; i++) yield return null;
                Assert.Less(face.GetBlendShapeWeight(brow), 1f);
                viewer.position = eyes + actor.forward;
                for (int i = 0; i < 32; i++) yield return null;
                float active = face.GetBlendShapeWeight(brow);
                Assert.That(active, Is.InRange(45f, 60f));
                Assert.That(face.GetBlendShapeWeight(mouth), Is.InRange(38f, 50f));
                scene.ToggleMenu();
                for (int i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(active, face.GetBlendShapeWeight(brow), .05f);
                scene.ToggleMenu();
                for (int i = 0; i < 155; i++) yield return null;
                Assert.Less(face.GetBlendShapeWeight(brow), 1f);
                for (int i = 0; i < 45; i++)
                {
                    yield return null;
                    Assert.Less(face.GetBlendShapeWeight(mouth), 1f, "Remaining nearby should not retrigger the pose");
                }
            }
            finally { Time.captureDeltaTime = previous; }
        }

        [UnityTest]
        public IEnumerator OnTheInspectTurnEyesLeadTheHeadAndBothReturnToTheAuthoredWalk()
        {
            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                    new LoadSceneParameters(LoadSceneMode.Single));
                var scene = Object.FindObjectOfType<PartBScene>();
                var actor = scene.World.Lin;
                var probe = new GameObject("Test rendered turn");
                var before = probe.AddComponent<ResidenceBeforeLook>();
                before.head = actor.GetComponentInChildren<Animator>().transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
                before.eye = before.head.Find("Left Eye");
                var after = probe.AddComponent<ResidenceAfterLook>();
                after.before = before;
                after.actor = actor;
                scene.ToggleIntroOff();
                yield return null;
                scene.ToggleDialogue();
                scene.SubmitTestAction("inspect");
                scene.ToggleDialogue();
                float direction = Mathf.Sign(Vector3.SignedAngle(actor.forward, scene.World.ObservationPoint.position - actor.position, actor.up));
                bool eyesFirst = false, checkedPause = false, neutralAgain = false;
                float maximum = 0f;
                for (int i = 0; i < 160; i++)
                {
                    yield return null;
                    float headTurn = after.headYaw * direction;
                    float eyeTurn = after.eyeYaw * direction;
                    eyesFirst |= eyeTurn > 3.5f && Mathf.Abs(headTurn) < .5f;
                    maximum = Mathf.Max(maximum, headTurn);
                    if (!checkedPause && headTurn > 6f)
                    {
                        scene.ToggleMenu();
                        for (int j = 0; j < 8; j++) yield return null;
                        Assert.AreEqual(headTurn, after.headYaw * direction, .15f);
                        Assert.AreEqual(eyeTurn, after.eyeYaw * direction, .15f);
                        checkedPause = true;
                        scene.ToggleMenu();
                    }
                    neutralAgain |= maximum >= 8f && Mathf.Abs(after.headYaw) < .2f && Mathf.Abs(after.eyeYaw) < .3f;
                }
                Assert.IsTrue(eyesFirst, "The eyes should turn toward the actual path before the head moves");
                Assert.That(maximum, Is.InRange(8f, 12.2f));
                Assert.IsTrue(checkedPause);
                Assert.IsTrue(neutralAgain, "The straight walk should recover the authored head and eyes");
            }
            finally { Time.captureDeltaTime = previous; }
        }
    }
}
#endif
