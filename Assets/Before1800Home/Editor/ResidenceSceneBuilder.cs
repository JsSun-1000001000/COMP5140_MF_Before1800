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
            var livingRoom = house.Find("Main");
            var roomParts = livingRoom.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToList();
            var viewThroughDoors = house.Find("Kitchen").GetComponentsInChildren<MeshRenderer>()
                .Concat(house.Find("Bedroom").GetComponentsInChildren<MeshRenderer>())
                .Concat(house.Find("General").GetComponentsInChildren<MeshRenderer>());
            // The exported hinged leaves are siblings of the room groups and door frames.
            var doors = house.Cast<Transform>().Where(t => t.name.StartsWith("DoorPhysic ", StringComparison.Ordinal))
                .SelectMany(t => t.GetComponentsInChildren<MeshRenderer>());
            var geometry = roomParts.Concat(viewThroughDoors).Concat(doors).Where(r => r.enabled).ToList();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Before 18 — Residence");
            var interior = Node("WeTogether apartment", root.transform, Vector3.zero);
            var shell = interior.gameObject.AddComponent<MeshFilter>();
            shell.sharedMesh = ResidenceMesh.Build(geometry, Folder + "/WeTogetherApartment.asset", out var surfaces);
            interior.gameObject.AddComponent<MeshRenderer>().sharedMaterials = surfaces;

            var collision = Node("Living room movement bounds", root.transform, Vector3.zero);
            var sources = new List<NavMeshBuildSource>();
            ColliderBox(collision, sources, "Living room floor", new Vector3(.5f, -.1f, .25f),
                new Vector3(9.0f, .2f, 8.5f), true);
            ColliderBox(collision, sources, "West boundary", new Vector3(-4.0f, 1.5f, .25f), new Vector3(.14f, 3, 8.6f));
            ColliderBox(collision, sources, "East boundary", new Vector3(5.0f, 1.5f, .25f), new Vector3(.14f, 3, 8.6f));
            ColliderBox(collision, sources, "North boundary", new Vector3(.5f, 1.5f, 4.45f), new Vector3(9, 3, .14f));
            ColliderBox(collision, sources, "South boundary", new Vector3(.5f, 1.5f, -4.00f), new Vector3(9, 3, .14f));
            var furniture = new HashSet<string> { "Sofa", "SofaChair", "SofaOttoman", "LivingTable", "TVTumb",
                "LivingRoomBookScaff", "LivingRoomBigTumb", "LivingRoomPlantTumb1", "HallwayTumb", "CornerSofa",
                "SofaTumb 1", "SofaTumb 2", "BigLamp", "CornerTumb", "LivingRoomTumb", "MiniShelvesStairs" };
            foreach (var item in roomParts.Where(r => furniture.Contains(r.name))) {
                var bounds = item.bounds;
                // One batched renderer contains the console and the high shelf above
                // the television. Its full bounding box would block the open sightline.
                if (item.name == "TVTumb") bounds.SetMinMax(bounds.min,
                    new Vector3(bounds.max.x, .64f, bounds.max.z));
                ColliderBox(collision, sources, item.name, bounds.center, bounds.size);
            }

            var assets = Node("Evidence and actor anchors", root.transform, Vector3.zero);
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

        private static void ColliderBox(Transform parent, List<NavMeshBuildSource> sources, string name,
            Vector3 position, Vector3 size, bool floor = false)
        {
            var box = Node(name, parent, position).gameObject.AddComponent<BoxCollider>();
            box.size = size;
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = box.transform.localToWorldMatrix,
                size = size, area = floor ? 0 : 1 });
        }

        private static NavMeshData Navigation(List<NavMeshBuildSource> sources)
        {
            const string path = Folder + "/ResidenceNavigation.asset";
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.overrideVoxelSize = true;
            settings.voxelSize = .075f;
            var navigation = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (navigation == null) {
                navigation = new NavMeshData(settings.agentTypeID) { name = "ResidenceNavigation" };
                AssetDatabase.CreateAsset(navigation, path);
            }
            if (!UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(navigation, settings, sources, new Bounds(Vector3.up, new Vector3(18, 8, 18))))
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
            var lamp = Node("Living room ceiling light", root, new Vector3(.49f, 2.66f, -.48f)).gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 11f;
            lamp.color = new Color(1f, .91f, .81f);
            lamp.intensity = .90f;
        }
    }
}
