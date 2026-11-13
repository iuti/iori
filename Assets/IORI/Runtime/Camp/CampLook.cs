using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// Everything chico tunes about how the camp looks, in one asset: the look at each hour, the
    /// lamps, the brush, and how rain, the seasons and sleep mode shift things.
    /// If no asset is assigned, <see cref="CreateDefault"/> is used.
    /// </summary>
    [CreateAssetMenu(menuName = "IORI/Camp Look", fileName = "CampLook")]
    public sealed class CampLook : ScriptableObject
    {
        /// <summary>Raised when the defaults change in code; CampSetup then rewrites an untouched asset.</summary>
        public const int CurrentVersion = 4;
        [HideInInspector] public int version;

        [Serializable]
        public sealed class Key
        {
            [Tooltip("時刻(0〜24)")] [Range(0, 24)] public float hour;
            public string label;
            public CampPalette palette = new CampPalette();
        }

        [Tooltip("時刻ごとの見た目。時刻の順に並べる。あいだはなめらかに補間する")]
        public List<Key> keys = new List<Key>();

        [Header("灯り(色、強さ、届く距離)")]
        public Color fireColor = CampPalette.Hex("#ff9a48");
        public float fireIntensity = 8f;
        public float fireRange = 10f;
        public Color lanternColor = CampPalette.Hex("#ffc070");
        public float lanternIntensity = 1.3f;
        public float lanternRange = 4.5f;
        public Color tentColor = CampPalette.Hex("#ff9438");
        public float tentIntensity = 10f;
        public float tentRange = 7f;
        [Tooltip("灯りの大きさ(m)。大きいほど、灯りのすぐそばの明るさが頭打ちになり、光がなだらかに広がる")]
        public float lampSize = 0.7f;
        [Tooltip("照らされた色がこれより明るくなると、白く飛ばさずに色を保ったまま頭打ちにしていく")]
        public float litKnee = 0.7f;
        [Tooltip("照らされた色の上限")]
        public float litTop = 1.3f;
        [Tooltip("灯りの色ののり方。0 だと物の色に掛け合わせるだけ(緑の草は暗い茶色になる)。上げるほど、物の色に関係なく灯りの色がのる(絵の具を重ねたように)")]
        [Range(0, 1)] public float lampGlaze = 0.35f;
        [Tooltip("テントの布、ランタンのガラス、ラジオの目盛り、熾火そのものの明るさ(テントの布の色は CampTent マテリアルの「中から照らされた布」で決める)")]
        public float tentGlow = 1f;
        public float lanternGlow = 3.5f;
        public float dialGlow = 2.2f;
        public float emberGlow = 3.2f;
        public float townGlow = 3.5f;

        [Header("筆")]
        [Tooltip("キャンプの近くでの筆あとの細かさ(1m あたりの模様の数)")] public float strokeTiles = 0.667f;
        [Tooltip("この距離までは筆あとが同じ大きさ。その先は遠いほど大きくなる")] public float strokeNear = 5f;
        [Range(0, 1)] public float strokeAmount = 1f;
        [Tooltip("灯りの輪の段の細かさ(0 でなめらか)")] [Range(0, 6)] public float lightBands = 2.5f;
        [Tooltip("空の筆あとの細かさ")] public float skyStrokeTiles = 0.33f;
        [Range(0, 1)] public float skyStrokeAmount = 1f;

        [Header("風")]
        public Vector2 windDirection = new Vector2(0.8f, 0.6f);
        public float wind = 1f;
        public float rainWind = 2.2f;

        [Header("雨")]
        [Range(0, 1)] public float rainSkyMix = 0.7f;
        [Range(0, 1)] public float rainKey = 0.3f;
        [Range(0, 1)] public float rainAmbient = 0.8f;
        public float rainFog = 3f;
        public float rainMist = 0.05f;
        [Range(0, 1)] public float rainFire = 0.8f;

        [Header("季節(草と葉に掛ける色。夏が基準)")]
        public Color springGrass = new Color(1.05f, 1.08f, 0.95f);
        public Color springLeaf = new Color(1.1f, 1.15f, 0.9f);
        public Color summerGrass = Color.white;
        public Color summerLeaf = Color.white;
        public Color autumnGrass = new Color(1.5f, 1.15f, 0.7f);
        public Color autumnLeaf = new Color(2.6f, 1.05f, 0.45f);
        public Color winterGrass = new Color(1.5f, 1.45f, 1.5f);
        public Color winterLeaf = new Color(1.1f, 1.0f, 1.05f);

        [Header("おやすみモード(ランタンを消したとき)")]
        [Range(0, 1)] public float sleepAmbient = 0.6f;
        [Range(0, 1)] public float sleepFire = 0.3f;
        [Range(0, 1)] public float sleepTent = 0.25f;
        public float sleepExposure = -0.35f;

        [Header("ぼかし(ピントの合う距離はカメラが決める)")]
        [Tooltip("絞り。小さいほど前後がぼける")] public float aperture = 11f;
        [Tooltip("小物に寄ったときの絞り")] public float apertureClose = 3.2f;
        public float focalLength = 85f;

        public static CampLook CreateDefault()
        {
            var look = CreateInstance<CampLook>();
            look.name = "CampLook (default)";
            look.version = CurrentVersion;
            look.FillDefaultKeys();
            return look;
        }

        public void FillDefaultKeys()
        {
            keys = new List<Key>
            {
                new Key { hour = 0f, label = "夜", palette = CampPalette.Night() },
                new Key { hour = 3.5f, label = "夜", palette = CampPalette.Night() },
                new Key { hour = 4.75f, label = "夜明け前", palette = CampPalette.Twilight(true) },
                new Key { hour = 6.25f, label = "朝焼け", palette = CampPalette.Dawn() },
                new Key { hour = 8.5f, label = "昼", palette = CampPalette.Day() },
                new Key { hour = 15.5f, label = "昼", palette = CampPalette.Day() },
                new Key { hour = 17.0f, label = "夕方", palette = CampPalette.Evening() },
                new Key { hour = 18.25f, label = "日没後", palette = CampPalette.Twilight(false) },
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

        /// <summary>The name of the part of the day at this hour, for the panel.</summary>
        public string LabelAt(float hour)
        {
            if (keys == null || keys.Count == 0) FillDefaultKeys();
            hour = Mathf.Repeat(hour, 24f);
            Key best = keys[0];
            float bestD = 99f;
            foreach (var k in keys)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(hour * 15f, k.hour * 15f)) / 15f;
                if (d < bestD) { bestD = d; best = k; }
            }
            return best.label;
        }
    }
}
