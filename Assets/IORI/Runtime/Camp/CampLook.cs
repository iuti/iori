using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// Everything chico tunes about the camp's colors, in one asset: the look at each hour,
    /// the colors of the props, and how rain and the seasons shift things.
    /// If no asset is assigned, <see cref="CreateDefault"/> is used.
    /// </summary>
    [CreateAssetMenu(menuName = "IORI/Camp Look", fileName = "CampLook")]
    public sealed class CampLook : ScriptableObject
    {
        [Serializable]
        public sealed class Key
        {
            [Tooltip("時刻(0〜24)")] [Range(0, 24)] public float hour;
            public string label;
            public CampPalette palette = new CampPalette();
        }

        [Tooltip("時刻ごとの見た目。時刻の順に並べる。あいだはなめらかに補間する")]
        public List<Key> keys = new List<Key>();

        [Header("小物の色(時間で変わらない。光は時間帯に合わせて当たる)")]
        public Color wood = CampPalette.Hex("#54487c");
        public Color dark = CampPalette.Hex("#22264c");
        public Color crate = CampPalette.Hex("#3c3c78");
        public Color brass = CampPalette.Hex("#d9a02c");
        public Color cream = CampPalette.Hex("#e2d8cc");
        public Color log = CampPalette.Hex("#3a2a4a");
        public Color knob = CampPalette.Hex("#151733");
        public Color stumpTop = CampPalette.Hex("#b8a8c8");
        [Tooltip("椅子の布")] public Color fabric = CampPalette.Hex("#3e3a7e");
        [Tooltip("灯りが消えているときのテントの布")] public Color tentCloth = CampPalette.Hex("#c9a07a");
        [Tooltip("草むらの小さな花")] public Color flower = CampPalette.Hex("#e8b8e8");

        [Header("雨")]
        [Range(0, 1)] public float rainSkyMix = 0.62f;
        [Range(0, 1)] public float rainFogMix = 0.5f;
        [Range(0, 1)] public float rainHillMix = 0.4f;
        [Range(0, 1)] public float rainSilhouetteMix = 0.25f;
        [Range(0, 1)] public float rainWaterMix = 0.45f;

        [Header("季節(草と葉に掛ける色。夏が基準)")]
        public Color springGrass = new Color(1.05f, 1.0f, 1.08f);
        public Color summerGrass = Color.white;
        public Color autumnGrass = new Color(1.25f, 0.82f, 0.95f);
        public Color winterGrass = new Color(1.25f, 1.15f, 1.35f);
        public Color autumnLeaf = new Color(1.6f, 0.9f, 1.05f);
        [Range(0, 1)] public float winterDesaturate = 0.55f;

        [Header("おやすみモード(ランタンを消したとき)")]
        [Range(0, 1)] public float sleepAmbient = 0.55f;
        [Range(0, 1)] public float sleepFire = 0.35f;

        public static CampLook CreateDefault()
        {
            var look = CreateInstance<CampLook>();
            look.name = "CampLook (default)";
            look.FillDefaultKeys();
            return look;
        }

        public void FillDefaultKeys()
        {
            keys = new List<Key>
            {
                new Key { hour = 0f, label = "夜", palette = CampPalette.Night() },
                new Key { hour = 3.5f, label = "夜", palette = CampPalette.Night() },
                new Key { hour = 4.75f, label = "夜明け前", palette = CampPalette.Twilight() },
                new Key { hour = 6.25f, label = "朝焼け", palette = CampPalette.Dawn() },
                new Key { hour = 8.5f, label = "昼", palette = CampPalette.Day() },
                new Key { hour = 15.5f, label = "昼", palette = CampPalette.Day() },
                new Key { hour = 17.0f, label = "夕方", palette = CampPalette.Evening() },
                new Key { hour = 18.25f, label = "日没後", palette = CampPalette.Twilight() },
                new Key { hour = 19.75f, label = "夜", palette = CampPalette.Night() },
                new Key { hour = 24f, label = "夜", palette = CampPalette.Night() },
            };
        }

        /// <summary>Palette at a given hour (0..24), blended between the surrounding keys.</summary>
        public void Evaluate(float hour, CampPalette into)
        {
            if (keys == null || keys.Count == 0) FillDefaultKeys();
            hour = Mathf.Repeat(hour, 24f);
            Key a = keys[keys.Count - 1], b = keys[0];
            float ha = a.hour - 24f, hb = b.hour;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].hour > hour) { b = keys[i]; hb = b.hour; a = i > 0 ? keys[i - 1] : keys[keys.Count - 1]; ha = i > 0 ? a.hour : a.hour - 24f; break; }
                if (i == keys.Count - 1) { a = keys[i]; ha = a.hour; b = keys[0]; hb = b.hour + 24f; }
            }
            float t = hb - ha > 1e-4f ? Mathf.Clamp01((hour - ha) / (hb - ha)) : 0f;
            t = t * t * (3f - 2f * t);
            CampPalette.Lerp(a.palette, b.palette, t, into);
        }
    }
}
