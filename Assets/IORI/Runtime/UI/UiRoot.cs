using Iori.Audio;
using Iori.Camp;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Iori.Ui
{
    /// <summary>
    /// The production UI of the camp. It makes itself when the camp scene starts: a canvas over the
    /// scene, the sheets (built the first time each opens), and the link to the props: tapping the
    /// radio or the fire moves the camera in (the camera rig does that) and raises that prop's sheet
    /// from the bottom edge, with the prop kept in view above it. A press outside the sheet, or the
    /// back key, closes it. The layout is for a phone held upright only.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        /// <summary>The width the look proposal is drawn at; a phone held upright is this many units across.</summary>
        public const float DesignWidth = 390f;
        const float MinHeight = 700f;

        public static UiRoot Instance { get; private set; }

        Canvas _canvas;
        RectTransform _canvasRect;
        UiBackdrop _backdrop;
        CampCameraRig _rig;
        CampDebugPanel _debug;
        bool _debugWasHidden;
        GameObject _scrim;
        RadioPanel _radio;
        AmbientPanel _ambient;
        UiPanel _open;
        int _screenW, _screenH;
        Rect _safe;

        public UiPanel OpenPanel => _open;
        public UiBackdrop Backdrop => _backdrop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var rig = FindFirstObjectByType<CampCameraRig>();
            if (!rig) return; // not the camp scene
            var go = new GameObject("IORI UI", typeof(RectTransform)) { layer = UiKit.UiLayer };
            go.AddComponent<UiRoot>()._rig = rig;
        }

        void Awake()
        {
            Instance = this;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            // the shape shader reads its numbers from these extra vertex channels
            _canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            _canvasRect = (RectTransform)transform;

            if (!FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            gameObject.AddComponent<UiSounds>();
            _backdrop = gameObject.AddComponent<UiBackdrop>();
            UiFonts.Ensure();

            // the area outside the open panel: a press there closes it
            var scrim = UiKit.Node("Outside", transform, 0, 0, 0, 0);
            UiKit.Stretch(scrim);
            scrim.gameObject.AddComponent<UiHit>();
            var b = scrim.gameObject.AddComponent<UiButton>();
            b.sound = UiSounds.Close;
            b.onClick = Close;
            _scrim = scrim.gameObject;
            _scrim.SetActive(false);

            _radio = MakePanel<RadioPanel>("Radio sheet");
            _ambient = MakePanel<AmbientPanel>("Ambient sheet");
        }

        T MakePanel<T>(string name) where T : UiPanel
        {
            var go = new GameObject(name, typeof(RectTransform));
            var p = go.AddComponent<T>();
            p.Setup(transform);
            return p;
        }

        void Start()
        {
            if (!_rig) _rig = FindFirstObjectByType<CampCameraRig>();
            _backdrop.sceneCamera = _rig ? _rig.GetComponent<Camera>() : Camera.main;
            _debug = FindFirstObjectByType<CampDebugPanel>();
            Fit();
        }

        void OnEnable() => CampProp.Tapped += OnPropTapped;

        void OnDisable()
        {
            CampProp.Tapped -= OnPropTapped;
            CampCameraRig.UiBlocked = false;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnPropTapped(CampProp prop)
        {
            if (_open) return;
            Open(prop.kind, prop);
        }

        UiPanel PanelFor(CampPropKind kind)
        {
            switch (kind)
            {
                case CampPropKind.Radio: return _radio;
                case CampPropKind.Fire: return _ambient;
                default: return null; // the music player, the lantern and Iori have no panel yet
            }
        }

        /// <summary>Opens the panel of a prop (also used by the screenshot runner). Returns false if that prop has none.</summary>
        public bool Open(CampPropKind kind, CampProp prop = null)
        {
            var panel = PanelFor(kind);
            if (!panel) return false;
            var radio = RadioDirector.Instance;
            if (!radio || radio.Program == null) return false; // the programme is still being read
            if (_open && _open != panel) _open.Close();

            if (!prop)
                foreach (var p in FindObjectsByType<CampProp>(FindObjectsSortMode.None))
                    if (p.kind == kind) prop = p;
            if (_rig && prop) _rig.focus = prop;

            if (!_open && _debug)
            {
                _debugWasHidden = _debug.hidden;
                _debug.hidden = true;
            }
            _open = panel;
            Fit(true);
            panel.Open();
            _scrim.SetActive(true);
            CampCameraRig.UiBlocked = true;
            _backdrop.wanted = true;
            UiSounds.Play(UiSounds.Open);
            return true;
        }

        public void Close()
        {
            if (!_open) return;
            _open.Close();
            _open = null;
            _scrim.SetActive(false);
            if (_rig)
            {
                _rig.focus = null;
                _rig.panelCover = 0f;
            }
            if (_debug) _debug.hidden = _debugWasHidden;
        }

        /// <summary>Sizes the UI for the screen: 390 units across a phone held upright, never less than 700 units tall.</summary>
        void Fit(bool force = false)
        {
            if (!force && Screen.width == _screenW && Screen.height == _screenH && Screen.safeArea == _safe) return;
            _screenW = Screen.width;
            _screenH = Screen.height;
            _safe = Screen.safeArea;
            float scale = Mathf.Max(0.1f, Mathf.Min(_screenW / DesignWidth, _screenH / MinHeight));
            _canvas.scaleFactor = scale;
            float inset = Mathf.Max(0f, _safe.yMin) / scale;
            _radio.SetBottomInset(inset);
            _ambient.SetBottomInset(inset);
        }

        void Update()
        {
            Fit();
            bool showing = _radio.IsShowing || _ambient.IsShowing;
            // the camera keeps its hands off until the panel has gone and the finger is up
            if (!_open && !showing && CampCameraRig.UiBlocked && !Input.GetMouseButton(0) && Input.touchCount == 0)
                CampCameraRig.UiBlocked = false;
            if (!showing) _backdrop.wanted = false;

            if (_open && _rig)
            {
                // keep the prop in the part of the screen the panel leaves free
                float canvasHeight = _screenH / _canvas.scaleFactor;
                _rig.panelCover = Mathf.Clamp01(_open.Top / Mathf.Max(1f, canvasHeight));
            }

            if (_open && Input.GetKeyDown(KeyCode.Escape)) Close();
        }
    }
}
