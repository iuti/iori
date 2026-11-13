using System.Collections.Generic;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// On-screen panel for checking the camp's looks while playing.
    /// A small bar at the top always says what is showing now (time, weather, season, and whether
    /// it follows the real clock). Tapping it opens a sheet with one row per thing you can change:
    /// time of day, weather, season, lights, camera, quality. Every button is a plain word, the
    /// current choice is highlighted, and "自動に戻す" puts everything back to following the clock.
    /// </summary>
    public sealed class CampDebugPanel : MonoBehaviour
    {
        public bool open;
        [Tooltip("上の一行(いまの景色)も隠す")] public bool hidden;

        Vector2 _scroll;
        Rect _barRect, _sheetRect, _sliderRect;
        bool _pressed, _scrolling;
        float _pressY;
        float _scale = 1f;
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

        sealed class Styles
        {
            public Font font;
            public GUIStyle bar, barText, barHint, sheet, header, note, chip, chipOn, title, sliderTrack, sliderThumb, small;
            readonly List<Texture2D> _textures = new List<Texture2D>();

            public Styles()
            {
                // a font that has Japanese on Windows, Android and macOS; the default skin's font is the fallback
                font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Noto Sans CJK JP", "Noto Sans JP", "Hiragino Sans", "Roboto" }, 16);
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
                title = Text(16, text, FontStyle.Bold);
                header = Text(12, dim, FontStyle.Bold);
                header.margin = new RectOffset(0, 0, 10, 4);
                note = Text(12, dim, FontStyle.Normal);
                note.wordWrap = true;
                small = Text(11, dim, FontStyle.Normal);
                small.alignment = TextAnchor.UpperCenter;
                chip = Text(14, text, FontStyle.Normal);
                chip.alignment = TextAnchor.MiddleCenter;
                chip.border = new RectOffset(10, 10, 10, 10);
                chip.padding = new RectOffset(10, 10, 0, 0);
                chip.margin = new RectOffset(0, 6, 0, 6);
                chip.fixedHeight = 38;
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
                sliderTrack = new GUIStyle { border = new RectOffset(4, 4, 4, 4), fixedHeight = 8, margin = new RectOffset(10, 10, 16, 4) };
                sliderTrack.normal.background = track;
                sliderThumb = new GUIStyle { fixedWidth = 26, fixedHeight = 26, border = new RectOffset(10, 10, 10, 10), overflow = new RectOffset(0, 0, 9, 9) };
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

        void OnDisable()
        {
            _s?.Dispose();
            _s = null;
            CampCameraRig.InputBlocked = false;
        }

        void Update()
        {
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
            if (GUI.Button(_barRect, GUIContent.none, GUIStyle.none)) open = !open;

            if (open)
            {
                _sheetRect = tall ? new Rect(8, h * 0.46f, w - 16, h * 0.54f - 8) : new Rect(w - 392, 56, 384, h - 64);
                GUILayout.BeginArea(_sheetRect, _s.sheet);
                _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUIStyle.none);
                float inner = _sheetRect.width - 32;
                DragToScroll();
                DrawSheet(atm, inner);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            GUI.matrix = old;
        }

        /// <summary>Scrolls the open sheet: 0 is the top, 1 the bottom.</summary>
        public void ScrollTo(float t) => _scroll.y = Mathf.Clamp01(t) * 4000f;

        /// <summary>
        /// A finger has no wheel: dragging up or down anywhere on the sheet scrolls it. Called inside the
        /// scroll view before its buttons, so a drag that has turned into a scroll does not press one.
        /// The time slider keeps its own (sideways) drag.
        /// </summary>
        void DragToScroll()
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    _pressed = !_sliderRect.Contains(e.mousePosition);
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
            return s;
        }

        void DrawSheet(CampAtmosphere atm, float width)
        {
            // ---- top: is this the real thing or a preview
            GUILayout.BeginHorizontal();
            GUILayout.Label("景色の確認", _s.title, GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("自動に戻す", atm.FollowingEverything ? _s.chipOn : _s.chip, GUILayout.Width(110))) atm.FollowEverything();
            GUILayout.EndHorizontal();
            GUILayout.Label(atm.FollowingEverything
                ? "いまは端末の時計、ラジオの天気、暦に合わせて動いているよ。下のボタンを押すと、その景色を確かめられる。"
                : "手で選んだ景色を表示中。「自動に戻す」で時計、天気、暦に合わせる動きに戻るよ。", _s.note);

            // ---- time of day
            GUILayout.Label("時刻", _s.header);
            Row(width, Times.Length, i => Times[i].label, i => !atm.followClock && atm.hoursPerSecond == 0f && Mathf.Abs(atm.previewHour - Times[i].hour) < 0.05f, i =>
            {
                atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = Times[i].hour;
            }, 3);
            float hour = atm.CurrentHour;
            float picked = GUILayout.HorizontalSlider(hour, 0f, 24f, _s.sliderTrack, _s.sliderThumb);
            if (Event.current.type == EventType.Repaint)
            {
                _sliderRect = GUILayoutUtility.GetLastRect();
                _sliderRect.yMin -= 14; _sliderRect.yMax += 14;
            }
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
            GUILayout.Space(6);
            bool running = !atm.followClock && atm.hoursPerSecond != 0f;
            if (GUILayout.Button(running ? "■ 早送りを止める" : "▶ 一日を早送りで見る(約 50 秒)", running ? _s.chipOn : _s.chip))
            {
                if (running) atm.hoursPerSecond = 0f;
                else { atm.previewHour = atm.CurrentHour; atm.followClock = false; atm.hoursPerSecond = 0.5f; }
            }

            // ---- weather
            GUILayout.Label("天気", _s.header);
            Row(width, Weathers.Length, i => Weathers[i].label, i => atm.weather == Weathers[i].w, i => atm.SetWeatherByHand(Weathers[i].w), 3);
            if (atm.weather == CampWeather.AfterRain)
                GUILayout.Label($"水たまりが残っていて、あと約 {Mathf.CeilToInt(atm.MinutesUntilDry)} 分で乾くよ。", _s.note);
            else if (atm.weather == CampWeather.Rain)
                GUILayout.Label("雨の間は水たまりができて、やむと「雨上がり」になるよ。", _s.note);

            // ---- season
            GUILayout.Label("季節", _s.header);
            Row(width, Seasons.Length, i => Seasons[i].label, i => atm.season == Seasons[i].s, i => atm.season = Seasons[i].s, 5);

            // ---- lights
            GUILayout.Label("灯り", _s.header);
            if (GUILayout.Button(atm.sleep ? "おやすみモード中(ランタンを消している)" : "おやすみモードにする(ランタンを消す)", atm.sleep ? _s.chipOn : _s.chip))
                atm.SetSleep(!atm.sleep);

            // ---- camera
            if (_rig)
            {
                GUILayout.Label("カメラ", _s.header);
                Row(width, Focuses.Length + 1, i => i == 0 ? "全体" : Focuses[i - 1].label, i => i == 0 ? _rig.focus == null : (_rig.focus != null && _rig.focus.kind == Focuses[i - 1].kind), i =>
                {
                    if (i == 0) { _rig.focus = null; return; }
                    foreach (var p in _props) if (p && p.kind == Focuses[i - 1].kind) _rig.focus = p;
                }, 3);
                if (GUILayout.Button(_rig.autoSway ? "ゆっくり揺れる: オン" : "ゆっくり揺れる: オフ", _rig.autoSway ? _s.chipOn : _s.chip)) _rig.autoSway = !_rig.autoSway;
                GUILayout.Label("画面をなぞると回り込み、つまむ(ホイール)と寄れるよ。小物を押すとそこへ寄る。", _s.note);
            }

            // ---- quality
            var q = CampQuality.Instance;
            if (q)
            {
                GUILayout.Label("画質", _s.header);
                Row(width, Qualities.Length, i => Qualities[i].label, i => q.level == Qualities[i].q, i => q.level = Qualities[i].q, 3);
                string now = q.Applied == CampQualityLevel.Full ? "フル" : "スマホ相当";
                GUILayout.Label($"いまは「{now}」で動いていて、毎秒 {(q.Fps > 0 ? q.Fps.ToString("0") : "…")} コマ。スマホ相当は、ぼかし、焚き火の影、草の一部、水たまり専用の鏡像を省くよ。", _s.note);
            }
            GUILayout.Space(8);
            GUILayout.Label("色や光そのものの調整は、Unity の CampLook アセットでできるよ。", _s.note);
        }

        /// <summary>A row of choices as equal buttons, wrapped to perRow per line; the current one is highlighted.</summary>
        void Row(float width, int count, System.Func<int, string> label, System.Func<int, bool> on, System.Action<int> pick, int perRow)
        {
            float bw = (width - 6f * (perRow - 1)) / perRow;
            for (int start = 0; start < count; start += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < Mathf.Min(count, start + perRow); i++)
                    if (GUILayout.Button(label(i), on(i) ? _s.chipOn : _s.chip, GUILayout.Width(bw))) pick(i);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
    }
}
