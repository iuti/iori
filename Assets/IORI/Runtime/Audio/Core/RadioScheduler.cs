using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>予定表を作るときの状況。</summary>
    public sealed class RadioContext
    {
        /// <summary>予定表の基準になる現在時刻(動作確認で時計をずらしているときは、ずらした後の時刻)。</summary>
        public DateTimeOffset Now;
        /// <summary>時間帯の判定に使うタイムゾーン。端末のローカル時刻なら TimeZoneInfo.Local。</summary>
        public TimeZoneInfo TimeZone = TimeZoneInfo.Local;
        public string Weather = WeatherIds.Clear;
        /// <summary>true なら時間帯に関係なく静けさの時間にする(寝る前など)。</summary>
        public bool QuietOverride;
        /// <summary>
        /// true なら音楽プレーヤーが自分の曲を鳴らしている: ラジオは黙る(曲、正時のチャイム、静けさのフレーズを予定表に入れない)。
        /// 環境音は鳴り続け、時間帯の並びも変わらない。
        /// </summary>
        public bool PlayerOn;
        /// <summary>何時間先まで予定表を作るか。アプリが裏にいる間はこの範囲を鳴らす。24 時間以上にする。</summary>
        public TimeSpan Horizon = TimeSpan.FromHours(48);
        public long ClockOffsetMs;
        public float MusicVolume = 1f;
        public float OneShotVolume = 1f;
        /// <summary>曲の長さ。無いと Crossfade の曲も継ぎ目ループとして扱う。</summary>
        public AudioIndex Audio = new AudioIndex();
    }

    /// <summary>
    /// 選曲の頭脳。番組表(RadioProgram)と状況(RadioContext)から予定表(PlaybackPlan)を作る。
    /// 同じ入力からは必ず同じ予定表ができる(テストできるように、乱数も時刻から決める)。
    /// クリップの開始時刻は時間帯の切り替わりから決まり、現在時刻には左右されないので、
    /// 作り直しても鳴っている曲は同じ Id・同じ再生位置のまま続く。
    /// </summary>
    public static class RadioScheduler
    {
        public static PlaybackPlan BuildPlan(RadioProgram program, RadioContext ctx, AmbientMixer mixer = null)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Horizon <= TimeSpan.Zero) throw new ArgumentException("Horizon は正の値です");

            var nowMs = ctx.Now.ToUnixTimeMilliseconds();
            var endMs = nowMs + (long)ctx.Horizon.TotalMilliseconds;
            var weather = string.IsNullOrEmpty(ctx.Weather) ? program.DefaultWeather : ctx.Weather;
            var audio = ctx.Audio ?? new AudioIndex();

            var plan = new PlaybackPlan
            {
                GeneratedAtMs = nowMs,
                ValidUntilMs = endMs,
                ClockOffsetMs = ctx.ClockOffsetMs,
                Weather = weather,
                MusicVolume = ctx.MusicVolume,
                OneShotVolume = ctx.OneShotVolume,
            };

            // 1. 現在の時間帯から horizon の終わりまでの時間帯の区間。
            var boundaries = PhaseBoundaries(program, ctx.TimeZone, nowMs, endMs);
            var first = boundaries.FindLastIndex(b => b.UtcMs <= nowMs);
            var runs = new List<Run>();
            for (var i = first; i < boundaries.Count - 1 && boundaries[i].UtcMs < endMs; i++)
            {
                var b = boundaries[i];
                var span = new PhaseSpan
                {
                    StartMs = b.UtcMs,
                    EndMs = boundaries[i + 1].UtcMs,
                    Phase = b.Phase.Id,
                    Mode = ctx.QuietOverride ? RadioMode.Quiet : b.Phase.Mode,
                };
                plan.Phases.Add(span);

                var track = span.Mode == RadioMode.Theme && !ctx.PlayerOn ? program.FindTrack(b.Phase.Id, weather, b.DayIndex) : null;
                // 同じ曲が続く時間帯はつなげて、無駄なクロスフェードをしない。
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && last.EndMs == span.StartMs && SameTrack(last.Track, track))
                    last.EndMs = span.EndMs;
                else
                    runs.Add(new Run { StartMs = span.StartMs, EndMs = span.EndMs, Track = track, Phase = span.Phase });
            }

            // 2. 曲のクリップ。
            var crossfadeMs = (int)Math.Round(program.CrossfadeSeconds * 1000);
            var introFadeMs = (int)Math.Round(program.IntroFadeSeconds * 1000);
            foreach (var run in runs)
            {
                if (run.Track == null) continue;
                AddClips(plan, run, audio.DurationOf(run.Track.File), crossfadeMs, introFadeMs, nowMs, endMs);
                var d = audio.DurationOf(run.Track.File);
                if (d > 0) plan.Durations[run.Track.File] = d;
            }
            plan.Music.Sort((a, c) => a.StartMs != c.StartMs ? a.StartMs.CompareTo(c.StartMs) : string.CompareOrdinal(a.Id, c.Id));

            // 3. 単発音(音楽プレーヤーが鳴っている間は鳴らさない)。
            if (!ctx.PlayerOn)
            {
                AddQuietPhrases(program, plan, ctx.TimeZone, nowMs, endMs);
                if (program.Chime != null) AddChimes(program, plan, ctx.TimeZone, nowMs, endMs);
            }
            plan.OneShots.Sort((a, c) => a.AtMs.CompareTo(c.AtMs));

            // 4. 環境音。
            plan.Ambient = (mixer ?? new AmbientMixer(program.Ambient)).BuildStates(weather);
            return plan;
        }

        sealed class Run
        {
            public long StartMs;
            public long EndMs;
            public TrackDef Track;
            public string Phase;
        }

        static bool SameTrack(TrackDef a, TrackDef b)
        {
            if (a == null || b == null) return a == b;
            return a.File == b.File && a.Loop == b.Loop && Math.Abs(a.Volume - b.Volume) < 1e-4f
                   && Math.Abs(a.GapSeconds - b.GapSeconds) < 1e-6;
        }

        /// <summary>
        /// [run.StartMs, run.EndMs) の間、曲を鳴らすクリップを並べる。最後のクリップは次の時間帯の頭と
        /// 重なるため run.EndMs + crossfade まで伸び、そこでフェードアウトする。
        /// 時間帯の頭(各 run の最初の回)は introFadeMs(既定 0)で入る。イントロの張りをそのまま聞かせるため。
        /// </summary>
        static void AddClips(PlaybackPlan plan, Run run, long durationMs, int crossfadeMs, int introFadeMs, long nowMs, long endMs)
        {
            var t = run.Track;
            var x = crossfadeMs;
            var runEndWithTail = run.EndMs + x;

            if (t.Loop == TrackLoopMode.Seamless || durationMs <= 2L * x)
            {
                AddIfVisible(plan, new MusicClip
                {
                    StartMs = run.StartMs,
                    EndMs = runEndWithTail,
                    File = t.File,
                    Volume = t.Volume,
                    FadeInMs = introFadeMs,
                    FadeOutMs = x,
                    Loop = true,
                    Phase = run.Phase,
                }, nowMs, endMs);
                return;
            }

            // 曲を最後まで流し、終わり際に次の回と重ねる(gapSeconds があれば重ねずに間を空ける)。
            var gapMs = (long)Math.Round(t.GapSeconds * 1000);
            var overlap = gapMs > 0 ? 0 : x;
            var period = durationMs - overlap + gapMs;
            // 今より前に終わった回は飛ばす。
            var k = Math.Max(0, (nowMs - run.StartMs - durationMs) / period);
            for (; ; k++)
            {
                var start = run.StartMs + k * period;
                if (start >= run.EndMs || start >= endMs) break;
                // 時間帯の終わり間際に始まる回は、ほとんど鳴らずに切れるので始めない。
                if (k > 0 && run.EndMs - start < 2L * x) break;
                var naturalEnd = start + durationMs;
                var end = Math.Min(naturalEnd, runEndWithTail);
                AddIfVisible(plan, new MusicClip
                {
                    StartMs = start,
                    EndMs = end,
                    File = t.File,
                    Volume = t.Volume,
                    // 同じ曲の繰り返しで重ねるときだけクロスフェードで入る(時間帯の頭は introFadeMs)。
                    FadeInMs = k == 0 ? introFadeMs : overlap > 0 ? x : 0,
                    FadeOutMs = end < naturalEnd || overlap > 0 ? x : 0,
                    Loop = false,
                    Phase = run.Phase,
                }, nowMs, endMs);
            }
        }

        static void AddIfVisible(PlaybackPlan plan, MusicClip clip, long nowMs, long endMs)
        {
            if (clip.EndMs <= nowMs || clip.StartMs >= endMs) return;
            clip.Id = clip.StartMs + ":" + clip.File;
            plan.Music.Add(clip);
        }

        /// <summary>
        /// 静けさの時間に鳴らすフレーズの時刻を決める。乱数は現地の日付ごとに 0:00 から決めるので、
        /// 予定表を作り直しても、時間帯をまたいでも、フレーズの時刻は変わらない。
        /// </summary>
        static void AddQuietPhrases(RadioProgram program, PlaybackPlan plan, TimeZoneInfo tz, long nowMs, long endMs)
        {
            var phrases = program.Quiet.Phrases;
            if (phrases.Count == 0 || !plan.Phases.Exists(p => p.Mode == RadioMode.Quiet)) return;
            var minMs = program.Quiet.MinGapSeconds * 1000;
            var maxMs = program.Quiet.MaxGapSeconds * 1000;
            var lastAdded = nowMs - (long)minMs;

            var day = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), tz).DateTime.Date;
            while (true)
            {
                var dayStart = LocalToUtcMs(day, tz);
                if (dayStart > endMs) break;
                var dayEnd = LocalToUtcMs(day.AddDays(1), tz);
                var rng = new SplitMix64((ulong)dayStart ^ 0x9E3779B97F4A7C15UL);
                var previous = -1;
                var t = dayStart;
                while (true)
                {
                    t += (long)(minMs + rng.NextDouble() * (maxMs - minMs));
                    if (t >= dayEnd || t > endMs) break;
                    var pick = rng.NextInt(phrases.Count);
                    if (phrases.Count > 1 && pick == previous) pick = (pick + 1) % phrases.Count;
                    previous = pick;
                    if (t <= nowMs || t - lastAdded < minMs) continue;
                    var span = plan.SpanAt(t);
                    if (span == null || span.Mode != RadioMode.Quiet) continue;
                    plan.OneShots.Add(new ScheduledOneShot
                    {
                        AtMs = t,
                        File = phrases[pick].File,
                        Volume = phrases[pick].Volume,
                        Kind = OneShotKinds.Phrase,
                    });
                    lastAdded = t;
                }
                day = day.AddDays(1);
            }
        }

        static void AddChimes(RadioProgram program, PlaybackPlan plan, TimeZoneInfo tz, long nowMs, long endMs)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), tz).DateTime;
            var hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
            while (true)
            {
                if (!tz.IsInvalidTime(hour))
                {
                    var at = LocalToUtcMs(hour, tz);
                    if (at > endMs) break;
                    var span = plan.SpanAt(at);
                    if (at > nowMs && span != null && span.Mode == RadioMode.Theme)
                        plan.OneShots.Add(new ScheduledOneShot
                        {
                            AtMs = at,
                            File = program.Chime.File,
                            Volume = program.Chime.Volume,
                            Kind = OneShotKinds.Chime,
                        });
                }
                hour = hour.AddHours(1);
            }
        }

        internal struct Boundary
        {
            public long UtcMs;
            public PhaseDef Phase;
            /// <summary>その時間帯が始まった現地の日付(日替わりの曲選びに使う)。</summary>
            public long DayIndex;
        }

        /// <summary>[now の少し前, end の少し後] の時間帯の切り替わり時刻を、時刻順に並べる。</summary>
        internal static List<Boundary> PhaseBoundaries(RadioProgram program, TimeZoneInfo tz, long nowMs, long endMs)
        {
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), tz).DateTime.Date;
            var days = (int)Math.Ceiling((endMs - nowMs) / 86400000.0) + 2;
            var result = new List<Boundary>();
            for (var d = -1; d <= days; d++)
            {
                var date = localNow.AddDays(d);
                foreach (var ph in program.Phases)
                {
                    var local = DateTime.SpecifyKind(date.AddMinutes(ph.StartMinute), DateTimeKind.Unspecified);
                    result.Add(new Boundary
                    {
                        UtcMs = LocalToUtcMs(local, tz),
                        Phase = ph,
                        DayIndex = (long)(date - DateTime.MinValue).TotalDays,
                    });
                }
            }
            result.Sort((a, b) => a.UtcMs.CompareTo(b.UtcMs));
            return result;
        }

        /// <summary>現地時刻を UTC ミリ秒にする。夏時間で存在しない時刻は 1 時間後ろにずらす。</summary>
        internal static long LocalToUtcMs(DateTime local, TimeZoneInfo tz)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (tz.IsInvalidTime(local)) local = local.AddHours(1);
            var offset = tz.GetUtcOffset(local);
            return new DateTimeOffset(local, offset).ToUnixTimeMilliseconds();
        }
    }

    /// <summary>実行環境によらず同じ列を返す乱数(System.Random は環境で実装が違うことがある)。</summary>
    internal struct SplitMix64
    {
        ulong _state;

        public SplitMix64(ulong seed) { _state = seed; }

        public ulong Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

        public int NextInt(int maxExclusive) => (int)(Next() % (ulong)maxExclusive);
    }
}
