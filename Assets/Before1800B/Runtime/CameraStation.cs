using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    // Hidden camera that pans to follow Lin between two floor marks. E1 is only
    // written after the housing has tracked her to the second mark.
    public sealed class CameraStation : MonoBehaviour
    {
        [SerializeField] Transform device;
        [SerializeField] Transform housing;
        [SerializeField] Transform observeA;
        [SerializeField] Transform observeB;
        [SerializeField] float panSpeed = 140f;

        Quaternion restLocalRotation;
        bool hasRest;

        public Transform Device => device ? device : transform;
        public Transform ObserveA => observeA;
        public Transform ObserveB => observeB;
        public bool IsAvailable => housing && observeA && observeB && Device &&
            observeA.gameObject.activeInHierarchy && observeB.gameObject.activeInHierarchy &&
            Device.gameObject.activeInHierarchy;

        public void ResetPose()
        {
            if (!housing) return;
            if (!hasRest)
            {
                restLocalRotation = housing.localRotation;
                hasRest = true;
            }
            housing.localRotation = restLocalRotation;
        }

        // Used on scene start / restart so the housing already faces Lin.
        public void SnapTo(Transform lin)
        {
            if (!IsAvailable || !lin) return;
            if (!hasRest)
            {
                restLocalRotation = housing.localRotation;
                hasRest = true;
            }
            Vector3 worldDelta = lin.position + Vector3.up * 1.55f - housing.position;
            if (worldDelta.sqrMagnitude < .0001f) return;
            Quaternion worldLook = Quaternion.LookRotation(worldDelta.normalized, Vector3.up);
            housing.localRotation = Quaternion.Inverse(housing.parent.rotation) * worldLook;
        }

        public bool CanStartTracking(Transform view, Transform lin)
        {
            if (!IsAvailable || !view || !lin || !AtFirstMark(lin)) return false;
            return PlayerCanSeeDevice(view);
        }

        public bool PlayerCanSeeDevice(Transform view)
        {
            if (!IsAvailable || !view) return false;
            Vector3 origin = view.position;
            Vector3 delta = Device.position - origin;
            if (delta.sqrMagnitude > 3.2f * 3.2f || Vector3.Angle(view.forward, delta) > 22f) return false;
            return InteractionSight.Clear(origin, delta, view.GetComponentInParent<CharacterController>()?.transform, Device);
        }

        public bool AtFirstMark(Transform lin)
        {
            if (!IsAvailable || !lin) return false;
            if (Vector3.Distance(lin.position, observeA.position) > .65f) return false;
            Vector3 eye = lin.position + Vector3.up * 1.55f;
            Vector3 toward = Device.position - eye;
            return Vector3.Angle(lin.forward, Flat(toward)) <= 35f &&
                InteractionSight.Clear(eye, toward, lin, Device);
        }

        public bool AtSecondMark(Transform lin)
        {
            if (!IsAvailable || !lin) return false;
            if (Vector3.Distance(lin.position, observeB.position) > .65f) return false;
            Vector3 eye = lin.position + Vector3.up * 1.55f;
            Vector3 toward = Device.position - eye;
            return Vector3.Angle(lin.forward, Flat(toward)) <= 40f;
        }

        public bool TrackingVisible(Transform lin)
        {
            if (!IsAvailable || !lin) return false;
            Vector3 lens = housing.position;
            Vector3 target = lin.position + Vector3.up * 1.55f;
            Vector3 delta = target - lens;
            if (delta.sqrMagnitude < .01f) return true;
            float yawError = Vector3.Angle(Flat(housing.forward), Flat(delta));
            float pitchError = Mathf.Abs(Vector3.SignedAngle(housing.forward, delta.normalized, housing.right));
            return yawError <= 18f && pitchError <= 28f &&
                InteractionSight.Clear(lens, delta, null, lin);
        }

        public void StepTrack(Transform lin, float dt)
        {
            if (!IsAvailable || !lin || dt <= 0f) return;
            if (!hasRest)
            {
                restLocalRotation = housing.localRotation;
                hasRest = true;
            }
            Vector3 target = lin.position + Vector3.up * 1.55f;
            Vector3 worldDelta = target - housing.position;
            if (worldDelta.sqrMagnitude < .0001f) return;
            Quaternion worldLook = Quaternion.LookRotation(worldDelta.normalized, Vector3.up);
            Quaternion localLook = Quaternion.Inverse(housing.parent.rotation) * worldLook;
            housing.localRotation = Quaternion.RotateTowards(housing.localRotation, localLook, panSpeed * dt);
        }

        public bool CanReachSecondMark()
        {
            if (!IsAvailable) return false;
            return NavMesh.SamplePosition(observeB.position, out var from, .45f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(observeA.position, out var to, .45f, NavMesh.AllAreas) &&
                PathComplete(from.position, to.position);
        }

        static bool PathComplete(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Greybox and residence both call this so authored scenes need no menu rebuild.
        public static CameraStation Ensure(Transform device, Transform observeA, Material markMaterial = null)
        {
            if (!device || !observeA) return null;
            var existing = device.GetComponent<CameraStation>() ?? device.GetComponentInChildren<CameraStation>(true);
            if (existing && existing.IsAvailable) return existing;

            var station = existing ? existing : device.gameObject.AddComponent<CameraStation>();
            station.device = device;
            station.observeA = observeA;
            station.housing = FindHousing(device);
            if (!station.housing)
            {
                station.housing = new GameObject("Camera housing").transform;
                station.housing.SetParent(device, false);
            }
            if (!station.observeB)
            {
                var siblingParent = observeA.parent ? observeA.parent : device.parent;
                station.observeB = siblingParent.Find("e1_observe_b");
                if (!station.observeB)
                {
                    station.observeB = new GameObject("e1_observe_b").transform;
                    station.observeB.SetParent(siblingParent, false);
                }
                Vector3 offset = Flat(Quaternion.Euler(0f, 90f, 0f) * (device.position - observeA.position)).normalized;
                if (offset.sqrMagnitude < .01f) offset = Vector3.right;
                station.observeB.position = observeA.position + offset * 1.55f;
                station.observeB.rotation = Quaternion.LookRotation(Flat(device.position - station.observeB.position));
            }
            PaintSecondMark(station.observeB, markMaterial);
            station.ResetPose();
            return station.IsAvailable ? station : null;
        }

        static Transform FindHousing(Transform device)
        {
            var named = device.Find("Camera housing");
            if (named) return named;
            for (int i = 0; i < device.childCount; i++)
            {
                var child = device.GetChild(i);
                if (child.name.IndexOf("housing", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("Camera", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }
            return device.childCount > 0 ? device.GetChild(0) : null;
        }

        static void PaintSecondMark(Transform mark, Material material)
        {
            if (!mark || mark.childCount > 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                bar.name = "Observation mark B";
                bar.SetParent(mark, false);
                bar.localPosition = new Vector3(side * .42f, .01f, 0f);
                bar.localScale = new Vector3(.08f, .012f, .95f);
                if (material) bar.GetComponent<Renderer>().sharedMaterial = material;
                var collider = bar.GetComponent<Collider>();
                if (collider) collider.enabled = false;
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                bar.name = "Observation mark B";
                bar.SetParent(mark, false);
                bar.localPosition = new Vector3(0f, .01f, side * .42f);
                bar.localScale = new Vector3(.95f, .012f, .08f);
                if (material) bar.GetComponent<Renderer>().sharedMaterial = material;
                var collider = bar.GetComponent<Collider>();
                if (collider) collider.enabled = false;
            }
        }
    }
}
