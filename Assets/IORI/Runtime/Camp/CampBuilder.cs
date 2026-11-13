using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using S = Iori.Camp.CampPalette.Slot;

namespace Iori.Camp
{
    /// <summary>
    /// Builds the camp's geometry from code: faceted ground with puddle dips, the props, the
    /// framing trees, silhouette layers of conifers, the far ridges with town lights, water and rain.
    /// The layout is the three.js mock's (scene3.js), mirrored into Unity's frame. Props follow
    /// the anchor transforms in the scene, so they can be moved in the Editor; the generated
    /// objects are not saved with the scene and are rebuilt when an anchor moves.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    public sealed class CampBuilder : MonoBehaviour
    {
        public const int GroundLayer = 8;   // "CampGround": not drawn into the reflection
        public const int WaterLayer = 4;    // built-in "Water"
        public const int RainLayer = 1;     // built-in "TransparentFX"

        [Header("小物の置き場所(動かすと組み立て直す)")]
        public Transform fireAnchor;
        public Transform ioriAnchor;
        public Transform tableAnchor;
        public Transform radioAnchor;
        public Transform playerAnchor;
        public Transform tentAnchor;

        [Header("シェーダー")]
        public Shader painted;
        public Shader flat;
        public Shader glow;
        public Shader embers;
        public Shader rain;
        public Shader water;
        public Shader blob;

        [Header("量(スマホで重いときに減らす)")]
        [Range(40, 240)] public int groundResolution = 200;
        [Range(0, 6000)] public int grassTufts = 2600;
        [Range(0, 6000)] public int nearBlades = 2400;
        [Range(0, 1500)] public int rainStreaks = 900;
        public uint seed = 20261113;

        Transform _root;
        readonly List<Object> _owned = new List<Object>();
        readonly Dictionary<Transform, Matrix4x4> _anchorState = new Dictionary<Transform, Matrix4x4>();

        public Transform Root => _root;
        public Renderer Puddle { get; private set; }
        public Transform FlamesRoot { get; private set; }

        // mock-frame constants
        const float WaterY = -0.2f, PudY = -0.03f;
        static readonly Vector3 Cen = new Vector3(0, 0, 0.5f);
        static readonly float[][] Pud =
        {
            new[] { 0.25f, .55f, .5f, .55f }, new[] { -1.35f, .5f, .45f, .7f }, new[] { 1.55f, .55f, .45f, .6f }, new[] { 0.18f, 1.78f, .62f, .5f },
            new[] { -2.1f, -1.2f, .7f, .6f }, new[] { 1.0f, -1.6f, .8f, .5f }, new[] { 2.5f, -.2f, .6f, .8f }, new[] { -2.8f, 1.3f, .6f, .7f },
            new[] { 0.2f, -3.0f, .9f, .5f }, new[] { 3.4f, 1.6f, .7f, .6f }, new[] { -3.6f, -.4f, .6f, .8f },
        };

        CampRandom _rnd;
        float Rr(float a, float b) => _rnd.Range(a, b);
        float Rnd() => _rnd.Next();

        static Vector3 M(Transform t, Vector3 fallback) => t ? new Vector3(t.position.x, t.position.y, -t.position.z) : fallback;

        void OnEnable() => Rebuild();
        void OnDisable() => Clear();

        void Update()
        {
            if (Application.isPlaying) return;
            if (AnchorsMoved()) Rebuild();
        }

        bool AnchorsMoved()
        {
            bool moved = false;
            foreach (var t in new[] { fireAnchor, ioriAnchor, tableAnchor, radioAnchor, playerAnchor, tentAnchor })
            {
                if (!t) continue;
                var m = t.localToWorldMatrix;
                if (!_anchorState.TryGetValue(t, out var old) || old != m) { _anchorState[t] = m; moved = true; }
            }
            return moved;
        }

        [ContextMenu("組み立て直す")]
        public void Rebuild()
        {
            Clear();
            AnchorsMoved();
            if (!painted || !flat) return;
            _rnd = new CampRandom(seed);
            var go = new GameObject("Generated (自動生成。保存されない)") { hideFlags = HideFlags.DontSave };
            _root = go.transform;
            _root.SetParent(transform, false);
            BuildAll();
        }

        void Clear()
        {
            foreach (var o in _owned) if (o) DestroyImmediate(o);
            _owned.Clear();
            if (_root) DestroyImmediate(_root.gameObject);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name.StartsWith("Generated")) DestroyImmediate(c.gameObject);
            }
            _root = null;
            Puddle = null;
            FlamesRoot = null;
        }

        Material Mat(Shader s, string name, System.Action<Material> setup = null)
        {
            var m = new Material(s) { name = name, hideFlags = HideFlags.DontSave };
            setup?.Invoke(m);
            _owned.Add(m);
            return m;
        }

        Renderer Emit(string name, MeshKit kit, Material mat, int layer = 0, Bounds? bounds = null, Transform parent = null)
        {
            var mesh = kit.Build(name, bounds);
            mesh.hideFlags = HideFlags.DontSave;
            _owned.Add(mesh);
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = layer };
            go.transform.SetParent(parent ? parent : _root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        // ------------------------------------------------------------------ ground
        float Bowl(float x, float z)
        {
            float d = 0;
            foreach (var p in Pud)
            {
                float dx = (x - p[0]) / p[2], dz = (z - p[1]) / (p[2] * p[3]);
                float q = dx * dx + dz * dz;
                if (q > 2.2f) continue;
                q += (CampNoise.VNoise(x * 2.6f + p[0] * 3f, z * 2.6f + p[1]) - .5f) * .55f;
                d = Mathf.Max(d, 1 - CampNoise.SStep(.3f, 1.0f, q));
            }
            return d;
        }

        public float GroundH(float x, float z)
        {
            float h = (CampNoise.Fbm(x * .35f + 10, z * .35f + 3) - .5f) * .22f + (CampNoise.Fbm(x * 1.7f, z * 1.7f) - .5f) * .05f;
            float dC = Mathf.Sqrt((x - Cen.x) * (x - Cen.x) + (z - Cen.z) * (z - Cen.z));
            h *= CampNoise.SStep(1.4f, 4.6f, dC) * 0.9f + 0.1f;
            float shore = -6.6f + Mathf.Sin(x * .35f) * .9f + (CampNoise.VNoise(x * .2f, 5) - .5f) * 2.0f;
            h -= CampNoise.SStep(shore, shore - 4.5f, z) * 1.1f;
            h += CampNoise.SStep(7, 15, Mathf.Abs(x)) * 1.6f * CampNoise.SStep(-14, -2, z);
            h -= Bowl(x, z) * .075f;
            return h;
        }

        FaceAttr Patch(float cx, float cz, float h)
        {
            float u = cx * 2.3f + cz * .9f, v = cz * 2.6f - cx * .6f;
            float iu = Mathf.Floor(u), iv = Mathf.Floor(v);
            float tri = ((u - iu) + (v - iv) > 1) ? 1 : 0;
            float r = CampNoise.Hash2(iu * 2 + tri + .5f, iv + .25f), r2 = CampNoise.Hash2(iu + 7.3f, iv * 2 + tri + 1.7f);
            float n = CampNoise.Fbm(cx * .5f + 4, cz * .5f);
            float dC = Mathf.Sqrt((cx - Cen.x) * (cx - Cen.x) + (cz - Cen.z) * (cz - Cen.z));
            S slot = r < .5f ? S.GrassA : S.GrassB, alt = r < .5f ? S.GrassB : S.GrassA;
            if (r2 < .14f + .34f * CampNoise.SStep(.42f, .68f, n)) { slot = S.GroundShade; alt = S.GrassA; }
            float dd = CampNoise.SStep(1.9f, .6f, dC + (n - .5f) * 1.4f);
            if (r2 < dd * .92f) { slot = r < .55f ? S.Dirt : S.Dirt2; alt = r < .55f ? S.Dirt2 : S.Dirt; }
            if (h < WaterY + .3f && r2 < CampNoise.SStep(WaterY + .3f, WaterY + .05f, h) + .1f) { slot = S.Sand; alt = S.Dirt2; }
            var f = FaceAttr.Of(slot, alt, .9f + .2f * r);
            f.wet = h < PudY + .035f ? 1f : 0f;
            return f;
        }

        void BuildGround()
        {
            int NX = groundResolution, NZ = Mathf.RoundToInt(groundResolution * 0.8f);
            var gx = new float[(NX + 1) * (NZ + 1)]; var gz = new float[gx.Length]; var gy = new float[gx.Length];
            for (int j = 0; j <= NZ; j++)
                for (int i = 0; i <= NX; i++)
                {
                    float u = -1 + 2f * i / NX, v = -1.12f + 1.84f * j / NZ;
                    int k = j * (NX + 1) + i;
                    float jx = CampNoise.Hash2(i * 1.3f + .7f, j * 2.1f) - .5f, jz = CampNoise.Hash2(i * 3.7f, j * 1.9f + .3f) - .5f;
                    float x = 45 * (.25f * u + .75f * u * u * u) + jx * (.06f + .5f * Mathf.Abs(u));
                    float z = .5f + 35 * (.25f * v + .75f * v * v * v) + jz * (.05f + .4f * Mathf.Abs(v));
                    gx[k] = x; gz[k] = z; gy[k] = GroundH(x, z);
                }
            var kit = new MeshKit();
            void Put(int a, int b, int c)
            {
                var A = new Vector3(gx[a], gy[a], gz[a]); var B = new Vector3(gx[b], gy[b], gz[b]); var C = new Vector3(gx[c], gy[c], gz[c]);
                var cen = (A + B + C) / 3f;
                kit.Tri(A, B, C, Vector3.up, Patch(cen.x, cen.z, cen.y));
            }
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                {
                    int a = j * (NX + 1) + i, b = a + 1, c = a + NX + 1, d = c + 1;
                    if (((i + j) & 1) == 1) { Put(a, c, b); Put(b, c, d); } else { Put(a, c, d); Put(a, d, b); }
                }
            Emit("Ground", kit, Mat(painted, "Ground"), GroundLayer);
        }

        // ------------------------------------------------------------------ far layers
        void BuildConifers(Vector3 cam)
        {
            var kit = new MeshKit();
            void Conifer(float x, float z, float hgt, int ci)
            {
                float y0 = GroundH(x, z) - .1f;
                int tiers = 6 + Mathf.FloorToInt(Rnd() * 2);
                float wid = Rr(.14f, .19f);
                float bright = 1f + Rr(-.03f, .03f);
                var at = FaceAttr.Of(S.Sil0 + ci, bright);
                kit.Add(MeshKit.Cylinder(hgt * .012f, hgt * .024f, hgt * .9f, 5, true), MeshKit.TRS(new Vector3(x, y0 + hgt * .45f, z)), at);
                for (int i = 0; i < tiers; i++)
                {
                    float t = i / (float)(tiers - 1);
                    float r = hgt * (wid * (1 - t * .86f) + .012f) * Rr(.82f, 1.15f), hh = hgt * (.24f - .1f * t);
                    kit.Add(MeshKit.Cone(r, hh, 6, true), MeshKit.TRS(new Vector3(x, y0 + hgt * (.2f + .7f * t) + hh * .5f, z), 0, Rnd() * 6, 0), at);
                }
            }
            var spots = new List<Vector3>
            {
                new Vector3(-6.4f, -2.2f, 10), new Vector3(-8.2f, 0.6f, 12), new Vector3(-5.6f, -5.4f, 8), new Vector3(-9.6f, -4.0f, 11), new Vector3(-7.3f, -6.6f, 7),
                new Vector3(6.9f, -2.6f, 10.5f), new Vector3(8.6f, 0.2f, 12.5f), new Vector3(5.9f, -5.3f, 7.5f), new Vector3(9.8f, -4.6f, 11), new Vector3(7.9f, -7.0f, 7),
                new Vector3(-11.5f, -7.5f, 9), new Vector3(11.8f, -8, 9), new Vector3(-12, -2, 13), new Vector3(12.4f, -2.5f, 13.5f), new Vector3(-6.6f, 2.4f, 13.5f), new Vector3(6.9f, 2.2f, 14),
            };
            for (int i = 0; i < 90; i++)
            {
                float side = Rnd() < .5f ? -1 : 1;
                float x = side * Rr(8.5f, 36), z = Rr(-13, 2.5f);
                if (GroundH(x, z) < WaterY + .3f) continue;
                spots.Add(new Vector3(x, z, Rr(6, 12)));
            }
            foreach (var s in spots)
            {
                float d = Mathf.Sqrt((s.x - cam.x) * (s.x - cam.x) + (s.y - cam.z) * (s.y - cam.z));
                Conifer(s.x, s.y, s.z, Mathf.Min(3, Mathf.FloorToInt(CampNoise.SStep(5, 34, d) * 3.99f)));
            }
            Emit("Conifers", kit, Mat(flat, "Conifers", m => { m.SetFloat("_FogAmount", 1); m.SetFloat("_Cull", 0); }));
        }

        float RidgeH(float x, float baseH, float amp, float freq, float sd) => baseH + amp * (CampNoise.Fbm(x * freq + sd, sd * 1.7f) - .4f) * 2;

        void Ridge(MeshKit kit, float z, float baseH, float amp, float freq, float teeth, S slot, float sd, float x0, float x1, int N)
        {
            Vector3 prevTop = default, prevBot = default;
            for (int i = 0; i <= N; i++)
            {
                float x = x0 + (x1 - x0) * i / N;
                float h = RidgeH(x, baseH, amp, freq, sd);
                if (teeth > 0)
                {
                    float add = 0;
                    foreach (var sp in new[] { 2.7f, 4.3f })
                    {
                        float u = x / sp + sd * sp; float cell = Mathf.Floor(u), f = u - cell;
                        float c0 = .3f + .4f * CampNoise.Hash2(cell, sd + sp);
                        float th = teeth * (.25f + 1.0f * CampNoise.Hash2(cell + 5, sd));
                        add = Mathf.Max(add, th * Mathf.Max(0, 1 - Mathf.Abs(f - c0) * 3.2f));
                    }
                    h += add;
                }
                var top = new Vector3(x, Mathf.Max(h, .2f), z); var bot = new Vector3(x, -2, z);
                if (i > 0)
                {
                    var at = FaceAttr.Of(slot);
                    kit.Tri(prevTop, prevBot, top, Vector3.forward, at);
                    kit.Tri(prevBot, bot, top, Vector3.forward, at);
                }
                prevTop = top; prevBot = bot;
            }
        }

        void BuildFar()
        {
            var kit = new MeshKit();
            Ridge(kit, -110, 2.0f, 2.2f, .012f, 2.0f, S.Sil3, 3.1f, -420, 420, 1800);
            Ridge(kit, -210, 17, 26, .017f, 0, S.Hill0, 9.4f, -900, 900, 420);
            Ridge(kit, -430, 26, 36, .0032f, 0, S.Hill1, 17.2f, -1200, 1200, 200);
            Ridge(kit, -760, 40, 60, .002f, 0, S.Hill2, 4.4f, -1800, 1800, 160);
            Emit("Ridges", kit, Mat(flat, "Ridges", m => { m.SetFloat("_FogAmount", 0); m.SetFloat("_Cull", 0); }));

            if (!glow) return;
            var lights = new MeshKit();
            var col = CampPalette.Hex("#ffd49a").linear;
            int count = 0;
            for (int i = 0; i < 2600 && count < 520; i++)
            {
                float x = Rr(-190, 230);
                float top = RidgeH(x, 17, 26, .017f, 9.4f);
                float y = .6f + Mathf.Pow(Rnd(), 1.9f) * top * .72f;
                float dens = CampNoise.Fbm(x * .045f + 2f, y * .12f);
                if (y > 5 && dens < .5f) continue;
                lights.Billboard(new Vector3(x, y, -209.5f), col, new Vector4(.9f, 3, 1, 1));
                count++;
            }
            Emit("Town lights", lights, Mat(glow, "Town lights"), 0, new Bounds(new Vector3(0, 20, 210), new Vector3(600, 100, 20)));
        }

        // ------------------------------------------------------------------ framing trees
        void BuildTrees()
        {
            var trunks = new MeshKit();
            var tips = new List<Vector3>();
            int fi = 0;
            FaceAttr TrunkAttr() { fi++; return FaceAttr.Of(Rnd() < .75f ? S.Tree : S.TreeHi, S.TreeHi); }
            void Seg(Vector3 a, Vector3 b, float r1, float r2)
            {
                var dv = b - a; float len = dv.magnitude;
                var q = Quaternion.FromToRotation(Vector3.up, dv.normalized);
                var shape = MeshKit.Cylinder(r2, r1, len, 5, true);
                trunks.Add(shape, Matrix4x4.TRS(a + dv * .5f, q, Vector3.one), _ => TrunkAttr());
            }
            void LeafTree(float x, float z, float hgt, float lean, float sd)
            {
                float y0 = GroundH(x, z) - .06f; const int N = 7; var pts = new Vector3[N + 1]; float r0 = hgt * .02f + .035f;
                for (int i = 0; i <= N; i++) { float t = i / (float)N; pts[i] = new Vector3(x + lean * hgt * t * t + Mathf.Sin(t * 5 + sd) * .05f, y0 + hgt * t, z + Mathf.Cos(t * 4 + sd) * .05f); }
                for (int i = 0; i < N; i++) Seg(pts[i], pts[i + 1], r0 * (1 - i / (float)N * .78f), r0 * (1 - (i + 1) / (float)N * .78f));
                for (int b = 0; b < 6; b++)
                {
                    float t = .42f + .55f * b / 5f; int k = Mathf.Min(N - 1, Mathf.FloorToInt(t * N));
                    var bas = Vector3.Lerp(pts[k], pts[k + 1], t * N - k);
                    float ang = sd * 3 + b * 2.4f + Rr(-.4f, .4f); float len = hgt * Rr(.2f, .34f);
                    float inward = -Mathf.Sign(x) * .35f;
                    var tip = bas + new Vector3((Mathf.Cos(ang) * .8f + inward) * len, len * Rr(.35f, .7f), Mathf.Sin(ang) * len * .5f);
                    var mid = Vector3.Lerp(bas, tip, .55f); mid.y += len * .08f;
                    Seg(bas, mid, r0 * .34f, r0 * .2f); Seg(mid, tip, r0 * .2f, r0 * .08f);
                    tips.Add(tip); tips.Add(Vector3.Lerp(mid, tip, .5f));
                }
                tips.Add(pts[N]);
            }
            float[][] trees =
            {
                new[] { -1.28f, -1.2f, 5.2f, .05f, 1.1f }, new[] { 1.24f, -1.15f, 4.8f, -.06f, 2.3f }, new[] { -2.7f, .6f, 5.6f, .04f, 3.7f }, new[] { 2.9f, .2f, 5.4f, -.05f, 4.1f },
                new[] { -3.9f, -2.2f, 6f, .03f, 5.9f }, new[] { 4.1f, -1.8f, 6.2f, -.03f, 6.4f }, new[] { -1.25f, 2.6f, 5f, .03f, 7.7f }, new[] { 1.2f, 2.4f, 5.2f, -.03f, 8.2f },
            };
            foreach (var t in trees) LeafTree(t[0], t[1], t[2], t[3], t[4]);
            Emit("Tree trunks", trunks, Mat(painted, "Tree trunks", m => m.SetFloat("_Cull", 0)));

            // loose dark leaves: small diamonds that sway
            var leaves = new MeshKit();
            var leafShape = new MeshKit.Shape();
            leafShape.T(new Vector3(0, 0, 0), new Vector3(.05f, .07f, 0), new Vector3(0, .18f, 0));
            leafShape.T(new Vector3(0, 0, 0), new Vector3(0, .18f, 0), new Vector3(-.05f, .07f, 0));
            foreach (var tp in tips)
                for (int k = 0; k < 36; k++)
                {
                    var p = new Vector3(tp.x + Rr(-.6f, .6f), tp.y + Rr(-.35f, .45f), tp.z + Rr(-.4f, .4f));
                    float rx = Rr(-.6f, .6f), ry = Rnd() * 6, rz = Rr(-2.4f, 2.4f); float s = Rr(.8f, 1.7f);
                    var at = FaceAttr.Of(Rnd() < .75f ? S.Leaf : S.Tree).Sway(1f);
                    leaves.Add(leafShape, MeshKit.TRS(p, Vector3.one * s, rx, ry, rz), at);
                }
            // ferns right in front of the camera
            var fern = new MeshKit.Shape();
            fern.T(new Vector3(0, 0, 0), new Vector3(.05f, .3f, 0), new Vector3(0, .75f, 0));
            fern.T(new Vector3(0, 0, 0), new Vector3(0, .75f, 0), new Vector3(-.05f, .3f, 0));
            float[][] fs = { new[] { -.42f, 2.55f }, new[] { .44f, 2.7f }, new[] { -1.5f, 2.2f }, new[] { 1.7f, 2.1f }, new[] { -2.4f, 2.9f }, new[] { 2.6f, 3.0f } };
            foreach (var f in fs)
                for (int k = 0; k < 9; k++)
                {
                    var p = new Vector3(f[0] + Rr(-.06f, .06f), GroundH(f[0], f[1]) - .02f, f[1] + Rr(-.06f, .06f));
                    float rx = Rr(-.2f, .5f), ry = Rnd() * 6, rz = Rr(-.95f, .95f); float s = Rr(.6f, .95f);
                    var rot = Quaternion.AngleAxis(ry * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(rx * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward);
                    leaves.Add(fern, Matrix4x4.TRS(p, rot, new Vector3(s * 1.4f, s, s)), FaceAttr.Of(S.Leaf).Sway(0, .6f, 1f));
                }
            Emit("Leaves", leaves, Mat(flat, "Leaves", m => { m.SetFloat("_FogAmount", 1); m.SetFloat("_Cull", 0); }));
        }

        // ------------------------------------------------------------------ camp
        void BuildCamp(Vector3 FIRE, Vector3 IORI, Vector3 TABLE, Vector3 RADIO, Vector3 STUMP, Vector3 TENT)
        {
            var kit = new MeshKit();
            var glowKit = new MeshKit();
            var blobs = new MeshKit();

            // fire: ring of stones and logs
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2; float s = Rr(.08f, .13f);
                var p = new Vector3(FIRE.x + Mathf.Cos(a) * .46f, GroundH(FIRE.x, FIRE.z) + s * .3f, FIRE.z + Mathf.Sin(a) * .46f);
                kit.Add(MeshKit.Rock(s), MeshKit.TRS(p, new Vector3(1, .75f, 1), Rnd() * 3, Rnd() * 3, 0), FaceAttr.Of(i % 3 == 0 ? S.Rock : S.RockHi, S.Rock));
            }
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2 + .3f;
                var p = new Vector3(FIRE.x + Mathf.Cos(a) * .15f, .27f, FIRE.z + Mathf.Sin(a) * .15f);
                kit.Add(MeshKit.Cylinder(.04f, .055f, .68f, 5), MeshKit.TRS(p, -Mathf.Sin(a) * .55f, 0, Mathf.Cos(a) * .55f), FaceAttr.Of(S.Log));
            }
            // a few glowing coals between the logs
            for (int i = 0; i < 7; i++)
            {
                var p = new Vector3(FIRE.x + Rr(-.16f, .16f), .05f, FIRE.z + Rr(-.16f, .16f));
                kit.Add(MeshKit.Rock(Rr(.03f, .05f)), MeshKit.TRS(p, Rnd() * 3, Rnd() * 3, 0), FaceAttr.Glow(CampPalette.Hex(i % 2 == 0 ? "#ff5a1e" : "#ff9a3c"), 2.2f, 0));
            }
            glowKit.Billboard(new Vector3(FIRE.x, .55f, FIRE.z + .1f), CampPalette.Hex("#ff7a2a").linear, new Vector4(3.0f, 0, 0, .3f));
            blobs.GroundQuad(FIRE, 1.7f, 1.7f, GroundH, .45f);

            // flames: separate objects so CampFlames can flicker them
            FlamesRoot = new GameObject("Flames") { hideFlags = HideFlags.DontSave }.transform;
            FlamesRoot.SetParent(_root, false);
            var prof = new Vector2[9];
            for (int i = 0; i <= 8; i++) { float t = i / 8f; prof[i] = new Vector2(.2f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, .75f))), .9f) * (1 - t * .25f), t * .95f); }
            var flameShape = MeshKit.Lathe(prof, 6);
            var flameMat = Mat(flat, "Flames", m => { m.SetFloat("_FogAmount", 0); m.SetFloat("_Cull", 0); });
            void Flame(string col, float k, float s, float dx, float dz, float lean)
            {
                var fk = new MeshKit();
                fk.Add(flameShape, Matrix4x4.identity, FaceAttr.Glow(CampPalette.Hex(col), k, 7));
                var r = Emit("Flame", fk, flameMat, 0, null, FlamesRoot);
                var t = r.transform;
                t.localPosition = MeshKit.U(new Vector3(FIRE.x + dx, .1f, FIRE.z + dz));
                t.localScale = new Vector3(s, s * 1.05f, s);
                t.localRotation = Quaternion.Euler(0, 0, lean * Mathf.Rad2Deg);
            }
            Flame("#ff4d1a", 1.5f, .82f, 0, 0, .06f); Flame("#ff8a26", 2.0f, .62f, .03f, .05f, -.1f); Flame("#ffd66b", 2.8f, .41f, -.02f, .08f, .04f);
            Flame("#ff6a20", 1.7f, .41f, -.15f, -.04f, .22f); Flame("#ff7a22", 1.7f, .34f, .16f, .02f, -.25f);
            var flicker = FlamesRoot.gameObject.AddComponent<CampFlames>();
            flicker.Capture();

            // embers
            if (embers)
            {
                var ek = new MeshKit();
                for (int i = 0; i < 24; i++)
                {
                    float t = Rnd() * Rnd();
                    ek.Billboard(new Vector3(FIRE.x + Rr(-.3f, .3f) * (.3f + t), 0, FIRE.z + Rr(-.25f, .25f)), Color.white, new Vector4(i, 0, 0, 0));
                }
                Emit("Embers", ek, Mat(embers, "Embers"), RainLayer, new Bounds(MeshKit.U(FIRE) + Vector3.up * 1.5f, new Vector3(2, 4, 2)));
            }

            // dome tent, glowing from inside (glow over the cloth color, fades out by day)
            {
                var tent = MeshKit.Hemisphere(1.3f, 7, 3);
                string[] warm = { "#ffb030", "#f08020", "#ffd060", "#e06a18", "#ff9a28" };
                var m = MeshKit.TRS(new Vector3(TENT.x, GroundH(TENT.x, TENT.z) - .03f, TENT.z), new Vector3(1, .8f, 1), 0, .3f, 0);
                kit.Add(tent, m, _ =>
                {
                    var f = FaceAttr.Of(S.TentCloth, .9f + .2f * Rnd());
                    var c = CampPalette.Hex(warm[Mathf.FloorToInt(Rnd() * warm.Length)]).linear;
                    f.emit = new Color(c.r * .8f, c.g * .8f, c.b * .8f); f.channel = 1;
                    return f;
                });
                glowKit.Billboard(new Vector3(TENT.x, .6f, TENT.z + .5f), CampPalette.Hex("#ff9a3c").linear, new Vector4(6.5f, 1, 0, .5f));
            }

            // folding table with the lantern (left of Iori)
            {
                float TX = TABLE.x, TZ = TABLE.z, TH = .5f;
                kit.Add(MeshKit.Box(.74f, .035f, .46f), MeshKit.TRS(new Vector3(TX, TH, TZ), 0, .25f, 0), FaceAttr.Of(S.Wood, S.Crate));
                foreach (var s in new[] { -1f, 1f })
                {
                    kit.Add(MeshKit.Box(.03f, .66f, .03f), MeshKit.TRS(new Vector3(TX + s * .02f, TH / 2, TZ + s * .16f), 0, .25f, .6f), FaceAttr.Of(S.Dark));
                    kit.Add(MeshKit.Box(.03f, .66f, .03f), MeshKit.TRS(new Vector3(TX + s * .02f, TH / 2, TZ + s * .16f), 0, .25f, -.6f), FaceAttr.Of(S.Dark));
                }
                kit.Add(MeshKit.Cylinder(.04f, .035f, .08f, 8), MeshKit.TRS(new Vector3(TX + .2f, TH + .06f, TZ + .08f)), FaceAttr.Of(S.Cream));
                float lx = TX - .1f, lz = TZ + .02f, ly = TH + .02f;
                // glass: glows with the lantern, keeps a faint cream color when it is off
                var glass = FaceAttr.Of(S.Cream, .45f);
                var gl = CampPalette.Hex("#fff0c4").linear;
                glass.emit = new Color(gl.r * 2.6f, gl.g * 2.6f, gl.b * 2.6f); glass.channel = 2;
                kit.Add(MeshKit.Cylinder(.085f, .095f, .24f, 8), MeshKit.TRS(new Vector3(lx, ly + .2f, lz)), glass);
                kit.Add(MeshKit.Cylinder(.11f, .12f, .06f, 8), MeshKit.TRS(new Vector3(lx, ly + .04f, lz)), FaceAttr.Of(S.Dark));
                kit.Add(MeshKit.Cylinder(.11f, .11f, .035f, 8), MeshKit.TRS(new Vector3(lx, ly + .335f, lz)), FaceAttr.Of(S.Dark));
                kit.Add(MeshKit.Cone(.115f, .09f, 8), MeshKit.TRS(new Vector3(lx, ly + .395f, lz)), FaceAttr.Of(S.Dark));
                kit.Add(MeshKit.Torus(.06f, .008f, 5, 12), MeshKit.TRS(new Vector3(lx, ly + .47f, lz)), FaceAttr.Of(S.Brass));
                for (int i = 0; i < 4; i++)
                {
                    float a = i / 4f * Mathf.PI * 2 + .4f;
                    kit.Add(MeshKit.Box(.012f, .25f, .012f), MeshKit.TRS(new Vector3(lx + Mathf.Cos(a) * .1f, ly + .2f, lz + Mathf.Sin(a) * .1f)), FaceAttr.Of(S.Dark));
                }
                glowKit.Billboard(new Vector3(lx, ly + .22f, lz), CampPalette.Hex("#ffc070").linear, new Vector4(1.4f, 2, 0, .55f));
                blobs.GroundQuad(new Vector3(TX, 0, TZ), 1.0f, .7f, GroundH, .4f);
            }

            // radio on a crate (right of Iori)
            {
                float RX = RADIO.x, RZ = RADIO.z;
                kit.Add(MeshKit.Box(.58f, .36f, .44f), MeshKit.TRS(new Vector3(RX, .18f, RZ), 0, -.3f, 0), FaceAttr.Of(S.Crate, S.Wood));
                var rm = MeshKit.TRS(new Vector3(RX - .02f, .36f, RZ + .03f), 0, -.36f, 0);
                void P(MeshKit.Shape s, Vector3 p, FaceAttr a, float rx = 0, float ry = 0, float rz = 0) => kit.Add(s, rm * MeshKit.TRS(p, rx, ry, rz), a);
                P(MeshKit.Box(.46f, .28f, .16f), new Vector3(0, .14f, 0), FaceAttr.Of(S.Dark));
                P(MeshKit.Box(.2f, .07f, .012f), new Vector3(-.09f, .2f, .082f), FaceAttr.Glow(CampPalette.Hex("#ffd27a"), 2.4f, 4));
                P(MeshKit.Cylinder(.078f, .078f, .012f, 12), new Vector3(.115f, .14f, .082f), FaceAttr.Of(S.Knob), Mathf.PI / 2);
                P(MeshKit.Cylinder(.022f, .022f, .02f, 8), new Vector3(-.15f, .075f, .086f), FaceAttr.Of(S.Brass), Mathf.PI / 2);
                P(MeshKit.Cylinder(.022f, .022f, .02f, 8), new Vector3(-.06f, .075f, .086f), FaceAttr.Of(S.Brass), Mathf.PI / 2);
                P(MeshKit.Box(.34f, .016f, .03f), new Vector3(0, .288f, 0), FaceAttr.Of(S.Brass));
                P(MeshKit.Cylinder(.006f, .006f, .55f, 5), new Vector3(.19f, .53f, -.04f), FaceAttr.Of(S.Dark), 0, 0, -.28f);
                glowKit.Billboard(new Vector3(RX - .1f, .6f, RZ + .15f), CampPalette.Hex("#ffc870").linear, new Vector4(.8f, 4, 0, .3f));
                blobs.GroundQuad(new Vector3(RX, 0, RZ), 1.0f, .8f, GroundH, .45f);
            }

            // cassette deck on a stump (front right) — the music player
            {
                float SX = STUMP.x, SZ = STUMP.z;
                kit.Add(MeshKit.Cylinder(.23f, .27f, .3f, 8), MeshKit.TRS(new Vector3(SX, .15f, SZ)), FaceAttr.Of(S.Wood, S.Log));
                kit.Add(MeshKit.Cylinder(.215f, .215f, .012f, 8), MeshKit.TRS(new Vector3(SX, .306f, SZ)), FaceAttr.Of(S.StumpTop, S.Cream));
                var dm = MeshKit.TRS(new Vector3(SX, .31f, SZ), 0, -.35f, 0);
                void P(MeshKit.Shape s, Vector3 p, FaceAttr a) => kit.Add(s, dm * MeshKit.TRS(p), a);
                P(MeshKit.Box(.4f, .11f, .25f), new Vector3(0, .055f, 0), FaceAttr.Of(S.Dark));
                P(MeshKit.Box(.22f, .012f, .1f), new Vector3(0, .112f, -.02f), FaceAttr.Glow(CampPalette.Hex("#ffe2a8"), 1.5f, 5));
                P(MeshKit.Cylinder(.032f, .032f, .02f, 10), new Vector3(-.055f, .118f, -.02f), FaceAttr.Of(S.Knob));
                P(MeshKit.Cylinder(.032f, .032f, .02f, 10), new Vector3(.055f, .118f, -.02f), FaceAttr.Of(S.Knob));
                for (int i = 0; i < 5; i++) P(MeshKit.Box(.045f, .02f, .03f), new Vector3(-.13f + i * .065f, .115f, .09f), FaceAttr.Of(i == 1 ? S.Brass : S.Cream));
                P(MeshKit.Box(.03f, .03f, .25f), new Vector3(-.2f, .06f, 0), FaceAttr.Of(S.Brass));
                // loose cassettes on the ground
                (string c, float y, float r)[] tapes = { ("#f0b02a", 0, .1f), ("#e2d8cc", .02f, -.25f), ("#8a78d0", .04f, .2f) };
                foreach (var tp in tapes)
                {
                    float x = SX - .42f, z = SZ + .3f;
                    var at = tp.c == "#f0b02a" ? FaceAttr.Of(S.Brass) : tp.c == "#e2d8cc" ? FaceAttr.Of(S.Cream) : FaceAttr.Of(S.Crate, 1.4f);
                    kit.Add(MeshKit.Box(.11f, .018f, .07f), MeshKit.TRS(new Vector3(x, GroundH(x, z) + .012f + tp.y, z), 0, tp.r, 0), at);
                }
                glowKit.Billboard(new Vector3(SX, .48f, SZ), CampPalette.Hex("#ffe2a8").linear, new Vector4(.5f, 5, 0, .25f));
                blobs.GroundQuad(new Vector3(SX, 0, SZ), .8f, .8f, GroundH, .45f);
            }

            // Iori's folding chair (she will sit here once the seated art exists)
            {
                float CX = IORI.x - .05f, CZ = IORI.z - .28f;
                var cm = MeshKit.TRS(new Vector3(CX, 0, CZ), 0, .12f, 0);
                void P(MeshKit.Shape s, Vector3 p, FaceAttr a, float rx = 0, float ry = 0, float rz = 0) => kit.Add(s, cm * MeshKit.TRS(p, rx, ry, rz), a);
                P(MeshKit.Box(.5f, .03f, .42f), new Vector3(0, .42f, 0), FaceAttr.Of(S.Fabric, S.Crate));
                P(MeshKit.Box(.5f, .42f, .03f), new Vector3(0, .66f, -.22f), FaceAttr.Of(S.Fabric, S.Crate), -.22f);
                foreach (var sx in new[] { -.24f, .24f })
                {
                    P(MeshKit.Box(.025f, .62f, .025f), new Vector3(sx, .3f, 0), FaceAttr.Of(S.Dark), .55f);
                    P(MeshKit.Box(.025f, .62f, .025f), new Vector3(sx, .3f, 0), FaceAttr.Of(S.Dark), -.55f);
                    P(MeshKit.Box(.025f, .025f, .4f), new Vector3(sx, .5f, -.02f), FaceAttr.Of(S.Dark));
                }
            }
            blobs.GroundQuad(new Vector3(IORI.x + .12f, 0, IORI.z), 1.4f, .55f, GroundH, .55f);

            // rocks: big flat facets in two blues
            float[][] rocks = { new[] { 2.3f, 1.6f, .7f }, new[] { -2.6f, .9f, .5f }, new[] { -2.9f, -3.6f, .5f }, new[] { 3.4f, -4.2f, .65f }, new[] { 4.6f, -3.0f, .35f }, new[] { 1.6f, -4.9f, .4f }, new[] { -4.7f, 1.9f, .45f }, new[] { 4.3f, 2.4f, .4f }, new[] { 1.9f, -1.4f, .22f }, new[] { -1.7f, -2.6f, .2f } };
            foreach (var r in rocks)
            {
                S[] pal = { S.Rock, S.Rock, S.RockHi, S.GroundShade };
                kit.Add(MeshKit.Rock(r[2]), MeshKit.TRS(new Vector3(r[0], GroundH(r[0], r[1]) + r[2] * .22f, r[1]), new Vector3(1, .6f, 1), Rnd() * 3, Rnd() * 3, 0),
                    _ => FaceAttr.Of(pal[Mathf.FloorToInt(Rnd() * pal.Length)], S.RockHi));
            }

            Emit("Camp props", kit, Mat(painted, "Camp props"));
            if (glow) Emit("Glows", glowKit, Mat(glow, "Glows"), RainLayer, new Bounds(MeshKit.U(Cen) + Vector3.up, new Vector3(12, 6, 14)));
            if (blob) Emit("Blob shadows", blobs, Mat(blob, "Blob shadows"), GroundLayer);
        }

        // ------------------------------------------------------------------ grass, flowers, pebbles
        void BuildGrass(Vector3 cam)
        {
            var kit = new MeshKit();
            var tuft = MeshKit.Cone(.035f, .3f, 3, true);
            S[] cs = { S.GrassA, S.GrassB, S.GrassB, S.GroundShade };
            float[] bright = { 1, 1, 1.2f, 1 };
            FaceAttr TuftAttr(int ci)
            {
                var f = FaceAttr.Of(cs[ci], cs[(ci + 1) % 4], bright[ci]);
                return f;
            }
            int n = 0;
            for (int i = 0; i < grassTufts * 3 && n < grassTufts; i++)
            {
                float x = Rr(-9, 9), z = Rr(-6.8f, 5.5f);
                float df = Mathf.Sqrt((x - Cen.x) * (x - Cen.x) + (z - Cen.z) * (z - Cen.z));
                if (df < 1.7f + Rnd() * 1.7f) continue;
                float h = GroundH(x, z); if (h < WaterY + .25f || h < PudY + .02f) continue;
                float s = Rr(.7f, 1.9f), sy = Rr(.7f, 1.5f);
                int ci = Mathf.FloorToInt(Rnd() * cs.Length);
                var at = TuftAttr(ci);
                var m = MeshKit.TRS(new Vector3(x, h - .02f + .15f * s * sy, z), new Vector3(s, s * sy, s), Rr(-.25f, .25f), Rnd() * 6, Rr(-.25f, .25f));
                kit.Add(tuft, m, at);
                n++;
            }
            // near the camera: thin single blades, flowers and pebbles, for the near-field detail chico asked for
            var blade = new MeshKit.Shape();
            blade.T(new Vector3(-.012f, 0, 0), new Vector3(.012f, 0, 0), new Vector3(.003f, .22f, 0));
            n = 0;
            for (int i = 0; i < nearBlades * 4 && n < nearBlades; i++)
            {
                float x = Rr(-3.2f, 3.2f), z = Rr(-.6f, 3.6f);
                float df = Mathf.Sqrt((x - Cen.x) * (x - Cen.x) + (z - Cen.z) * (z - Cen.z));
                if (df < 1.15f + Rnd() * .9f) continue;
                float h = GroundH(x, z); if (h < PudY + .015f) continue;
                if (Mathf.Abs(x - (-0.48f)) < .7f && Mathf.Abs(z - 1.05f) < .7f) continue;
                int ci = Mathf.FloorToInt(Rnd() * cs.Length);
                float s = Rr(.6f, 1.5f);
                var at = TuftAttr(ci).Sway(0, 0, .8f);
                kit.Add(blade, MeshKit.TRS(new Vector3(x, h - .01f, z), new Vector3(s, s * Rr(.7f, 1.4f), s), Rr(-.35f, .35f), Rnd() * 6, Rr(-.35f, .35f)), at);
                n++;
            }
            var flower = new MeshKit.Shape();
            for (int k = 0; k < 5; k++)
            {
                float a0 = k / 5f * Mathf.PI * 2, a1 = (k + .5f) / 5f * Mathf.PI * 2;
                flower.T(Vector3.zero, new Vector3(Mathf.Cos(a0) * .03f, 0, Mathf.Sin(a0) * .03f), new Vector3(Mathf.Cos(a1) * .012f, 0, Mathf.Sin(a1) * .012f));
            }
            for (int i = 0; i < 160; i++)
            {
                float x = Rr(-4, 4), z = Rr(-1.5f, 3.4f);
                float df = Mathf.Sqrt((x - Cen.x) * (x - Cen.x) + (z - Cen.z) * (z - Cen.z));
                if (df < 1.6f) continue;
                float h = GroundH(x, z); if (h < PudY + .02f) continue;
                kit.Add(flower, MeshKit.TRS(new Vector3(x, h + Rr(.04f, .12f), z), Vector3.one * Rr(.7f, 1.3f), Rr(-.3f, .3f), Rnd() * 6, 0), FaceAttr.Of(S.Flower, S.Cream).Sway(.5f));
            }
            for (int i = 0; i < 140; i++)
            {
                float x = Rr(-3, 3), z = Rr(-2.5f, 3.4f);
                float h = GroundH(x, z);
                kit.Add(MeshKit.Rock(Rr(.015f, .045f)), MeshKit.TRS(new Vector3(x, h, z), new Vector3(1, .6f, 1), Rnd() * 3, Rnd() * 3, 0), FaceAttr.Of(Rnd() < .5f ? S.Rock : S.RockHi, S.GroundShade));
            }
            Emit("Grass", kit, Mat(painted, "Grass", m => m.SetFloat("_Cull", 0)), GroundLayer);
        }

        // ------------------------------------------------------------------ water and rain
        void BuildWater()
        {
            if (!water) return;
            Renderer Plane(string name, Vector3 centerUnity, Vector2 size, Material mat)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                float x = size.x / 2, z = size.y / 2;
                mesh.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) };
                mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                _owned.Add(mesh);
                var go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = WaterLayer };
                go.transform.SetParent(_root, false);
                go.transform.localPosition = centerUnity;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                return r;
            }
            Plane("Lake", new Vector3(0, WaterY, 900), new Vector2(3000, 2000), Mat(water, "Lake", m => { m.SetFloat("_Puddle", 0); m.SetFloat("_Ripple", 1); }));
            Puddle = Plane("Puddles", MeshKit.U(new Vector3(Cen.x, PudY, Cen.z)), new Vector2(13.4f, 9.4f),
                Mat(water, "Puddles", m => { m.SetFloat("_Puddle", 1); m.SetFloat("_Ripple", .12f); m.SetVector("_Center", new Vector4(Cen.x, -Cen.z, 6.6f, 4.6f)); }));
        }

        void BuildRain()
        {
            if (!rain || rainStreaks <= 0) return;
            var kit = new MeshKit();
            Vector2[] cs = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < rainStreaks; i++)
            {
                var p = new Vector3(Rr(-5, 5), Rr(0, 7), Rr(-7, 4.6f));
                float speed = Rr(9, 13);
                kit.Billboard(p, Color.white, new Vector4(speed, 0, 0, 0));
            }
            Emit("Rain", kit, Mat(rain, "Rain"), RainLayer, new Bounds(new Vector3(0, 4, 1), new Vector3(12, 10, 14)));
        }

        // ------------------------------------------------------------------
        void BuildAll()
        {
            var FIRE = M(fireAnchor, new Vector3(-0.48f, 0, 1.05f));
            var IORI = M(ioriAnchor, new Vector3(0.1f, 0, 0));
            var TABLE = M(tableAnchor, new Vector3(-0.9f, 0, -0.45f));
            var RADIO = M(radioAnchor, new Vector3(0.86f, 0, -0.45f));
            var STUMP = M(playerAnchor, new Vector3(0.64f, 0, 1.0f));
            var TENT = M(tentAnchor, new Vector3(-1.9f, 0, -5.2f));
            var cam = new Vector3(0.04f, 1.38f, 4.03f);

            BuildGround();
            BuildConifers(cam);
            BuildTrees();
            BuildFar();
            BuildCamp(FIRE, IORI, TABLE, RADIO, STUMP, TENT);
            BuildGrass(cam);
            BuildWater();
            BuildRain();
        }
    }
}
