using UnityEngine;
using UnityEngine.UI;

namespace Before1800.PartB
{
    public sealed class PlayerUIViewGradient : MaskableGraphic
    {
        private Color32 from, to;
        private bool vertical;

        internal void Set(Color start, Color end, bool verticalFade)
        {
            from = start;
            to = end;
            vertical = verticalFade;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            var r = GetPixelAdjustedRect();
            mesh.Clear();
            mesh.AddVert(new Vector2(r.xMin,r.yMin),from,Vector2.zero);
            mesh.AddVert(new Vector2(r.xMin,r.yMax),vertical ? to : from,Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax,r.yMax),to,Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax,r.yMin),vertical ? from : to,Vector2.zero);
            mesh.AddTriangle(0,1,2);
            mesh.AddTriangle(0,2,3);
        }
    }
}
