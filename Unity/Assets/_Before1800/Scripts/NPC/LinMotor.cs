using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Before1800
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class LinMotor : MonoBehaviour
    {
        public Transform head;
        public Transform visual;
        public Func<bool> IsPaused;
        public bool IsFollowing { get; private set; }
        public bool IsMoving { get; private set; }
        public string MotionStatus { get; private set; } = "Ready to talk";
        public NavMeshAgent Agent { get; private set; }
        private Transform followTarget;
        private float nextFollowUpdate;
        private int motionGeneration;

        private void Awake() { Agent = GetComponent<NavMeshAgent>(); }
        private void Start()
        {
            Agent.enabled=true;
            if(NavMesh.SamplePosition(transform.position,out var hit,2,NavMesh.AllAreas))Agent.Warp(hit.position);
        }
        private void Update()
        {
            if (Agent == null || !Agent.isOnNavMesh) return;
            bool paused = IsPaused != null && IsPaused();
            Agent.isStopped = paused;
            if (paused) return;
            if (IsFollowing && followTarget != null && Time.time >= nextFollowUpdate)
            {
                nextFollowUpdate = Time.time + .3f;
                if (Vector3.Distance(transform.position, followTarget.position) > 1.8f)
                {
                    if (NavMesh.SamplePosition(followTarget.position - followTarget.forward * 1.3f, out var hit, 1.5f, NavMesh.AllAreas))
                        Agent.SetDestination(hit.position);
                }
                else { Agent.ResetPath(); Face(followTarget.position); }
            }
            IsMoving = Agent.velocity.sqrMagnitude > .02f;
            if (visual != null) visual.localPosition = Vector3.up * (IsMoving ? Mathf.Sin(Time.time * 9) * .025f : 0);
        }

        public void Follow(Transform target)
        {
            IsFollowing = true; followTarget = target; nextFollowUpdate = 0;
            MotionStatus = "Following you";
        }

        public void Stop(string message = "Ready to talk")
        {
            motionGeneration++;
            IsFollowing = false; followTarget = null; IsMoving = false;
            if (Agent != null && Agent.isOnNavMesh) Agent.ResetPath();
            MotionStatus = message;
        }

        public IEnumerator MoveTo(Vector3 target, Action<bool, string> finished, float timeout = 20)
        {
            Stop("Walking to observation point");
            int thisMotion=motionGeneration;
            if (Agent == null) Agent = GetComponent<NavMeshAgent>();
            if (!Agent.isOnNavMesh || !NavMesh.SamplePosition(target, out var destination, .6f, NavMesh.AllAreas))
            { finished(false, "target_not_on_navmesh"); yield break; }
            var path = new NavMeshPath();
            if (!Agent.CalculatePath(destination.position, path) || path.status != NavMeshPathStatus.PathComplete)
            { finished(false, "path_unreachable"); yield break; }
            Agent.SetPath(path);
            float activeTime = 0;
            while (activeTime < timeout)
            {
                if(thisMotion!=motionGeneration){finished(false,"navigation_cancelled");yield break;}
                if (!(IsPaused != null && IsPaused()))
                {
                    activeTime += Time.deltaTime;
                    if (!Agent.pathPending && Agent.pathStatus != NavMeshPathStatus.PathComplete)
                    { Stop("Path blocked"); finished(false, "path_interrupted"); yield break; }
                    if (!Agent.pathPending && Vector3.Distance(transform.position, destination.position) < .3f)
                    { Stop("Observing"); finished(true, "arrived"); yield break; }
                }
                yield return null;
            }
            Stop("Could not reach the point"); finished(false, "navigation_timeout");
        }

        public void Face(Vector3 target)
        {
            Vector3 direction = target - transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 220 * Time.deltaTime);
        }

        public void ResetAt(Vector3 position)
        {
            Stop();
            if (Agent == null) Agent = GetComponent<NavMeshAgent>();
            if (Agent.isOnNavMesh) Agent.Warp(position); else transform.position = position;
            transform.rotation = Quaternion.Euler(0, 180, 0);
        }
    }
}
