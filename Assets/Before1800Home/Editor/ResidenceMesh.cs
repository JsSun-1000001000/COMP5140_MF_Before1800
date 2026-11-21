using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Before1800.Home.Editor
{
    // The exported apartment uses shared static batches: a chair's MeshFilter can
    // reference the whole room's mesh. Copy only that renderer's draw ranges.
    internal static class ResidenceMesh
    {
        private sealed class Source
        {
            internal readonly Vector3[] positions, normals;
            internal readonly Vector2[] uv;
            internal readonly Dictionary<int, int[]> submeshes = new Dictionary<int, int[]>();

            internal Source(Mesh mesh)
            {
                positions = mesh.vertices;
                normals = mesh.normals;
                uv = mesh.uv;
            }

            internal int[] Triangles(Mesh mesh, int slot)
            {
                if (!submeshes.TryGetValue(slot, out var indices))
                    submeshes.Add(slot, indices = mesh.GetTriangles(slot));
                return indices;
            }
        }

        public static Mesh Build(IList<MeshRenderer> renderers, string path, out Material[] materials)
        {
            var sources = new Dictionary<Mesh, Source>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<List<int>>();
            var slots = new List<Material>();
            foreach (var renderer in renderers) {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var original = filter.sharedMesh;
                if (!sources.TryGetValue(original, out var data))
                    sources.Add(original, data = new Source(original));
                var materialSlots = renderer.sharedMaterials;
                var first = renderer.subMeshStartIndex;
                var localBounds = new Bounds();
                bool initialized = false;
                for (int i = 0; i < materialSlots.Length && first + i < original.subMeshCount; i++) {
                    foreach (var index in data.Triangles(original, first+i)) {
                        if (!initialized) { localBounds = new Bounds(data.positions[index], Vector3.zero); initialized = true; }
                        else localBounds.Encapsulate(data.positions[index]);
                    }
                }
                if (!initialized) continue;
                // Some exported batches store vertices in world space, while ordinary
                // mesh props store local vertices. The source renderer bounds distinguish them.
                var transform = renderer.transform.localToWorldMatrix;
                if (BoundsError(localBounds, Matrix4x4.identity, renderer.bounds) < BoundsError(localBounds, transform, renderer.bounds))
                    transform = Matrix4x4.identity;
                var normalTransform = transform.inverse.transpose;
                bool mirrored = transform.determinant < 0;
                var remap = new Dictionary<int, int>();
                for (int i = 0; i < materialSlots.Length && first + i < original.subMeshCount; i++) {
                    var material = materialSlots[i];
                    if (!material) continue;
                    int slot = slots.IndexOf(material);
                    if (slot < 0) { slot = slots.Count; slots.Add(material); triangles.Add(new List<int>()); }
                    var indices = data.Triangles(original, first + i);
                    for (int j = 0; j < indices.Length; j++) {
                        int index = indices[mirrored && j % 3 != 0 ? (j % 3 == 1 ? j + 1 : j - 1) : j];
                        if (!remap.TryGetValue(index, out var output)) {
                            remap.Add(index, output = positions.Count);
                            positions.Add(transform.MultiplyPoint3x4(data.positions[index]));
                            normals.Add(index < data.normals.Length ? normalTransform.MultiplyVector(data.normals[index]).normalized : Vector3.up);
                            uv.Add(index < data.uv.Length ? data.uv[index] : Vector2.zero);
                        }
                        triangles[slot].Add(output);
                    }
                }
            }
            if (positions.Count == 0) throw new InvalidOperationException("Source apartment has no renderable geometry.");
            var result = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (result == null) {
                result = new Mesh { name = "WeTogetherApartment" };
                AssetDatabase.CreateAsset(result, path);
            }
            result.Clear();
            result.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            result.SetVertices(positions);
            result.SetNormals(normals);
            result.SetUVs(0, uv);
            result.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) result.SetTriangles(triangles[i], i, false);
            result.RecalculateBounds();
            EditorUtility.SetDirty(result);
            materials = slots.ToArray();
            return result;
        }

        private static float BoundsError(Bounds local, Matrix4x4 toWorld, Bounds expected)
        {
            Vector3 x = toWorld.MultiplyVector(Vector3.right * local.extents.x);
            Vector3 y = toWorld.MultiplyVector(Vector3.up * local.extents.y);
            Vector3 z = toWorld.MultiplyVector(Vector3.forward * local.extents.z);
            var extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return (toWorld.MultiplyPoint3x4(local.center) - expected.center).sqrMagnitude + (extents - expected.extents).sqrMagnitude;
        }
    }
}
