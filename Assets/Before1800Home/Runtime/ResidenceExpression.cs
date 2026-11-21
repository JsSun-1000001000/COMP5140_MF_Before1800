using UnityEngine;

namespace Before1800.Home
{
    sealed class ResidenceExpression
    {
        readonly Transform visual, head, leftEye, rightEye;
        readonly Camera viewer;
        readonly AnimationClip blink;
        readonly SkinnedMeshRenderer face;
        readonly int raisedBrow = -1, questioningMouth = -1;
        readonly System.Random intervals = new System.Random();
        float blinkTime = -2f;
        float attentionTime, timeAway, questionWeight;
        bool attending;
        Vector2 gaze;
        float turnTime, headYaw, turnDirection;
        Quaternion leftPose, rightPose, headPose;
        bool eyesAdjusted, headAdjusted;

        public ResidenceExpression(Transform visual, AnimationClip blink, Camera viewer)
        {
            this.visual = visual;
            this.blink = blink;
            this.viewer = viewer;
            var faceNode = visual.Find("Head");
            if (faceNode && faceNode.TryGetComponent(out face) && face.sharedMesh)
            {
                raisedBrow = face.sharedMesh.GetBlendShapeIndex("EyeBrowRightUp");
                questioningMouth = face.sharedMesh.GetBlendShapeIndex("AnnoyToLeft");
            }
            head = visual.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
            if (!head) return;
            leftEye = head.Find("Left Eye");
            rightEye = head.Find("Right Eye");
        }

        // Put the authored pose back before the Animator evaluates the next frame.
        public void RestorePose()
        {
            if (eyesAdjusted)
            {
                leftEye.localRotation = leftPose;
                rightEye.localRotation = rightPose;
                eyesAdjusted = false;
            }
            if (headAdjusted)
            {
                head.localRotation = headPose;
                headAdjusted = false;
            }
        }

        public void Animate(float dt, bool stepping, Vector3 desiredFacing)
        {
            if (blink)
            {
                blinkTime += dt;
                if (blinkTime >= 0f)
                {
                    // This original Mita clip only keys Head/blendShape.Blink.
                    blink.SampleAnimation(visual.gameObject, Mathf.Min(blinkTime, blink.length));
                    if (blinkTime >= blink.length) blinkTime = -Mathf.Lerp(2.8f, 5.1f, (float)intervals.NextDouble());
                }
            }

            if (!leftEye || !rightEye || !viewer) return;
            var actor = visual.parent;
            var target = Vector2.zero;
            float interest = 0f;
            float turnAngle = desiredFacing.sqrMagnitude > .001f
                ? Vector3.SignedAngle(actor.forward, desiredFacing, actor.up) : 0f;
            float turn = Mathf.Sign(turnAngle) * Mathf.InverseLerp(9f, 55f, Mathf.Abs(turnAngle));
            if (dt > 0f)
            {
                if (turn == 0f || turn * turnDirection < 0f) turnTime = 0f;
                else turnTime += dt;
                turnDirection = turn;
                // The eyes find the corner first. As the neck catches up, the local eye offset diminishes.
                float lead = Mathf.InverseLerp(.07f, .18f, turnTime);
                headYaw = Mathf.MoveTowards(headYaw, 12f * turn * lead, dt * 65f);
            }
            if (turn != 0f)
            {
                target.x = turn * (9f - Mathf.Abs(headYaw) * .55f);
            }
            else if (!stepping)
            {
                var eyes = (leftEye.position + rightEye.position) * .5f;
                var direction = actor.InverseTransformDirection(viewer.transform.position - eyes);
                var aim = direction.normalized;
                float attention = Mathf.InverseLerp(4f, 2.8f, direction.magnitude) *
                    Mathf.InverseLerp(.45f, .8f, aim.z);
                interest = attention * Mathf.InverseLerp(3.15f, 2.4f, direction.magnitude);
                target = new Vector2(
                    Mathf.Clamp(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, -9f, 9f),
                    Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg, -6f, 6f)) * attention;
            }
            AnimateQuestion(interest, dt);
            gaze = Vector2.MoveTowards(gaze, target, (turn != 0f ? 150f : 55f) * dt);
            if (Mathf.Abs(headYaw) > .01f)
            {
                headPose = head.localRotation;
                head.rotation = Quaternion.AngleAxis(headYaw, actor.up) * head.rotation;
                headAdjusted = true;
            }
            if (gaze.sqrMagnitude < .0001f) return;
            var offset = Quaternion.AngleAxis(gaze.x, actor.up) * Quaternion.AngleAxis(-gaze.y, actor.right);
            leftPose = leftEye.localRotation;
            rightPose = rightEye.localRotation;
            leftEye.rotation = offset * leftEye.rotation;
            rightEye.rotation = offset * rightEye.rotation;
            eyesAdjusted = true;
        }

        void AnimateQuestion(float interest, float dt)
        {
            if (dt <= 0f || !face || raisedBrow < 0 || questioningMouth < 0) return;
            // React once per approach. A sustained departure (or walking away) rearms the expression.
            timeAway = interest < .12f ? timeAway + dt : 0f;
            if (timeAway >= .35f) attending = false;
            if (!attending && interest >= .55f)
            {
                attending = true;
                attentionTime = 0f;
            }
            if (attending) attentionTime += dt;
            float settling = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.8f, 2.5f, attentionTime));
            float target = attending ? interest * .69f * settling : 0f;
            questionWeight = Mathf.MoveTowards(questionWeight, target, dt * 1.9f);
            float strength = Mathf.SmoothStep(0f, 1f, questionWeight);
            // Mita E-Quest keys the raised right brow at 70 and the asymmetric mouth at 60.
            face.SetBlendShapeWeight(raisedBrow, 70f * strength);
            face.SetBlendShapeWeight(questioningMouth, 60f * strength);
        }
    }
}
