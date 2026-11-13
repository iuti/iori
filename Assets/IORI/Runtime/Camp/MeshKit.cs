using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Iori.Camp
{
    /// <summary>Per-triangle attributes written into the camp vertex layout (see IORI/Camp/Painted).</summary>
    public struct FaceAttr
    {
        public float slot, alt, bright, channel, wet;
        public Color emit;
        public float swayA, swayB, swayC;

        public static FaceAttr Of(CampPalette.Slot s, float bright = 1f) => Of(s, s, bright);
        public static FaceAttr Of(CampPalette.Slot s, CampPalette.Slot alt, float bright = 1f)
            => new FaceAttr { slot = (int)s, alt = (int)alt, bright = bright, channel = -1, emit = Color.clear };
        /// <summary>Glowing only (no lit base color). Color is sRGB; intensity is applied in linear.</summary>
        public static FaceAttr Glow(Color c, float intensity, int channel)
        {
            var l = c.linear;
            return new FaceAttr { slot = -1, alt = -1, bright = 1, channel = channel, emit = new Color(l.r * intensity, l.g * intensity, l.b * intensity) };
        }
        public FaceAttr Sway(float a, float b, float c) { swayA = a; swayB = b; swayC = c; return this; }
        public FaceAttr Sway(float all) => Sway(all, all, all);
    }

    /// <summary>
    /// Collects flat-shaded triangles. Geometry is generated in the three.js mock's frame
    /// (right-handed, z towards the viewer) so the numbers match the mock; on the way in each
    /// point is mirrored to Unity's frame (z negated) and the winding is fixed from a hint
    /// direction that says which side is "outside".
    /// </summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Vector3> _nrm = new List<Vector3>();
        readonly List<Color> _col = new List<Color>();
        readonly List<Vector4> _d1 = new List<Vector4>();
        readonly List<Vector4> _d2 = new List<Vector4>();
        readonly List<Vector2> _uv0 = new List<Vector2>();

        public int VertexCount => _pos.Count;

        public static Vector3 U(Vector3 m) => new Vector3(m.x, m.y, -m.z);

        /// <summary>Adds a triangle given in mock coordinates. hint: a direction (mock frame) on the front side.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, in FaceAttr f)
        {
            Vector3 A = U(a), B = U(b), C = U(c), H = U(hint);
            float sa = f.swayA, sb = f.swayB, sc = f.swayC;
            var n = Vector3.Cross(B - A, C - A);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, H) < 0) { (B, C) = (C, B); (sb, sc) = (sc, sb); n = -n; }
            n.Normalize();
            var d1 = new Vector4(f.slot, f.alt, f.bright, f.channel);
            var d2 = new Vector4(f.wet, 0, 0, 0);
            Add(A, n, f.emit, sa, d1, d2, Vector2.zero);
            Add(B, n, f.emit, sb, d1, d2, Vector2.zero);
            Add(C, n, f.emit, sc, d1, d2, Vector2.zero);
        }

        /// <summary>A camera-facing quad (all four corners at the same point; the shader expands them).</summary>
        public void Billboard(Vector3 centerMock, Color color, Vector4 data)
        {
            var p = U(centerMock);
            Vector2[] cs = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (var c in cs) Add(p, Vector3.back, color, 0, data, Vector4.zero, c);
        }

        /// <summary>A quad lying on the ground (for blob shadows); y is per-corner so it follows the slope.</summary>
        public void GroundQuad(Vector3 centerMock, float sx, float sz, System.Func<float, float, float> height, float opacity)
        {
            Vector2[] cs = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            foreach (var c in cs)
            {
                float x = centerMock.x + c.x * sx * 0.5f, z = centerMock.z + c.y * sz * 0.5f;
                var p = U(new Vector3(x, height(x, z) + 0.03f, z));
                Add(p, Vector3.up, Color.clear, 0, new Vector4(opacity, 0, 0, 0), Vector4.zero, c);
            }
        }

        void Add(Vector3 p, Vector3 n, Color emit, float sway, Vector4 d1, Vector4 d2, Vector2 uv)
        {
            _pos.Add(p); _nrm.Add(n); _col.Add(new Color(emit.r, emit.g, emit.b, sway)); _d1.Add(d1); _d2.Add(d2); _uv0.Add(uv);
        }

        public Mesh Build(string name, Bounds? bounds = null)
        {
            var m = new Mesh { name = name, indexFormat = _pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_pos);
            m.SetNormals(_nrm);
            m.SetColors(_col);
            m.SetUVs(0, _uv0);
            m.SetUVs(1, _d1);
            m.SetUVs(2, _d2);
            var idx = new int[_pos.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.SetIndices(idx, MeshTopology.Triangles, 0, true);
            if (bounds.HasValue) m.bounds = bounds.Value;
            m.UploadMeshData(true);
            return m;
        }

        // ------------------------------------------------------------------
        // Shapes: triangle soups in local mock coordinates, centered like three.js geometries.
        // ------------------------------------------------------------------

        public sealed class Shape
        {
            public readonly List<Vector3> tris = new List<Vector3>();
            public Vector3 center;
            public void T(Vector3 a, Vector3 b, Vector3 c) { tris.Add(a); tris.Add(b); tris.Add(c); }
        }

        /// <summary>Adds a shape transformed by m (mock frame). attr(i) gives the attributes of face i.</summary>
        public void Add(Shape s, Matrix4x4 m, System.Func<int, FaceAttr> attr)
        {
            var c0 = m.MultiplyPoint3x4(s.center);
            for (int i = 0, f = 0; i < s.tris.Count; i += 3, f++)
            {
                var a = m.MultiplyPoint3x4(s.tris[i]);
                var b = m.MultiplyPoint3x4(s.tris[i + 1]);
                var c = m.MultiplyPoint3x4(s.tris[i + 2]);
                var cen = (a + b + c) / 3f;
                Tri(a, b, c, cen - c0, attr(f));
            }
        }

        public void Add(Shape s, Matrix4x4 m, FaceAttr attr) => Add(s, m, _ => attr);

        public static Shape Cylinder(float rTop, float rBot, float h, int seg, bool open = false)
        {
            var s = new Shape();
            float y0 = -h / 2, y1 = h / 2;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
                Vector3 b0 = new Vector3(Mathf.Sin(a0) * rBot, y0, Mathf.Cos(a0) * rBot), b1 = new Vector3(Mathf.Sin(a1) * rBot, y0, Mathf.Cos(a1) * rBot);
                Vector3 t0 = new Vector3(Mathf.Sin(a0) * rTop, y1, Mathf.Cos(a0) * rTop), t1 = new Vector3(Mathf.Sin(a1) * rTop, y1, Mathf.Cos(a1) * rTop);
                if (rTop > 1e-5f) { s.T(b0, b1, t1); s.T(b0, t1, t0); } else s.T(b0, b1, t0);
                if (!open)
                {
                    if (rTop > 1e-5f) s.T(new Vector3(0, y1, 0), t0, t1);
                    if (rBot > 1e-5f) s.T(new Vector3(0, y0, 0), b1, b0);
                }
            }
            return s;
        }

        public static Shape Cone(float r, float h, int seg, bool open = false) => Cylinder(0, r, h, seg, open);

        public static Shape Box(float w, float h, float d)
        {
            var s = new Shape();
            float x = w / 2, y = h / 2, z = d / 2;
            Vector3[] v =
            {
                new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(-x, y, -z),
                new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z),
            };
            int[] q = { 0, 1, 2, 3, 5, 4, 7, 6, 4, 0, 3, 7, 1, 5, 6, 2, 3, 2, 6, 7, 4, 5, 1, 0 };
            for (int i = 0; i < q.Length; i += 4) { s.T(v[q[i]], v[q[i + 1]], v[q[i + 2]]); s.T(v[q[i]], v[q[i + 2]], v[q[i + 3]]); }
            return s;
        }

        /// <summary>Icosahedron: big flat facets for stones and rocks.</summary>
        public static Shape Rock(float r)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var s = new Shape();
            for (int i = 0; i < f.Length; i += 3) s.T(v[f[i]].normalized * r, v[f[i + 1]].normalized * r, v[f[i + 2]].normalized * r);
            return s;
        }

        public static Shape Hemisphere(float r, int wSeg, int hSeg)
        {
            var s = new Shape();
            for (int j = 0; j < hSeg; j++)
            {
                float th0 = j / (float)hSeg * Mathf.PI / 2, th1 = (j + 1) / (float)hSeg * Mathf.PI / 2;
                for (int i = 0; i < wSeg; i++)
                {
                    float p0 = i / (float)wSeg * Mathf.PI * 2, p1 = (i + 1) / (float)wSeg * Mathf.PI * 2;
                    Vector3 P(float th, float ph) => new Vector3(-Mathf.Cos(ph) * Mathf.Sin(th) * r, Mathf.Cos(th) * r, Mathf.Sin(ph) * Mathf.Sin(th) * r);
                    Vector3 a = P(th0, p0), b = P(th0, p1), c = P(th1, p1), d = P(th1, p0);
                    if (j == 0) s.T(a, c, d); else { s.T(a, b, c); s.T(a, c, d); }
                }
            }
            return s;
        }

        public static Shape Lathe(Vector2[] profile, int seg)
        {
            var s = new Shape();
            s.center = new Vector3(0, profile[profile.Length / 2].y * 0.5f, 0);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
                for (int j = 0; j < profile.Length - 1; j++)
                {
                    Vector3 P(Vector2 q, float a) => new Vector3(Mathf.Sin(a) * q.x, q.y, Mathf.Cos(a) * q.x);
                    Vector3 a = P(profile[j], a0), b = P(profile[j], a1), c = P(profile[j + 1], a1), d = P(profile[j + 1], a0);
                    s.T(a, b, c); s.T(a, c, d);
                }
            }
            return s;
        }

        public static Shape Torus(float R, float r, int radial, int tubular)
        {
            var s = new Shape();
            for (int j = 0; j < radial; j++)
                for (int i = 0; i < tubular; i++)
                {
                    Vector3 P(int jj, int ii)
                    {
                        float u = ii / (float)tubular * Mathf.PI * 2, v = jj / (float)radial * Mathf.PI * 2;
                        return new Vector3((R + r * Mathf.Cos(v)) * Mathf.Cos(u), (R + r * Mathf.Cos(v)) * Mathf.Sin(u), r * Mathf.Sin(v));
                    }
                    Vector3 a = P(j, i), b = P(j + 1, i), c = P(j + 1, i + 1), d = P(j, i + 1);
                    s.T(a, b, d); s.T(b, c, d);
                }
            return s;
        }

        /// <summary>Euler rotation in three.js order ('XYZ'), radians.</summary>
        public static Quaternion Euler3(float rx, float ry, float rz)
        {
            return Quaternion.AngleAxis(rx * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(ry * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward);
        }

        public static Matrix4x4 TRS(Vector3 p, float rx = 0, float ry = 0, float rz = 0) => Matrix4x4.TRS(p, Euler3(rx, ry, rz), Vector3.one);
        public static Matrix4x4 TRS(Vector3 p, Vector3 scale, float rx = 0, float ry = 0, float rz = 0) => Matrix4x4.TRS(p, Euler3(rx, ry, rz), scale);
    }
}
