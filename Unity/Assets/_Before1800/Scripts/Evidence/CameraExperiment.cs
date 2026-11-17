using System;
using System.Collections;
using UnityEngine;

namespace Before1800
{
    public sealed class CameraExperiment : MonoBehaviour
    {
        public Transform cameraPivot;
        public Transform pointA;
        public Transform pointB;
        public LinMotor lin;
        public bool trackingEnabled = true;
        public Func<bool> IsPaused;
        public string Phase { get; private set; } = "Not inspected";
        public bool Running { get; private set; }
        public int ObservedPositions { get; private set; }
        public string InstanceId { get; private set; }
        public event Action<string> PhaseChanged;
        public float observationSeconds = 1.3f;
        public float maxAimError = 8;
        private Quaternion firstRotation;
        private int generation;

        private void Update()
        {
            if (IsPaused != null && IsPaused()) return;
            if (trackingEnabled && lin != null && cameraPivot != null)
            {
                Vector3 direction = lin.head.position - cameraPivot.position;
                cameraPivot.rotation = Quaternion.RotateTowards(cameraPivot.rotation, Quaternion.LookRotation(direction), 80 * Time.deltaTime);
            }
        }

        public IEnumerator Run(Action<bool, string, string> completed)
        {
            if (Running) { completed(false, "experiment_busy", InstanceId); yield break; }
            Running = true; ObservedPositions = 0; InstanceId = Guid.NewGuid().ToString("N");
            int thisGeneration=++generation;
            string thisInstance = InstanceId;
            if (Vector3.Distance(pointA.position, pointB.position) < 1.5f)
            { Running = false; SetPhase("Invalid observation layout"); completed(false, "observation_points_too_close", thisInstance); yield break; }
            foreach (Transform point in new[] { pointA, pointB })
            {
                SetPhase(ObservedPositions == 0 ? "Walking to point A" : "Changing position to point B");
                bool arrived = false; string moveReason = "unknown";
                yield return lin.MoveTo(point.position, (ok, reason) => { arrived = ok; moveReason = reason; });
                if(thisGeneration!=generation)yield break;
                if (!arrived) { Running = false; SetPhase("Inspection interrupted"); completed(false, moveReason, thisInstance); yield break; }
                SetPhase(ObservedPositions == 0 ? "Watching the lens at A" : "Watching the lens at B");
                float validDuration = 0, elapsed = 0;
                while (validDuration < observationSeconds && elapsed < 9)
                {
                    if(thisGeneration!=generation)yield break;
                    if (!(IsPaused != null && IsPaused()))
                    {
                        elapsed += Time.deltaTime;
                        lin.Face(cameraPivot.position);
                        bool valid = CanObserveAt(point.position);
                        validDuration = valid ? validDuration + Time.deltaTime : 0;
                    }
                    yield return null;
                }
                if (validDuration < observationSeconds)
                { Running = false; SetPhase("Could not observe tracking"); completed(false, "observation_failed", thisInstance); yield break; }
                if (ObservedPositions == 0) firstRotation = cameraPivot.rotation;
                else if (Quaternion.Angle(firstRotation, cameraPivot.rotation) < 15)
                { Running = false; SetPhase("No clear change in lens direction"); completed(false, "insufficient_tracking_change", thisInstance); yield break; }
                ObservedPositions++;
            }
            Running = false; SetPhase("Verified: lens followed Lin at A and B");
            completed(true, "Lin observed the camera track his position at two separated points.", thisInstance);
        }

        public bool CanObserveAt(Vector3 point)
        {
            if (Vector3.Distance(lin.transform.position, point) > .4f) return false;
            Vector3 toLin = lin.head.position - cameraPivot.position;
            if (Vector3.Angle(cameraPivot.forward, toLin) > maxAimError) return false;
            Vector3 toCamera = cameraPivot.position - lin.head.position;
            Vector3 horizontal = toCamera; horizontal.y = 0;
            if (Vector3.Angle(lin.transform.forward, horizontal) > 20) return false;
            foreach (var hit in Physics.RaycastAll(lin.head.position, toCamera.normalized, toCamera.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(lin.transform) || hit.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        public void ResetExperiment()
        {
            generation++;
            Running = false; ObservedPositions = 0; InstanceId = null;
            trackingEnabled = true; SetPhase("Not inspected");
        }

        private void SetPhase(string value) { Phase = value; PhaseChanged?.Invoke(value); }
    }
}
