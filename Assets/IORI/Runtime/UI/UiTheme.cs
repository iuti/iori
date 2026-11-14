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

        [Header("音楽プレーヤー(画面いっぱいのガラス)")]
        [Tooltip("プレーヤーの画面の上の色と下の色")] public Color32 playerTop = new Color32(48, 42, 120, 107);
        public Color32 playerBottom = new Color32(18, 20, 68, 173);
        [Tooltip("曲の一覧の画面の上の色と下の色")] public Color32 listTop = new Color32(48, 42, 120, 117);
        public Color32 listBottom = new Color32(18, 20, 68, 179);
        [Tooltip("ジャケットの下の影")] public Color32 jacketShadow = new Color32(6, 8, 40, 87);
        public float jacketShadowBlur = 40f;
        public float jacketShadowDrop = 16f;
        [Tooltip("絵の無いジャケットの面")] public Color32 jacketBlank = new Color32(241, 238, 255, 20);
        [Tooltip("ホームの下の帯(ガラス)の上の色と下の色")] public Color32 barTop = new Color32(46, 42, 118, 128);
        public Color32 barBottom = new Color32(20, 22, 72, 179);
        [Tooltip("曲の一覧の中の下の帯")] public Color32 barSolid = new Color32(27, 25, 84, 240);
        public Color32 barShadow = new Color32(6, 8, 40, 77);
        public float barShadowBlur = 34f;
        public float barShadowRise = 14f;
        [Tooltip("帯の上端の線、タブの下の線")] public Color32 barLine = new Color32(255, 255, 255, 41);
        public Color32 tabLine = new Color32(241, 238, 255, 41);

        [Header("つまみ")]
        public Color32 track = new Color32(255, 255, 255, 51);
        public Color32 trackDim = new Color32(255, 255, 255, 33);
        public Color32 fillDim = new Color32(241, 238, 255, 97);

        [Header("動き")]
        public float openSeconds = 0.34f;
        public float closeSeconds = 0.22f;
        [Tooltip("画面いっぱいのシートが上がる、下がる時間")] public float screenOpenSeconds = 0.42f;
        public float screenCloseSeconds = 0.28f;

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
