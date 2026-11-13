using System;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// One look of the camp at a given hour: the sky, where the light comes from and its color,
    /// the air, how strong each lamp is, and the finishing touches on the picture.
    /// Things have their own colors (in the models); this only says what light falls on them.
    /// Rule from the art direction: only light sources are warm; shade is deep indigo, never black.
    /// Colors are authored and blended in sRGB.
    /// </summary>
    [Serializable]
    public sealed class CampPalette
    {
        [Header("空")]
        public Color horizon = Hex("#eaa0d6");
        public Color mid = Hex("#a58ae8");
        public Color zenith = Hex("#4a6cdc");
        public Color cloud = Hex("#f4b4dc");
        public Color cloudDark = Hex("#8a80dc");
        [Range(0, 1)] public float cloudAmount = 0.3f;
        [Range(0, 1)] public float stars = 0.55f;

        [Header("太陽(方角 0 = 湖の向こう正面、+ で右。高さは地平線からの角度。地平線の下でもよい)")]
        public float sunAzimuth = -22f;
        public float sunElevation = -5f;
        public Color sunGlowColor = Hex("#ffb4c4");
        [Range(0, 2)] public float sunGlow = 0.7f;
        [Range(0, 1)] public float sunDisc = 0f;

        [Header("月(1 で月が出て、月明かりが主な光になる)")]
        [Range(0, 1)] public float moon = 0f;

        [Header("主な光(太陽、残照、月明かり)")]
        public Color keyColor = Hex("#ff9ccc");
        public float keyIntensity = 0.5f;
        [Range(0, 1)] public float keyShadow = 0.55f;

        [Header("まわりの光(上から、横から、地面から)")]
        public Color ambientSky = Hex("#58c8f0");
        public Color ambientEquator = Hex("#7862e8");
        public Color ambientGround = Hex("#34348c");
        public float ambientIntensity = 2.4f;

        [Header("空気")]
        public Color fog = Hex("#7c84e2");
        [Tooltip("1m あたりのかすみ")] public float fogDensity = 0.004f;
        public Color mist = Hex("#a8a0ee");
        [Tooltip("水面近くの霧")] public float mistDensity = 0.02f;

        [Header("遠くの山(手前と奥)と町の灯り")]
        public Color farNear = Hex("#2a2c80");
        public Color farFar = Hex("#7f8ee6");
        public float town = 1f;

        [Header("湖の色")]
        public Color water = Hex("#5f6fd0");

        [Header("灯りの強さ(1 が基準)")]
        public float fire = 1f;
        public float tent = 1f;
        public float lantern = 1f;

        [Header("仕上げ")]
        [Tooltip("明るさ(EV)")] public float exposure = 0f;
        [Range(-100, 100)] public float saturation = 6f;
        [Range(-100, 100)] public float contrast = 4f;
        public Color filter = Color.white;
        [Range(0, 3)] public float bloom = 0.9f;

        [Header("雨のときに空と空気へ混ぜる色")]
        public Color rainTint = Hex("#5a6498");

        /// <summary>"#rrggbb" to Color. Plain C# so it is safe in field initializers (Unity may run them during deserialization).</summary>
        public static Color Hex(string hex)
        {
            int start = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
            int v = Convert.ToInt32(hex.Substring(start, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public CampPalette Clone() => (CampPalette)MemberwiseClone();

        /// <summary>Blends every field into <paramref name="into"/> (the sun's direction the short way around).
        /// Written out field by field: it runs every frame and must not allocate.</summary>
        public static void Lerp(CampPalette a, CampPalette b, float t, CampPalette into)
        {
            into.horizon = Color.Lerp(a.horizon, b.horizon, t);
            into.mid = Color.Lerp(a.mid, b.mid, t);
            into.zenith = Color.Lerp(a.zenith, b.zenith, t);
            into.cloud = Color.Lerp(a.cloud, b.cloud, t);
            into.cloudDark = Color.Lerp(a.cloudDark, b.cloudDark, t);
            into.sunGlowColor = Color.Lerp(a.sunGlowColor, b.sunGlowColor, t);
            into.keyColor = Color.Lerp(a.keyColor, b.keyColor, t);
            into.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
            into.ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
            into.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
            into.fog = Color.Lerp(a.fog, b.fog, t);
            into.mist = Color.Lerp(a.mist, b.mist, t);
            into.farNear = Color.Lerp(a.farNear, b.farNear, t);
            into.farFar = Color.Lerp(a.farFar, b.farFar, t);
            into.water = Color.Lerp(a.water, b.water, t);
            into.filter = Color.Lerp(a.filter, b.filter, t);
            into.rainTint = Color.Lerp(a.rainTint, b.rainTint, t);
            into.cloudAmount = Mathf.Lerp(a.cloudAmount, b.cloudAmount, t);
            into.stars = Mathf.Lerp(a.stars, b.stars, t);
            into.sunElevation = Mathf.Lerp(a.sunElevation, b.sunElevation, t);
            into.sunGlow = Mathf.Lerp(a.sunGlow, b.sunGlow, t);
            into.sunDisc = Mathf.Lerp(a.sunDisc, b.sunDisc, t);
            into.moon = Mathf.Lerp(a.moon, b.moon, t);
            into.keyIntensity = Mathf.Lerp(a.keyIntensity, b.keyIntensity, t);
            into.keyShadow = Mathf.Lerp(a.keyShadow, b.keyShadow, t);
            into.ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t);
            into.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            into.mistDensity = Mathf.Lerp(a.mistDensity, b.mistDensity, t);
            into.town = Mathf.Lerp(a.town, b.town, t);
            into.fire = Mathf.Lerp(a.fire, b.fire, t);
            into.tent = Mathf.Lerp(a.tent, b.tent, t);
            into.lantern = Mathf.Lerp(a.lantern, b.lantern, t);
            into.exposure = Mathf.Lerp(a.exposure, b.exposure, t);
            into.saturation = Mathf.Lerp(a.saturation, b.saturation, t);
            into.contrast = Mathf.Lerp(a.contrast, b.contrast, t);
            into.bloom = Mathf.Lerp(a.bloom, b.bloom, t);
            into.sunAzimuth = Mathf.LerpAngle(a.sunAzimuth, b.sunAzimuth, t);
        }

        // ---------------------------------------------------------------- the looks

        /// <summary>The blue hour that chico approved ("the air before dawn"). Before dawn the sun is
        /// behind the camp; after sunset its glow sits on the horizon across the lake.</summary>
        public static CampPalette Twilight(bool beforeDawn) => new CampPalette
        {
            sunAzimuth = beforeDawn ? 150f : -22f,
            sunElevation = beforeDawn ? -6f : -5f,
            sunGlow = beforeDawn ? 0.3f : 0.75f,
            keyIntensity = beforeDawn ? 0.35f : 0.5f,
            stars = beforeDawn ? 0.45f : 0.6f,
            mistDensity = beforeDawn ? 0.035f : 0.02f,
        };

        public static CampPalette Night() => new CampPalette
        {
            horizon = Hex("#4658c0"), mid = Hex("#2a3288"), zenith = Hex("#0f1340"),
            cloud = Hex("#3a48a8"), cloudDark = Hex("#141a55"), cloudAmount = 0.25f, stars = 1f,
            sunAzimuth = -22f, sunElevation = -30f, sunGlowColor = Hex("#e6ecff"), sunGlow = 0f, sunDisc = 0f,
            moon = 1f,
            keyColor = Hex("#a9bcff"), keyIntensity = 0.55f, keyShadow = 0.7f,
            ambientSky = Hex("#3a8ad8"), ambientEquator = Hex("#4438b0"), ambientGround = Hex("#1c1c60"), ambientIntensity = 1.15f,
            fog = Hex("#1e2670"), fogDensity = 0.006f, mist = Hex("#2c3684"), mistDensity = 0.03f,
            farNear = Hex("#12154a"), farFar = Hex("#2e3e98"), town = 1f,
            water = Hex("#243090"),
            fire = 1.15f, tent = 1f, lantern = 1f,
            exposure = 0f, saturation = 6f, contrast = 6f, bloom = 1.0f,
            rainTint = Hex("#232a55"),
        };

        /// <summary>Sunrise. The sun comes up behind the camp and lights the mountains across the lake.</summary>
        public static CampPalette Dawn() => new CampPalette
        {
            horizon = Hex("#ffd2b4"), mid = Hex("#e2c6ee"), zenith = Hex("#86aef0"),
            cloud = Hex("#fff0e4"), cloudDark = Hex("#c0b8e8"), cloudAmount = 0.4f, stars = 0f,
            sunAzimuth = 140f, sunElevation = 7f, sunGlowColor = Hex("#ffe9c4"), sunGlow = 0.5f, sunDisc = 1f,
            moon = 0f,
            keyColor = Hex("#ffd0a0"), keyIntensity = 1.5f, keyShadow = 0.8f,
            ambientSky = Hex("#9cc4ff"), ambientEquator = Hex("#b8a8e8"), ambientGround = Hex("#7078b0"), ambientIntensity = 1.3f,
            fog = Hex("#cfd0f2"), fogDensity = 0.003f, mist = Hex("#e8dcf0"), mistDensity = 0.05f,
            farNear = Hex("#5062b0"), farFar = Hex("#d8c4e4"), town = 0.15f,
            water = Hex("#b4c2f0"),
            fire = 0.8f, tent = 0f, lantern = 0.3f,
            exposure = 0f, saturation = 4f, contrast = 2f, bloom = 0.6f,
            rainTint = Hex("#8088a8"),
        };

        /// <summary>Daytime. The sun stands high to the right, so shadows fall across the camp.</summary>
        public static CampPalette Day() => new CampPalette
        {
            horizon = Hex("#d6dcfa"), mid = Hex("#86b4f6"), zenith = Hex("#3f82ec"),
            cloud = Hex("#ffffff"), cloudDark = Hex("#b8c8f0"), cloudAmount = 0.55f, stars = 0f,
            sunAzimuth = 75f, sunElevation = 58f, sunGlowColor = Hex("#fff0c8"), sunGlow = 0.35f, sunDisc = 1f,
            moon = 0f,
            keyColor = Hex("#fff2d8"), keyIntensity = 2.4f, keyShadow = 0.85f,
            ambientSky = Hex("#a8ccff"), ambientEquator = Hex("#a0b0e0"), ambientGround = Hex("#6a7a60"), ambientIntensity = 1.0f,
            fog = Hex("#bcd0f6"), fogDensity = 0.0022f, mist = Hex("#d0dcf8"), mistDensity = 0.004f,
            farNear = Hex("#4460b4"), farFar = Hex("#acc2ee"), town = 0f,
            water = Hex("#8fb4f0"),
            fire = 0.7f, tent = 0f, lantern = 0f,
            exposure = 0f, saturation = 4f, contrast = 2f, bloom = 0.4f,
            rainTint = Hex("#7884a8"),
        };

        /// <summary>Late afternoon. The sun is low over the lake, in the picture, and everything is lit from behind.</summary>
        public static CampPalette Evening() => new CampPalette
        {
            horizon = Hex("#ffc4a0"), mid = Hex("#e8b8d8"), zenith = Hex("#7c9cec"),
            cloud = Hex("#ffe0c8"), cloudDark = Hex("#b890c8"), cloudAmount = 0.4f, stars = 0f,
            sunAzimuth = -20f, sunElevation = 7f, sunGlowColor = Hex("#ffd8a0"), sunGlow = 0.9f, sunDisc = 1f,
            moon = 0f,
            keyColor = Hex("#ffb070"), keyIntensity = 1.6f, keyShadow = 0.85f,
            ambientSky = Hex("#8fb0f0"), ambientEquator = Hex("#b098d8"), ambientGround = Hex("#806890"), ambientIntensity = 1.1f,
            fog = Hex("#e8c0c8"), fogDensity = 0.003f, mist = Hex("#f0c8c0"), mistDensity = 0.02f,
            farNear = Hex("#4a4a9a"), farFar = Hex("#c8a8d8"), town = 0.4f,
            water = Hex("#b0a8e0"),
            fire = 0.9f, tent = 0.3f, lantern = 0.5f,
            exposure = 0f, saturation = 6f, contrast = 4f, bloom = 0.8f,
            rainTint = Hex("#8088a8"),
        };
    }
}
