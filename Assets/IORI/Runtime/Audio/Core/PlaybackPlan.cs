using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// 1 回ぶんの曲の再生。[StartMs, EndMs) の間鳴り、始めと終わりでフェードする。
    /// クリップ同士は重なってよい(クロスフェード)し、間に無音があってもよい。
    /// 時刻はすべて UTC の Unix ミリ秒。
    ///
    /// 再生位置の決まり: 時刻 t での位置は (t - StartMs + OffsetMs)。Loop のときはファイル長で割った余り。
    /// PC と Android が同じ式で位置を出すので、予定表を作り直しても同じクリップは同じ位置で鳴り続ける。
    /// </summary>
    public sealed class MusicClip
    {
        /// <summary>クリップの同一性。予定表を作り直しても同じクリップなら同じ Id になる。</summary>
        public string Id = "";
        public long StartMs;
        public long EndMs;
        public string File = "";
        public float Volume = 1f;
        public int FadeInMs;
        public int FadeOutMs;
        /// <summary>true ならファイルを継ぎ目なくループする(ループ用に作った素材向け)。</summary>
        public bool Loop;
        public long OffsetMs;
        public string Phase = "";

        public bool IsActiveAt(long t) => t >= StartMs && t < EndMs;

        /// <summary>フェードを含めた音量の係数(0〜1、Volume は掛けない)。等パワーのカーブ。</summary>
        public float FadeGainAt(long t)
        {
            if (!IsActiveAt(t)) return 0f;
            var gain = 1.0;
            if (FadeInMs > 0 && t - StartMs < FadeInMs)
                gain *= Math.Sin((t - StartMs) / (double)FadeInMs * Math.PI / 2);
            if (FadeOutMs > 0 && EndMs - t < FadeOutMs)
                gain *= Math.Sin((EndMs - t) / (double)FadeOutMs * Math.PI / 2);
            return (float)gain;
        }

        /// <summary>時刻 t での再生位置(ミリ秒)。durationMs が分からない(0)なら折り返さない。</summary>
        public long PositionAt(long t, long durationMs)
        {
            var p = t - StartMs + OffsetMs;
            if (p < 0) p = 0;
            if (Loop && durationMs > 0) p %= durationMs;
            return p;
        }
    }

    /// <summary>時間帯の区間。表示や、チャイム・フレーズを鳴らすかどうかの判定に使う。隙間なく並ぶ。</summary>
    public sealed class PhaseSpan
    {
        public long StartMs;
        public long EndMs;
        public string Phase = "";
        public RadioMode Mode;
    }

    public static class OneShotKinds
    {
        public const string Chime = "chime";
        public const string Phrase = "phrase";
        /// <summary>イオリの声(時報)。Unity は声に合わせて口を動かす。</summary>
        public const string Voice = "voice";
    }

    /// <summary>決まった時刻に一度だけ鳴らす音(正時のチャイム、静けさの時間のピアノなど)。</summary>
    public sealed class ScheduledOneShot
    {
        public long AtMs;
        public string File;
        public float Volume = 1f;
        public string Kind;
    }

    public sealed class AmbientLayerState
    {
        public string Id;
        public string File;
        /// <summary>実際に鳴らす音量(ユーザー音量 × マスター × 天気による有効/無効)。</summary>
        public float Volume;
    }

    /// <summary>
    /// 「いつ何を鳴らすか」の予定表。Unity(C#)だけが作り、Android のネイティブ再生サービスと
    /// PC の Unity 再生係はこれを鳴らすだけにする。JSON にして受け渡す。
    /// ファイルパスは StreamingAssets/IORI/Audio/ からの相対パス。
    /// </summary>
    public sealed class PlaybackPlan
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public long GeneratedAtMs;
        /// <summary>
        /// 予定表の終わり。これを過ぎてもアプリが作り直さないときは、再生係は最後の 24 時間を繰り返す。
        /// </summary>
        public long ValidUntilMs;
        /// <summary>動作確認用の時計のずれ。再生係は「現在時刻 + これ」を予定表の時刻として扱う。</summary>
        public long ClockOffsetMs;
        public string Weather = WeatherIds.Clear;
        public float MusicVolume = 1f;
        public float OneShotVolume = 1f;
        public List<PhaseSpan> Phases = new List<PhaseSpan>();
        /// <summary>開始時刻順。</summary>
        public List<MusicClip> Music = new List<MusicClip>();
        /// <summary>時刻順。</summary>
        public List<ScheduledOneShot> OneShots = new List<ScheduledOneShot>();
        public List<AmbientLayerState> Ambient = new List<AmbientLayerState>();
        /// <summary>曲ファイルの長さ(ミリ秒)。再生位置の折り返しに使う。</summary>
        public Dictionary<string, long> Durations = new Dictionary<string, long>();

        public const long DayMs = 24L * 3600 * 1000;

        /// <summary>
        /// 予定表の時刻に直す。ValidUntilMs を過ぎていたら、最後の 24 時間を繰り返す位置に折り返す。
        /// </summary>
        public long ToPlanTime(long t)
        {
            if (ValidUntilMs <= 0 || t < ValidUntilMs || ValidUntilMs - GeneratedAtMs < DayMs) return t;
            return ValidUntilMs - DayMs + (t - ValidUntilMs) % DayMs;
        }

        public PhaseSpan SpanAt(long t)
        {
            foreach (var s in Phases)
                if (t >= s.StartMs && t < s.EndMs) return s;
            return null;
        }

        public List<MusicClip> ClipsAt(long t)
        {
            var result = new List<MusicClip>();
            foreach (var c in Music)
            {
                if (c.StartMs > t) break;
                if (c.IsActiveAt(t)) result.Add(c);
            }
            return result;
        }

        public long DurationOf(string file) => file != null && Durations.TryGetValue(file, out var d) ? d : 0;

        /// <summary>(fromExclusive, toInclusive] に予定されている単発音。</summary>
        public List<ScheduledOneShot> OneShotsBetween(long fromExclusive, long toInclusive)
        {
            var result = new List<ScheduledOneShot>();
            foreach (var s in OneShots)
                if (s.AtMs > fromExclusive && s.AtMs <= toInclusive) result.Add(s);
            return result;
        }

        public string ToJson()
        {
            var phases = new List<object>();
            foreach (var p in Phases)
                phases.Add(new Dictionary<string, object>
                {
                    ["start"] = p.StartMs,
                    ["end"] = p.EndMs,
                    ["phase"] = p.Phase ?? "",
                    ["mode"] = p.Mode == RadioMode.Quiet ? "quiet" : "theme",
                });
            var music = new List<object>();
            foreach (var m in Music)
                music.Add(new Dictionary<string, object>
                {
                    ["id"] = m.Id ?? "",
                    ["start"] = m.StartMs,
                    ["end"] = m.EndMs,
                    ["file"] = m.File ?? "",
                    ["volume"] = (double)m.Volume,
                    ["fadeIn"] = m.FadeInMs,
                    ["fadeOut"] = m.FadeOutMs,
                    ["loop"] = m.Loop,
                    ["offset"] = m.OffsetMs,
                    ["phase"] = m.Phase ?? "",
                });
            var oneShots = new List<object>();
            foreach (var s in OneShots)
                oneShots.Add(new Dictionary<string, object>
                {
                    ["at"] = s.AtMs,
                    ["file"] = s.File,
                    ["volume"] = (double)s.Volume,
                    ["kind"] = s.Kind ?? "",
                });
            var ambient = new List<object>();
            foreach (var a in Ambient)
                ambient.Add(new Dictionary<string, object>
                {
                    ["id"] = a.Id,
                    ["file"] = a.File,
                    ["volume"] = (double)a.Volume,
                });
            var durations = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in Durations) durations[kv.Key] = kv.Value;
            return Json.Write(new Dictionary<string, object>
            {
                ["version"] = Version,
                ["generatedAt"] = GeneratedAtMs,
                ["validUntil"] = ValidUntilMs,
                ["clockOffsetMs"] = ClockOffsetMs,
                ["weather"] = Weather,
                ["musicVolume"] = (double)MusicVolume,
                ["oneShotVolume"] = (double)OneShotVolume,
                ["phases"] = phases,
                ["music"] = music,
                ["oneShots"] = oneShots,
                ["ambient"] = ambient,
                ["durations"] = new Dictionary<string, object>(durations),
            });
        }

        public static PlaybackPlan FromJson(string json)
        {
            var root = JsonRead.Obj(Json.Parse(json), "plan");
            var plan = new PlaybackPlan
            {
                Version = (int)JsonRead.Num(root, "version", CurrentVersion),
                GeneratedAtMs = (long)JsonRead.Num(root, "generatedAt", 0),
                ValidUntilMs = (long)JsonRead.Num(root, "validUntil", 0),
                ClockOffsetMs = (long)JsonRead.Num(root, "clockOffsetMs", 0),
                Weather = JsonRead.Str(root, "weather", WeatherIds.Clear),
                MusicVolume = (float)JsonRead.Num(root, "musicVolume", 1),
                OneShotVolume = (float)JsonRead.Num(root, "oneShotVolume", 1),
            };
            foreach (var item in JsonRead.Arr(root, "phases"))
            {
                var o = JsonRead.Obj(item, "phases の要素");
                plan.Phases.Add(new PhaseSpan
                {
                    StartMs = (long)JsonRead.Num(o, "start", 0, required: true),
                    EndMs = (long)JsonRead.Num(o, "end", 0, required: true),
                    Phase = JsonRead.Str(o, "phase", ""),
                    Mode = JsonRead.Str(o, "mode", "theme") == "quiet" ? RadioMode.Quiet : RadioMode.Theme,
                });
            }
            foreach (var item in JsonRead.Arr(root, "music"))
            {
                var o = JsonRead.Obj(item, "music の要素");
                plan.Music.Add(new MusicClip
                {
                    Id = JsonRead.Str(o, "id", ""),
                    StartMs = (long)JsonRead.Num(o, "start", 0, required: true),
                    EndMs = (long)JsonRead.Num(o, "end", 0, required: true),
                    File = JsonRead.Str(o, "file", required: true),
                    Volume = (float)JsonRead.Num(o, "volume", 1),
                    FadeInMs = (int)JsonRead.Num(o, "fadeIn", 0),
                    FadeOutMs = (int)JsonRead.Num(o, "fadeOut", 0),
                    Loop = o.TryGetValue("loop", out var loop) && loop is bool b && b,
                    OffsetMs = (long)JsonRead.Num(o, "offset", 0),
                    Phase = JsonRead.Str(o, "phase", ""),
                });
            }
            foreach (var item in JsonRead.Arr(root, "oneShots"))
            {
                var o = JsonRead.Obj(item, "oneShots の要素");
                plan.OneShots.Add(new ScheduledOneShot
                {
                    AtMs = (long)JsonRead.Num(o, "at", 0, required: true),
                    File = JsonRead.Str(o, "file", required: true),
                    Volume = (float)JsonRead.Num(o, "volume", 1),
                    Kind = JsonRead.Str(o, "kind", ""),
                });
            }
            foreach (var item in JsonRead.Arr(root, "ambient"))
            {
                var o = JsonRead.Obj(item, "ambient の要素");
                plan.Ambient.Add(new AmbientLayerState
                {
                    Id = JsonRead.Str(o, "id", required: true),
                    File = JsonRead.Str(o, "file", required: true),
                    Volume = (float)JsonRead.Num(o, "volume", 0),
                });
            }
            if (root.TryGetValue("durations", out var d) && d is Dictionary<string, object> durations)
                foreach (var kv in durations)
                    if (kv.Value is double ms) plan.Durations[kv.Key] = (long)ms;
            return plan;
        }
    }
}
