using System;
using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    [DisallowMultipleComponent]
    public sealed class ApartmentDoor : MonoBehaviour
    {
        [SerializeField] Transform leaf;
        [SerializeField] BoxCollider panel;
        [SerializeField] Quaternion closedRotation = Quaternion.identity;
        [SerializeField] float openAngle;
        [SerializeField] AudioSource sound;
        [SerializeField] AudioClip openingSound, closingSound;
        float angle;
        float speed;
        bool playOpeningOnMove;

        public Transform Leaf => leaf;
        public BoxCollider Panel => panel;
        public bool WantsOpen { get; private set; }
        public bool IsOpen => leaf && Mathf.Approximately(angle, openAngle);
        public bool IsMoving => !Mathf.Approximately(angle, WantsOpen ? openAngle : 0f);
        public bool IsBlocked { get; private set; }

        // The panel's transform may be offset from its hinge. Both the collider
        // and the navigation cut follow the same physical leaf as the artwork.
        public void Configure(Transform hingedLeaf, float signedOpenAngle)
        {
            if (!hingedLeaf || !hingedLeaf.IsChildOf(transform) || Mathf.Abs(signedOpenAngle) < 1f)
                throw new ArgumentException("Supply a hinged leaf under this door and its signed opening angle.");
            BoxCollider solid = null;
            foreach (var box in hingedLeaf.GetComponentsInChildren<BoxCollider>(true))
                if (box.enabled && !box.isTrigger) { solid = box; break; }
            if (!solid) throw new ArgumentException("The hinged leaf needs a solid panel BoxCollider.");
            leaf = hingedLeaf;
            panel = solid;
            openAngle = signedOpenAngle;
            closedRotation = leaf.localRotation;
            var obstacle = panel.GetComponent<NavMeshObstacle>();
            if (!obstacle) obstacle = panel.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = panel.center;
            obstacle.size = panel.size;
            obstacle.carvingMoveThreshold = .04f;
            obstacle.carveOnlyStationary = false;
            obstacle.carving = true;
            ResetDoor();
        }

        public void ConfigureSounds(AudioClip opening, AudioClip closing)
        {
            openingSound = opening;
            closingSound = closing;
            if (!leaf || (!opening && !closing)) return;
            if (!sound) sound = leaf.gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 1f;
            sound.volume = .35f;
            sound.minDistance = 1f;
            sound.maxDistance = 9f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.dopplerLevel = 0f;
        }

        internal void ResetDoor()
        {
            angle = speed = 0f;
            WantsOpen = IsBlocked = playOpeningOnMove = false;
            if (sound && Application.isPlaying) sound.Stop();
            if (leaf) leaf.localRotation = closedRotation;
        }

        internal void Toggle()
        {
            WantsOpen = !WantsOpen;
            playOpeningOnMove = WantsOpen;
            IsBlocked = false;
            speed = 0f;
        }

        internal bool CanAimAt(Ray aim, float reach, Transform observer)
        {
            if (!isActiveAndEnabled || !panel || !panel.enabled) return false;
            if (InView(aim, reach, observer, Quaternion.identity)) return true;
            // Keep the doorway usable when the leaf swings out of the player's sightline.
            return angle != 0f && InView(aim, reach, observer, Quaternion.AngleAxis(-angle, leaf.up));
        }

        bool InView(Ray aim, float reach, Transform observer, Quaternion poseOffset)
        {
            var t = panel.transform;
            var center = leaf.position + poseOffset * (t.TransformPoint(panel.center) - leaf.position);
            var rotation = poseOffset * t.rotation;
            var size = Vector3.Scale(panel.size, t.lossyScale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            var inverse = Quaternion.Inverse(rotation);
            var localRay = new Ray(inverse * (aim.origin - center), inverse * aim.direction);
            // Include the jamb and handle, but keep the test anchored to the door's orientation.
            var focusBox = new Bounds(Vector3.zero, size + new Vector3(.40f, .24f, .40f));
            if (!focusBox.IntersectRay(localRay, out var entry) || entry < 0f || entry > reach) return false;

            // Check a point just inside the opening so a visible jamb is selectable
            // from either side while an intervening wall or character still blocks use.
            var innerBox = new Bounds(Vector3.zero, size * .76f);
            var localPoint = innerBox.ClosestPoint(localRay.GetPoint(entry));
            var toDoor = center + rotation * localPoint - aim.origin;
            return toDoor.sqrMagnitude <= reach * reach && Vector3.Dot(aim.direction, toDoor) > 0f
                && InteractionSight.Clear(aim.origin, toDoor, observer, transform);
        }

        // Approach the doorway outside the sweep of a re-opening leaf. Use the
        // closed pose even if somebody has paused the leaf in mid-swing.
        internal void ApproachPoints(out Vector3 front, out Vector3 back)
        {
            var t = panel.transform;
            var undoSwing = Quaternion.AngleAxis(-angle, leaf.up);
            var center = leaf.position + undoSwing * (t.TransformPoint(panel.center) - leaf.position);
            center.y = panel.bounds.min.y + .05f;
            var size = Vector3.Scale(panel.size, t.lossyScale);
            float x = Mathf.Abs(size.x), z = Mathf.Abs(size.z);
            var normal = Vector3.ProjectOnPlane(undoSwing * (x < z ? t.right : t.forward), Vector3.up).normalized;
            var offset = normal * (Mathf.Max(x, z) + .44f);
            front = center + offset;
            back = center - offset;
        }

        internal bool Step(float dt, CharacterController player, CharacterController lin)
        {
            if (!isActiveAndEnabled || !panel || !IsMoving || dt <= 0f) return false;
            const float acceleration = 950f;
            float target = WantsOpen ? openAngle : 0f;
            float remaining = Mathf.Abs(target - angle);
            float brakingSpeed = Mathf.Sqrt(2f * acceleration * remaining);
            speed = Mathf.MoveTowards(speed, Mathf.Min(155f, brakingSpeed), acceleration * dt);
            float next = Mathf.MoveTowards(angle, target, speed * dt);
            float start = angle;
            IsBlocked = false;

            // Even a long frame must check the swept arc, not just the final pose.
            while (angle != next)
            {
                float candidate = Mathf.MoveTowards(angle, next, 2f);
                if (Occupied(candidate - angle, player, lin))
                {
                    IsBlocked = true;
                    speed = 0f;
                    break;
                }
                angle = candidate;
                leaf.localRotation = closedRotation * Quaternion.AngleAxis(angle, Vector3.up);
            }
            if (angle == start) return false;
            if (playOpeningOnMove && !IsBlocked)
            {
                if (sound && openingSound) sound.PlayOneShot(openingSound);
                playOpeningOnMove = false;
            }
            if (!WantsOpen && !IsMoving && sound && closingSound) sound.PlayOneShot(closingSound);
            return true;
        }

        bool Occupied(float step, CharacterController player, CharacterController lin)
        {
            var panelTransform = panel.transform;
            var swing = Quaternion.AngleAxis(step, leaf.up);
            var offset = panelTransform.TransformPoint(panel.center) - leaf.position;
            var center = leaf.position + swing * offset;
            var orientation = Quaternion.Inverse(swing * panelTransform.rotation);
            var size = Vector3.Scale(panel.size, panelTransform.lossyScale);
            var half = .5f * new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            // Expand for the maximum horizontal displacement within this tiny arc.
            float radius = Vector3.ProjectOnPlane(offset, leaf.up).magnitude + new Vector2(half.x, half.z).magnitude;
            float clearance = .01f + radius * Mathf.Abs(step) * Mathf.Deg2Rad;
            return OverlapsActor(player, center, orientation, half, clearance)
                || OverlapsActor(lin, center, orientation, half, clearance);
        }

        static bool OverlapsActor(CharacterController actor, Vector3 center, Quaternion inverseRotation,
            Vector3 half, float clearance)
        {
            if (!actor || !actor.enabled) return false;
            var bounds = actor.bounds;
            var local = inverseRotation * (bounds.center - center);
            if (Mathf.Abs(local.y) > bounds.extents.y + half.y) return false;
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - half.x);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z) - half.z);
            float actorRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) + clearance;
            return dx * dx + dz * dz <= actorRadius * actorRadius;
        }
    }
}
