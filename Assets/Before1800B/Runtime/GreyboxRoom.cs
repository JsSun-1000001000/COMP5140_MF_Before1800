using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Before1800.PartB
{
    // Builds the editable room for the scene authoring pass from Unity primitives.
    internal sealed class GreyboxRoom
    {
        readonly Transform root;
        readonly Transform room;
        readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        internal readonly List<Material> Materials = new List<Material>();
        readonly Material chalk, concrete, slate, graphite, brass, jacket, skin, glass;

        public GreyboxRoom(Transform root)
        {
            this.root = root;
            room = Group(root, "Observation Room");
            chalk = Paint("Plaster", new Color(.78f, .77f, .70f));
            concrete = Paint("Concrete", new Color(.43f, .49f, .49f));
            slate = Paint("Slate", new Color(.21f, .33f, .34f));
            graphite = Paint("Graphite", new Color(.11f, .15f, .16f));
            brass = Paint("Signal Gold", new Color(.95f, .59f, .20f));
            jacket = Paint("Lin Jacket", new Color(.14f, .41f, .49f));
            skin = Paint("Skin", new Color(.78f, .55f, .38f));
            glass = Paint("Window", new Color(.65f, .82f, .83f));
        }

        public void Build(out Transform cameraTarget, out Transform observationPoint, out Transform returnPoint,
            out Transform withdrawPoint, out Transform playerStart)
        {
            var shell = Group(room, "Structure");
            Solid(shell, "Floor", V(0, -.15f, 0), V(12, .3f, 11), concrete, true);
            Solid(shell, "North wall", V(0, 1.85f, 5.5f), V(12, 3.7f, .24f), chalk);
            Solid(shell, "South wall", V(0, 1.85f, -5.5f), V(12, 3.7f, .24f), chalk);
            Solid(shell, "West wall", V(-6, 1.85f, 0), V(.24f, 3.7f, 11), chalk);
            Solid(shell, "East wall", V(6, 1.85f, 0), V(.24f, 3.7f, 11), chalk);
            Box(shell, "Ceiling", V(0, 3.82f, 0), V(12, .2f, 11), chalk);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(shell, "Wall base", V(0, .28f, side * 5.35f), V(11.78f, .56f, .05f), slate);
                Box(shell, "Wall base", V(side * 5.85f, .28f, 0), V(.05f, .56f, 10.7f), slate);
                Box(shell, "Ceiling strip", V(side * 2.4f, 3.64f, -.2f), V(.18f, .08f, 7), glass);
            }

            var window = Group(room, "Observation Window");
            Box(window, "Window frame", V(-2.55f, 2.04f, 5.32f), V(4.7f, 1.62f, .12f), graphite);
            for (int i = 0; i < 3; i++)
                Box(window, "Frosted glass", V(-4.1f + 1.55f * i, 2.05f, 5.23f), V(1.43f, 1.46f, .12f), glass);
            Solid(window, "Low cabinet", V(-3.8f, .41f, 4.55f), V(3.2f, .82f, .95f), slate);
            Box(window, "Countertop", V(-3.8f, .86f, 4.55f), V(3.3f, .10f, 1.02f), chalk);
            // A solid studio bench makes the inside and outside routes visibly different.
            var bench = Group(room, "Studio bench");
            Solid(bench, "Bench", V(-.18f, .42f, 1.05f), V(2.08f, .84f, .9f), slate);
            Box(bench, "Seat", V(-.18f, .88f, 1.05f), V(2.14f, .11f, .96f), chalk);
            Box(bench, "Seat edge", V(-.18f, .79f, .59f), V(1.90f, .045f, .035f), brass);

            cameraTarget = Group(room, "camera_test");
            cameraTarget.localPosition = V(2.65f, 2.12f, 4.88f);
            Box(cameraTarget, "Mount", V(0, 0, .36f), V(.20f, .18f, .52f), graphite, true);
            Box(cameraTarget, "Camera housing", V(0, 0, 0), V(.56f, .38f, .41f), graphite, true);
            Box(cameraTarget, "Face plate", V(0, 0, -.22f), V(.50f, .32f, .035f), chalk, true);
            var lens = Shape(cameraTarget, "Lens", PrimitiveType.Cylinder, V(0, 0, -.27f), V(.24f, .05f, .24f), graphite, true);
            lens.localRotation = Quaternion.Euler(90, 0, 0);
            Box(cameraTarget, "Status lamp", V(.2f, .10f, -.247f), V(.045f, .045f, .03f), brass);
            Box(room, "Test station sign", V(2.65f, 2.92f, 5.30f), V(3.35f, .52f, .09f), slate);
            Label(room, "Camera label", "CAMERA TEST", V(2.65f, 2.91f, 5.23f), .07f, Color.white);

            var anchors = Group(room, "Navigation Anchors");
            observationPoint = Group(anchors, "e1_observe_a", V(2.65f, .02f, 2.68f));
            returnPoint = Group(anchors, "e1_return", V(-.58f, .02f, -.67f));
            returnPoint.localRotation = Quaternion.Euler(0, 166, 0);
            withdrawPoint = Group(anchors, "withdraw_point", V(-3.80f, .02f, .1f));
            playerStart = Group(anchors, "player_start", V(0, .02f, -4.02f));
            playerStart.localRotation = Quaternion.Euler(0f, 6f, 0f);
            var mark = Group(room, "Observation floor mark", V(2.65f, .011f, 2.68f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box(mark, "Observation mark", V(0, 0, side * .62f), V(1.30f, .014f, .065f), brass);
                Box(mark, "Observation mark", V(side * .62f, 0, 0), V(.065f, .014f, 1.18f), brass);
            }

            LightAt("Soft key", LightType.Directional, V(-2, 3.3f, -3), new Color(1f, .94f, .84f), .94f,
                Quaternion.Euler(40, 24, 0));
            LightAt("Window fill", LightType.Point, V(-2.5f, 2.75f, 3.25f), new Color(.79f, .91f, 1f), .82f,
                Quaternion.identity);
            LightAt("Room fill", LightType.Point, V(2f, 2.9f, -2f), new Color(1f, .90f, .79f), 1.28f,
                Quaternion.identity);
        }

        public CharacterController CreateActor(string name, Vector3 at)
        {
            var actor = Group(root, name);
            actor.position = at;
            var controller = actor.gameObject.AddComponent<CharacterController>();
            controller.height = 1.77f;
            controller.radius = .28f;
            controller.center = new Vector3(0f, .89f, 0f);
            controller.skinWidth = .025f;
            controller.minMoveDistance = 0f;
            controller.stepOffset = .20f;
            return controller;
        }

        public void DressLin(Transform actor)
        {
            Shape(actor, "Jacket", PrimitiveType.Capsule, V(0, 1.05f, 0), V(.47f, .36f, .34f), jacket);
            Shape(actor, "Head", PrimitiveType.Sphere, V(0, 1.58f, .015f), V(.34f, .39f, .32f), skin);
            Shape(actor, "Hair", PrimitiveType.Sphere, V(0, 1.72f, -.005f), V(.35f, .19f, .34f), graphite);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(actor, "Leg", PrimitiveType.Capsule, V(side * .13f, .43f, 0), V(.20f, .32f, .21f), graphite);
                Box(actor, "Shoe", V(side * .13f, .106f, .055f), V(.20f, .16f, .31f), graphite);
                Shape(actor, "Sleeve", PrimitiveType.Capsule, V(side * .265f, .99f, 0), V(.16f, .24f, .18f), jacket);
                Shape(actor, "Hand", PrimitiveType.Sphere, V(side * .265f, .73f, .015f), V(.13f, .16f, .13f), skin);
                Box(actor, "Eye", V(side * .075f, 1.60f, .160f), V(.042f, .034f, .018f), graphite);
            }
            Box(actor, "Badge", V(-.09f, 1.17f, .156f), V(.095f, .055f, .034f), brass);
            Box(actor, "Name plate", V(0, 1.93f, 0), V(.48f, .22f, .022f), graphite);
            var tag = Label(actor, "Lin name", "LIN", V(0, 1.93f, .025f), .027f, Color.white);
            tag.localRotation = Quaternion.Euler(0, 180f, 0);
        }

        public NavMeshData BakeNavigation()
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            return NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(root.position, V(16, 7, 15)),
                Vector3.zero, Quaternion.identity);
        }

        void Solid(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool walkable = false)
        {
            var box = Box(parent, name, pos, size, mat, true);
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(box.position, box.rotation, Vector3.one),
                size = box.lossyScale,
                area = walkable ? 0 : 1
            });
        }

        Material Paint(string name, Color color)
        {
            bool urp = GraphicsSettings.currentRenderPipeline;
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (urp && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .18f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .18f);
            Materials.Add(mat);
            return mat;
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool solid = false) =>
            Shape(parent, name, PrimitiveType.Cube, pos, size, mat, solid);

        static Transform Shape(Transform parent, string name, PrimitiveType kind, Vector3 pos, Vector3 size,
            Material mat, bool solid = false)
        {
            var item = GameObject.CreatePrimitive(kind);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = pos;
            item.transform.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid)
            {
                var collider = item.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            }
            return item.transform;
        }

        static Transform Group(Transform parent, string name, Vector3 pos = default)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = pos;
            return node;
        }

        static Transform Label(Transform parent, string name, string caption, Vector3 pos, float size, Color color)
        {
            var node = Group(parent, name, pos);
            var text = node.gameObject.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.GetComponent<Renderer>().sharedMaterial = text.font.material;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = size;
            text.text = caption;
            text.color = color;
            return node;
        }

        void LightAt(string name, LightType type, Vector3 pos, Color color, float intensity, Quaternion rotation)
        {
            var node = Group(room, name, pos);
            node.localRotation = rotation;
            var light = node.gameObject.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = 11f;
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    }
}
