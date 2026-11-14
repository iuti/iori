using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Iori.Ui
{
    /// <summary>
    /// The small building blocks the panels are put together from. Positions are given the way the
    /// look proposal gives them: x and y of the top-left corner inside the parent, then width and
    /// height, in units where a phone held upright is 390 across.
    /// </summary>
    public static class UiKit
    {
        public const int UiLayer = 5;

        public static RectTransform Node(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static UiShape Shape(string name, Transform parent, float x, float y, float w, float h, ShapeStyle style)
        {
            var rt = Node(name, parent, x, y, w, h);
            var shape = rt.gameObject.AddComponent<UiShape>();
            shape.raycastTarget = false;
            shape.Style = style;
            return shape;
        }

        public static UiShape Box(string name, Transform parent, float x, float y, float w, float h, Color32 color, float radius = 0f)
            => Shape(name, parent, x, y, w, h, ShapeStyle.Fill(color, radius));

        /// <summary>One line of text in a box: the line sits in the middle of the box's height, as a line of that height does in a browser.</summary>
        public static TextMeshProUGUI Text(string name, Transform parent, float x, float y, float w, float h, string text,
            TMP_FontAsset font, float size, Color32 color, TextAlignmentOptions align = TextAlignmentOptions.Left, float spacingEm = 0f)
        {
            var rt = Node(name, parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = spacingEm * 100f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = false;
            t.margin = Vector4.zero;
            t.text = text;
            return t;
        }

        /// <summary>A text too long for its box ends in "…" instead of running out of it.</summary>
        public static void Clip(TextMeshProUGUI t) => t.overflowMode = TextOverflowModes.Ellipsis;

        public static Texture2D LoadIcon(string icon)
        {
            var tex = Resources.Load<Texture2D>("IoriUI/Icons/" + icon);
            if (!tex) Debug.LogWarning("[IORI] アイコンが見つかりません: IoriUI/Icons/" + icon);
            // (the pictures are drawn much smaller than they are stored: lean towards the sharper size)
            else tex.mipMapBias = -0.5f;
            return tex;
        }

        /// <summary>
        /// An icon: a white picture tinted with a colour. It is drawn by the shape shader, like everything
        /// else, so its thin lines get the same weight as in the look proposal. Change it later with
        /// <see cref="Tint"/> and <see cref="UiShape.Picture"/>.
        /// </summary>
        public static UiShape Icon(string name, Transform parent, float x, float y, float size, string icon, Color32 color)
        {
            var shape = Shape(name, parent, x, y, size, size, ShapeStyle.Fill(color, 0f));
            shape.Picture = LoadIcon(icon);
            if (!shape.Picture) shape.enabled = false;
            return shape;
        }

        public static void Tint(UiShape icon, Color32 color) => icon.Style = ShapeStyle.Fill(color, 0f);

        /// <summary>A press area. Put the things that should shrink a little while pressed under <see cref="UiButton.visual"/>.</summary>
        public static UiButton Button(string name, Transform parent, float x, float y, float w, float h, Action onClick, string sound = UiSounds.Tap)
        {
            var rt = Node(name, parent, x, y, w, h);
            rt.gameObject.AddComponent<UiHit>();
            var b = rt.gameObject.AddComponent<UiButton>();
            b.onClick = onClick;
            b.sound = sound;
            b.visual = Node("Visual", rt, 0, 0, w, h);
            return b;
        }

        /// <summary>A button that is only its icon (no disc behind it), with a press area of the given size.</summary>
        public static UiButton IconButton(string name, Transform parent, float x, float y, float size, string icon, float iconSize, Color32 color, Action onClick, string sound = UiSounds.Tap)
        {
            var b = Button(name, parent, x, y, size, size, onClick, sound);
            float o = (size - iconSize) * 0.5f;
            b.icon = Icon("Icon", b.visual, o, o, iconSize, icon, color);
            return b;
        }

        public static float EaseOut(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }

    /// <summary>A button: shrinks a little while held, plays its sound and calls <see cref="onClick"/> when let go on it.</summary>
    public sealed class UiButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Action onClick;
        public RectTransform visual;
        public UiShape icon;
        public string sound = UiSounds.Tap;
        public float shrink = 0.10f;
        float _press, _target;

        public void OnPointerDown(PointerEventData e) => _target = 1f;
        public void OnPointerUp(PointerEventData e) => _target = 0f;

        public void OnPointerClick(PointerEventData e)
        {
            UiSounds.Play(sound);
            onClick?.Invoke();
        }

        void OnDisable()
        {
            _press = _target = 0f;
            if (visual) visual.localScale = Vector3.one;
        }

        void Update()
        {
            if (Mathf.Approximately(_press, _target)) return;
            _press = Mathf.MoveTowards(_press, _target, Time.unscaledDeltaTime * (_target > _press ? 14f : 7f));
            if (visual) visual.localScale = Vector3.one * (1f - shrink * UiKit.EaseInOut(_press));
        }
    }

    /// <summary>
    /// A slider: a thin line, the filled part of it, and a small upright bar as the handle. The press
    /// area is 44 high. Pressing or dragging anywhere on it moves the handle there.
    /// </summary>
    public sealed class UiSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public Action<float> onChanged;
        /// <summary>Called when the finger lets go, with the value it was left at.</summary>
        public Action<float> onReleased;
        float _value, _width, _handleHeight = 18f;
        bool _dim;
        RectTransform _rt;
        UiShape _track, _fill, _handle;
        Color32 _fillColor;
        float _press, _pressTarget;

        public float Value => _value;
        /// <summary>True while a finger is on it.</summary>
        public bool IsHeld => _pressTarget > 0f;

        /// <summary>The line is 21 below y, in the middle of the press area. handle is the height of the upright bar.</summary>
        public static UiSlider Create(string name, Transform parent, float x, float y, float w, float handle = 18f)
        {
            var rt = UiKit.Node(name, parent, x, y, w, 44f);
            rt.gameObject.AddComponent<UiHit>();
            var s = rt.gameObject.AddComponent<UiSlider>();
            var th = UiTheme.Current;
            s._rt = rt;
            s._width = w;
            s._handleHeight = handle;
            s._fillColor = th.text;
            s._track = UiKit.Box("Track", rt, 0, 21, w, 2, th.track);
            s._fill = UiKit.Box("Fill", rt, 0, 21, 0, 2, th.text);
            s._handle = UiKit.Box("Handle", rt, -1.5f, 22 - handle * 0.5f, 3, handle, th.textBright);
            s.Paint();
            s.Layout();
            return s;
        }

        /// <summary>The colour of the filled part (amber for a playing position).</summary>
        public void SetFillColor(Color32 c)
        {
            _fillColor = c;
            Paint();
        }

        /// <summary>Greyed, for a sound that is not heard now (rain while it is not raining). It can still be set.</summary>
        public void SetDim(bool dim)
        {
            if (_dim == dim) return;
            _dim = dim;
            Paint();
        }

        public void SetValue(float v, bool notify = false)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(v, _value)) return;
            _value = v;
            Layout();
            if (notify) onChanged?.Invoke(_value);
        }

        void Paint()
        {
            var th = UiTheme.Current;
            _track.Style = ShapeStyle.Fill(_dim ? th.trackDim : th.track, 0);
            _fill.Style = ShapeStyle.Fill(_dim ? th.fillDim : _fillColor, 0);
            _handle.Style = ShapeStyle.Fill(_dim ? th.textDim : th.textBright, 0);
        }

        void Layout()
        {
            float grow = 4f * UiKit.EaseInOut(_press);
            UiKit.Place(_fill.rectTransform, 0, 21, _width * _value, 2);
            UiKit.Place(_handle.rectTransform, _width * _value - 1.5f, 22 - (_handleHeight + grow) * 0.5f, 3, _handleHeight + grow);
        }

        void Set(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var lp)) return;
            SetValue((lp.x + _width * 0.5f) / _width, true);
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnPointerDown(PointerEventData e) { _pressTarget = 1f; Set(e); }
        public void OnDrag(PointerEventData e) => Set(e);
        public void OnPointerUp(PointerEventData e)
        {
            _pressTarget = 0f;
            UiSounds.Play(UiSounds.Slider, 0.7f);
            onReleased?.Invoke(_value);
        }

        void OnDisable() { _press = _pressTarget = 0f; if (_handle) Layout(); }

        void Update()
        {
            if (Mathf.Approximately(_press, _pressTarget)) return;
            // the handle grows a little taller while held
            _press = Mathf.MoveTowards(_press, _pressTarget, Time.unscaledDeltaTime * 10f);
            Layout();
        }
    }
}
