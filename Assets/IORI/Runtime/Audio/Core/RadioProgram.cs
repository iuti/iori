using System;
using System.Collections.Generic;
using System.Globalization;

namespace Iori.Audio
{
    /// <summary>天気の識別子。データ側(radio.json)では文字列で書く。</summary>
    public static class WeatherIds
    {
        public const string Clear = "clear";
        public const string Rain = "rain";
    }

    /// <summary>ラジオの流れ方。</summary>
    public enum RadioMode
    {
        /// <summary>どうぶつの森式。時間帯ごとのテーマ曲がループする。</summary>
        Theme,
        /// <summary>ブレワイ式。曲は流さず、環境音とたまに鳴る短いフレーズだけ。</summary>
        Quiet,
    }

    /// <summary>1日の中の時間帯。start は現地時刻で、次の時間帯の start まで続く。</summary>
    public sealed class PhaseDef
    {
        public string Id;
        /// <summary>0:00 からの分。</summary>
        public int StartMinute;
        public RadioMode Mode = RadioMode.Theme;
    }

    public enum TrackLoopMode
    {
        /// <summary>
        /// 曲を最後まで流し、終わり際に同じ曲の頭とクロスフェードする(Suno の曲のように頭と終わりがある曲向け)。
        /// 曲の長さが分からないときは Seamless として扱う。
        /// </summary>
        Crossfade,
        /// <summary>ファイルを継ぎ目なくループする(ループ用に作った素材向け)。</summary>
        Seamless,
    }

    public sealed class TrackDef
    {
        public string Phase;
        public string Weather;
        public string File;
        public float Volume = 1f;
        public TrackLoopMode Loop = TrackLoopMode.Crossfade;
        /// <summary>Crossfade のとき、曲が終わってから次に始めるまでの無音(秒)。0 なら重ねてつなぐ。</summary>
        public double GapSeconds;
    }

    public sealed class OneShotDef
    {
        public string File;
        public float Volume = 1f;
    }

    public sealed class QuietDef
    {
        public List<OneShotDef> Phrases = new List<OneShotDef>();
        public double MinGapSeconds = 60;
        public double MaxGapSeconds = 240;
    }

    public sealed class AmbientDef
    {
        public string Id;
        public string File;
        public float DefaultVolume = 0.7f;
        /// <summary>空なら常に鳴る。指定があればその天気のときだけ鳴る(例: 雨音)。</summary>
        public List<string> Weathers = new List<string>();

        public bool IsActiveIn(string weather) => Weathers.Count == 0 || Weathers.Contains(weather);
    }

    /// <summary>
    /// ラジオの番組表。StreamingAssets/IORI/radio.json から読む。
    /// 曲の差し替えや時間帯の細分化はこのデータだけで行えるようにしてある。
    /// </summary>
    public sealed class RadioProgram
    {
        public int Version = 1;
        /// <summary>曲が切り替わるとき、前の曲がフェードアウトする時間(秒)。新しい曲はその間に重なって始まる。</summary>
        public double CrossfadeSeconds = 8;
        /// <summary>
        /// 新しい曲の頭のフェードイン(秒)。既定の 0 は頭から全開で入る(イントロの張りを残すため。chico の希望)。
        /// </summary>
        public double IntroFadeSeconds;
        public string DefaultWeather = WeatherIds.Clear;
        public List<PhaseDef> Phases = new List<PhaseDef>();
        public List<TrackDef> Tracks = new List<TrackDef>();
        /// <summary>正時に鳴らす音。null なら鳴らさない。</summary>
        public OneShotDef Chime;
        public QuietDef Quiet = new QuietDef();
        public List<AmbientDef> Ambient = new List<AmbientDef>();

        public static RadioProgram Parse(string json)
        {
            var root = JsonRead.Obj(Json.Parse(json), "radio.json");
            var p = new RadioProgram
            {
                Version = (int)JsonRead.Num(root, "version", 1),
                CrossfadeSeconds = JsonRead.Num(root, "crossfadeSeconds", 8),
                IntroFadeSeconds = JsonRead.Num(root, "introFadeSeconds", 0),
                DefaultWeather = JsonRead.Str(root, "defaultWeather", WeatherIds.Clear),
            };

            foreach (var item in JsonRead.Arr(root, "phases", required: true))
            {
                var o = JsonRead.Obj(item, "phases の要素");
                p.Phases.Add(new PhaseDef
                {
                    Id = JsonRead.Str(o, "id", required: true),
                    StartMinute = ParseClock(JsonRead.Str(o, "start", required: true)),
                    Mode = ParseMode(JsonRead.Str(o, "mode", "theme")),
                });
            }

            foreach (var item in JsonRead.Arr(root, "tracks"))
            {
                var o = JsonRead.Obj(item, "tracks の要素");
                p.Tracks.Add(new TrackDef
                {
                    Phase = JsonRead.Str(o, "phase", required: true),
                    Weather = JsonRead.Str(o, "weather", p.DefaultWeather),
                    File = JsonRead.Str(o, "file", required: true),
                    Volume = (float)JsonRead.Num(o, "volume", 1),
                    Loop = ParseLoop(JsonRead.Str(o, "loop", "crossfade")),
                    GapSeconds = JsonRead.Num(o, "gapSeconds", 0),
                });
            }

            if (root.TryGetValue("chime", out var chime) && chime != null)
                p.Chime = ReadOneShot(JsonRead.Obj(chime, "chime"));

            if (root.TryGetValue("quiet", out var quiet) && quiet != null)
            {
                var q = JsonRead.Obj(quiet, "quiet");
                p.Quiet.MinGapSeconds = JsonRead.Num(q, "minGapSeconds", 60);
                p.Quiet.MaxGapSeconds = JsonRead.Num(q, "maxGapSeconds", 240);
                foreach (var item in JsonRead.Arr(q, "phrases"))
                    p.Quiet.Phrases.Add(ReadOneShot(JsonRead.Obj(item, "quiet.phrases の要素")));
            }

            foreach (var item in JsonRead.Arr(root, "ambient"))
            {
                var o = JsonRead.Obj(item, "ambient の要素");
                p.Ambient.Add(new AmbientDef
                {
                    Id = JsonRead.Str(o, "id", required: true),
                    File = JsonRead.Str(o, "file", required: true),
                    DefaultVolume = (float)JsonRead.Num(o, "defaultVolume", 0.7),
                    Weathers = JsonRead.StrList(o, "weathers"),
                });
            }

            p.Validate();
            return p;
        }

        static OneShotDef ReadOneShot(Dictionary<string, object> o) => new OneShotDef
        {
            File = JsonRead.Str(o, "file", required: true),
            Volume = (float)JsonRead.Num(o, "volume", 1),
        };

        static TrackLoopMode ParseLoop(string s)
        {
            switch (s)
            {
                case "crossfade": return TrackLoopMode.Crossfade;
                case "seamless": return TrackLoopMode.Seamless;
                default: throw new FormatException("loop は crossfade か seamless です: " + s);
            }
        }

        static RadioMode ParseMode(string s)
        {
            switch (s)
            {
                case "theme": return RadioMode.Theme;
                case "quiet": return RadioMode.Quiet;
                default: throw new FormatException("mode は theme か quiet です: " + s);
            }
        }

        /// <summary>"HH:mm" を 0:00 からの分にする。</summary>
        public static int ParseClock(string s)
        {
            var parts = s.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                || h < 0 || h > 23 || m < 0 || m > 59)
                throw new FormatException("時刻は HH:mm で書いてください: " + s);
            return h * 60 + m;
        }

        public void Validate()
        {
            if (Phases.Count == 0) throw new FormatException("phases が空です");
            Phases.Sort((a, b) => a.StartMinute.CompareTo(b.StartMinute));
            var ids = new HashSet<string>();
            var starts = new HashSet<int>();
            foreach (var ph in Phases)
            {
                if (!ids.Add(ph.Id)) throw new FormatException("phase id が重複しています: " + ph.Id);
                if (!starts.Add(ph.StartMinute)) throw new FormatException("同じ開始時刻の phase があります: " + ph.Id);
            }
            foreach (var t in Tracks)
            {
                if (!ids.Contains(t.Phase)) throw new FormatException("track が存在しない phase を指しています: " + t.Phase);
                if (t.GapSeconds < 0) throw new FormatException("gapSeconds は 0 以上です: " + t.File);
            }
            if (CrossfadeSeconds < 0) throw new FormatException("crossfadeSeconds は 0 以上です");
            if (IntroFadeSeconds < 0) throw new FormatException("introFadeSeconds は 0 以上です");
            if (Quiet.MinGapSeconds <= 0 || Quiet.MaxGapSeconds < Quiet.MinGapSeconds)
                throw new FormatException("quiet の minGapSeconds / maxGapSeconds が不正です");
            var ambientIds = new HashSet<string>();
            foreach (var a in Ambient)
                if (!ambientIds.Add(a.Id)) throw new FormatException("ambient id が重複しています: " + a.Id);
        }

        /// <summary>
        /// その時間帯と天気の曲を選ぶ。天気に合う曲がなければ既定の天気の曲を使う。
        /// 同じ条件に複数曲あるときは dayIndex で日替わりにする。
        /// </summary>
        public TrackDef FindTrack(string phaseId, string weather, long dayIndex)
        {
            var exact = Tracks.FindAll(t => t.Phase == phaseId && t.Weather == weather);
            if (exact.Count == 0 && weather != DefaultWeather)
                exact = Tracks.FindAll(t => t.Phase == phaseId && t.Weather == DefaultWeather);
            if (exact.Count == 0) return null;
            var idx = (int)(((dayIndex % exact.Count) + exact.Count) % exact.Count);
            return exact[idx];
        }

        public PhaseDef FindPhase(string id) => Phases.Find(p => p.Id == id);
    }
}
