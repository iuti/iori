using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// A glass panel that opens from the bottom of the screen when a prop is tapped. It is built from
    /// code the first time it opens. Opening fades it in while it rises a little; closing is quicker.
    /// </summary>
    public abstract class UiPanel : MonoBehaviour
    {
        public const float Width = 370f;
        public const float Margin = 10f;

        protected RectTransform Rect;
        protected RectTransform Content;
        CanvasGroup _group;
        bool _built, _open;
        float _t;
        float _bottom;

        /// <summary>Height of the panel in UI units.</summary>
        public abstract float Height { get; }
        public bool IsOpen => _open;
        public bool IsShowing => _open || _t > 0f;
        /// <summary>How far up the screen the panel reaches, in UI units from the bottom edge.</summary>
        public float Top => _bottom + Height;

        protected abstract void Build();
        /// <summary>Called every frame while the panel is showing, to follow what is going on (the clock, the radio).</summary>
        protected virtual void Refresh() { }
        protected virtual void OnOpened() { }

        public void Setup(Transform canvas)
        {
            gameObject.layer = UiKit.UiLayer;
            Rect = gameObject.GetComponent<RectTransform>();
            if (!Rect) Rect = gameObject.AddComponent<RectTransform>();
            Rect.SetParent(canvas, false);
            Rect.anchorMin = Rect.anchorMax = new Vector2(0.5f, 0f);
            Rect.pivot = new Vector2(0.5f, 0f);
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>Space to keep free under the panel (the phone's gesture bar), in UI units.</summary>
        public void SetBottomInset(float inset)
        {
            _bottom = Margin + inset;
            if (Rect) Apply();
        }

        public void Open()
        {
            // (active before anything is added to it, so every new piece wakes up as it is made)
            gameObject.SetActive(true);
            if (!_built)
            {
                _built = true;
                Rect.sizeDelta = new Vector2(Width, Height);
                var th = UiTheme.Current;
                var glass = ShapeStyle.Gradient(th.glassTop, th.glassBottom, th.glassRadius).Glass()
                    .TopLight(th.glassTopLight).Ring(th.glassRing).Glow(th.glassShadow, th.glassShadowBlur, th.glassShadowDrop);
                var pane = UiKit.Shape("Glass", Rect, 0, 0, Width, Height, glass);
                // the pane takes the presses that land between the controls, so they do not close the panel
                pane.raycastTarget = true;
                Content = UiKit.Node("Content", Rect, 0, 0, Width, Height);
                Build();
            }
            _open = true;
            Refresh();
            Apply();
            OnOpened();
        }

        public void Close() => _open = false;

        void Apply()
        {
            var th = UiTheme.Current;
            float k = _open ? UiKit.EaseOut(_t) : UiKit.EaseInOut(_t);
            _group.alpha = k;
            _group.blocksRaycasts = _open;
            Rect.anchoredPosition = new Vector2(0f, _bottom - (1f - k) * th.slide);
        }

        void Update()
        {
            var th = UiTheme.Current;
            float target = _open ? 1f : 0f;
            if (!Mathf.Approximately(_t, target))
                _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, _open ? th.openSeconds : th.closeSeconds));
            Apply();
            if (!_open && _t <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }
            Refresh();
        }

        // ---- pieces every panel has

        /// <summary>The small title at the top left and the round close button at the top right.</summary>
        protected void Header(string title, System.Action close)
        {
            var th = UiTheme.Current;
            UiKit.Text("Title", Content, 22, 24.5f, 220, 19, title, UiFonts.Medium, 13, th.textSoft, TMPro.TextAlignmentOptions.Left, 0.18f);
            var b = UiKit.Button("Close", Content, Width - 56, 12, 44, 44, close, UiSounds.Close);
            UiKit.Shape("Circle", b.visual, 5, 5, 34, 34, ShapeStyle.Fill(th.chip, 17));
            UiKit.Icon("Icon", b.visual, 14, 14, 16, "chevron_down", th.text);
        }
    }
}
