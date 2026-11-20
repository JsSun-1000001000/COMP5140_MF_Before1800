using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    public enum TravelResult { Moving, Arrived, Failed }

    // The runner supplies active simulation steps. Nothing here moves independently on Update.
    public sealed class GreyboxWorld : MonoBehaviour
    {
        [SerializeField] CharacterController player;
        [SerializeField] CharacterController lin;
        [SerializeField] Camera view;
        [SerializeField] Transform cameraTarget;
        [SerializeField] Transform observationPoint;
        [SerializeField] Transform returnPoint;
        [SerializeField] Transform withdrawPoint;
        [SerializeField] Transform playerStart;
        [SerializeField] NavMeshData navigationData;
        [SerializeField] Material[] layoutMaterials;
        [SerializeField] bool usesSceneBindings;
        [SerializeField] SetBackStation setBackStation;

        NavMeshDataInstance navigation;
        Transform namePlate;
        Transform nameLabel;
        bool initialized;
        bool runtimeLayout;
        Vector3 openObservationPoint;
        Vector3 plannedGoal;
        Vector3[] corners;
        int nextCorner;
        float pitch;
        float stageTime;
        float stuckTime;
        float progress;
        float walkingSpeed;
        float turnSpeed;
        Vector3 facingDirection;
        int lastFacingFrame = -1;
        bool waitingForPlayer;
        const float TravelSpeed = 2.2f;
        const float ArrivalRadius = .35f;
        // The WeTogether Mita agent uses these room-scale movement settings.
        const float ResidenceSpeed = 1.1f;
        const float ResidenceAcceleration = 5f;
        const float ResidenceTurnSpeed = 180f;
        const float ResidenceTurnAcceleration = 480f;

        public Transform Player => player.transform;
        public Transform Lin => lin.transform;
        public Camera ViewCamera => view;
        public Transform ObservationPoint => observationPoint;
        public Transform CameraTarget => cameraTarget;
        public SetBackStation Station => setBackStation && setBackStation.IsAvailable ? setBackStation : null;
        public NavMeshData NavigationData => navigationData;
        public bool UsesSceneBindings => usesSceneBindings;
        // The visual rig can glance toward the current waypoint before the body finishes turning.
        public Vector3 DesiredFacing => lastFacingFrame == Time.frameCount ? facingDirection : Vector3.zero;
        public Material[] LayoutMaterials => layoutMaterials;
        public string FailureCode { get; private set; }
        public float RemainingDistance { get; private set; }
        public bool ObservationBlockedForTest { get; private set; }
        public bool CanReachObservation => initialized && ObservationAvailable &&
            PathTo(observationPoint.position, new NavMeshPath());
        public bool CanInspect(string targetId)
        {
            if (targetId == SessionState.CameraTarget) return CanReachObservation;
            return initialized && targetId == SessionState.SetBackTarget && Station &&
                PathTo(Station.ObservationPoint.position, new NavMeshPath());
        }

        public bool AtObservation(string targetId)
        {
            if (targetId == SessionState.CameraTarget) return IsAtObservation;
            return initialized && targetId == SessionState.SetBackTarget && Station && Station.AtObservation(Lin);
        }
        bool ObservationAvailable => observationPoint && cameraTarget &&
            observationPoint.gameObject.activeInHierarchy && cameraTarget.gameObject.activeInHierarchy;

        public bool IsAtObservation
        {
            get
            {
                if (!initialized || !ObservationAvailable ||
                    Vector3.Distance(Lin.position, observationPoint.position) > .65f)
                    return false;
                var eye = Lin.position + Vector3.up * 1.55f;
                var target = cameraTarget.position - eye;
                return Vector3.Angle(Lin.forward, Flat(target)) <= 35f &&
                    ClearSight(eye, target, target.magnitude + .02f);
            }
        }

        public bool CanDiscoverCamera
        {
            get
            {
                RaycastHit hit;
                return initialized && Physics.Raycast(view.transform.position, view.transform.forward, out hit, 2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && IsCameraCollider(hit.transform);
            }
        }

        // The residence builder supplies its own room, actors and baked navigation data.
        // All bindings are scene references, so the built player needs no editor assets.
        public void ConfigureScene(CharacterController playerActor, CharacterController linActor, Camera firstPerson,
            Transform device, Transform observe, Transform home, Transform withdraw, Transform spawn, NavMeshData navigation)
        {
            if (initialized)
                throw new System.InvalidOperationException("Cannot replace Part B scene bindings during play.");
            player = playerActor;
            lin = linActor;
            view = firstPerson;
            cameraTarget = device;
            observationPoint = observe;
            returnPoint = home;
            withdrawPoint = withdraw;
            playerStart = spawn;
            navigationData = navigation;
            usesSceneBindings = true;
            ValidateSceneBindings();
        }

        void ValidateSceneBindings()
        {
            if (!player || !lin || player == lin || !view || !view.transform.IsChildOf(player.transform))
                throw new System.InvalidOperationException("Part B requires distinct player and Lin controllers and a camera attached to the player.");
            if (!cameraTarget || !observationPoint || !returnPoint || !withdrawPoint || !playerStart || !navigationData)
                throw new System.InvalidOperationException("Part B requires a device, observation, return, withdrawal and spawn anchors, and baked navigation data.");
        }

        // Editor scene generation calls this once on an empty object, then saves the returned
        // materials/NavMeshData as assets. These references and all layout objects serialize.
        public void BuildLayout()
        {
            if (usesSceneBindings || view) return;
            var room = new GreyboxRoom(transform);
            room.Build(out cameraTarget, out observationPoint, out returnPoint, out withdrawPoint, out playerStart);
            player = room.CreateActor("Player", playerStart.position);
            view = new GameObject("First Person Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            view.tag = "MainCamera";
            view.transform.SetParent(Player, false);
            view.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            view.fieldOfView = 68f;
            view.nearClipPlane = .05f;
            view.farClipPlane = 80f;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.31f, .36f, .37f);
            Player.rotation = playerStart.rotation;
            lin = room.CreateActor("Lin", returnPoint.position);
            room.DressLin(Lin);
            Lin.rotation = returnPoint.rotation;
            layoutMaterials = room.Materials.ToArray();
            navigationData = room.BakeNavigation();
            if (!navigationData)
                throw new System.InvalidOperationException("Could not bake the observation room NavMesh.");
            navigationData.name = "ObservationRoom";
        }

        public void Initialize()
        {
            if (initialized) return;
            if (usesSceneBindings)
                ValidateSceneBindings();
            else
            {
                runtimeLayout = !view;
                BuildLayout();
            }
            navigation = NavMesh.AddNavMeshData(navigationData);
            if (!navigation.valid)
                throw new System.InvalidOperationException("Could not register the Part B scene NavMesh.");
            if (!setBackStation) setBackStation = GetComponentInChildren<SetBackStation>(true);
            if (!setBackStation && !usesSceneBindings)
                setBackStation = SetBackStation.BuildGreybox(transform, layoutMaterials, returnPoint);
            openObservationPoint = observationPoint.position;
            namePlate = Lin.Find("Name plate");
            nameLabel = Lin.Find("Lin name");
            initialized = true;
            ResetWorld();
        }

        public void ResetWorld()
        {
            SetObservationBlockedForTest(false);
            if (setBackStation) setBackStation.ResetDoor();
            ResetActor(player, playerStart);
            ResetActor(lin, returnPoint);
            pitch = 0f;
            view.transform.localRotation = Quaternion.identity;
            FaceNamePlateToPlayer();
            ResetTravel();
            Physics.SyncTransforms();
        }

        public void SetObservationBlockedForTest(bool blocked)
        {
            ObservationBlockedForTest = blocked;
            observationPoint.position = openObservationPoint + (blocked ? Vector3.right * 24f : Vector3.zero);
        }

        public void ResetTravel()
        {
            corners = null;
            nextCorner = 0;
            walkingSpeed = 0f;
            turnSpeed = 0f;
            lastFacingFrame = -1;
            waitingForPlayer = false;
            progress = stageTime = stuckTime = RemainingDistance = 0f;
            FailureCode = "";
        }

        public void StepPlayer(Vector2 move, Vector2 look, float dt)
        {
            if (dt <= 0f) return;
            Player.Rotate(0f, look.x * 2.1f, 0f);
            pitch = Mathf.Clamp(pitch - look.y * 2.1f, -80f, 80f);
            view.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            move = Vector2.ClampMagnitude(move, 1f);
            var direction = Player.right * move.x + Player.forward * move.y;
            player.Move((direction * 3.35f + Vector3.down * .9f) * dt);
            FaceNamePlateToPlayer();
        }

        public TravelResult StepInspect(float dt) => StepInspect(SessionState.CameraTarget, dt);

        public TravelResult StepInspect(string targetId, float dt)
        {
            Transform goal, focus;
            if (targetId == SessionState.CameraTarget && ObservationAvailable)
            {
                goal = observationPoint;
                focus = cameraTarget;
            }
            else if (targetId == SessionState.SetBackTarget && Station)
            {
                goal = Station.ObservationPoint;
                focus = Station.SetMark;
            }
            else
                return Fail("RC_NAV_NO_PATH");
            var result = Travel(goal.position, ArrivalRadius, dt);
            if (result != TravelResult.Arrived) return result;
            if (!Face(focus.position, dt)) return TravelResult.Moving;
            return AtObservation(targetId) ? TravelResult.Arrived : Fail("RC_OBSERVATION_BLOCKED");
        }

        public TravelResult StepFollow(float dt) => StepFollow(dt, false);

        public TravelResult StepFollow(float dt, bool passageAllowed)
        {
            bool waitAtBoundary = !passageAllowed && Station && Station.InRestrictedArea(Player.position);
            Vector3 goal = waitAtBoundary ? Station.ObservationPoint.position : Player.position;
            var result = Travel(goal, waitAtBoundary ? ArrivalRadius : 1.4f, dt, !passageAllowed);
            if (result == TravelResult.Arrived) Face(Player.position, dt);
            return result;
        }

        public TravelResult StepWithdraw(float dt)
        {
            var result = Travel(withdrawPoint.position, ArrivalRadius, dt);
            if (result == TravelResult.Arrived && !Face(Player.position, dt)) return TravelResult.Moving;
            return result;
        }

        public TravelResult StepReturn(float dt) => StepReturn(SessionState.CameraTarget, dt);

        public TravelResult StepReturn(string targetId, float dt)
        {
            Transform destination = targetId == SessionState.SetBackTarget && Station && Station.ReturnPoint
                ? Station.ReturnPoint : returnPoint;
            var result = Travel(destination.position, ArrivalRadius, dt);
            if (result == TravelResult.Arrived && !Face(Player.position, dt)) return TravelResult.Moving;
            return result;
        }

        TravelResult Travel(Vector3 goal, float stoppingDistance, float dt, bool restrictPassage = false)
        {
            if (FailureCode.Length > 0) return TravelResult.Failed;
            bool following = stoppingDistance > ArrivalRadius;
            if (corners == null || (plannedGoal - goal).sqrMagnitude > (following ? .09f : .0001f))
            {
                var path = new NavMeshPath();
                if (!PathTo(goal, path)) return Fail("RC_NAV_NO_PATH");
                corners = path.corners;
                nextCorner = 0;
                plannedGoal = goal;
            }
            // Require a complete path and actual physical proximity; an empty/partial path
            // or unknown remaining distance can never be used as an arrival signal.
            float arrivalDistance = stoppingDistance + (waitingForPlayer ? .15f : 0f);
            if (Vector3.Distance(Lin.position, goal) <= arrivalDistance)
            {
                RemainingDistance = 0f;
                progress = stageTime = stuckTime = 0f;
                walkingSpeed = 0f;
                waitingForPlayer = following && usesSceneBindings;
                return TravelResult.Arrived;
            }
            waitingForPlayer = false;
            while (nextCorner < corners.Length && Flat(corners[nextCorner] - Lin.position).sqrMagnitude <= .0064f)
                nextCorner++;
            var waypoint = nextCorner < corners.Length ? corners[nextCorner] : plannedGoal;
            var delta = Flat(waypoint - Lin.position);
            Face(waypoint, dt);
            float speed = TravelSpeed;
            if (usesSceneBindings)
            {
                // Brake for the end of the path and shorten steps during a tight turn.
                // A player reversing direction can otherwise send Lin skating sideways.
                float distanceToStop = Mathf.Max(0f, Vector3.Distance(Lin.position, goal) - stoppingDistance);
                float brakingSpeed = Mathf.Sqrt(2f * ResidenceAcceleration * distanceToStop);
                float alignment = Vector3.Dot(Lin.forward, delta.normalized);
                float desired = Mathf.Min(ResidenceSpeed, Mathf.Max(.12f, brakingSpeed))
                    * Mathf.InverseLerp(.5f, .96f, alignment);
                walkingSpeed = Mathf.MoveTowards(walkingSpeed, desired, ResidenceAcceleration * dt);
                speed = alignment > .5f ? walkingSpeed : 0f;
            }
            var step = Vector3.ClampMagnitude(delta, speed * dt);
            if (restrictPassage && Station && Station.InRestrictedArea(Lin.position + step))
            {
                walkingSpeed = 0f;
                corners = null;
                return TravelResult.Arrived;
            }
            lin.Move(step + Vector3.down * (.9f * dt));
            progress += delta.magnitude - Flat(waypoint - Lin.position).magnitude;
            RemainingDistance = Flat(waypoint - Lin.position).magnitude;
            for (int i = nextCorner + 1; i < corners.Length; i++)
                RemainingDistance += Flat(corners[i] - corners[i - 1]).magnitude;
            stageTime += dt;
            stuckTime += dt;
            if (progress >= .1f) { progress = stuckTime = 0f; }
            if (stuckTime >= 3f) return Fail("RC_NAV_NO_PROGRESS");
            if (stageTime >= 20f) return Fail("RC_NAV_TIMEOUT");
            return TravelResult.Moving;
        }

        bool PathTo(Vector3 goal, NavMeshPath path)
        {
            NavMeshHit start, end;
            // The player can stand closer to a wall than the baked agent radius.
            if (!NavMesh.SamplePosition(Lin.position, out start, .6f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(goal, out end, .55f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete;
        }

        bool ClearSight(Vector3 origin, Vector3 delta, float distance)
        {
            RaycastHit hit;
            return !Physics.Raycast(origin, delta.normalized, out hit, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) || IsCameraCollider(hit.transform);
        }

        bool IsCameraCollider(Transform item) => item == cameraTarget || item.IsChildOf(cameraTarget);

        bool Face(Vector3 point, float dt)
        {
            var forward = Flat(point - Lin.position);
            if (forward.sqrMagnitude < .0001f) return true;
            facingDirection = forward.normalized;
            lastFacingFrame = Time.frameCount;
            var target = Quaternion.LookRotation(forward);
            if (usesSceneBindings)
            {
                float angle = Quaternion.Angle(Lin.rotation, target);
                float brakingSpeed = Mathf.Sqrt(2f * ResidenceTurnAcceleration * angle);
                turnSpeed = Mathf.MoveTowards(turnSpeed, Mathf.Min(ResidenceTurnSpeed, brakingSpeed),
                    ResidenceTurnAcceleration * dt);
                Lin.rotation = Quaternion.RotateTowards(Lin.rotation, target, turnSpeed * dt);
            }
            else Lin.rotation = target;
            FaceNamePlateToPlayer();
            return Quaternion.Angle(Lin.rotation, target) < 1f;
        }

        void FaceNamePlateToPlayer()
        {
            if (!namePlate || !nameLabel) return;
            var towardPlayer = Flat(view.transform.position - namePlate.position);
            if (towardPlayer.sqrMagnitude < .0001f) return;
            namePlate.rotation = Quaternion.LookRotation(towardPlayer);
            nameLabel.SetPositionAndRotation(namePlate.position + namePlate.forward * .025f,
                Quaternion.LookRotation(-towardPlayer));
        }

        TravelResult Fail(string code)
        {
            FailureCode = code;
            return TravelResult.Failed;
        }

        static void ResetActor(CharacterController actor, Transform anchor)
        {
            actor.enabled = false;
            actor.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            actor.enabled = true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDestroy()
        {
            if (navigation.valid) navigation.Remove();
            if (!runtimeLayout) return;
            if (navigationData) Destroy(navigationData);
            if (layoutMaterials != null)
                foreach (var material in layoutMaterials) if (material) Destroy(material);
        }
    }
}
