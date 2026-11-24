using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Before1800.Home.Editor
{
    public static class ResidenceCharacterBuilder
    {
        const string SourceScene = "Assets/Scenes/Game/Scene 3 - WeTogether.unity";
        const string ArtFolder = "Assets/Before1800Home/CharacterArt";
        const string IdleSource = "Assets/AnimationClip/Mita Idle_2.anim";
        const string WalkSource = "Assets/AnimationClip/Walk With Controller.anim";

        public static void Attach(Transform linRoot)
        {
            if (!linRoot) throw new ArgumentNullException(nameof(linRoot));
            if (linRoot.Find("Mita Visual")) throw new InvalidOperationException("The Mita rig is already attached.");
            var source = SceneManager.GetSceneByPath(SourceScene);
            bool previouslyLoaded = source.isLoaded;
            if (!previouslyLoaded) source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            try
            {
                Transform sourceModel = null;
                foreach (var root in source.GetRootGameObjects())
                    if (root.name == "World") sourceModel = root.transform.Find("Acts/Mita/MitaPerson Mita");
                if (!sourceModel) throw new InvalidOperationException("Scene 3 is missing its Mita character.");
                if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Before1800Home", "CharacterArt");
                var visual = new GameObject("Mita Visual").transform;
                visual.SetParent(linRoot, false);
                var bones = new Dictionary<Transform, Transform>();
                CopyBone(sourceModel.Find("Armature"), visual, bones);
                foreach (var part in sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (part.transform.parent == sourceModel && part.sharedMesh)
                        CopySkin(part, visual, bones);
                }
                var animator = visual.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = GetController();
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                visual.gameObject.AddComponent<ResidenceLocomotion>()
                    .SetBlinkClip(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Blink.anim"));
                animator.Rebind();
                animator.Play("Idle", 0, 0);
                animator.Update(.0001f);
                FitToFeet(visual, linRoot);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (!previouslyLoaded) EditorSceneManager.CloseScene(source, true);
            }
        }

        // Clone transform data only. Missing scripts, IK, audio, cameras and original gameplay
        // stay in their source scene. Every skin bone is remapped to the new hierarchy.
        static void CopyBone(Transform original, Transform parent, Dictionary<Transform, Transform> bones)
        {
            var bone = CopyTransform(original, parent);
            bones.Add(original, bone);
            foreach (Transform child in original) CopyBone(child, bone, bones);
        }

        static Transform CopyTransform(Transform original, Transform parent)
        {
            var copy = new GameObject(original.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = original.localPosition;
            copy.localRotation = original.localRotation;
            copy.localScale = original.localScale;
            return copy;
        }

        static void CopySkin(SkinnedMeshRenderer source, Transform visual, Dictionary<Transform, Transform> bones)
        {
            var renderer = CopyTransform(source.transform, visual).gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = GetCostumeMesh(source);
            renderer.rootBone = source.rootBone ? bones[source.rootBone] : null;
            renderer.bones = Array.ConvertAll(source.bones, b => b ? bones[b] : null);
            renderer.sharedMaterials = Array.ConvertAll(source.sharedMaterials, GetMaterial);
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = source.localBounds;
            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; ++i)
                renderer.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
        }

        // The source garment uses spring bones. Keep its original pleats and attach each
        // lower skirt panel to the nearby thighs so they support the cloth as Lin walks.
        static Mesh GetCostumeMesh(SkinnedMeshRenderer skin)
        {
            if (skin.name != "Body" && skin.name != "Skirt" && skin.name != "Sweater") return skin.sharedMesh;
            string path = ArtFolder + "/" + skin.name + " fitted.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
            mesh.name = skin.name + " fitted";
            if (skin.name == "Body")
            {
                // Original bind-pose metres in the Mita model. Keep the exposed thighs, neck
                // and hands; remove skin hidden inside the sweater or pleated skirt.
                var vertex = mesh.vertices;
                var triangles = mesh.triangles;
                var exposed = new List<int>(triangles.Length);
                var toRig = skin.transform.parent.worldToLocalMatrix * skin.localToWorldMatrix;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var centre = toRig.MultiplyPoint3x4((vertex[triangles[i]] + vertex[triangles[i+1]]
                        + vertex[triangles[i+2]]) / 3f);
                    if (centre.y <= .885f || centre.y >= 1.392f || Mathf.Abs(centre.x) >= .4f)
                    {
                        exposed.Add(triangles[i]);
                        exposed.Add(triangles[i+1]);
                        exposed.Add(triangles[i+2]);
                    }
                }
                mesh.SetTriangles(exposed, 0);
            }
            else if (skin.name == "Skirt")
            {
                BindSkirtToLegs(skin, mesh);
            }
            else
            {
                var bones = skin.bones;
                var stable = new int[bones.Length];
                for (int i = 0; i < bones.Length; ++i)
                {
                    var bone = bones[i];
                    while (bone && (bone.name.StartsWith("Skirt", StringComparison.Ordinal)
                        || bone.name.StartsWith("Sweater", StringComparison.Ordinal)
                        || bone.name.StartsWith("Tit ", StringComparison.Ordinal))) bone = bone.parent;
                    int baseIndex = Array.IndexOf(bones, bone);
                    stable[i] = baseIndex < 0 ? i : baseIndex;
                }
                var weights = mesh.boneWeights;
                for (int i = 0; i < weights.Length; ++i)
                {
                    var weight = weights[i];
                    weight.boneIndex0 = stable[weight.boneIndex0];
                    weight.boneIndex1 = stable[weight.boneIndex1];
                    weight.boneIndex2 = stable[weight.boneIndex2];
                    weight.boneIndex3 = stable[weight.boneIndex3];
                    weights[i] = weight;
                }
                mesh.boneWeights = weights;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void BindSkirtToLegs(SkinnedMeshRenderer skin, Mesh mesh)
        {
            var bones = skin.bones;
            int hips = Array.FindIndex(bones, b => b && b.name == "Hips");
            int left = Array.FindIndex(bones, b => b && b.name == "Left leg");
            int right = Array.FindIndex(bones, b => b && b.name == "Right leg");
            if (hips < 0 || left < 0 || right < 0)
                throw new InvalidOperationException("Mita's pelvis and both thigh bindings are required by the skirt.");

            var toRig = skin.transform.parent.worldToLocalMatrix * skin.localToWorldMatrix;
            var toMesh = toRig.inverse;
            var vertices = mesh.vertices;
            var weights = new BoneWeight[vertices.Length];
            var indices = new int[3];
            var amounts = new float[3];
            // Coordinates in the original rig's bind pose, in metres. The band stays on the pelvis.
            // A broad overlap across the centre lets the front and back pleats open between the thighs.
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toRig.MultiplyPoint3x4(vertices[i]);
                float hem = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.12f, .87f, p.y));
                float leftShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f, -.16f, p.x));
                // Ease the outer pleats in by up to twelve millimetres where the fingertips pass.
                // The waistband and central pleats keep their original positions.
                float side = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .17f, Mathf.Abs(p.x)));
                float lowerPanel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.105f, .955f, p.y));
                p.x -= Mathf.Sign(p.x) * (.012f * side * lowerPanel);
                vertices[i] = toMesh.MultiplyPoint3x4(p);
                indices[0] = hips;  amounts[0] = 1f - hem;
                indices[1] = left;  amounts[1] = hem * leftShare;
                indices[2] = right; amounts[2] = hem * (1f - leftShare);
                Array.Sort(amounts, indices);
                weights[i] = new BoneWeight {
                    boneIndex0 = indices[2], weight0 = amounts[2],
                    boneIndex1 = indices[1], weight1 = amounts[1],
                    boneIndex2 = indices[0], weight2 = amounts[0]
                };
            }
            mesh.vertices = vertices;
            mesh.boneWeights = weights;
        }

        static Material GetMaterial(Material original)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(original));
            string path = ArtFolder + "/" + stem + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            // GraphicsSettings.customRenderPipeline is None: use Built-in Standard.
            // Locked Poiyomi variants often compile to magenta on machines without a clean Poiyomi install.
            if (stem == "Face Layer")
            {
                var blush = AssetDatabase.LoadAssetAtPath<Shader>(ArtFolder + "/Face Blush.shader");
                if (!blush) throw new InvalidOperationException("Missing Mita face blush shader.");
                var result = existing ? existing : new Material(blush) { name = stem };
                result.shader = blush;
                result.mainTexture = original.mainTexture;
                result.SetFloat("_Opacity", .55f);
                result.renderQueue = (int)RenderQueue.Transparent;
                if (existing) EditorUtility.SetDirty(result);
                else AssetDatabase.CreateAsset(result, path);
                return result;
            }
            var shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("Missing Built-in Standard shader.");
            var material = existing ? existing : new Material(shader) { name = stem };
            material.shader = shader;
            var texture = original.GetTexture("_MainTex");
            if (!texture) texture = original.mainTexture;
            if (!texture) throw new InvalidOperationException("Missing Mita albedo on " + stem);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Glossiness", .08f);
            material.SetFloat("_Metallic", 0f);
            bool skin = stem == "Face_0" || stem == "Body";
            material.SetColor("_Color", skin ? new Color(.86f, .85f, .86f) : Color.white);
            if (stem == "Hair_0")
            {
                material.SetFloat("_Mode", 1f);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cutoff", .45f);
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (existing) EditorUtility.SetDirty(material);
            else AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static AnimatorController GetController()
        {
            var idleClip = ResidenceClip(IdleSource, "Residence Idle");
            // Scene 3 also assigns this native quaternion clip to Mita. Its arms swing at her sides.
            var walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkSource);
            if (!walkClip) throw new InvalidOperationException("Source walk missing at " + WalkSource);
            string path = ArtFolder + "/ResidenceLocomotion.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing) return existing;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Walk", AnimatorControllerParameterType.Bool);
            var strideRate = new AnimatorControllerParameter {
                name = "StrideRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
            };
            controller.AddParameter(strideRate);
            var machine = controller.layers[0].stateMachine;
            var idle = machine.AddState("Idle");
            var walk = machine.AddState("Walk");
            idle.motion = idleClip;
            walk.motion = walkClip;
            walk.speedParameter = "StrideRate";
            walk.speedParameterActive = true;
            machine.defaultState = idle;
            SetTransition(idle.AddTransition(walk), AnimatorConditionMode.If);
            SetTransition(walk.AddTransition(idle), AnimatorConditionMode.IfNot);
            return controller;
        }

        // The Scene 3 generic idle contains XYZ Euler keys, exported with Unity's ZXY flag.
        // Change just the order of those rotation curves. All authored keys, tangents, footstep
        // timing, blend shapes and loop settings stay identical to the source animation.
        static AnimationClip ResidenceClip(string sourcePath, string name)
        {
            string path = ArtFolder + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing) return existing;

            string yaml = File.ReadAllText(sourcePath);
            int start = yaml.IndexOf("  m_EulerCurves:", StringComparison.Ordinal);
            int end = yaml.IndexOf("  m_PositionCurves:", StringComparison.Ordinal);
            if (start < 0 || end <= start)
                throw new InvalidOperationException("Missing native Euler curves in " + sourcePath);
            string rotations = yaml.Substring(start, end - start);
            string corrected = rotations.Replace("m_RotationOrder: 4", "m_RotationOrder: 0");
            if (rotations == corrected)
                throw new InvalidOperationException("Unexpected exported rotation order in " + sourcePath);
            File.WriteAllText(path, yaml.Substring(0, start) + corrected + yaml.Substring(end));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!result) throw new InvalidOperationException("Could not import " + path);
            return result;
        }

        static void SetTransition(AnimatorStateTransition transition, AnimatorConditionMode mode)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = mode == AnimatorConditionMode.If ? .26f : .20f;
            transition.interruptionSource = TransitionInterruptionSource.Destination;
            transition.AddCondition(mode, 0f, "Walk");
        }

        static void FitToFeet(Transform visual, Transform linRoot)
        {
            float bottom = float.PositiveInfinity;
            float top = float.NegativeInfinity;
            var mesh = new Mesh();
            try
            {
                foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(mesh);
                    var localToActor = linRoot.worldToLocalMatrix * skin.localToWorldMatrix;
                    foreach (var vertex in mesh.vertices)
                    {
                        float height = localToActor.MultiplyPoint3x4(vertex).y;
                        bottom = Mathf.Min(bottom, height);
                        top = Mathf.Max(top, height);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            if (top - bottom < .1f) throw new InvalidOperationException("Mita has no valid standing mesh.");
            float scale = 1.64f / (top - bottom);
            visual.localScale = Vector3.one * scale;
            visual.localPosition = new Vector3(0, -bottom * scale, 0);
        }
    }
}
