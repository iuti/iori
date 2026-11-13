using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// A sheet of glass that rises from the bottom edge of the screen when a prop is tapped. It runs
    /// the full width of the screen with square corners, a thin light along its top edge and a soft
    /// shadow above it; what is on it sits in a column 390 wide in the middle. It is built from code
    /// the first time it opens. It has no close button: a press outside it (or the back key) closes it.
    /// </summary>
    public abstract class UiPanel : MonoBehaviour
    {
        /// <summary>The width of the column the controls are laid out in.</summary>
        public const float Width = UiRoot.DesignWidth;

        protected RectTransform Rect;
        protected RectTransform Content;
        CanvasGroup _group;
        bool _built, _open;
        float _t;
        float _inset;

        /// <summary>Height of the sheet's content in UI units (the room for the phone's gesture bar comes on top of this).</summary>
        public abstract float Height { get; }
        public bool IsOpen => _open;
        public bool IsShowing => _open || _t > 0f;
        /// <summary>How far up the screen the open sheet reaches, in UI units from the bottom edge.</summary>
        public float Top => Height + _inset;

        protected abstract void Build();
        /// <summary>Called every frame while the sheet is showing, to follow what is going on (the clock, the radio).</summary>
        protected virtual void Refresh() { }
        protected virtual void OnOpened() { }

        public void Setup(Transform canvas)
        {
            gameObject.layer = UiKit.UiLayer;
            Rect = gameObject.GetComponent<RectTransform>();
            if (!Rect) Rect = gameObject.AddComponent<RectTransform>();
            Rect.SetParent(canvas, false);
            // along the whole bottom edge
            Rect.anchorMin = new Vector2(0f, 0f);
            Rect.anchorMax = new Vector2(1f, 0f);
            Rect.pivot = new Vector2(0.5f, 0f);
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>Room to keep free at the bottom of the sheet (the phone's gesture bar), in UI units. The glass runs through it.</summary>
        public void SetBottomInset(float inset)
        {
            _inset = Mathf.Max(0f, inset);
            if (Rect) Apply();
        }

        public void Open()
        {
            // (active before anything is added to it, so every new piece wakes up as it is made)
            gameObject.SetActive(true);
            if (!_built)
            {
                _built = true;
                var th = UiTheme.Current;
                var glass = ShapeStyle.Gradient(th.glassTop, th.glassBottom, 0f).Glass()
                    .TopLight(th.glassTopLight).Glow(th.glassShadow, th.glassShadowBlur, -th.glassShadowRise);
                var pane = UiKit.Shape("Glass", Rect, 0, 0, 0, 0, glass);
                UiKit.Stretch(pane.rectTransform);
                // the pane takes the presses that land between the controls, so they do not close the sheet
                pane.raycastTarget = true;
                Content = UiKit.Node("Content", Rect, 0, 0, Width, Height);
                Content.anchorMin = Content.anchorMax = new Vector2(0.5f, 1f);
                Content.pivot = new Vector2(0.5f, 1f);
                Content.anchoredPosition = Vector2.zero;
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
            float k = _open ? UiKit.EaseOut(_t) : UiKit.EaseInOut(_t);
            float h = Height + _inset;
            Rect.sizeDelta = new Vector2(0f, h);
            // it rises from below the edge; the quick fade only keeps its shadow from popping in
            Rect.anchoredPosition = new Vector2(0f, -(1f - k) * h);
            _group.alpha = Mathf.Clamp01(k * 4f);
            _group.blocksRaycasts = _open;
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
    }
}
