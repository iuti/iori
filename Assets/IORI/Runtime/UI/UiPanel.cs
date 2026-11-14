using UnityEngine;
using UnityEngine.EventSystems;

namespace Iori.Ui
{
    /// <summary>
    /// A sheet of glass that rises from the bottom edge of the screen when a prop is tapped. It runs
    /// the full width of the screen with square corners, a thin light along its top edge and a soft
    /// shadow above it; what is on it sits in a column 390 wide in the middle. It is built from code
    /// the first time it opens. It has no close button: a press outside it (or the back key) closes it.
    /// A sheet can also cover the whole screen (<see cref="FullScreen"/>: the music player); such a
    /// sheet has a button of its own to go back.
    /// A finger that strokes downward on the sheet draws it down with it; let go far enough down, or
    /// with a flick, and the UI closes (back to the home screen), otherwise the sheet comes back up.
    /// (A stroke that starts on a button, a slider or the list of songs is handed on to the sheet:
    /// see <see cref="UiSlider"/> and <see cref="UiListScroll"/>.)
    /// </summary>
    public abstract class UiPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>Drawn down further than this (UI units, or a third of the sheet if that is less) the sheet goes away when let go.</summary>
        const float PullAway = 110f;
        /// <summary>... or let go while moving down faster than this (UI units a second).</summary>
        const float PullFlick = 650f;

        /// <summary>The width of the column the controls are laid out in.</summary>
        public const float Width = UiRoot.DesignWidth;

        protected RectTransform Rect;
        protected RectTransform Content;
        CanvasGroup _group;
        bool _built, _open, _covered;
        float _t;
        float _screen = 866f, _inset, _topInset;
        float _laidOut = -1f;
        // drawn down by a finger: how far (UI units), and how the finger moves
        float _pull, _pullBase, _pullFromY, _pullLastY, _pullLastT, _pullSpeed;
        bool _pulling, _flung;

        /// <summary>Height of the sheet's content in UI units (the room for the phone's gesture bar comes on top of this).</summary>
        public abstract float Height { get; }
        /// <summary>True for a sheet that covers the whole screen. Its <see cref="Height"/> is <see cref="Room"/>.</summary>
        public virtual bool FullScreen => false;
        public bool IsOpen => _open;
        public bool IsShowing => _open || _t > 0f;
        /// <summary>Open and done rising.</summary>
        public bool IsSettled => _open && _t >= 1f;
        /// <summary>True while a finger has drawn the open sheet down from its place.</summary>
        public bool IsPulled => _open && _pull > 0.5f;
        /// <summary>How far up the screen the open sheet reaches, in UI units from the bottom edge.</summary>
        public float Top => FullScreen ? _screen : Height + _inset;
        /// <summary>The height of the screen between the parts to keep free at its top and bottom, in UI units.</summary>
        protected float Room => Mathf.Max(200f, _screen - _inset - _topInset);
        protected float BottomInset => _inset;

        /// <summary>
        /// Set while another sheet lies over this one and hides all of it: it is then not drawn and does not follow what is going on.
        /// </summary>
        public bool Covered
        {
            get => _covered;
            set
            {
                if (_covered == value) return;
                _covered = value;
                if (Rect && gameObject.activeSelf) Apply();
            }
        }

        protected abstract void Build();
        /// <summary>Called every frame while the sheet is showing, to follow what is going on (the clock, the radio).</summary>
        protected virtual void Refresh() { }
        protected virtual void OnOpened() { }
        /// <summary>Called after <see cref="Build"/> and whenever <see cref="Height"/> has changed (a sheet over the whole screen follows the screen's size).</summary>
        protected virtual void Layout() { }
        /// <summary>The glass of the sheet.</summary>
        protected virtual ShapeStyle Pane()
        {
            var th = UiTheme.Current;
            return ShapeStyle.Gradient(th.glassTop, th.glassBottom, 0f).Glass()
                .TopLight(th.glassTopLight).Glow(th.glassShadow, th.glassShadowBlur, -th.glassShadowRise);
        }
        /// <summary>The back key (or the sheet's own back button). Return true if the sheet used it itself (to leave an inner page); false closes the sheet.</summary>
        public virtual bool Back() => false;

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

        /// <summary>
        /// The size of the screen in UI units, and the room to keep free at its bottom (the phone's gesture bar; the glass
        /// runs through it) and at its top (the camera's cut-out; only a sheet over the whole screen reaches it).
        /// </summary>
        public void SetScreen(float height, float bottomInset, float topInset)
        {
            _screen = Mathf.Max(1f, height);
            _inset = Mathf.Max(0f, bottomInset);
            _topInset = Mathf.Max(0f, topInset);
            if (Rect && gameObject.activeSelf) Apply();
        }

        public void Open()
        {
            // (active before anything is added to it, so every new piece wakes up as it is made)
            gameObject.SetActive(true);
            _covered = false;
            if (!_built)
            {
                _built = true;
                var pane = UiKit.Shape("Glass", Rect, 0, 0, 0, 0, Pane());
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
            _pull = 0f;
            _pulling = _flung = false;
            Apply();
            Refresh();
            OnOpened();
        }

        public void Close()
        {
            _open = false;
            _pulling = false;
        }

        // ---- drawn down by a finger

        /// <summary>For the screenshot runner: holds the sheet as if a finger had drawn it down this far. Let go with OnEndDrag(null).</summary>
        public void HoldPulled(float units)
        {
            _pulling = _open && units > 0f;
            _pull = _pulling ? Mathf.Min(units, Top) : 0f;
            _pullSpeed = 0f;
            _pullLastT = Time.unscaledTime;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _pulling = false;
            var root = UiRoot.Instance;
            Vector2 d = e.position - e.pressPosition;
            if (Debug.isDebugBuild) Debug.Log($"[IORI] ui pull begin {name}: open {_open} top {(root ? root.OpenPanel == this : false)} d {d.x:0},{d.y:0}");
            if (!_open || !root || root.OpenPanel != this) return;
            // only a stroke that goes down
            if (-d.y <= Mathf.Abs(d.x) * 1.2f) return;
            _pulling = true;
            // (from where the finger is now: the sheet does not jump by the distance it took to tell a stroke from a press)
            _pullBase = _pull;
            _pullFromY = _pullLastY = e.position.y;
            _pullLastT = Time.unscaledTime;
            _pullSpeed = 0f;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_pulling) return;
            float scale = Mathf.Max(0.01f, Rect.lossyScale.y);
            _pull = Mathf.Clamp(_pullBase + (_pullFromY - e.position.y) / scale, 0f, Top);
            float now = Time.unscaledTime;
            if (now - _pullLastT > 0.001f)
            {
                float speed = (_pullLastY - e.position.y) / scale / (now - _pullLastT);
                _pullSpeed = Mathf.Lerp(_pullSpeed, speed, 0.6f);
                _pullLastY = e.position.y;
                _pullLastT = now;
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Debug.isDebugBuild) Debug.Log($"[IORI] ui pull end {name}: pulling {_pulling} pull {_pull:0} speed {_pullSpeed:0} top {Top:0}");
            if (!_pulling) return;
            _pulling = false;
            // (a finger that rested before it let go did not flick)
            if (Time.unscaledTime - _pullLastT > 0.12f) _pullSpeed = 0f;
            bool away = _pull > Mathf.Min(PullAway, Top * 0.33f) || (_pullSpeed > PullFlick && _pull > 12f);
            var root = UiRoot.Instance;
            if (!away || !_open || !root) return;
            _flung = true;
            UiSounds.Play(UiSounds.Close);
            root.Close();
        }

        void Apply()
        {
            // (let go by a finger that drew it down, it leaves at the finger's even pace, without a slow start)
            float k = _open ? UiKit.EaseOut(_t) : _flung ? Mathf.Clamp01(_t) : UiKit.EaseInOut(_t);
            float h = Top;
            Rect.sizeDelta = new Vector2(0f, h);
            // it rises from below the edge; the quick fade only keeps its shadow from popping in
            // (drawn down by a finger it sits that much lower; closing from there it goes the rest of the way)
            Rect.anchoredPosition = new Vector2(0f, -(1f - k) * h - Mathf.Min(_pull, h) * k);
            _group.alpha = _covered ? 0f : Mathf.Clamp01(k * 4f);
            _group.blocksRaycasts = _open;
            if (!Content) return;
            float height = Height;
            Content.sizeDelta = new Vector2(Width, height);
            Content.anchoredPosition = new Vector2(0f, FullScreen ? -_topInset : 0f);
            if (!Mathf.Approximately(height, _laidOut))
            {
                _laidOut = height;
                Layout();
            }
        }

        void Update()
        {
            var th = UiTheme.Current;
            float target = _open ? 1f : 0f;
            if (!Mathf.Approximately(_t, target))
            {
                float seconds = _open ? (FullScreen ? th.screenOpenSeconds : th.openSeconds) : (FullScreen ? th.screenCloseSeconds : th.closeSeconds);
                _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
            }
            // let go without having gone far: back up to its place
            if (_open && !_pulling && _pull > 0f)
            {
                _pull = Mathf.Lerp(_pull, 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f));
                if (_pull < 0.5f) _pull = 0f;
            }
            Apply();
            if (!_open && _t <= 0f)
            {
                _pull = 0f;
                _flung = false;
                gameObject.SetActive(false);
                return;
            }
            if (!_covered) Refresh();
        }
    }
}
