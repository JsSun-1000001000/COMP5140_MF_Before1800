using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Before1800.PartB
{
    // Place on a maintenance doorway, facing into the passage. Assign scene objects to
    // the fields below to use the same interactions on an authored set.
    public sealed class SetBackStation : MonoBehaviour
    {
        [SerializeField] Transform doorHinge;
        [SerializeField] float openAngle = -90f;
        [SerializeField] Transform doorControl, observationPoint;
        [Tooltip("Optional e5_return. Defaults to Lin's usual return anchor.")]
        [SerializeField] Transform returnPoint;
        [Tooltip("Visible objects representing the support and the production mark.")]
        [SerializeField] Renderer setBrace, setMark;
        [SerializeField] Transform routeProbe, routeSign, exitPoint;
        [Tooltip("Trigger covering the passage beyond Lin's investigation boundary.")]
        [SerializeField] BoxCollider restrictedArea;
        [Tooltip("Optional authored obstruction. While active, the route cannot be certified.")]
        [SerializeField] GameObject routeBlocker;

        readonly Collider[] routeHits = new Collider[24];
        Quaternion closedRotation;
        bool hasClosedRotation;

        public bool DoorOpen { get; private set; }
        public Transform DoorControl => doorControl;
        public Transform ObservationPoint => observationPoint;
        public Transform ReturnPoint => returnPoint;
        public Transform SetMark => setMark.transform;
        public Transform SetBrace => setBrace.transform;
        public Transform RouteProbe => routeProbe;
        public Transform RouteSign => routeSign;
        public Transform ExitPoint => exitPoint;
        public bool IsAvailable => isActiveAndEnabled && doorHinge && doorControl && observationPoint &&
            setBrace && setMark && routeProbe && routeSign && exitPoint && restrictedArea;

        public void ResetDoor()
        {
            DoorOpen = false;
            if (!doorHinge) return;
            if (!hasClosedRotation)
            {
                closedRotation = doorHinge.localRotation;
                hasClosedRotation = true;
            }
            doorHinge.localRotation = closedRotation;
            Physics.SyncTransforms();
        }

        public bool CanOpenDoor(Transform view) => !DoorOpen && AtDoorControl(view);
        public bool CanStartObservation(Transform view) => DoorOpen && AtDoorControl(view);

        public bool TryOpenDoor(Transform view)
        {
            if (!CanOpenDoor(view)) return false;
            doorHinge.localRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
            DoorOpen = true;
            Physics.SyncTransforms();
            return true;
        }

        bool AtDoorControl(Transform view)
        {
            return IsAvailable && view && !InRestrictedArea(view.position) &&
                InView(view.position, view.forward, doorControl, 2f, 35f, view.GetComponentInParent<CharacterController>()?.transform);
        }

        public bool AtObservation(Transform lin)
        {
            if (!IsAvailable || !lin || !observationPoint.gameObject.activeInHierarchy ||
                Vector3.Distance(lin.position, observationPoint.position) > .65f) return false;
            Vector3 towardSet = Vector3.ProjectOnPlane(SetMark.position - lin.position, Vector3.up);
            return Vector3.Angle(lin.forward, towardSet) <= 35f;
        }

        public bool InspectionVisible(Transform lin)
        {
            if (!DoorOpen || !AtObservation(lin) || !setMark.enabled || !setBrace.enabled) return false;
            Vector3 eyes = lin.position + Vector3.up * 1.55f;
            return InView(eyes, lin.forward, SetMark, 8f, 50f, lin) &&
                InView(eyes, lin.forward, SetBrace, 8f, 50f, lin);
        }

        public bool InRestrictedArea(Vector3 position)
        {
            if (!restrictedArea) return false;
            Vector3 local = restrictedArea.transform.InverseTransformPoint(position) - restrictedArea.center;
            Vector3 half = restrictedArea.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public bool CanCheckRoute(Transform player, Transform view)
        {
            if (!IsAvailable || !DoorOpen || !player || !view || !InRestrictedArea(player.position) ||
                !routeProbe.gameObject.activeInHierarchy || !exitPoint.gameObject.activeInHierarchy ||
                Vector3.Distance(player.position, routeProbe.position) > .8f ||
                !InView(view.position, view.forward, routeSign, 3f, 35f, player) ||
                (routeBlocker && routeBlocker.activeInHierarchy)) return false;

            // This probe certifies the marked short passage. A detour around it elsewhere on
            // the navigation mesh, or a floor with a physical obstruction, cannot pass.
            if (!NavMesh.SamplePosition(routeProbe.position, out var from, .35f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(exitPoint.position, out var to, .35f, NavMesh.AllAreas) ||
                NavMesh.Raycast(from.position, to.position, out _, NavMesh.AllAreas)) return false;
            Vector3 axis = Vector3.ProjectOnPlane(exitPoint.position - routeProbe.position, Vector3.up);
            if (axis.sqrMagnitude < .01f) return false;
            Vector3 centre = (routeProbe.position + exitPoint.position) * .5f + Vector3.up * .92f;
            int count = Physics.OverlapBoxNonAlloc(centre, new Vector3(.26f, .79f, axis.magnitude * .5f + .03f),
                routeHits, Quaternion.LookRotation(axis), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == routeHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!routeHits[i].transform.IsChildOf(player)) return false;
            return true;
        }

        static bool InView(Vector3 origin, Vector3 forward, Transform target, float range, float angle, Transform observer)
        {
            if (!target.gameObject.activeInHierarchy) return false;
            Vector3 ray = target.position - origin;
            if (ray.sqrMagnitude > range * range || Vector3.Angle(forward, ray) > angle) return false;
            return InteractionSight.Clear(origin, ray, observer, target);
        }

        internal static SetBackStation BuildGreybox(Transform parent, Material[] palette, Transform returnPoint)
        {
            Transform root = Node(parent, "E5 maintenance door - Part B test station", new Vector3(3.45f, 0f, -2.45f));
            root.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var station = root.gameObject.AddComponent<SetBackStation>();
            station.returnPoint = returnPoint;
            Material panel = palette[2], steel = palette[3], gold = palette[4], paint = palette[0];
            for (int side = -1; side <= 1; side += 2)
            {
                Carve(Box(root, "Passage side", new Vector3(1.17f * side, 1.09f, 1.22f),
                    new Vector3(.13f, 2.18f, 2.48f), panel));
                Box(root, "Door jamb", new Vector3(1.08f * side, 1.13f, 0f), new Vector3(.17f, 2.26f, .17f), steel);
            }
            Box(root, "Lintel", new Vector3(0f, 2.26f, 0f), new Vector3(2.32f, .17f, .17f), steel);
            station.doorHinge = Node(root, "Door hinge", new Vector3(-.99f, 0f, 0f));
            Box(station.doorHinge, "Maintenance door", new Vector3(.99f, 1.07f, 0f), new Vector3(1.98f, 2.14f, .09f), panel);
            station.doorControl = Box(root, "Door control", new Vector3(1.06f, 1.30f, -.15f), new Vector3(.21f, .38f, .10f), gold);
            Caption(root, "E5   |   MAINTENANCE", new Vector3(0f, 2.46f, -.09f), .047f);

            station.observationPoint = Node(root, "e5_set_observe", new Vector3(0f, .02f, -.96f));
            Transform brace = Box(root, "Set support", new Vector3(-.54f, 1.26f, 1.98f), new Vector3(.14f, 1.62f, .13f), gold);
            brace.localRotation = Quaternion.Euler(0f, 0f, -23f);
            Box(root, "Scenery crossbar", new Vector3(-.54f, 1.88f, 2.07f), new Vector3(1.05f, .12f, .13f), steel);
            Carve(Box(root, "Unfinished panel", new Vector3(0f, 1.14f, 2.22f),
                new Vector3(2.17f, 2.24f, .10f), paint));
            station.setBrace = brace.GetComponent<Renderer>();
            Transform mark = Box(root, "Production mark", new Vector3(.40f, 1.66f, 2.09f), new Vector3(.88f, .44f, .08f), steel);
            station.setMark = mark.GetComponent<Renderer>();
            Caption(root, "SET 04 / B", new Vector3(.40f, 1.67f, 2.041f), .027f);
            station.routeProbe = Node(root, "route_probe", new Vector3(.04f, .025f, .84f));
            station.exitPoint = Node(root, "Route clearance endpoint", new Vector3(.04f, .025f, 1.73f));
            station.routeSign = Box(root, "Route check plate", new Vector3(.54f, 1.18f, 2.08f), new Vector3(.94f, .30f, .09f), panel);
            Caption(root, "CHECK ROUTE", new Vector3(.54f, 1.18f, 2.026f), .022f);
            // Floor paint is flush and has no blocking collider.
            Transform paintStrip = Box(root, "Route probe floor mark", new Vector3(.04f, .012f, .84f), new Vector3(.85f, .014f, .10f), gold);
            paintStrip.GetComponent<Collider>().enabled = false;
            station.restrictedArea = root.gameObject.AddComponent<BoxCollider>();
            station.restrictedArea.isTrigger = true;
            station.restrictedArea.center = new Vector3(0f, 1f, 1.19f);
            station.restrictedArea.size = new Vector3(2.23f, 2.8f, 2.34f);
            return station;
        }

        // Compact east-wall closet for the WeTogether living room: no greybox wing required.
        // Local +Z points into the niche; the door faces the living room (-Z).
        internal static SetBackStation BuildResidenceAlcove(Transform parent, Transform returnPoint)
        {
            Material panel = Mat("Residence Closet Panel", new Color(.43f, .49f, .49f));
            Material steel = Mat("Residence Closet Steel", new Color(.18f, .20f, .22f));
            Material gold = Mat("Residence Closet Gold", new Color(.95f, .59f, .20f));
            Material paint = Mat("Residence Closet Paint", new Color(.78f, .77f, .70f));

            // Face the living room (-X). Local +Z extends into a shallow east-wall niche.
            Transform root = Node(parent, "E5 residence maintenance closet", new Vector3(4.15f, 0f, .55f));
            root.localRotation = Quaternion.Euler(0f, -90f, 0f);
            var station = root.gameObject.AddComponent<SetBackStation>();
            station.returnPoint = returnPoint;

            for (int side = -1; side <= 1; side += 2)
            {
                Carve(Box(root, "Closet side", new Vector3(.78f * side, 1.05f, .72f),
                    new Vector3(.10f, 2.10f, 1.55f), panel));
                Box(root, "Door jamb", new Vector3(.70f * side, 1.08f, 0f), new Vector3(.12f, 2.16f, .12f), steel);
            }
            Box(root, "Lintel", new Vector3(0f, 2.16f, 0f), new Vector3(1.52f, .14f, .12f), steel);
            station.doorHinge = Node(root, "Door hinge", new Vector3(-.66f, 0f, 0f));
            Box(station.doorHinge, "Maintenance closet door", new Vector3(.66f, 1.02f, 0f),
                new Vector3(1.32f, 2.04f, .07f), panel);
            station.doorControl = Box(root, "Door control", new Vector3(.72f, 1.22f, -.12f),
                new Vector3(.16f, .30f, .08f), gold);
            Caption(root, "E5  |  CLOSET", new Vector3(0f, 2.34f, -.08f), .034f);

            station.observationPoint = Node(root, "e5_set_observe", new Vector3(0f, .02f, -.78f));
            MarkFloor(station.observationPoint, gold);
            Transform brace = Box(root, "Set support", new Vector3(-.32f, 1.12f, 1.18f),
                new Vector3(.11f, 1.35f, .11f), gold);
            brace.localRotation = Quaternion.Euler(0f, 0f, -18f);
            Box(root, "Scenery crossbar", new Vector3(-.10f, 1.72f, 1.22f), new Vector3(.85f, .09f, .10f), steel);
            Carve(Box(root, "Unfinished panel", new Vector3(0f, 1.05f, 1.32f),
                new Vector3(1.42f, 2.05f, .08f), paint));
            station.setBrace = brace.GetComponent<Renderer>();
            Transform mark = Box(root, "Production mark", new Vector3(.28f, 1.48f, 1.24f),
                new Vector3(.62f, .32f, .06f), steel);
            station.setMark = mark.GetComponent<Renderer>();
            Caption(root, "SET 04 / B", new Vector3(.28f, 1.49f, 1.205f), .02f);

            station.routeProbe = Node(root, "route_probe", new Vector3(0f, .025f, .42f));
            MarkFloor(station.routeProbe, gold);
            station.exitPoint = Node(root, "Route clearance endpoint", new Vector3(0f, .025f, 1.05f));
            station.routeSign = Box(root, "Route check plate", new Vector3(.36f, 1.12f, 1.22f),
                new Vector3(.70f, .24f, .07f), panel);
            Caption(root, "CHECK", new Vector3(.36f, 1.12f, 1.178f), .018f);

            station.restrictedArea = root.gameObject.AddComponent<BoxCollider>();
            station.restrictedArea.isTrigger = true;
            station.restrictedArea.center = new Vector3(0f, 1f, .78f);
            station.restrictedArea.size = new Vector3(1.45f, 2.5f, 1.55f);
            station.openAngle = -95f;
            station.ResetDoor();
            return station;
        }

        static Material Mat(string name, Color color)
        {
            var shader = GraphicsSettings.currentRenderPipeline
                ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name, color = color };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .18f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .18f);
            return mat;
        }

        static void MarkFloor(Transform mark, Material gold)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var a = Box(mark, "Floor mark", new Vector3(side * .28f, .01f, 0f), new Vector3(.05f, .01f, .55f), gold);
                a.GetComponent<Collider>().enabled = false;
                var b = Box(mark, "Floor mark", new Vector3(0f, .01f, side * .28f), new Vector3(.55f, .01f, .05f), gold);
                b.GetComponent<Collider>().enabled = false;
            }
        }

        static Transform Node(Transform parent, string name, Vector3 position)
        {
            Transform item = new GameObject(name).transform;
            item.SetParent(parent, false);
            item.localPosition = position;
            return item;
        }

        static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            Transform item = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            item.name = name;
            item.SetParent(parent, false);
            item.localPosition = position;
            item.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item;
        }

        static void Carve(Transform wall)
        {
            var obstacle = wall.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
        }

        static void Caption(Transform parent, string caption, Vector3 position, float size)
        {
            var label = Node(parent, caption, position).gameObject.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.GetComponent<Renderer>().sharedMaterial = label.font.material;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = size;
            label.text = caption;
        }
    }
}
