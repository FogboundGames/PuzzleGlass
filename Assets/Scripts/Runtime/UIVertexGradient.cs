using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PuzzleGlass
{
    /// <summary>
    /// UI Graphic'e (Text/Image) köşe rengiyle degrade verir: dikeyde üst → alt ya da yatayda sol → sağ.
    /// Yeni doku/malzeme gerektirmez; Graphic'in kendi rengiyle çarpılır.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIVertexGradient : BaseMeshEffect
    {
        public Color colorA = Color.white; // üst ya da sol
        public Color colorB = Color.black; // alt ya da sağ
        public bool horizontal;

        private static readonly List<UIVertex> s_Verts = new List<UIVertex>(256);

        public void Set(Color a, Color b, bool isHorizontal)
        {
            colorA = a;
            colorB = b;
            horizontal = isHorizontal;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            s_Verts.Clear();
            vh.GetUIVertexStream(s_Verts);

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < s_Verts.Count; i++)
            {
                float v = horizontal ? s_Verts[i].position.x : s_Verts[i].position.y;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            float span = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < s_Verts.Count; i++)
            {
                UIVertex vert = s_Verts[i];
                float t = ((horizontal ? vert.position.x : vert.position.y) - min) / span;
                // dikeyde t=1 üst: colorA üstte olsun
                Color c = horizontal ? Color.Lerp(colorA, colorB, t) : Color.Lerp(colorB, colorA, t);
                vert.color = (Color32)((Color)vert.color * c);
                s_Verts[i] = vert;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(s_Verts);
        }
    }
}
