using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The colours and sizes every panel shares. The rule of the look: warm amber is only for what is
    /// sounding or lit right now; everything else is indigo glass with pale wisteria text, the same
    /// rule as in the scenery ("warm colours are light sources only").
    /// To tune it in the editor, make one with Create > IORI > UI Theme and save it as
    /// Assets/IORI/Resources/IoriUI/Theme. Without that file these defaults are used.
    /// </summary>
    [CreateAssetMenu(menuName = "IORI/UI Theme", fileName = "Theme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("文字")]
        public Color32 text = new Color32(0xF1, 0xEE, 0xFF, 255);
        public Color32 textBright = new Color32(0xFB, 0xF9, 0xFF, 255);
        public Color32 textSoft = new Color32(0xC9, 0xC5, 0xF2, 255);
        public Color32 textDim = new Color32(0xA9, 0xA5, 0xD8, 255);
        public Color32 textWarm = new Color32(0xFF, 0xCF, 0x8A, 255);

        [Header("ガラス")]
        [Tooltip("パネルの上の色。アルファが景色の上にかぶせる濃さ")] public Color32 glassTop = new Color32(52, 46, 124, 107);
        [Tooltip("パネルの下の色")] public Color32 glassBottom = new Color32(20, 22, 72, 158);
        public float glassRadius = 30f;
        [Range(0, 1)] public float glassTopLight = 0.24f;
        [Range(0, 1)] public float glassRing = 0.05f;
        public Color32 glassShadow = new Color32(6, 8, 40, 107);
        public float glassShadowBlur = 48f;
        public float glassShadowDrop = 18f;
        [Tooltip("後ろの景色のぼかしの強さ(大きいほどぼける)")] [Range(0.6f, 2.5f)] public float blurSpread = 1.25f;
        [Tooltip("後ろの景色の色の濃さ")] [Range(0.5f, 2f)] public float blurSaturation = 1.25f;

        [Header("鳴っている、点いているもの")]
        public Color32 amber = new Color32(0xFF, 0xC5, 0x6B, 255);
        public Color32 amberLight = new Color32(0xFF, 0xE2, 0xA8, 255);
        public Color32 amberDeep = new Color32(0xF3, 0xA6, 0x4E, 255);
        public Color32 amberGlow = new Color32(255, 180, 90, 128);
        public Color32 amberInk = new Color32(0x3A, 0x20, 0x06, 255);

        [Header("つまみ")]
        public Color32 track = new Color32(255, 255, 255, 56);
        public Color32 knob = new Color32(0xFB, 0xF9, 0xFF, 255);
        public Color32 knobShadow = new Color32(6, 8, 40, 115);
        public Color32 chip = new Color32(255, 255, 255, 31);

        [Header("動き")]
        public float openSeconds = 0.34f;
        public float closeSeconds = 0.22f;
        public float slide = 26f;

        static UiTheme _current;

        public static UiTheme Current
        {
            get
            {
                if (_current) return _current;
                _current = Resources.Load<UiTheme>("IoriUI/Theme");
                if (!_current)
                {
                    _current = CreateInstance<UiTheme>();
                    _current.hideFlags = HideFlags.HideAndDontSave;
                }
                return _current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetCurrent() => _current = null;

        public static Color32 Rgba(int r, int g, int b, float a) => new Color32((byte)r, (byte)g, (byte)b, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

        public static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
    }
}
