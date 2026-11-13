using System;
using Iori.Audio;
using TMPro;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The radio's sheet. It says what is on by its layout, not by words: the name of the time slot
    /// that is playing, the day's programme as a fine scale with a needle at the present time, one
    /// button that stops the songs (the radio then keeps the quiet hours: the fire, the wind and now
    /// and then a piano) and the music volume. The amber marks are lit only while a song is on.
    /// </summary>
    public sealed class RadioPanel : UiPanel
    {
        static readonly (string id, string label)[] PhaseNames =
        {
            ("early_morning", "早朝"), ("morning", "朝"), ("day", "昼"), ("evening", "夕方"), ("night", "夜"), ("late_night", "深夜"),
        };

        const float Pad = 26f;
        const float BarWidth = 2f, BarGap = 2.5f, BarFloor = 53f;

        public override float Height => 230f;

        TextMeshProUGUI _name;
        readonly UiShape[] _bars = new UiShape[3];
        UnityEngine.UI.RawImage _weather;
        Texture2D _sunIcon, _rainIcon, _pauseIcon, _playIcon;
        UiButton _play;
        RadioDial _dial;
        UiSlider _volume;
        bool _shownOnce, _shownQuiet, _shownRain;
        string _shownPhase;
        float _level;

        static string NameOf(string id)
        {
            foreach (var p in PhaseNames) if (p.id == id) return p.label;
            return id ?? "";
        }

        protected override void Build()
        {
            var th = UiTheme.Current;

            // what is on now: the level bars, the slot's name, the weather the songs are chosen for
            for (int i = 0; i < 3; i++)
                _bars[i] = UiKit.Box("Bar", Content, Pad + i * (BarWidth + BarGap), BarFloor - 2f, BarWidth, 2f, th.amber);
            _name = UiKit.Text("Name", Content, 49, 24, 200, 44, "", UiFonts.Mincho, 34, th.textBright, TextAlignmentOptions.Left, 0.04f);
            _weather = UiKit.Icon("Weather", Content, 49, 36, 20, "sun", th.textSoft);
            _sunIcon = _weather.texture as Texture2D;
            _rainIcon = UiKit.LoadIcon("rain");

            _play = UiKit.IconButton("Play", Content, Width - Pad - 44 + 7, 24, 44, "pause", 30, th.text, TogglePlay, UiSounds.Toggle);
            _pauseIcon = _play.icon.texture as Texture2D;
            _playIcon = UiKit.LoadIcon("play");

            // the day's programme
            var radio = RadioDirector.Instance;
            _dial = new RadioDial(Content, Pad, 92, Width - Pad * 2, radio ? radio.Program : null);

            // the music volume
            UiKit.Icon("Volume icon", Content, Pad, 168, 20, "volume", th.textSoft);
            _volume = UiSlider.Create("Volume", Content, 62, 156, Width - Pad - 62);
            _volume.onChanged = v => { if (RadioDirector.Instance) RadioDirector.Instance.SetMusicVolume(v); };
        }

        protected override void OnOpened()
        {
            var radio = RadioDirector.Instance;
            if (radio) _volume.SetValue(radio.MusicVolume);
        }

        /// <summary>Stopping the radio is going over to the quiet hours; starting it is leaving them.</summary>
        void TogglePlay()
        {
            var radio = RadioDirector.Instance;
            if (radio) radio.SetQuiet(!radio.Quiet);
        }

        protected override void Refresh()
        {
            var radio = RadioDirector.Instance;
            if (!radio || radio.Plan == null || radio.Program == null) return;
            var th = UiTheme.Current;

            var now = radio.VirtualNow;
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
            var span = radio.Plan.SpanAt(radio.Plan.ToPlanTime(now.ToUnixTimeMilliseconds()));
            string phase = span?.Phase;
            bool quiet = span == null || span.Mode == RadioMode.Quiet;
            bool rain = radio.Weather == WeatherIds.Rain;

            // the level bars move while a song is on and lie flat while it is quiet
            _level = Mathf.MoveTowards(_level, quiet ? 0f : 1f, Time.unscaledDeltaTime * 4f);
            float t = Time.unscaledTime;
            for (int i = 0; i < 3; i++)
            {
                float wave = Mathf.Clamp01(0.5f + 0.34f * Mathf.Sin(t * (5.1f + i * 1.3f) + i * 1.9f) + 0.22f * Mathf.Sin(t * (8.7f - i) + i * 0.7f));
                float h = Mathf.Lerp(2f, 4f + 10f * wave, _level);
                UiKit.Place(_bars[i].rectTransform, Pad + i * (BarWidth + BarGap), BarFloor - h, BarWidth, h);
                _bars[i].Style = ShapeStyle.Fill(Color32.Lerp(th.textDim, th.amber, _level), 0f);
            }

            _dial.SetTime(local.Hour * 60 + local.Minute + local.Second / 60f);

            // the rest changes only when the slot, the weather or the button changes
            if (_shownOnce && phase == _shownPhase && quiet == _shownQuiet && rain == _shownRain) return;
            _shownOnce = true; _shownPhase = phase; _shownQuiet = quiet; _shownRain = rain;

            _name.text = NameOf(phase);
            float w = _name.GetPreferredValues(_name.text).x;
            UiKit.Place(_weather.rectTransform, 49 + w + 12, 36, 20, 20);
            _weather.texture = rain ? _rainIcon : _sunIcon;
            _play.icon.texture = quiet ? _playIcon : _pauseIcon;
            _dial.SetState(phase, !quiet);
        }
    }

    /// <summary>
    /// The programme of the day drawn as the scale of a radio dial, in fine lines: the day runs left
    /// to right from the start of the first slot (4:00), a short line stands at every hour and a long
    /// one where a slot starts, with its hour under it. The slot of the present time is underlined
    /// and the needle, with a small mark on top, stands at the present time. Needle and underline
    /// are amber while a song is on and pale while it is quiet.
    /// </summary>
    public sealed class RadioDial
    {
        const float H = 54f, Base = 30f;

        readonly RectTransform _root;
        readonly UiShape _under, _needle;
        readonly UnityEngine.UI.RawImage _marker;
        readonly TextMeshProUGUI[] _numbers;
        readonly string[] _ids;
        readonly float[] _startX, _endX;
        readonly float _width;
        readonly int _dayStart;
        string _phase;
        bool _lit = true;
        bool _painted;

        static readonly (int minute, string id)[] DefaultPhases =
        {
            (240, "early_morning"), (420, "morning"), (660, "day"), (960, "evening"), (1140, "night"), (1380, "late_night"),
        };

        float X(float minuteOfDay)
        {
            float m = minuteOfDay - _dayStart;
            if (m < 0f) m += 1440f;
            return m / 1440f * _width;
        }

        public RadioDial(Transform parent, float x, float y, float width, RadioProgram program)
        {
            var th = UiTheme.Current;
            bool given = program != null && program.Phases.Count > 0;
            int n = given ? program.Phases.Count : DefaultPhases.Length;
            _ids = new string[n];
            var starts = new int[n];
            for (int i = 0; i < n; i++)
            {
                _ids[i] = given ? program.Phases[i].Id : DefaultPhases[i].id;
                starts[i] = given ? program.Phases[i].StartMinute : DefaultPhases[i].minute;
            }
            _width = width;
            _dayStart = starts[0];
            _startX = new float[n];
            _endX = new float[n];
            for (int i = 0; i < n; i++)
            {
                _startX[i] = X(starts[i]);
                _endX[i] = i + 1 < n ? X(starts[i + 1]) : width;
            }

            _root = UiKit.Node("Dial", parent, x, y, width, H);
            UiKit.Box("Base line", _root, 0, Base, width, 1, UiTheme.Rgba(241, 238, 255, 0.20f));

            // a short line every hour, except where a slot starts
            var hour = UiTheme.Rgba(241, 238, 255, 0.36f);
            float firstHour = Mathf.Ceil(_dayStart / 60f) * 60f;
            for (int i = 0; i < 24; i++)
            {
                float minute = firstHour + i * 60f;
                bool slot = false;
                foreach (int s in starts) if (Mathf.Abs(Mathf.Repeat(minute - s, 1440f)) < 0.5f) slot = true;
                if (!slot) UiKit.Box("Hour", _root, X(Mathf.Repeat(minute, 1440f)) - 0.5f, Base - 6, 1, 6, hour);
            }

            // a long line where a slot starts (and at the end of the day), with the hour under it
            var mark = UiTheme.Rgba(241, 238, 255, 0.90f);
            _numbers = new TextMeshProUGUI[n];
            for (int i = 0; i <= n; i++)
            {
                UiKit.Box("Slot", _root, (i < n ? _startX[i] : width) - 0.5f, Base - 14, 1, 14, mark);
                if (i == n) break;
                int h = starts[i] / 60, m = starts[i] % 60;
                _numbers[i] = UiKit.Text("Hour number", _root, _startX[i] - 20, 39, 40, 16, m == 0 ? h.ToString() : $"{h}:{m:00}",
                    UiFonts.Medium, 11, th.textSoft, TextAlignmentOptions.Center);
            }

            _under = UiKit.Box("Now slot", _root, 0, Base - 0.5f, 10, 2, th.amber);
            // the needle lies over everything, as on the real dial
            _needle = UiKit.Box("Needle", _root, 0, 2, 1, Base - 1, th.amber);
            // (the triangle is 9 x 6, set at (3.5, 5) inside its square picture of 16)
            _marker = UiKit.Icon("Needle mark", _root, -8, -8, 16, "marker", th.amber);
            Paint();
        }

        public void SetTime(float minuteOfDay)
        {
            float x = X(minuteOfDay);
            UiKit.Place(_needle.rectTransform, x - 0.5f, 2, 1, Base - 1);
            UiKit.Place(_marker.rectTransform, x - 8f, -8, 16, 16);
        }

        public void SetState(string phase, bool lit)
        {
            if (_painted && phase == _phase && lit == _lit) return;
            _phase = phase;
            _lit = lit;
            Paint();
        }

        void Paint()
        {
            _painted = true;
            var th = UiTheme.Current;
            Color32 now = _lit ? th.amber : th.textSoft;
            _needle.Style = ShapeStyle.Fill(now, 0f);
            _marker.color = now;

            int cur = Array.IndexOf(_ids, _phase);
            _under.gameObject.SetActive(cur >= 0);
            if (cur >= 0) UiKit.Place(_under.rectTransform, _startX[cur], Base - 0.5f, _endX[cur] - _startX[cur], 2);
            _under.Style = ShapeStyle.Fill(now, 0f);
            for (int i = 0; i < _numbers.Length; i++)
            {
                _numbers[i].font = i == cur ? UiFonts.Bold : UiFonts.Medium;
                _numbers[i].color = i == cur ? th.text : th.textSoft;
            }
        }
    }
}
