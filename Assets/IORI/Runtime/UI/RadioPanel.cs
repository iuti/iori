using System;
using Iori.Audio;
using TMPro;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The radio's panel: what is on now, the day's programme as the radio's own dial made large
    /// (the needle is the time), a button to stop the radio, the music volume and the quiet hours.
    /// Everything amber here is something sounding now; when the radio is stopped the dial goes out.
    /// </summary>
    public sealed class RadioPanel : UiPanel
    {
        static readonly (string id, string label)[] PhaseNames =
        {
            ("early_morning", "早朝"), ("morning", "朝"), ("day", "昼"), ("evening", "夕方"), ("night", "夜"), ("late_night", "深夜"),
        };

        public Action requestClose;

        public override float Height => 460f;

        TextMeshProUGUI _state, _name, _sub, _next, _clock;
        readonly UiShape[] _bars = new UiShape[4];
        UiShape _disc, _dot;
        UnityEngine.UI.RawImage _discIcon;
        Texture2D _pauseIcon, _playIcon;
        RadioDial _dial;
        UiSlider _volume;
        UiSwitch _quiet;
        bool _shownOnce, _shownQuiet, _shownOff, _shownRain;
        int _shownMinute;
        string _shownPhase;

        static string NameOf(string id)
        {
            foreach (var p in PhaseNames) if (p.id == id) return p.label;
            return id ?? "";
        }

        protected override void Build()
        {
            var th = UiTheme.Current;
            Header("ラジオ", () => requestClose?.Invoke());

            // what is on now
            for (int i = 0; i < 4; i++) _bars[i] = UiKit.Box("Bar", Content, 22 + i * 4.5f, 78, 2.5f, 6, th.amber, 1);
            _state = UiKit.Text("State", Content, 46, 70, 230, 17, "", UiFonts.Medium, 12, th.textWarm);
            _name = UiKit.Text("Name", Content, 22, 91, 250, 48.3f, "", UiFonts.Mincho, 42, th.textBright, TextAlignmentOptions.Left, 0.04f);
            _sub = UiKit.Text("Sub", Content, 22, 143.3f, 250, 20, "", UiFonts.Regular, 14, th.textSoft);

            var play = UiKit.Button("Play", Content, 282, 83.6f, 66, 66, TogglePlay, UiSounds.Toggle);
            _disc = UiKit.Shape("Disc", play.visual, 0, 0, 66, 66, default);
            _discIcon = UiKit.Icon("Icon", play.visual, 21, 21, 24, "pause", th.amberInk);
            _pauseIcon = _discIcon.texture as Texture2D;
            _playIcon = UiKit.LoadIcon("play");

            // the day's programme
            var radio = RadioDirector.Instance;
            _dial = new RadioDial(Content, 22, 183.3f, radio ? radio.Program : null, NameOf);
            _next = UiKit.Text("Next", Content, 22, 289.3f, 200, 19, "", UiFonts.Regular, 12.5f, th.textSoft);
            _clock = UiKit.Text("Clock", Content, 148, 289.3f, 200, 19, "", UiFonts.Regular, 12.5f, th.textSoft, TextAlignmentOptions.Right);
            _dot = UiKit.Shape("Dot", Content, 0, 295.8f, 6, 6, default);

            // volume
            UiKit.Icon("Volume icon", Content, 22, 339.3f, 22, "volume", th.text);
            _volume = UiSlider.Create("Volume", Content, 58, 328.3f, 290);
            _volume.onChanged = v => { if (RadioDirector.Instance) RadioDirector.Instance.SetMusicVolume(v); };

            // the quiet hours
            UiKit.Text("Quiet title", Content, 22, 392.9f, 250, 21, "静けさの時間", UiFonts.Medium, 15, th.text);
            UiKit.Text("Quiet note", Content, 22, 416.9f, 264, 18.75f, "曲を止めて、火や風の音とピアノだけにする", UiFonts.Regular, 12.5f, th.textSoft);
            _quiet = UiSwitch.Create("Quiet", Content, 296, 392.3f);
            _quiet.onChanged = on => { if (RadioDirector.Instance) RadioDirector.Instance.SetQuiet(on); };
        }

        protected override void OnOpened()
        {
            var radio = RadioDirector.Instance;
            if (!radio) return;
            _volume.SetValue(radio.MusicVolume);
            _quiet.Set(radio.Quiet, false);
        }

        void TogglePlay()
        {
            var radio = RadioDirector.Instance;
            if (radio) radio.SetOff(!radio.Off);
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
            bool quiet = span != null && span.Mode == RadioMode.Quiet;
            bool off = radio.Off;
            bool playing = !off && !quiet;

            // the little level bars move only while a song is on
            float t = Time.unscaledTime;
            for (int i = 0; i < 4; i++)
            {
                float h = 3f + 9f * Mathf.Clamp01(0.5f + 0.34f * Mathf.Sin(t * (5.1f + i * 1.3f) + i * 1.9f) + 0.22f * Mathf.Sin(t * (8.7f - i) + i * 0.7f));
                UiKit.Place(_bars[i].rectTransform, 22 + i * 4.5f, 84.5f - h, 2.5f, h);
                if (_bars[i].gameObject.activeSelf != playing) _bars[i].gameObject.SetActive(playing);
            }

            _dial.SetTime(local.Hour * 60 + local.Minute + local.Second / 60f);

            // the rest changes only when the minute, the phase or a switch changes
            int minute = local.Hour * 60 + local.Minute;
            bool rain = radio.Weather == WeatherIds.Rain;
            if (_shownOnce && minute == _shownMinute && phase == _shownPhase && quiet == _shownQuiet && off == _shownOff && rain == _shownRain) return;
            _shownOnce = true; _shownMinute = minute; _shownPhase = phase; _shownQuiet = quiet; _shownOff = off; _shownRain = rain;

            UiKit.Place(_state.rectTransform, playing ? 46 : 22, 70, 230, 17);
            _state.text = off ? "ラジオは止まっている" : quiet ? "いまは静けさの時間" : "いま流れている曲";
            _state.color = playing ? th.textWarm : th.textSoft;
            _name.text = NameOf(phase);
            _name.color = off ? th.textSoft : th.textBright;
            _sub.text = off ? "まわりの音だけが鳴っている"
                : quiet ? "曲はお休み。ピアノがときどき鳴る"
                : rain ? "雨の日の曲" : "晴れの日の曲";

            _disc.Style = off
                ? ShapeStyle.Fill(UiTheme.Rgba(255, 255, 255, 0.14f), 33).Ring(0.14f)
                : ShapeStyle.Gradient(th.amberLight, th.amberDeep, 33).Radial().Glow(th.amberGlow, 30).TopLight(0.7f);
            _discIcon.texture = off ? _playIcon : _pauseIcon;
            _discIcon.color = off ? th.text : th.amberInk;

            _dial.SetState(phase, !off);

            // what comes next, from the programme
            var phases = radio.Program.Phases;
            int idx = phases.FindIndex(p => p.Id == phase);
            if (idx >= 0 && phases.Count > 1)
            {
                var next = phases[(idx + 1) % phases.Count];
                _next.text = $"つぎは {next.StartMinute / 60}:{next.StartMinute % 60:00} から「{NameOf(next.Id)}」";
            }
            else _next.text = "";

            _clock.text = $"いまの時間 {local.Hour}:{local.Minute:00}";
            float w = _clock.GetPreferredValues(_clock.text).x;
            UiKit.Place(_dot.rectTransform, 348 - w - 12, 295.8f, 6, 6);
            _dot.Style = off
                ? ShapeStyle.Fill(th.textDim, 3)
                : ShapeStyle.Fill(th.amber, 3).Glow(UiTheme.Rgba(255, 190, 100, 0.9f), 8);
        }
    }

    /// <summary>
    /// The programme of the day drawn as a radio dial: the day runs left to right from the first
    /// phase's start (4:00), each phase has its name above the scale, and the red needle is the time.
    /// Lit amber while the radio is on.
    /// </summary>
    public sealed class RadioDial
    {
        const float W = 326f, H = 96f, Left = 14f, Right = 312f;

        readonly RectTransform _root;
        readonly UiShape _body, _band, _needle, _cap;
        readonly UiShape[] _ticks, _marks;
        readonly TextMeshProUGUI[] _labels, _numbers;
        readonly string[] _ids;
        readonly float[] _startX, _endX;
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
            return Left + m / 1440f * (Right - Left);
        }

        public RadioDial(Transform parent, float x, float y, RadioProgram program, Func<string, string> nameOf)
        {
            int n = program != null && program.Phases.Count > 0 ? program.Phases.Count : DefaultPhases.Length;
            _ids = new string[n];
            var starts = new int[n];
            for (int i = 0; i < n; i++)
            {
                _ids[i] = program != null && program.Phases.Count > 0 ? program.Phases[i].Id : DefaultPhases[i].id;
                starts[i] = program != null && program.Phases.Count > 0 ? program.Phases[i].StartMinute : DefaultPhases[i].minute;
            }
            _dayStart = starts[0];
            _startX = new float[n];
            _endX = new float[n];
            for (int i = 0; i < n; i++)
            {
                _startX[i] = X(starts[i]);
                _endX[i] = i + 1 < n ? X(starts[i + 1]) : Right;
            }

            _root = UiKit.Node("Dial", parent, x, y, W, H);
            _body = UiKit.Shape("Body", _root, 0, 0, W, H, default);
            _band = UiKit.Box("Now band", _root, 0, 0, 10, H, UiTheme.Rgba(255, 255, 255, 0.40f));

            // the scale: a short line every hour, a long one where a phase starts
            _ticks = new UiShape[25];
            for (int i = 0; i < 25; i++)
                _ticks[i] = UiKit.Box("Hour", _root, Left + i * (Right - Left) / 24f, 49, 1, 9, default);
            _marks = new UiShape[n + 1];
            for (int i = 0; i <= n; i++)
                _marks[i] = UiKit.Box("Mark", _root, (i < n ? _startX[i] : Right) - 0.5f, 42, 2, 20, default, 1);

            _labels = new TextMeshProUGUI[n];
            _numbers = new TextMeshProUGUI[n];
            for (int i = 0; i < n; i++)
            {
                _labels[i] = UiKit.Text("Phase", _root, _startX[i], 15, _endX[i] - _startX[i], 17, nameOf(_ids[i]), UiFonts.Medium, 12, default, TextAlignmentOptions.Center);
                int hour = starts[i] / 60, minute = starts[i] % 60;
                _numbers[i] = UiKit.Text("Hour number", _root, _startX[i] - 12, 68, 24, 15, minute == 0 ? hour.ToString() : $"{hour}:{minute:00}", UiFonts.Medium, 10, default, TextAlignmentOptions.Center);
            }

            // the needle lies over everything, as on the real dial
            _needle = UiKit.Box("Needle", _root, 0, 38, 2.4f, 52, default, 2);
            _cap = UiKit.Box("Needle cap", _root, 0, 34, 8, 8, default, 4);
            Paint();
        }

        public void SetTime(float minuteOfDay)
        {
            float x = X(minuteOfDay);
            UiKit.Place(_needle.rectTransform, x - 1.2f, 38, 2.4f, 52);
            UiKit.Place(_cap.rectTransform, x - 4f, 34, 8, 8);
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
            Color32 ink, inkStrong, tick, mark, needle;
            if (_lit)
            {
                // (the bottom colour already holds the mock's soft inner shade at the lower edge)
                _body.Style = ShapeStyle.Gradient(new Color32(0xFF, 0xE9, 0xB6, 255), new Color32(0xEC, 0xA0, 0x4C, 255), 16)
                    .Glow(UiTheme.Rgba(255, 176, 84, 0.40f), 34).TopLight(0.65f);
                ink = new Color32(0x6B, 0x44, 0x16, 255);
                inkStrong = new Color32(0x2E, 0x18, 0x04, 255);
                tick = new Color32(0x7C, 0x55, 0x2C, 255);
                mark = new Color32(0x4A, 0x2A, 0x0C, 255);
                needle = new Color32(0xC6, 0x32, 0x1A, 255);
            }
            else
            {
                _body.Style = ShapeStyle.Gradient(new Color32(0xD3, 0xCF, 0xEA, 255), new Color32(0x9C, 0x97, 0xC6, 255), 16).TopLight(0.45f);
                ink = new Color32(0x4C, 0x48, 0x82, 255);
                inkStrong = new Color32(0x22, 0x20, 0x50, 255);
                tick = new Color32(0x5A, 0x56, 0x90, 255);
                mark = new Color32(0x38, 0x35, 0x6C, 255);
                needle = new Color32(0x9E, 0x3A, 0x30, 255);
            }
            foreach (var t in _ticks) t.Style = ShapeStyle.Fill(tick, 0);
            foreach (var m in _marks) m.Style = ShapeStyle.Fill(mark, 1);
            _needle.Style = ShapeStyle.Fill(needle, 2);
            _cap.Style = ShapeStyle.Fill(needle, 4);

            int now = Array.IndexOf(_ids, _phase);
            _band.gameObject.SetActive(now >= 0);
            if (now >= 0) UiKit.Place(_band.rectTransform, _startX[now], 0, _endX[now] - _startX[now], H);
            _band.Style = ShapeStyle.Fill(UiTheme.Rgba(255, 255, 255, _lit ? 0.40f : 0.30f), 0);
            for (int i = 0; i < _labels.Length; i++)
            {
                _labels[i].font = i == now ? UiFonts.Bold : UiFonts.Medium;
                _labels[i].color = i == now ? inkStrong : ink;
                _numbers[i].color = ink;
            }
        }
    }
}
