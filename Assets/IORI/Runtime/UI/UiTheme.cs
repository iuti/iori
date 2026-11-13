using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The colours and sizes every panel shares. The rules of the look (from chico's review of the
    /// first proposal): square corners, no glow, as few words as possible. Amber is only the thin
    /// mark of "now" (the radio's needle, the playing position); everything else is indigo glass
    /// with pale wisteria lines and letters.
    /// To tune it in the editor, make one with Create > IORI > UI Theme and save it as
    /// Assets/IORI/Resources/IoriUI/Theme. Without that file these defaults are used.
    /// </summary>
    [CreateAssetMenu(menuName = "IORI/UI Theme", fileName = "Theme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("文字と線")]
        public Color32 text = new Color32(0xF1, 0xEE, 0xFF, 255);
        public Color32 textBright = new Color32(0xFB, 0xF9, 0xFF, 255);
        public Color32 textSoft = new Color32(0xB4, 0xB0, 0xE4, 255);
        public Color32 textDim = new Color32(0x8F, 0x8B, 0xC4, 255);
        [Tooltip("明るい文字は暗い背景の上で太って見えるので、その分だけ細らせる(0 でそのまま)")]
        [Range(-0.3f, 0.1f)] public float textThin = -0.08f;

        [Header("いまを示す印")]
        public Color32 amber = new Color32(0xFF, 0xC5, 0x6B, 255);

        [Header("ガラスのシート")]
        [Tooltip("シートの上の色。アルファが景色の上にかぶせる濃さ")] public Color32 glassTop = new Color32(52, 46, 124, 112);
        [Tooltip("シートの下の色")] public Color32 glassBottom = new Color32(20, 22, 72, 168);
        [Tooltip("上端の細い光")] [Range(0, 1)] public float glassTopLight = 0.26f;
        public Color32 glassShadow = new Color32(6, 8, 40, 77);
        public float glassShadowBlur = 44f;
        public float glassShadowRise = 18f;
        [Tooltip("後ろの景色のぼかしの強さ(大きいほどぼける)")] [Range(0.6f, 2.5f)] public float blurSpread = 1.25f;
        [Tooltip("後ろの景色の色の濃さ")] [Range(0.5f, 2f)] public float blurSaturation = 1.25f;

        [Header("つまみ")]
        public Color32 track = new Color32(255, 255, 255, 51);
        public Color32 trackDim = new Color32(255, 255, 255, 33);
        public Color32 fillDim = new Color32(241, 238, 255, 97);

        [Header("動き")]
        public float openSeconds = 0.34f;
        public float closeSeconds = 0.22f;

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
    }
}
