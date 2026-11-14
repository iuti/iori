using UnityEngine;
using UnityEngine.UI;

namespace Iori.Ui
{
    /// <summary>How one shape looks. Colours are the usual screen colours (as in a browser).</summary>
    public struct ShapeStyle
    {
        public float radius;
        public Color32 top, bottom;
        /// <summary>Shows the blurred scene behind, tinted by the fill (the fill's alpha is how strong the tint is).</summary>
        public bool glass;
        /// <summary>The fill runs from a point a little above the middle outwards, instead of top to bottom.</summary>
        public bool radial;
        /// <summary>A thin light along the top edge, 0 to 1.</summary>
        public float topLight;
        /// <summary>A faint ring just inside the edge, 0 to 1.</summary>
        public float ring;
        /// <summary>A soft glow or shadow around the shape: its colour, how far it spreads, and how far it drops.</summary>
        public Color32 glow;
        public float glowBlur, glowDrop;

        public static ShapeStyle Fill(Color32 c, float radius) => new ShapeStyle { top = c, bottom = c, radius = radius };
        public static ShapeStyle Gradient(Color32 top, Color32 bottom, float radius) => new ShapeStyle { top = top, bottom = bottom, radius = radius };

        public ShapeStyle Glow(Color32 c, float blur, float drop = 0f) { glow = c; glowBlur = blur; glowDrop = drop; return this; }
        public ShapeStyle TopLight(float a) { topLight = a; return this; }
        public ShapeStyle Ring(float a) { ring = a; return this; }
        public ShapeStyle Radial() { radial = true; return this; }
        public ShapeStyle Glass() { glass = true; return this; }

        public static ShapeStyle Lerp(in ShapeStyle a, in ShapeStyle b, float t)
        {
            var s = t < 0.5f ? a : b;
            s.radius = Mathf.Lerp(a.radius, b.radius, t);
            s.top = Color32.Lerp(a.top, b.top, t);
            s.bottom = Color32.Lerp(a.bottom, b.bottom, t);
            s.topLight = Mathf.Lerp(a.topLight, b.topLight, t);
            s.ring = Mathf.Lerp(a.ring, b.ring, t);
            // a glow that is absent on one side takes the other side's colour, so it fades instead of changing hue
            var ga = a.glow.a == 0 ? new Color32(b.glow.r, b.glow.g, b.glow.b, 0) : a.glow;
            var gb = b.glow.a == 0 ? new Color32(a.glow.r, a.glow.g, a.glow.b, 0) : b.glow;
            s.glow = Color32.Lerp(ga, gb, t);
            s.glowBlur = Mathf.Lerp(a.glow.a == 0 ? b.glowBlur : a.glowBlur, b.glow.a == 0 ? a.glowBlur : b.glowBlur, t);
            s.glowDrop = Mathf.Lerp(a.glowDrop, b.glowDrop, t);
            return s;
        }
    }

    /// <summary>
    /// One drawn shape of the UI: a rounded box, its fill, its glow, its top light. It holds no picture;
    /// the shader "IORI/UI/Shape" draws it from the numbers put into the vertices here.
    /// The canvas must pass the extra vertex channels (UiRoot sets that up).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiShape : MaskableGraphic
    {
        ShapeStyle _style;
        Texture _picture;
        static Material s_material;

        public ShapeStyle Style
        {
            get => _style;
            set { _style = value; SetVerticesDirty(); }
        }

        /// <summary>A picture to fill the shape with (stretched over it), or null.</summary>
        public Texture Picture
        {
            get => _picture;
            set { _picture = value; SetMaterialDirty(); SetVerticesDirty(); }
        }

        public static Material SharedMaterial
        {
            get
            {
                if (s_material) return s_material;
                var shader = Resources.Load<Shader>("IoriUI/Shape");
                if (!shader) shader = Shader.Find("IORI/UI/Shape");
                if (!shader)
                {
                    Debug.LogError("[IORI] UI のシェーダー IoriUI/Shape が見つかりません");
                    return Canvas.GetDefaultCanvasMaterial();
                }
                s_material = new Material(shader) { name = "IORI UI Shape", hideFlags = HideFlags.HideAndDontSave };
                return s_material;
            }
        }

        public override Material defaultMaterial => SharedMaterial;
        public override Texture mainTexture => _picture ? _picture : s_WhiteTexture;

        static float Pack(Color32 c) => c.r * 65536f + c.g * 256f + c.b;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            var s = _style;
            Vector2 c = r.center, half = r.size * 0.5f;
            float radius = Mathf.Min(s.radius, Mathf.Min(half.x, half.y));
            bool hasGlow = s.glow.a > 0 && s.glowBlur > 0f;
            // room around the box for the glow (three sigmas) and for the smoothed edge
            float m = hasGlow ? s.glowBlur * 1.5f + Mathf.Abs(s.glowDrop) + 2f : 1.5f;
            float kind = (s.glass ? 1f : 0f) + (s.radial ? 2f : 0f) + (_picture ? 4f : 0f);

            var v = UIVertex.simpleVert;
            v.color = color;
            v.uv1 = new Vector4(radius, s.glowBlur * 0.5f, s.topLight, kind);
            v.uv2 = new Vector4(Pack(s.top), s.top.a / 255f, Pack(s.bottom), s.bottom.a / 255f);
            v.uv3 = new Vector4(Pack(s.glow), hasGlow ? s.glow.a / 255f : 0f, s.glowDrop, s.ring);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 1) ? -1f : 1f;
                float sy = (i == 0 || i == 3) ? -1f : 1f;
                var local = new Vector2(sx * (half.x + m), sy * (half.y + m));
                v.position = new Vector3(c.x + local.x, c.y + local.y, 0f);
                v.uv0 = new Vector4(local.x, local.y, half.x, half.y);
                vh.AddVert(v);
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }

    /// <summary>Something that can be pressed but draws nothing (the press area of a button, the area outside a panel).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiHit : Graphic
    {
        public override void SetMaterialDirty() { }
        public override void SetVerticesDirty() { }
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
