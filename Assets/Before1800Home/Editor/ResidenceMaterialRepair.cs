using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Before1800.Home.Editor
{
    // This project runs the Built-in pipeline (GraphicsSettings.customRenderPipeline = null).
    // Magenta means a missing/unsupported shader (broken Poiyomi lock, URP-only shader, etc.).
    public static class ResidenceMaterialRepair
    {
        const string CloneFolder = "Assets/Before1800Home/RepairedMaterials";
        const string ResidenceScene = "Assets/Before1800Home/PartB_Residence.unity";
        const string CharacterFolder = "Assets/Before1800Home/CharacterArt";

        [MenuItem("Before 18/Repair magenta materials (Built-in)")]
        public static void RepairAll()
        {
            if (GraphicsSettings.currentRenderPipeline)
            {
                EditorUtility.DisplayDialog(
                    "Before 18",
                    "当前 GraphicsSettings 挂了 Scriptable Render Pipeline。本工程应按 Built-in 运行。\n" +
                    "请先在 Edit → Project Settings → Graphics 把 Scriptable Render Pipeline Settings 清空为 None，再运行本菜单。",
                    "OK");
                return;
            }

            var standard = Shader.Find("Standard");
            if (!standard) throw new System.InvalidOperationException("Built-in Standard shader missing.");

            EnsureFolder(CloneFolder);
            int characterFixed = RepairCharacterArt(standard);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ResidenceScene, OpenSceneMode.Single);
            int sceneFixed = RepairSceneRenderers(standard);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(
                "Before 18",
                $"已按 Built-in Standard 修复。\n角色材质：{characterFixed}\n场景 Renderer 槽位：{sceneFixed}\n克隆目录：{CloneFolder}",
                "OK");
        }

        static int RepairCharacterArt(Shader standard)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { CharacterFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat || Path.GetFileNameWithoutExtension(path) == "Face Layer") continue;
                if (!NeedsRepair(mat)) continue;
                ApplyStandard(mat, standard, softSkin: mat.name == "Body" || mat.name == "Face_0");
                if (mat.name == "Hair_0") ApplyCutout(mat);
                EditorUtility.SetDirty(mat);
                n++;
            }
            // Face Layer keeps the Built-in CG blush shader.
            var blush = AssetDatabase.LoadAssetAtPath<Material>(CharacterFolder + "/Face Layer.mat");
            var blushShader = AssetDatabase.LoadAssetAtPath<Shader>(CharacterFolder + "/Face Blush.shader");
            if (blush && blushShader && blush.shader != blushShader)
            {
                blush.shader = blushShader;
                blush.SetFloat("_Opacity", .55f);
                blush.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(blush);
                n++;
            }
            return n;
        }

        static int RepairSceneRenderers(Shader standard)
        {
            var cache = new Dictionary<Material, Material>();
            int slots = 0;
            foreach (var renderer in Object.FindObjectsOfType<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (!source) continue;
                    if (!NeedsRepair(source)) continue;
                    if (!cache.TryGetValue(source, out var clone))
                    {
                        clone = CreateClone(source, standard);
                        cache[source] = clone;
                    }
                    shared[i] = clone;
                    dirty = true;
                    slots++;
                }
                if (!dirty) continue;
                renderer.sharedMaterials = shared;
                EditorUtility.SetDirty(renderer);
            }
            return slots;
        }

        static bool NeedsRepair(Material material)
        {
            if (!material) return true;
            var shader = material.shader;
            if (!shader) return true;
            string name = shader.name ?? "";
            if (name == "Hidden/InternalErrorShader") return true;
            if (name.StartsWith("Hidden/Locked/.poiyomi", System.StringComparison.Ordinal)) return true;
            if (name.Contains("Universal Render Pipeline")) return true;
            if (!shader.isSupported) return true;
            return false;
        }

        static Material CreateClone(Material source, Shader standard)
        {
            string safe = string.Join("_", source.name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safe)) safe = "Material";
            string path = $"{CloneFolder}/{safe}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
            {
                if (NeedsRepair(existing)) ApplyStandard(existing, standard, false);
                return existing;
            }
            var clone = new Material(standard) { name = source.name + " (repaired)" };
            CopyAlbedo(source, clone);
            if (source.name.IndexOf("Hair", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.name.IndexOf("Fade", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.name.IndexOf("Curtain", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ApplyCutout(clone);
            AssetDatabase.CreateAsset(clone, path);
            return clone;
        }

        static void ApplyStandard(Material mat, Shader standard, bool softSkin)
        {
            Texture albedo = null;
            Color tint = Color.white;
            if (mat.HasProperty("_MainTex")) albedo = mat.GetTexture("_MainTex");
            if (!albedo && mat.HasProperty("_BaseMap")) albedo = mat.GetTexture("_BaseMap");
            if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");
            else if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
            mat.shader = standard;
            if (albedo) mat.SetTexture("_MainTex", albedo);
            mat.SetColor("_Color", softSkin ? new Color(.86f, .85f, .86f, 1f) : tint);
            mat.SetFloat("_Glossiness", .08f);
            mat.SetFloat("_Metallic", 0f);
        }

        static void CopyAlbedo(Material source, Material dest)
        {
            Texture albedo = null;
            Color tint = Color.white;
            if (source.HasProperty("_MainTex")) albedo = source.GetTexture("_MainTex");
            if (!albedo && source.HasProperty("_BaseMap")) albedo = source.GetTexture("_BaseMap");
            if (source.HasProperty("_Color")) tint = source.GetColor("_Color");
            else if (source.HasProperty("_BaseColor")) tint = source.GetColor("_BaseColor");
            if (albedo) dest.SetTexture("_MainTex", albedo);
            dest.SetColor("_Color", tint);
            dest.SetFloat("_Glossiness", .1f);
            dest.SetFloat("_Metallic", 0f);
        }

        static void ApplyCutout(Material mat)
        {
            mat.SetFloat("_Mode", 1f);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetFloat("_Cutoff", .45f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
