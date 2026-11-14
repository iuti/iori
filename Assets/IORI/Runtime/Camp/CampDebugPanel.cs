using System;
using System.Collections.Generic;
using Iori.Audio;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// The one on-screen panel for checking things while playing.
    /// A small bar at the top always says what is showing now (time, weather, season, and whether
    /// it follows the real clock). Tapping it opens a sheet at the bottom with three tabs:
    /// the look (time of day, weather, season, lights), the camera and quality, and the sound
    /// (the radio's controls, which used to be a second panel lying over the picture).
    /// Every button is a plain word, the current choice is highlighted, and "自動に戻す" puts
    /// everything back to following the clock.
    /// </summary>
    public sealed class CampDebugPanel : MonoBehaviour
    {
        public enum Tab { Look, Camera, Sound }

        public bool open;
        public Tab tab;
        [Tooltip("上の一行(いまの景色)も隠す")] public bool hidden;

        Vector2 _scroll;
        Rect _barRect, _sheetRect;
        readonly List<Rect> _sliders = new List<Rect>();
        readonly float[] _contentHeight = { 460f, 380f, 440f };
        bool _pressed, _scrolling;
        float _pressY;
        float _scale = 1f;
        float _fps;
        CampCameraRig _rig;
        CampProp[] _props;
        Styles _s;

        static readonly (string label, float hour)[] Times =
        {
            ("夜明け前", 4.9f), ("朝焼け", 6.4f), ("昼", 12f), ("夕方", 17.2f), ("日没後", 18.6f), ("夜", 22f),
        };
        static readonly (string label, CampWeather w)[] Weathers = { ("晴れ", CampWeather.Clear), ("雨", CampWeather.Rain), ("雨上がり", CampWeather.AfterRain) };
        static readonly (string label, CampSeason s)[] Seasons = { ("暦どおり", CampSeason.Auto), ("春", CampSeason.Spring), ("夏", CampSeason.Summer), ("秋", CampSeason.Autumn), ("冬", CampSeason.Winter) };
        static readonly (string label, CampPropKind kind)[] Focuses = { ("焚き火", CampPropKind.Fire), ("イオリ", CampPropKind.Iori), ("ラジオ", CampPropKind.Radio), ("音楽プレーヤー", CampPropKind.Player) };
        static readonly (string label, CampQualityLevel q)[] Qualities = { ("端末に合わせる", CampQualityLevel.Auto), ("スマホ相当", CampQualityLevel.Phone), ("フル", CampQualityLevel.Full) };
        static readonly (string label, string cue)[] VoiceTests =
        {
            ("あいさつ", VoiceCues.Greet), ("タップ", VoiceCues.Tap), ("プレーヤー", VoiceCues.PlayerOpen),
            ("曲が止まった", VoiceCues.MusicStopped), ("雨が降った", VoiceCues.RainStart), ("雨が止んだ", VoiceCues.RainStop), ("火をつけた", VoiceCues.Firewood),
        };
        static readonly string[] TalkActions = { "LLM のキーを貼る", "声のキーを貼る", "返事の声", "キーを消す", "記憶を消す", "話しかけてみる", "声の ID を貼る", "声の ID を消す" };
        string _talkNote = "";

        void TalkAction(TalkDirector talk, int action)
        {
            var pasted = (GUIUtility.systemCopyBuffer ?? "").Trim();
            switch (action)
            {
                case 0:
                    if (pasted.StartsWith("sk-ant-")) { talk.SetLlmKey(pasted); _talkNote = "LLM のキーをしまったよ。"; }
                    else _talkNote = "コピーしてあるものが Claude の API キー(sk-ant- で始まる)じゃないみたい。";
                    break;
                case 1:
                    if (TalkApi.LooksLikeVoiceId(pasted)) _talkNote = "コピーしてあるのは声の ID みたい。「声の ID を貼る」のほうを押してね。";
                    else if (pasted.Length >= 20 && pasted.IndexOf(' ') < 0 && !pasted.StartsWith("sk-ant-")) { talk.SetVoiceKey(pasted); _talkNote = "声のキーをしまったよ。"; }
                    else _talkNote = "コピーしてあるものが ElevenLabs の API キーじゃないみたい。";
                    break;
                case 2: talk.VoiceOn = !talk.VoiceOn; break;
                case 3: talk.SetLlmKey(""); talk.SetVoiceKey(""); _talkNote = "キーを消したよ。"; break;
                case 4: talk.ForgetAll(); _talkNote = "記憶を消したよ。"; break;
                case 6:
                    if (TalkApi.LooksLikeVoiceId(pasted)) { talk.SetVoiceId(pasted); _talkNote = "声の ID を覚えたよ(" + pasted + ")。"; }
                    else _talkNote = "コピーしてあるものが声の ID(英数字 20 文字)じゃないみたい。";
                    break;
                case 7: talk.SetVoiceId(""); _talkNote = "声の ID を消したよ(名前で探す)。"; break;
                case 5: _talkNote = talk.Ask("こんばんは") ? "「こんばんは」と話しかけたよ。" : "いまは話せない(キーが無いか、返事待ち)。"; break;
            }
        }

        static readonly string[] Tabs = { "景色", "カメラ・画質", "音" };
        static readonly (string id, string label)[] PhaseNames = { ("early_morning", "早朝"), ("morning", "朝"), ("day", "昼"), ("evening", "夕方"), ("night", "夜"), ("late_night", "深夜") };
        static readonly (string id, string label)[] LayerNames = { ("fire", "焚き火"), ("wind", "風"), ("insects", "虫"), ("rain", "雨") };

        sealed class Styles
        {
            public Font font;
            public GUIStyle bar, barText, barHint, sheet, header, note, chip, chipOn, chipSmall, chipSmallOn, tab, tabOn, sliderTrack, sliderThumb, small, rowLabel, rowValue;
            readonly List<Texture2D> _textures = new List<Texture2D>();

            public Styles()
            {
                // the UI's own Japanese font, shipped in Resources (OS font names differ by phone maker: on an OPPO the
                // OS lookup gave a font without Japanese and the panel showed only boxes). OS fonts are the fallback.
                font = Resources.Load<Font>("IoriUI/Fonts/ZenKakuGothicNew-Regular");
                if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Noto Sans CJK JP", "Noto Sans JP", "Hiragino Sans", "Roboto" }, 16);
                var text = new Color(0.90f, 0.92f, 1f);
                var dim = new Color(0.63f, 0.68f, 0.90f);
                var panel = Round(new Color(0.07f, 0.08f, 0.19f, 0.90f), 14);
                var chipTex = Round(new Color(1f, 1f, 1f, 0.11f), 10);
                var chipOnTex = Round(new Color(1f, 0.72f, 0.38f, 1f), 10);
                var track = Round(new Color(1f, 1f, 1f, 0.16f), 4);
                var thumb = Round(new Color(1f, 0.72f, 0.38f, 1f), 10);

                bar = new GUIStyle { border = new RectOffset(14, 14, 14, 14), padding = new RectOffset(14, 14, 8, 8) };
                bar.normal.background = panel;
                barText = Text(15, text, FontStyle.Bold);
                barHint = Text(12, dim, FontStyle.Normal);
                barHint.alignment = TextAnchor.MiddleRight;
                sheet = new GUIStyle { border = new RectOffset(14, 14, 14, 14), padding = new RectOffset(16, 16, 14, 14) };
                sheet.normal.background = panel;
                header = Text(12, dim, FontStyle.Bold);
                header.margin = new RectOffset(0, 0, 10, 4);
                rowLabel = Text(13, text, FontStyle.Normal);
                rowValue = Text(12, dim, FontStyle.Normal);
                rowValue.alignment = TextAnchor.MiddleRight;
                note = Text(12, dim, FontStyle.Normal);
                note.wordWrap = true;
                small = Text(11, dim, FontStyle.Normal);
                small.alignment = TextAnchor.UpperCenter;
                chip = Text(14, text, FontStyle.Normal);
                chip.alignment = TextAnchor.MiddleCenter;
                chip.border = new RectOffset(10, 10, 10, 10);
                chip.padding = new RectOffset(10, 10, 0, 0);
                chip.margin = new RectOffset(0, 6, 0, 6);
                chip.fixedHeight = 36;
                chip.normal.background = chipTex;
                chip.hover.background = chipTex;
                chip.active.background = chipOnTex;
                chip.hover.textColor = text;
                chip.active.textColor = new Color(0.16f, 0.10f, 0.03f);
                chipOn = new GUIStyle(chip) { fontStyle = FontStyle.Bold };
                chipOn.normal.background = chipOnTex;
                chipOn.hover.background = chipOnTex;
                chipOn.normal.textColor = new Color(0.16f, 0.10f, 0.03f);
                chipOn.hover.textColor = new Color(0.16f, 0.10f, 0.03f);
                chipSmall = new GUIStyle(chip) { fontSize = 12, fixedHeight = 26, margin = new RectOffset(0, 0, 6, 2) };
                chipSmallOn = new GUIStyle(chipOn) { fontSize = 12, fixedHeight = 26, margin = new RectOffset(0, 0, 6, 2) };
                // the tabs: the chosen one is light text on a lighter plate, with no orange (orange means "this look is on")
                var tabOnTex = Round(new Color(1f, 1f, 1f, 0.26f), 10);
                tab = new GUIStyle(chip) { fontSize = 13 };
                tab.normal.textColor = dim;
                tab.normal.background = null; tab.hover.background = null;
                tab.active.background = tabOnTex; tab.active.textColor = text;
                tabOn = new GUIStyle(tab) { fontStyle = FontStyle.Bold };
                tabOn.normal.background = tabOnTex; tabOn.hover.background = tabOnTex;
                tabOn.normal.textColor = text; tabOn.hover.textColor = text;
                sliderTrack = new GUIStyle { border = new RectOffset(4, 4, 4, 4), fixedHeight = 8, margin = new RectOffset(10, 10, 15, 13) };
                sliderTrack.normal.background = track;
                // a round knob centered on the track: as tall as the track, drawn 7 more above and below
                sliderThumb = new GUIStyle { fixedWidth = 22, fixedHeight = 8, border = new RectOffset(10, 10, 10, 10), overflow = new RectOffset(0, 0, 7, 7) };
                sliderThumb.normal.background = thumb;
                sliderThumb.hover.background = thumb;
                sliderThumb.active.background = thumb;
            }

            GUIStyle Text(int size, Color color, FontStyle style)
            {
                var s = new GUIStyle { fontSize = size, fontStyle = style, alignment = TextAnchor.MiddleLeft, richText = true };
                if (font) s.font = font;
                s.normal.textColor = color;
                return s;
            }

            /// <summary>A rounded rectangle to stretch as a background (9-slice).</summary>
            Texture2D Round(Color color, int radius)
            {
                int n = radius * 2 + 4;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                        var c = color;
                        c.a *= Mathf.Clamp01(radius - d + 0.5f);
                        px[y * n + x] = c;
                    }
                tex.SetPixels(px);
                tex.Apply();
                _textures.Add(tex);
                return tex;
            }

            public void Dispose()
            {
                foreach (var t in _textures) if (t) Destroy(t);
                _textures.Clear();
            }
        }

        void OnEnable()
        {
            // the radio's own panel would lie on top of this one: its controls are the "音" tab here
            RadioDebugPanel.HostedElsewhere = true;
        }

        void OnDisable()
        {
            _s?.Dispose();
            _s = null;
            CampCameraRig.InputBlocked = false;
            RadioDebugPanel.HostedElsewhere = false;
        }

        void Update()
        {
            // frames per second right now (smoothed over about half a second), not an average since the start
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = _fps <= 0f ? 1f / dt : Mathf.Lerp(_fps, 1f / dt, 1f - Mathf.Exp(-dt * 2f));
            // keep taps on the panel from also turning the camera or poking the props
            if (hidden) { CampCameraRig.InputBlocked = false; return; }
            Vector2 m = Input.mousePosition;
            var p = new Vector2(m.x / _scale, (Screen.height - m.y) / _scale);
            CampCameraRig.InputBlocked = _barRect.Contains(p) || (open && _sheetRect.Contains(p));
        }

        void OnGUI()
        {
            var atm = CampAtmosphere.Instance;
            if (!atm || hidden) return;
            _s ??= new Styles();
            if (!_rig) _rig = FindFirstObjectByType<CampCameraRig>();
            if (_props == null || _props.Length == 0) _props = FindObjectsByType<CampProp>(FindObjectsSortMode.None);

            // sized for a phone held upright: 400 units across the short side
            _scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 400f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1));
            float w = Screen.width / _scale, h = Screen.height / _scale;
            bool tall = h >= w;

            if (Event.current.type == EventType.Repaint) _sliders.Clear();

            // ---- the bar: what is showing now
            float barW = Mathf.Min(w - 16, 384);
            _barRect = new Rect(w - barW - 8, 8, barW, 40);
            GUILayout.BeginArea(_barRect, _s.bar);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Summary(atm), _s.barText, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            GUILayout.Label(open ? "閉じる ▲" : "変える ▼", _s.barHint, GUILayout.Height(24));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            // (a press changes what is drawn only after this pass is over: the layout was made for what is showing now)
            bool toggle = GUI.Button(_barRect, GUIContent.none, GUIStyle.none);
            int pickedTab = -1;

            if (open)
            {
                // Upright: a sheet at the bottom, only as tall as what the tab holds, so the picture stays in view
                // above it. Sideways: a column on the right.
                float want = _contentHeight[(int)tab] + 30f;
                float sheetH = tall ? Mathf.Min(want, h * 0.62f) : Mathf.Min(want, h - 64f);
                _sheetRect = tall ? new Rect(8, h - 8 - sheetH, w - 16, sheetH) : new Rect(w - 392, 56, 384, sheetH);
                GUILayout.BeginArea(_sheetRect, _s.sheet);
                _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);
                float inner = _sheetRect.width - 32;
                DragToScroll();
                GUILayout.BeginVertical();
                Row(inner, Tabs.Length, i => Tabs[i], i => (int)tab == i, i => pickedTab = i, 3, _s.tab, _s.tabOn);
                switch (tab)
                {
                    case Tab.Look: DrawLook(atm, inner); break;
                    case Tab.Camera: DrawCamera(inner); break;
                    default: DrawSound(inner); break;
                }
                GUILayout.EndVertical();
                if (Event.current.type == EventType.Repaint) _contentHeight[(int)tab] = GUILayoutUtility.GetLastRect().height;
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            GUI.matrix = old;
            if (pickedTab >= 0 && pickedTab != (int)tab) { tab = (Tab)pickedTab; _scroll = Vector2.zero; }
            if (toggle) open = !open;
        }

        /// <summary>Scrolls the open sheet: 0 is the top, 1 the bottom.</summary>
        public void ScrollTo(float t) => _scroll.y = Mathf.Clamp01(t) * 4000f;

        /// <summary>
        /// A finger has no wheel: dragging up or down anywhere on the sheet scrolls it. Called inside the
        /// scroll view before its buttons, so a drag that has turned into a scroll does not press one.
        /// The sliders keep their own (sideways) drag.
        /// </summary>
        void DragToScroll()
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    _pressed = true;
                    foreach (var r in _sliders) if (r.Contains(e.mousePosition)) _pressed = false;
                    _scrolling = false;
                    _pressY = e.mousePosition.y + _scroll.y;
                    break;
                case EventType.MouseDrag:
                    if (!_pressed) break;
                    float moved = e.mousePosition.y + _scroll.y - _pressY;
                    if (!_scrolling && Mathf.Abs(moved) > 12f) { _scrolling = true; GUIUtility.hotControl = 0; }
                    if (_scrolling) { _scroll.y = Mathf.Max(0f, _scroll.y - e.delta.y); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (_scrolling) e.Use();
                    _pressed = false; _scrolling = false;
                    break;
            }
        }

        static string Clock(float hour)
        {
            int m = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f);
            return $"{m / 60:00}:{m % 60:00}";
        }

        static string WeatherName(CampAtmosphere atm)
        {
            switch (atm.weather)
            {
                case CampWeather.Rain: return "雨";
                case CampWeather.AfterRain: return "雨上がり";
                default: return "晴れ";
            }
        }

        static string SeasonName(CampSeason s)
        {
            switch (s)
            {
                case CampSeason.Spring: return "春";
                case CampSeason.Summer: return "夏";
                case CampSeason.Autumn: return "秋";
                case CampSeason.Winter: return "冬";
                default: return "暦どおり";
            }
        }

        static string Summary(CampAtmosphere atm)
        {
            string s = $"{Clock(atm.CurrentHour)} {atm.Look.LabelAt(atm.CurrentHour)}・{WeatherName(atm)}・{SeasonName(atm.CurrentSeason)}";
            if (atm.sleep) s += "・おやすみ";
            if (atm.FollowingEverything) s += " <size=11><color=#a0addf>自動</color></size>";
            return s;
        }

        /// <summary>Remembers where the slider just drawn is, so that dragging it does not scroll the sheet.</summary>
        void MarkSlider()
        {
            if (Event.current.type != EventType.Repaint) return;
            var r = GUILayoutUtility.GetLastRect();
            r.yMin -= 14; r.yMax += 14;
            _sliders.Add(r);
        }

        // ---------------------------------------------------------------- tab: the look
        void DrawLook(CampAtmosphere atm, float width)
        {
            // is this the real thing or a look chosen by hand
            bool auto = atm.FollowingEverything;
            GUILayout.BeginHorizontal();
            GUILayout.Label(auto ? "端末の時計、ラジオの天気、暦に合わせて動いているよ。" : "手で選んだ景色を表示中。", _s.note, GUILayout.Width(width - 122), GUILayout.Height(36));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(auto ? "自動で動作中" : "自動に戻す", auto ? _s.chipOn : _s.chip, GUILayout.Width(116)) && !auto) atm.FollowEverything();
            GUILayout.EndHorizontal();

            // ---- time of day
            bool running = !atm.followClock && atm.hoursPerSecond != 0f;
            GUILayout.BeginHorizontal();
            GUILayout.Label("時刻", _s.header, GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(running ? "■ 早送りを止める" : "▶ 一日を早送り(約 50 秒)", running ? _s.chipSmallOn : _s.chipSmall, GUILayout.Width(184)))
            {
                if (running) atm.hoursPerSecond = 0f;
                else { atm.previewHour = atm.CurrentHour; atm.followClock = false; atm.hoursPerSecond = 0.5f; }
            }
            GUILayout.EndHorizontal();
            Row(width, Times.Length, i => Times[i].label, i => !atm.followClock && atm.hoursPerSecond == 0f && Mathf.Abs(atm.previewHour - Times[i].hour) < 0.05f, i =>
            {
                atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = Times[i].hour;
            }, 3);
            float hour = atm.CurrentHour;
            float picked = GUILayout.HorizontalSlider(hour, 0f, 24f, _s.sliderTrack, _s.sliderThumb);
            MarkSlider();
            if (Mathf.Abs(picked - hour) > 0.001f) { atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = Mathf.Clamp(picked, 0f, 23.99f); }
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            for (int i = 0; i <= 4; i++)
            {
                GUILayout.Label($"{i * 6}時", _s.small, GUILayout.Width(30));
                if (i < 4) GUILayout.FlexibleSpace();
            }
            GUILayout.Space(4);
            GUILayout.EndHorizontal();

            // ---- weather (the radio's rain sound goes with it)
            GUILayout.Label("天気", _s.header);
            Row(width, Weathers.Length, i => Weathers[i].label, i => atm.weather == Weathers[i].w, i => atm.SetWeatherByHand(Weathers[i].w), 3);
            if (atm.weather == CampWeather.AfterRain)
                GUILayout.Label($"水たまりが残っていて、あと約 {Mathf.CeilToInt(atm.MinutesUntilDry)} 分で乾くよ。", _s.note);
            else if (atm.weather == CampWeather.Rain)
                GUILayout.Label("雨の間は水たまりができて、やむと「雨上がり」になるよ。雨音も一緒に鳴る。", _s.note);

            // ---- season
            GUILayout.Label("季節", _s.header);
            Row(width, Seasons.Length, i => Seasons[i].label, i => atm.season == Seasons[i].s, i => atm.season = Seasons[i].s, 5);

            // ---- lights
            GUILayout.Label("灯り", _s.header);
            if (GUILayout.Button(atm.sleep ? "おやすみモード中(ランタンを消している)" : "おやすみモードにする(ランタンを消す)", atm.sleep ? _s.chipOn : _s.chip))
                atm.SetSleep(!atm.sleep);
        }

        // ---------------------------------------------------------------- tab: camera and quality
        void DrawCamera(float width)
        {
            if (_rig)
            {
                GUILayout.Label("カメラが寄る先", _s.header);
                Row(width, Focuses.Length + 1, i => i == 0 ? "全体" : Focuses[i - 1].label, i => i == 0 ? _rig.focus == null : (_rig.focus != null && _rig.focus.kind == Focuses[i - 1].kind), i =>
                {
                    if (i == 0) { _rig.focus = null; return; }
                    foreach (var p in _props) if (p && p.kind == Focuses[i - 1].kind) _rig.focus = p;
                }, 3);
                if (GUILayout.Button(_rig.autoSway ? "ゆっくり揺れる: オン" : "ゆっくり揺れる: オフ", _rig.autoSway ? _s.chipOn : _s.chip)) _rig.autoSway = !_rig.autoSway;
                GUILayout.Label("画面をなぞると回り込み、つまむ(ホイール)と寄れるよ。小物を押すとそこへ寄る。", _s.note);
            }

            var q = CampQuality.Instance;
            if (q)
            {
                GUILayout.Label("画質", _s.header);
                Row(width, Qualities.Length, i => Qualities[i].label, i => q.level == Qualities[i].q, i => q.level = Qualities[i].q, 3);
                string now = q.Applied == CampQualityLevel.Full ? "フル" : "スマホ相当";
                GUILayout.Label($"いまは「{now}」で動いていて、毎秒 {(_fps > 0f ? _fps.ToString("0") : "…")} コマ。スマホ相当は、ぼかし、焚き火の影、草の一部、水たまり専用の鏡像を省くよ。", _s.note);
            }
            GUILayout.Space(6);
            GUILayout.Label("色や光そのものの調整は、Unity の CampLook アセットでできるよ。", _s.note);
        }

        // ---------------------------------------------------------------- tab: the sound (the radio)
        static string NameOf((string id, string label)[] names, string id)
        {
            foreach (var n in names) if (n.id == id) return n.label;
            return string.IsNullOrEmpty(id) ? "-" : id;
        }

        float VolumeRow(string label, float value, float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _s.rowLabel, GUILayout.Width(112), GUILayout.Height(36));
            float v = GUILayout.HorizontalSlider(value, 0f, 1f, _s.sliderTrack, _s.sliderThumb, GUILayout.Width(width - 112 - 34 - 24));
            MarkSlider();
            GUILayout.Label(Mathf.RoundToInt(v * 100f).ToString(), _s.rowValue, GUILayout.Width(34), GUILayout.Height(36));
            GUILayout.EndHorizontal();
            return v;
        }

        void DrawSound(float width)
        {
            var radio = RadioDirector.Instance;
            if (radio == null) { GUILayout.Label("ラジオが動いていないよ。", _s.note); return; }
            if (radio.LoadError != null) { GUILayout.Label("ラジオの読み込みエラー: " + radio.LoadError, _s.note); return; }
            if (radio.Plan == null) { GUILayout.Label("ラジオを読み込み中…", _s.note); return; }

            // ---- what is on the air now
            var now = radio.VirtualNow;
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
            var span = radio.Plan.SpanAt(radio.Plan.ToPlanTime(now.ToUnixTimeMilliseconds()));
            bool quietNow = span != null && span.Mode == RadioMode.Quiet;
            string shifted = radio.ClockOffsetMs != 0 ? $"(時計を {radio.ClockOffsetMs / 60000.0:+0;-0} 分ずらし中)" : "";
            GUILayout.Label($"いまのラジオ: {local:HH:mm:ss}{shifted}、時間帯「{NameOf(PhaseNames, span?.Phase)}」、{(quietNow ? "静けさ(曲なし)" : "テーマ曲")}、{(radio.Weather == WeatherIds.Rain ? "雨" : "晴れ")}の音。天気は「景色」のタブで変えられるよ。", _s.note, GUILayout.Width(width));

            // ---- how it plays
            GUILayout.Label("流れ方", _s.header);
            if (GUILayout.Button(radio.Quiet ? "静けさの時間: オン" : "静けさの時間にする", radio.Quiet ? _s.chipOn : _s.chip))
                radio.SetQuiet(!radio.Quiet);
            GUILayout.Label("オンの間は曲を流さず、環境音とたまに鳴る短いフレーズだけになるよ。", _s.note, GUILayout.Width(width));

            // ---- hearing a change of the hour without waiting for it
            GUILayout.Label("時間帯の切り替わりを聞く", _s.header);
            GUILayout.BeginHorizontal();
            float bw = (width - 12f) / 3f;
            if (GUILayout.Button("次の時間帯の直前へ", _s.chipSmall, GUILayout.Width(bw + 14f))) radio.JumpBeforeNextPhase(15);
            GUILayout.Space(6);
            if (GUILayout.Button("次の正時の直前へ", _s.chipSmall, GUILayout.Width(bw + 4f))) radio.JumpBeforeNextHour(5);
            GUILayout.Space(6);
            if (GUILayout.Button("時計を戻す", radio.ClockOffsetMs != 0 ? _s.chipSmallOn : _s.chipSmall, GUILayout.Width(bw - 18f))) radio.SetClockOffset(0);
            GUILayout.EndHorizontal();
            GUILayout.Label("切り替わりの直前まで時計を進めるよ。自動のときは景色の時刻も一緒に進む。", _s.note, GUILayout.Width(width));

            // ---- Iori's voice
            var voice = VoiceDirector.Instance;
            if (voice)
            {
                GUILayout.Label("イオリの声を聞く", _s.header);
                Row(width, VoiceTests.Length, i => VoiceTests[i].label, i => false, i => voice.Say(VoiceTests[i].cue, true), 4, _s.chipSmall, _s.chipSmallOn);
                GUILayout.Label("押すと、いまの時間帯と時刻に合うセリフを言うよ(本番は間隔と確率の決まりで黙ることも多い)。時報は「次の正時の直前へ」で聞ける。\n" + voice.Status, _s.note, GUILayout.Width(width));
            }

            var talk = TalkDirector.Instance;
            if (talk)
            {
                GUILayout.Label("イオリと話す(LLM)", _s.header);
                Row(width, TalkActions.Length, i => i == 2 ? (talk.VoiceOn ? "返事の声 オン" : "返事の声 オフ") : TalkActions[i], i => i == 2 && talk.VoiceOn, i => TalkAction(talk, i), 3, _s.chipSmall, _s.chipSmallOn);
                GUILayout.Label("キーをコピーしてから「貼る」を押すと、この端末の中に暗号化してしまうよ(設定画面ができるまでの入口)。キーがあるとき、イオリを長押しすると話せる。\n" + talk.Status + (_talkNote.Length > 0 ? "\n" + _talkNote : ""), _s.note, GUILayout.Width(width));
            }

            // ---- volumes
            GUILayout.Label("音量", _s.header);
            float music = VolumeRow("曲", radio.MusicVolume, width);
            if (Mathf.Abs(music - radio.MusicVolume) > 0.001f) radio.SetMusicVolume(music);
            foreach (var layer in radio.Mixer.Layers)
            {
                float v = radio.Mixer.GetUserVolume(layer.Id);
                string label = NameOf(LayerNames, layer.Id) + (layer.IsActiveIn(radio.Weather) ? "" : " <size=10><color=#a0addf>いまは鳴らない</color></size>");
                float nv = VolumeRow(label, v, width);
                if (Mathf.Abs(nv - v) > 0.001f) radio.SetAmbientVolume(layer.Id, nv);
            }
            GUILayout.Space(4);
            GUILayout.Label("再生係: " + radio.Backend.Name + "\n" + radio.Backend.Status, _s.note, GUILayout.Width(width));
        }

        /// <summary>A row of choices as equal buttons, wrapped to perRow per line; the current one is highlighted.</summary>
        void Row(float width, int count, Func<int, string> label, Func<int, bool> on, Action<int> pick, int perRow, GUIStyle off = null, GUIStyle lit = null)
        {
            off ??= _s.chip; lit ??= _s.chipOn;
            float bw = (width - 6f * (perRow - 1)) / perRow;
            for (int start = 0; start < count; start += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < Mathf.Min(count, start + perRow); i++)
                    if (GUILayout.Button(label(i), on(i) ? lit : off, GUILayout.Width(bw))) pick(i);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
    }
}
