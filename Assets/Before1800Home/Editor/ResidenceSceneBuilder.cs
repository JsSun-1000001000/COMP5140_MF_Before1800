using System;
using System.Collections.Generic;
using System.Linq;
using Before1800.PartB;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Before1800.Home.Editor
{
    public static class ResidenceSceneBuilder
    {
        private const string Folder = "Assets/Before1800Home";
        private const string SourceScene = "Assets/Scenes/Game/Scene 3 - WeTogether.unity";
        private const string ScenePath = Folder + "/PartB_Residence.unity";

        [MenuItem("Before 18/Build residence scene from WeTogether")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            var house = source.GetRootGameObjects().Select(r => r.transform.Find("House/HouseGame Tamagotchi/House"))
                .First(t => t != null);
            var fixedParts = new[] { "Main", "Kitchen", "Bedroom", "Toilet", "General", "DoorPhysic Exit" }
                .Select(name => house.Find(name)).ToArray();
            var clock = house.Find("Kitchen/WallClocks");
            var remote = house.Find("Main/RemoteTV");
            var geometry = fixedParts.SelectMany(part => part.GetComponentsInChildren<MeshRenderer>())
                .Where(r => r.enabled && r.transform != remote &&
                    (r.transform == clock || !r.transform.IsChildOf(clock))).ToList();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Before 18 — Residence");
            var interior = Node("WeTogether apartment", root.transform, Vector3.zero);
            var shell = interior.gameObject.AddComponent<MeshFilter>();
            shell.sharedMesh = ResidenceMesh.Build(geometry, Folder + "/WeTogetherApartment.asset", out var surfaces);
            interior.gameObject.AddComponent<MeshRenderer>().sharedMaterials = surfaces;

            var collision = Node("Apartment collision", root.transform, Vector3.zero);
            var sources = CopyCollision(collision, fixedParts);
            AddDoor(interior, house.Find("DoorPhysic Kitchen-Main"), 90f);
            AddDoor(interior, house.Find("DoorPhysic Main-Bedroom"), -90f);
            AddDoor(interior, house.Find("DoorPhysic Bedroom-Toilet"), -90f);

            var assets = Node("Evidence and actor anchors", root.transform, Vector3.zero);
            ResidenceClockBuilder.Build(root, assets, clock, remote, house.Find("Main/TV"));
            var spawn = Node("player_start", assets, new Vector3(2.90f, .02f, 2.35f));
            spawn.rotation = Quaternion.LookRotation(new Vector3(-.05f, 0f, -1f));
            var home = Node("e1_return", assets, new Vector3(2.73f, .02f, .10f));
            home.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(spawn.position - home.position, Vector3.up));
            var withdraw = Node("withdraw_point", assets, new Vector3(3.50f, .02f, -1.25f));
            var observe = Node("e1_observe_a", assets, new Vector3(-.70f, .02f, -2.65f));
            var cameraTest = SecurityCamera(assets);
            var player = Actor("Player", root.transform, spawn);
            var camera = Node("First Person Camera", player.transform, new Vector3(0, 1.60f, 0)).gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = .045f;
            camera.farClipPlane = 45f;
            camera.fieldOfView = 66f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.46f, .55f, .61f);
            camera.gameObject.AddComponent<AudioListener>();
            var lin = Actor("Lin", root.transform, home);
            ResidenceCharacterBuilder.Attach(lin.transform);
            FurnishLighting(root.transform);
            var world = root.AddComponent<GreyboxWorld>();
            world.ConfigureScene(player, lin, camera, cameraTest, observe, home, withdraw, spawn, Navigation(sources));
            root.AddComponent<PartBScene>();

            EditorSceneManager.CloseScene(source, true);
            VerifyRoute(world.NavigationData, new [] { spawn, home, observe, withdraw });
            VerifySight(observe, cameraTest);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Residence saved: {geometry.Count} imported apartment renderers, {surfaces.Length} material slots. {ScenePath}");
        }

        private static Transform Node(string name, Transform parent, Vector3 localPosition)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.localPosition = localPosition;
            return result;
        }

        private static CharacterController Actor(string name, Transform parent, Transform anchor)
        {
            var node = Node(name, parent, anchor.position);
            node.rotation = anchor.rotation;
            var body = node.gameObject.AddComponent<CharacterController>();
            body.height = 1.77f;
            body.center = Vector3.up * .89f;
            body.radius = .28f;
            body.skinWidth = .025f;
            body.stepOffset = .2f;
            body.minMoveDistance = 0;
            return body;
        }

        private static Transform SecurityCamera(Transform parent)
        {
            var device = Node(Before1800.PartB.SessionState.CameraTarget, parent, new Vector3(-.55f, 2.34f, -3.91f));
            var graphite = AssetDatabase.LoadAssetAtPath<Material>("Assets/Before1800B/Room/Graphite.mat");
            var casing = AssetDatabase.LoadAssetAtPath<Material>("Assets/Before1800B/Room/Plaster.mat");
            var indicator = AssetDatabase.LoadAssetAtPath<Material>("Assets/Before1800B/Room/Signal Gold.mat");
            Shape(device, "Wall mount", PrimitiveType.Cube, new Vector3(0, .04f, 0), new Vector3(.21f, .22f, .07f), graphite);
            var head = Node("Camera housing", device, new Vector3(0, 0, .15f));
            head.localRotation = Quaternion.Euler(12, 0, 0);
            Shape(head, "Case", PrimitiveType.Cube, Vector3.zero, new Vector3(.27f, .18f, .30f), casing);
            Shape(head, "Lens", PrimitiveType.Sphere, new Vector3(0, 0, .15f), new Vector3(.16f, .14f, .12f), graphite);
            Shape(head, "Recording light", PrimitiveType.Sphere, new Vector3(.10f, .045f, .15f), new Vector3(.03f, .03f, .025f), indicator);
            return device;
        }

        private static void Shape(Transform parent, string name, PrimitiveType kind, Vector3 pos, Vector3 size, Material mat)
        {
            var item = GameObject.CreatePrimitive(kind);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = pos;
            item.transform.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void AddDoor(Transform parent, Transform original, float angle)
        {
            // The exported model is Z-up. Keep its hinge position, but give the
            // gameplay transform a vertical Y axis and the original art its own rotation.
            var pivot = Node(original.name, parent, original.TransformPoint(original.GetComponent<HingeJoint>().anchor));
            foreach (var renderer in original.GetComponentsInChildren<MeshRenderer>()) {
                var part = Node(renderer.name, pivot, Vector3.zero);
                part.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                part.localScale = renderer.transform.lossyScale;
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            // The source "Block" trigger fits the entire leaf, including the latch end.
            var bounds = original.Find("Block").GetComponent<BoxCollider>().bounds;
            var leaf = pivot.gameObject.AddComponent<BoxCollider>();
            leaf.center = pivot.InverseTransformPoint(bounds.center);
            leaf.size = bounds.size;
            var door = pivot.gameObject.AddComponent<ApartmentDoor>();
            door.Configure(pivot, angle);
            door.ConfigureSounds(AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Door Open 1.ogg"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Door Close 1.ogg"));
        }

        private static List<NavMeshBuildSource> CopyCollision(Transform root, Transform[] parts)
        {
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var group in parts) {
                var room = Node(group.name, root, Vector3.zero);
                foreach (var original in group.GetComponentsInChildren<Collider>()) {
                    if (original.isTrigger || !original.enabled) continue;
                    var item = Node(original.name, room, original.transform.position);
                    item.rotation = original.transform.rotation;
                    item.localScale = original.transform.lossyScale;
                    var copy = (Collider)item.gameObject.AddComponent(original.GetType());
                    EditorUtility.CopySerialized(original, copy);
                    // The exported console's box spans the empty gap below its wall shelf.
                    if (original.name == "TVTumb" && copy is BoxCollider console) {
                        var size = console.size;
                        var center = console.center;
                        center.z += (.64f - size.z) / 2f;
                        console.size = new Vector3(size.x, size.y, .64f);
                        console.center = center;
                    }
                    // Shells include the floors and low door sills. Furniture should
                    // block the navmesh even when its top is flat enough to stand on.
                    var n = original.name;
                    bool walkable = n == "RoomMain" || n.StartsWith("Floor", StringComparison.Ordinal)
                        || n.StartsWith("Wall", StringComparison.Ordinal) || n.StartsWith("RoomDoorCage", StringComparison.Ordinal)
                        || n.Contains("Carpet") || n.Contains("LittleCover");
                    if (walkable) markups.Add(new NavMeshBuildMarkup { root = item, overrideArea = true, area = 0 });
                    if (n.StartsWith("RoomDoorCage ", StringComparison.Ordinal)) {
                        // The adjoining floor meshes end at the two wall faces, leaving
                        // the 30 cm thickness of the doorway unsupported.
                        var sill = Node("Floor under " + n, room, original.transform.position - .05f * Vector3.up);
                        sill.rotation = Quaternion.LookRotation(original.transform.TransformDirection(Vector3.right), Vector3.up);
                        sill.gameObject.AddComponent<BoxCollider>().size = new Vector3(.42f, .10f, 1.1f);
                        markups.Add(new NavMeshBuildMarkup { root = sill, overrideArea = true, area = 0 });
                    }
                }
            }
            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.PhysicsColliders, 1, markups, sources);
            return sources;
        }

        private static NavMeshData Navigation(List<NavMeshBuildSource> sources)
        {
            const string path = Folder + "/ResidenceNavigation.asset";
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .28f;
            settings.agentHeight = 1.77f;
            settings.agentClimb = .20f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .04f;
            var navigation = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (navigation == null) {
                navigation = new NavMeshData(settings.agentTypeID) { name = "ResidenceNavigation" };
                AssetDatabase.CreateAsset(navigation, path);
            }
            if (!UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(navigation, settings, sources, new Bounds(new Vector3(-2, .9f, 1), new Vector3(30, 3, 14))))
                throw new InvalidOperationException("The residence navigation bake failed.");
            EditorUtility.SetDirty(navigation);
            return navigation;
        }

        private static void VerifyRoute(NavMeshData navigation, Transform[] anchors)
        {
            var instance = NavMesh.AddNavMeshData(navigation);
            try {
                foreach (var anchor in anchors)
                    if (!NavMesh.SamplePosition(anchor.position, out _, .45f, NavMesh.AllAreas)) throw new InvalidOperationException("Unreachable: " + anchor.name);
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(anchors[1].position, anchors[2].position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("No complete path from Lin to the camera.");
            } finally { instance.Remove(); }
        }

        private static void VerifySight(Transform observation, Transform device)
        {
            Physics.SyncTransforms();
            Vector3 eye = observation.position + Vector3.up * 1.55f;
            if (Physics.Linecast(eye, device.position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.transform != device && !hit.transform.IsChildOf(device))
                throw new InvalidOperationException("Camera sightline blocked by " + hit.transform.name);
        }

        private static void FurnishLighting(Transform root)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.69f, .66f, .64f);
            RenderSettings.ambientIntensity = 1f;
            var sun = Node("Window daylight", root, new Vector3(-2, 4, -3)).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50, 15, 0);
            sun.color = new Color(1f, .93f, .84f);
            sun.intensity = .52f;
            AddCeilingLight(root, "Living room", new Vector3(.49f, 2.66f, -.48f), new Color(1f, .91f, .81f), 11f, .90f);
            AddCeilingLight(root, "Kitchen", new Vector3(7.80f, 2.45f, .8f), new Color(1f, .94f, .84f), 7f, .92f);
            AddCeilingLight(root, "Bedroom", new Vector3(-7.65f, 2.38f, -.75f), new Color(1f, .90f, .84f), 6f, .85f);
            AddCeilingLight(root, "Bathroom", new Vector3(-13.08f, 2.34f, .54f), new Color(.88f, .95f, 1f), 4.5f, .70f);
        }

        private static void AddCeilingLight(Transform root, string room, Vector3 position, Color color, float range, float intensity)
        {
            var lamp = Node(room + " ceiling light", root, position).gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = range;
            lamp.color = color;
            lamp.intensity = intensity;
        }
    }
}
