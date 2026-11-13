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

        public static Texture2D LoadIcon(string icon)
        {
            var tex = Resources.Load<Texture2D>("IoriUI/Icons/" + icon);
            if (!tex) Debug.LogWarning("[IORI] アイコンが見つかりません: IoriUI/Icons/" + icon);
            return tex;
        }

        public static UnityEngine.UI.RawImage Icon(string name, Transform parent, float x, float y, float size, string icon, Color32 color)
        {
            var rt = Node(name, parent, x, y, size, size);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            img.texture = LoadIcon(icon);
            img.color = color;
            img.raycastTarget = false;
            if (!img.texture) img.enabled = false;
            return img;
        }

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

        public static float EaseOut(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }

    /// <summary>A button: shrinks a little while held, plays its sound and calls <see cref="onClick"/> when let go on it.</summary>
    public sealed class UiButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Action onClick;
        public RectTransform visual;
        public string sound = UiSounds.Tap;
        public float shrink = 0.07f;
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
    /// A volume slider: a thin track, the filled part, and a round knob. The press area is 44 high.
    /// Dragging anywhere on it moves the knob there.
    /// </summary>
    public sealed class UiSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public Action<float> onChanged;
        float _value, _width;
        bool _dim;
        RectTransform _rt, _knob;
        UiShape _track, _fill, _knobShape;
        float _press, _pressTarget;

        public float Value => _value;

        public static UiSlider Create(string name, Transform parent, float x, float y, float w)
        {
            var rt = UiKit.Node(name, parent, x, y, w, 44f);
            rt.gameObject.AddComponent<UiHit>();
            var s = rt.gameObject.AddComponent<UiSlider>();
            var th = UiTheme.Current;
            s._rt = rt;
            s._width = w;
            s._track = UiKit.Box("Track", rt, 0, 20, w, 4, th.track, 2);
            s._fill = UiKit.Box("Fill", rt, 0, 20, 0, 4, th.text, 2);
            s._knobShape = UiKit.Shape("Knob", rt, -10, 12, 20, 20, default);
            s._knob = s._knobShape.rectTransform;
            s.Paint();
            s.Layout();
            return s;
        }

        /// <summary>Greyed, for a sound that is not playing now (rain while it is not raining). It can still be set.</summary>
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
            _track.Style = ShapeStyle.Fill(_dim ? UiTheme.Rgba(255, 255, 255, 0.16f) : th.track, 2);
            _fill.Style = ShapeStyle.Fill(_dim ? UiTheme.Rgba(241, 238, 255, 0.45f) : th.text, 2);
            _knobShape.Style = ShapeStyle.Fill(_dim ? th.textDim : th.knob, 10).Glow(th.knobShadow, 8, 2);
        }

        void Layout()
        {
            UiKit.Place(_fill.rectTransform, 0, 20, _width * _value, 4);
            UiKit.Place(_knob, _width * _value - 10, 12, 20, 20);
        }

        void Set(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var lp)) return;
            SetValue((lp.x + _width * 0.5f) / _width, true);
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnPointerDown(PointerEventData e) { _pressTarget = 1f; Set(e); }
        public void OnDrag(PointerEventData e) => Set(e);
        public void OnPointerUp(PointerEventData e) { _pressTarget = 0f; UiSounds.Play(UiSounds.Slider, 0.7f); }

        void OnDisable() { _press = _pressTarget = 0f; if (_knob) _knob.localScale = Vector3.one; }

        void Update()
        {
            if (Mathf.Approximately(_press, _pressTarget)) return;
            _press = Mathf.MoveTowards(_press, _pressTarget, Time.unscaledDeltaTime * 10f);
            _knob.localScale = Vector3.one * (1f + 0.18f * UiKit.EaseInOut(_press));
        }
    }

    /// <summary>An on/off switch. On is amber and glows (it means something is sounding or lit); off is glass.</summary>
    public sealed class UiSwitch : MonoBehaviour, IPointerClickHandler
    {
        public Action<bool> onChanged;
        bool _on;
        float _t;
        UiShape _track, _knob;
        ShapeStyle _trackOff, _trackOn, _knobOff, _knobOn;

        public bool On => _on;

        public static UiSwitch Create(string name, Transform parent, float x, float y)
        {
            var rt = UiKit.Node(name, parent, x, y, 52f, 44f);
            rt.gameObject.AddComponent<UiHit>();
            var s = rt.gameObject.AddComponent<UiSwitch>();
            var th = UiTheme.Current;
            s._trackOff = ShapeStyle.Fill(UiTheme.Rgba(255, 255, 255, 0.16f), 15).Ring(0.10f);
            s._trackOn = ShapeStyle.Gradient(new Color32(0xFF, 0xD5, 0x8A, 255), th.amberDeep, 15).Glow(UiTheme.Rgba(255, 180, 90, 0.45f), 16);
            s._knobOff = ShapeStyle.Fill(new Color32(0xDC, 0xD8, 0xFA, 255), 11).Glow(UiTheme.Rgba(6, 8, 40, 0.40f), 6, 2);
            s._knobOn = ShapeStyle.Fill(new Color32(0xFF, 0xF6, 0xE0, 255), 11).Glow(UiTheme.Rgba(90, 40, 0, 0.35f), 6, 2);
            s._track = UiKit.Shape("Track", rt, 0, 7, 52, 30, s._trackOff);
            s._knob = UiKit.Shape("Knob", rt, 4, 11, 22, 22, s._knobOff);
            return s;
        }

        public void Set(bool on, bool animate = true)
        {
            _on = on;
            if (!animate) { _t = on ? 1f : 0f; Paint(); }
        }

        public void OnPointerClick(PointerEventData e)
        {
            _on = !_on;
            UiSounds.Play(UiSounds.Toggle);
            onChanged?.Invoke(_on);
        }

        void Paint()
        {
            float k = UiKit.EaseInOut(_t);
            _track.Style = ShapeStyle.Lerp(_trackOff, _trackOn, k);
            _knob.Style = ShapeStyle.Lerp(_knobOff, _knobOn, k);
            UiKit.Place(_knob.rectTransform, Mathf.Lerp(4f, 26f, k), 11, 22, 22);
        }

        void Update()
        {
            float target = _on ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / 0.18f);
            Paint();
        }
    }
}
