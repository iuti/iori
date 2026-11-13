using System;
using System.Reflection;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// One look of the camp: sky, light and the colors of everything that is not a light source.
    /// Colors are authored in sRGB (as in the three.js mock and the direction doc) and blended in sRGB.
    /// Rule from the art direction: only light sources are warm; shadows are deep indigo, never black.
    /// </summary>
    [Serializable]
    public sealed class CampPalette
    {
        [Header("空")]
        public Color horizon = Hex("#eaa0d6");
        public Color mid = Hex("#a58ae8");
        public Color zenith = Hex("#4a6cdc");
        [Tooltip("太陽か月の方向(モックの座標。z が手前)")]
        public Vector3 sunDir = new Vector3(-0.55f, 0.02f, -1f);
        public Color sunColor = Hex("#ffb4c4");
        [Range(0, 2)] public float sunGlow = 0.7f;
        [Range(0, 1)] public float moonDisc = 0f;
        [Range(0, 1)] public float stars = 0.75f;
        [Range(0, 1)] public float streaks = 1f;
        public Color cloud = Hex("#f4b4dc");
        public Color cloudDark = Hex("#8a80dc");
        [Range(0, 1)] public float cloudAmount = 0.3f;

        [Header("霧")]
        public Color fog = Hex("#7c84e2");
        public float fogNear = 30f;
        public float fogFar = 760f;

        [Header("光")]
        public Color hemiSky = Hex("#b4b8ff");
        public Color hemiGround = Hex("#4a5ac0");
        public float hemiIntensity = 0.82f;
        public Color dirColor = Hex("#d8a8ff");
        public float dirIntensity = 0.3f;
        [Tooltip("焚き火の強さ")] public float fire = 1f;
        [Tooltip("テントの灯り")] public float tent = 1f;
        [Tooltip("遠くの町の灯り")] public float town = 2.2f;

        [Header("地面")]
        public Color water = Hex("#7f8ee6");
        public Color grassA = Hex("#2a9478");
        public Color grassB = Hex("#44b486");
        public Color groundShade = Hex("#3550b0");
        public Color dirt = Hex("#5b4a8c");
        public Color dirt2 = Hex("#463c80");
        public Color sand = Hex("#7f7cc4");
        public Color rock = Hex("#4a58b4");
        public Color rockHi = Hex("#6a7ad4");

        [Header("木と遠景(手前から奥)")]
        public Color tree = Hex("#231c62");
        public Color treeHi = Hex("#3b48b0");
        public Color leaf = Hex("#1d1750");
        public Color sil0 = Hex("#1f1a5c");
        public Color sil1 = Hex("#2a2c80");
        public Color sil2 = Hex("#3a44a8");
        public Color sil3 = Hex("#4f5fc8");
        public Color sil4 = Hex("#6f80dc");
        public Color hill0 = Hex("#3552cc");
        public Color hill1 = Hex("#5f78de");
        public Color hill2 = Hex("#99a4ea");

        [Header("イオリ")]
        public Color spriteAmbient = Hex("#b9aee6");
        public Color rim = Hex("#c9a0ff");

        [Header("雨のときに混ぜる色")]
        public Color rainTint = Hex("#5a6498");

        /// <summary>"#rrggbb" to Color. Plain C# so it is safe in field initializers (Unity may run them during deserialization).</summary>
        public static Color Hex(string hex)
        {
            int start = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
            int v = System.Convert.ToInt32(hex.Substring(start, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public CampPalette Clone() => (CampPalette)MemberwiseClone();

        static FieldInfo[] s_fields;
        static FieldInfo[] Fields => s_fields ??= typeof(CampPalette).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>Blends every field (colors in sRGB, vectors and floats linearly) into <paramref name="into"/>.</summary>
        public static void Lerp(CampPalette a, CampPalette b, float t, CampPalette into)
        {
            foreach (var f in Fields)
            {
                var va = f.GetValue(a);
                var vb = f.GetValue(b);
                switch (va)
                {
                    case Color ca: f.SetValue(into, Color.Lerp(ca, (Color)vb, t)); break;
                    case float fa: f.SetValue(into, Mathf.Lerp(fa, (float)vb, t)); break;
                    case Vector3 v: f.SetValue(into, Vector3.Lerp(v, (Vector3)vb, t)); break;
                }
            }
        }

        // ---- the looks from the mock, plus day and evening ----

        /// <summary>The blue hour. The mock called it "dusk"; chico liked it as the air before dawn.</summary>
        public static CampPalette Twilight() => new CampPalette();

        public static CampPalette Night() => new CampPalette
        {
            horizon = Hex("#4658c0"), mid = Hex("#2a3288"), zenith = Hex("#0f1340"),
            sunDir = new Vector3(0.16f, 0.24f, -1f), sunColor = Hex("#e6ecff"), sunGlow = 0.25f, moonDisc = 1f, stars = 1f, streaks = 0.6f,
            cloud = Hex("#3a48a8"), cloudDark = Hex("#141a55"), cloudAmount = 0.25f,
            fog = Hex("#1e2670"), fogNear = 12f, fogFar = 420f,
            hemiSky = Hex("#6f80e0"), hemiGround = Hex("#1a2060"), hemiIntensity = 0.62f, dirColor = Hex("#a9bcff"), dirIntensity = 0.4f,
            fire = 1.25f, tent = 1.1f, town = 2.6f,
            water = Hex("#243090"), grassA = Hex("#1a5a58"), grassB = Hex("#22705e"), groundShade = Hex("#1c2a78"),
            dirt = Hex("#5a3a52"), dirt2 = Hex("#3c2c5c"), sand = Hex("#3f4690"), rock = Hex("#3f50b0"), rockHi = Hex("#5868c8"),
            tree = Hex("#0e0c38"), treeHi = Hex("#1f2a80"), leaf = Hex("#0b0930"),
            sil0 = Hex("#0c0b34"), sil1 = Hex("#12154a"), sil2 = Hex("#1a2062"), sil3 = Hex("#242e7c"), sil4 = Hex("#30409a"),
            hill0 = Hex("#161d60"), hill1 = Hex("#202c7c"), hill2 = Hex("#2e3e98"),
            spriteAmbient = Hex("#8090d8"), rim = Hex("#8fa0ff"), rainTint = Hex("#232a55"),
        };

        public static CampPalette Dawn() => new CampPalette
        {
            horizon = Hex("#ffd2b4"), mid = Hex("#e2c6ee"), zenith = Hex("#86aef0"),
            sunDir = new Vector3(0.5f, 0.06f, -1f), sunColor = Hex("#ffe9c4"), sunGlow = 0.6f, moonDisc = 0f, stars = 0f, streaks = 0.3f,
            cloud = Hex("#fff0e4"), cloudDark = Hex("#c0b8e8"), cloudAmount = 0.4f,
            fog = Hex("#cfd0f2"), fogNear = 12f, fogFar = 460f,
            hemiSky = Hex("#e8e6ff"), hemiGround = Hex("#8f9ad8"), hemiIntensity = 1.2f, dirColor = Hex("#ffe0c0"), dirIntensity = 0.8f,
            fire = 0.25f, tent = 0f, town = 0.5f,
            water = Hex("#b4c2f0"), grassA = Hex("#4fa884"), grassB = Hex("#74c492"), groundShade = Hex("#5f78c8"),
            dirt = Hex("#b08a8a"), dirt2 = Hex("#9a80a8"), sand = Hex("#b8b4dc"), rock = Hex("#98a8ec"), rockHi = Hex("#c0c8f6"),
            tree = Hex("#34407f"), treeHi = Hex("#5a6cc0"), leaf = Hex("#2c3670"),
            sil0 = Hex("#3a4690"), sil1 = Hex("#5062b0"), sil2 = Hex("#6f82c8"), sil3 = Hex("#93a4dc"), sil4 = Hex("#b8c4ec"),
            hill0 = Hex("#7f96da"), hill1 = Hex("#a6b6e8"), hill2 = Hex("#ccd4f4"),
            spriteAmbient = Hex("#f0ecf4"), rim = Hex("#ffd8c0"), rainTint = Hex("#8088a8"),
        };

        /// <summary>Noon. Not in the mock; values from the Fable advisor, keeping the blue-green rule (sun warmth only in light).</summary>
        public static CampPalette Day() => new CampPalette
        {
            horizon = Hex("#d6dcfa"), mid = Hex("#86b4f6"), zenith = Hex("#3f82ec"),
            sunDir = new Vector3(0.2f, 0.75f, -0.6f), sunColor = Hex("#fff0c8"), sunGlow = 0.35f, moonDisc = 0f, stars = 0f, streaks = 0f,
            cloud = Hex("#ffffff"), cloudDark = Hex("#b8c8f0"), cloudAmount = 0.55f,
            fog = Hex("#bcd0f6"), fogNear = 20f, fogFar = 600f,
            hemiSky = Hex("#eef2ff"), hemiGround = Hex("#8a9ad6"), hemiIntensity = 1.25f, dirColor = Hex("#fff0c8"), dirIntensity = 0.95f,
            fire = 0.3f, tent = 0f, town = 0f,
            water = Hex("#8fb4f0"), grassA = Hex("#38b47c"), grassB = Hex("#68d28e"), groundShade = Hex("#5a7ed0"),
            dirt = Hex("#a48aa8"), dirt2 = Hex("#8c78a8"), sand = Hex("#aab4e0"), rock = Hex("#7a90dc"), rockHi = Hex("#a4b6f0"),
            tree = Hex("#2c3a86"), treeHi = Hex("#4f6ac8"), leaf = Hex("#243272"),
            sil0 = Hex("#2e4290"), sil1 = Hex("#4460b4"), sil2 = Hex("#6280cc"), sil3 = Hex("#86a2de"), sil4 = Hex("#acc2ee"),
            hill0 = Hex("#4470d8"), hill1 = Hex("#7896e6"), hill2 = Hex("#a8bef2"),
            spriteAmbient = Hex("#f4f2fa"), rim = Hex("#fff0d8"), rainTint = Hex("#7884a8"),
        };

        /// <summary>Late afternoon: the dawn colors with the sun in the west and a little more pink.</summary>
        public static CampPalette Evening()
        {
            var p = Dawn();
            p.sunDir = new Vector3(-0.5f, 0.08f, -1f);
            p.horizon = Hex("#ffc4c0");
            p.mid = Hex("#dab8ee");
            p.zenith = Hex("#7c9cec");
            p.cloud = Hex("#ffe0e4");
            p.fire = 0.5f;
            p.tent = 0.25f;
            p.town = 0.9f;
            return p;
        }

        // ---- palette slots used by the meshes (TEXCOORD1.x) ----
        public enum Slot
        {
            GrassA = 0, GrassB, GroundShade, Dirt, Dirt2, Sand, Rock, RockHi, Tree, TreeHi, Leaf,
            Sil0, Sil1, Sil2, Sil3, Sil4, Hill0, Hill1, Hill2, Water,
            Wood, Dark, Crate, Brass, Cream, Log, Knob, StumpTop, Fabric, TentCloth, Flower, Spare,
        }
    }
}
