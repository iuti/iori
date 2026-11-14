using System.Collections.Generic;
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
    /// back key, closes it. Tapping the music player raises a sheet over the whole screen: the player
    /// if a song is chosen, the list of songs if not; the one can be opened over the other. While the
    /// music player has the floor, a strip along the bottom of the home screen shows its song.
    /// The layout is for a phone held upright only.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        /// <summary>The width the look proposal is drawn at; a phone held upright is this many units across.</summary>
        public const float DesignWidth = 390f;
        const float MinHeight = 700f;

        public static UiRoot Instance { get; private set; }

        Canvas _canvas;
        UiBackdrop _backdrop;
        CampCameraRig _rig;
        CampDebugPanel _debug;
        bool _debugWasHidden;
        GameObject _scrim;
        RadioPanel _radio;
        AmbientPanel _ambient;
        PlayerPanel _player;
        LibraryPanel _library;
        MiniBar _bar;
        UiPanel[] _panels;
        /// <summary>The open sheets, the one on top last (only the music player's two screens lie over one another).</summary>
        readonly List<UiPanel> _stack = new List<UiPanel>();
        int _screenW, _screenH;
        Rect _safe;

        /// <summary>The open sheet (the one on top), or null.</summary>
        public UiPanel OpenPanel => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public UiBackdrop Backdrop => _backdrop;
        public LibraryPanel Library => _library;
        public MiniBar HomeBar => _bar;

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

            // (under the sheets: it slides away as one of them comes up)
            _bar = MiniBar.Create(transform, true, 16f, () => Open(CampPropKind.Player));

            _radio = MakePanel<RadioPanel>("Radio sheet");
            _ambient = MakePanel<AmbientPanel>("Ambient sheet");
            _player = MakePanel<PlayerPanel>("Player screen");
            _library = MakePanel<LibraryPanel>("Library screen");
            _panels = new UiPanel[] { _radio, _ambient, _player, _library };
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
            if (_stack.Count > 0) return;
            Open(prop.kind, prop);
        }

        UiPanel PanelFor(CampPropKind kind)
        {
            switch (kind)
            {
                case CampPropKind.Radio: return _radio;
                case CampPropKind.Fire: return _ambient;
                case CampPropKind.Player:
                    var music = MusicDirector.Instance;
                    if (!music || music.Backend == null) return null; // this device has no way to play its songs yet
                    return music.Current != null ? (UiPanel)_player : _library;
                default: return null; // the lantern and Iori have no panel yet
            }
        }

        /// <summary>Opens the panel of a prop (also used by the screenshot runner). Returns false if that prop has none.</summary>
        public bool Open(CampPropKind kind, CampProp prop = null)
        {
            var panel = PanelFor(kind);
            if (!panel) return false;
            if (panel == _radio || panel == _ambient)
            {
                var radio = RadioDirector.Instance;
                if (!radio || radio.Program == null) return false; // the programme is still being read
            }
            bool wasOpen = _stack.Count > 0;
            foreach (var p in _stack) if (p != panel) p.Close();
            _stack.Clear();

            if (!prop)
                foreach (var p in FindObjectsByType<CampProp>(FindObjectsSortMode.None))
                    if (p.kind == kind) prop = p;
            if (_rig && prop) _rig.focus = prop;

            if (!wasOpen && _debug)
            {
                _debugWasHidden = _debug.hidden;
                _debug.hidden = true;
            }
            Push(panel);
            _scrim.SetActive(true);
            CampCameraRig.UiBlocked = true;
            _backdrop.wanted = true;
            UiSounds.Play(UiSounds.Open);
            return true;
        }

        void Push(UiPanel panel)
        {
            panel.transform.SetAsLastSibling();
            _stack.Add(panel);
            Fit(true);
            panel.Open();
        }

        /// <summary>Brings one of the music player's two screens to the top: back down to it if it lies under the open one, else up over it.</summary>
        void Show(UiPanel panel)
        {
            if (_stack.Count == 0) return;
            int at = _stack.IndexOf(panel);
            if (at < 0)
            {
                Push(panel);
                return;
            }
            for (int i = _stack.Count - 1; i > at; i--)
            {
                _stack[i].Close();
                _stack.RemoveAt(i);
            }
            panel.Covered = false;
        }

        /// <summary>From the list of songs: the player (if a song is chosen).</summary>
        public void ShowPlayer()
        {
            var music = MusicDirector.Instance;
            if (music && music.Current != null) Show(_player);
        }

        /// <summary>From the player: the list of songs.</summary>
        public void ShowLibrary() => Show(_library);

        /// <summary>Puts the sheet on top away. The one under it shows again; if there is none, the UI closes.</summary>
        public void Pop()
        {
            if (_stack.Count <= 1)
            {
                Close();
                return;
            }
            _stack[_stack.Count - 1].Close();
            _stack.RemoveAt(_stack.Count - 1);
            _stack[_stack.Count - 1].Covered = false;
        }

        /// <summary>The back key: the sheet on top may use it itself (to leave an opened album); otherwise it is put away.</summary>
        public void Back()
        {
            var top = OpenPanel;
            if (!top || top.Back()) return;
            Pop();
        }

        /// <summary>Closes every open sheet.</summary>
        public void Close()
        {
            if (_stack.Count == 0) return;
            foreach (var p in _stack) p.Close();
            _stack.Clear();
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
            float bottom = Mathf.Max(0f, _safe.yMin) / scale;
            float top = Mathf.Max(0f, _screenH - _safe.yMax) / scale;
            foreach (var p in _panels) p.SetScreen(_screenH / scale, bottom, top);
            _bar.SetInset(bottom);
        }

        void Update()
        {
            Fit();
            bool showing = false;
            foreach (var p in _panels) showing |= p.IsShowing;
            var top = OpenPanel;

            // a sheet over the whole screen hides the one under it: that one need not be drawn
            for (int i = 0; i < _stack.Count - 1; i++) _stack[i].Covered = top.FullScreen && top.IsSettled;
            // ... and hides the scene but for what shows through its blurred glass: a phone draws the scene smaller meanwhile
            CampQuality.SceneCovered = top && top.FullScreen && top.IsSettled;

            // the strip of the song that is on, while the music player has the floor and nothing is open
            var music = MusicDirector.Instance;
            _bar.shown = music && music.Active && music.Current != null && !showing;
            // (a press that starts on the strip is the strip's, not the camera's)
            if (_bar.Visible && !showing && Input.GetMouseButtonDown(0) && _bar.Contains(Input.mousePosition))
                CampCameraRig.UiBlocked = true;

            // the camera keeps its hands off until the panel has gone and the finger is up
            if (!top && !showing && CampCameraRig.UiBlocked && !Input.GetMouseButton(0) && Input.touchCount == 0)
                CampCameraRig.UiBlocked = false;
            _backdrop.wanted = showing || _bar.Visible;

            if (top && _rig)
            {
                // keep the prop in the part of the screen the panel leaves free (a sheet over the whole screen leaves none)
                float canvasHeight = _screenH / _canvas.scaleFactor;
                _rig.panelCover = top.FullScreen ? 0f : Mathf.Clamp01(top.Top / Mathf.Max(1f, canvasHeight));
            }

            if (top && Input.GetKeyDown(KeyCode.Escape))
            {
                UiSounds.Play(UiSounds.Close);
                Back();
            }
        }
    }
}
