using System;
using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class RadioSchedulerTests
    {
        static readonly TimeZoneInfo JstZone = TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST");

        const string ProgramJson = @"{
  ""version"": 1,
  ""crossfadeSeconds"": 8,
  ""defaultWeather"": ""clear"",
  ""phases"": [
    { ""id"": ""morning"", ""start"": ""07:00"" },
    { ""id"": ""early_morning"", ""start"": ""04:00"" },
    { ""id"": ""day"", ""start"": ""11:00"" },
    { ""id"": ""evening"", ""start"": ""16:00"" },
    { ""id"": ""night"", ""start"": ""19:00"" },
    { ""id"": ""late_night"", ""start"": ""23:00"" }
  ],
  ""tracks"": [
    { ""phase"": ""early_morning"", ""weather"": ""clear"", ""file"": ""m/early_clear.ogg"" },
    { ""phase"": ""morning"", ""weather"": ""clear"", ""file"": ""m/morning_clear.ogg"" },
    { ""phase"": ""morning"", ""weather"": ""rain"", ""file"": ""m/morning_rain.ogg"", ""volume"": 0.8 },
    { ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_clear.ogg"" },
    { ""phase"": ""evening"", ""weather"": ""clear"", ""file"": ""m/evening_clear.ogg"" },
    { ""phase"": ""night"", ""weather"": ""clear"", ""file"": ""m/night_clear.ogg"" },
    { ""phase"": ""late_night"", ""weather"": ""clear"", ""file"": ""m/night_clear.ogg"" }
  ],
  ""chime"": { ""file"": ""fx/chime.ogg"", ""volume"": 0.6 },
  ""quiet"": {
    ""minGapSeconds"": 60,
    ""maxGapSeconds"": 240,
    ""phrases"": [ { ""file"": ""p/piano_01.ogg"" }, { ""file"": ""p/piano_02.ogg"", ""volume"": 0.7 } ]
  },
  ""ambient"": [
    { ""id"": ""fire"", ""file"": ""a/fire.ogg"", ""defaultVolume"": 0.8 },
    { ""id"": ""rain"", ""file"": ""a/rain.ogg"", ""defaultVolume"": 0.7, ""weathers"": [""rain""] }
  ]
}";

        static RadioProgram Program() => RadioProgram.Parse(ProgramJson);

        static DateTimeOffset Jst(int month, int day, int hour, int minute = 0) =>
            new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.FromHours(9));

        static RadioContext Ctx(DateTimeOffset now, string weather = WeatherIds.Clear, bool quiet = false, double hours = 48) =>
            new RadioContext { Now = now, TimeZone = JstZone, Weather = weather, QuietOverride = quiet, Horizon = TimeSpan.FromHours(hours) };

        [Test]
        public void Program_PhasesAreSortedByStart()
        {
            var p = Program();
            Assert.That(p.Phases.Select(x => x.Id), Is.EqualTo(new[] { "early_morning", "morning", "day", "evening", "night", "late_night" }));
        }

        static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

        static AudioIndex Durations(params (string file, long ms)[] items)
        {
            var index = new AudioIndex();
            foreach (var (file, ms) in items) index.DurationsMs[file] = ms;
            return index;
        }

        [Test]
        public void Phases_StartAtCurrentPhase_AndAreContiguous()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 15, 30)));
            Assert.That(plan.Phases[0].Phase, Is.EqualTo("day"));
            Assert.That(plan.Phases[0].StartMs, Is.EqualTo(Ms(Jst(11, 13, 11))));
            for (var i = 1; i < plan.Phases.Count; i++)
                Assert.That(plan.Phases[i].StartMs, Is.EqualTo(plan.Phases[i - 1].EndMs));
            Assert.That(plan.Phases.Last().EndMs, Is.GreaterThanOrEqualTo(plan.ValidUntilMs));
            Assert.That(plan.ValidUntilMs - plan.GeneratedAtMs, Is.EqualTo(48L * 3600 * 1000));
        }

        [Test]
        public void LateNightBeforeEarlyMorning_BelongsToPreviousDay()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 2)));
            Assert.That(plan.SpanAt(Ms(Jst(11, 13, 2))).Phase, Is.EqualTo("late_night"));
            Assert.That(plan.Phases[0].StartMs, Is.EqualTo(Ms(Jst(11, 12, 23))));
        }

        [Test]
        public void SeamlessFallback_OneLoopingClipPerPhase_WithCrossfadeAtBoundary()
        {
            // 長さが分からない曲は継ぎ目ループとして扱う。
            var now = Jst(11, 13, 15, 30);
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(now));

            var current = plan.ClipsAt(Ms(now));
            Assert.That(current.Count, Is.EqualTo(1));
            var day = current[0];
            Assert.That(day.File, Is.EqualTo("m/day_clear.ogg"));
            Assert.That(day.Loop, Is.True);
            Assert.That(day.StartMs, Is.EqualTo(Ms(Jst(11, 13, 11))), "開始は時間帯の頭(今ではない)");
            Assert.That(day.EndMs, Is.EqualTo(Ms(Jst(11, 13, 16)) + 8000), "次の時間帯と 8 秒重なる");

            var atBoundary = plan.ClipsAt(Ms(Jst(11, 13, 16)) + 4000).Select(c => c.File).ToArray();
            Assert.That(atBoundary, Is.EquivalentTo(new[] { "m/day_clear.ogg", "m/evening_clear.ogg" }));
            var evening = plan.Music.First(c => c.File == "m/evening_clear.ogg");
            Assert.That(evening.StartMs, Is.EqualTo(Ms(Jst(11, 13, 16))));
            Assert.That(evening.FadeInMs, Is.EqualTo(0), "時間帯の頭はフェードインしない(イントロの張りを残す)");
            Assert.That(day.FadeOutMs, Is.EqualTo(8000), "前の曲は重なりの間にフェードアウトする");
        }

        [Test]
        public void CrossfadeTracks_FirstClipOfPhase_StartsWithoutFadeIn()
        {
            var now = Jst(11, 13, 15, 59);
            var ctx = Ctx(now);
            ctx.Audio = Durations(("m/day_clear.ogg", 180_000), ("m/evening_clear.ogg", 180_000));
            var plan = RadioScheduler.BuildPlan(Program(), ctx);

            var evening = plan.Music.Where(c => c.File == "m/evening_clear.ogg").OrderBy(c => c.StartMs).ToList();
            Assert.That(evening[0].StartMs, Is.EqualTo(Ms(Jst(11, 13, 16))));
            Assert.That(evening[0].FadeInMs, Is.EqualTo(0), "時間帯の頭はイントロをそのまま鳴らす");
            Assert.That(evening[1].FadeInMs, Is.EqualTo(8000), "同じ曲の繰り返しの継ぎ目はクロスフェードのまま");

            var lastDay = plan.Music.Where(c => c.File == "m/day_clear.ogg" && c.StartMs < Ms(Jst(11, 13, 16))).OrderBy(c => c.StartMs).Last();
            Assert.That(lastDay.EndMs, Is.EqualTo(Ms(Jst(11, 13, 16)) + 8000));
            Assert.That(lastDay.FadeOutMs, Is.EqualTo(8000), "前の時間帯の曲はフェードアウトする");
        }

        [Test]
        public void CrossfadeTracks_RepeatWithOverlap_AndPositionsAreDeterministic()
        {
            var now = Jst(11, 13, 15, 30);
            var ctx = Ctx(now);
            ctx.Audio = Durations(("m/day_clear.ogg", 180_000));
            var plan = RadioScheduler.BuildPlan(Program(), ctx);

            var dayClips = plan.Music.Where(c => c.File == "m/day_clear.ogg" && c.StartMs < Ms(Jst(11, 13, 16))).ToList();
            Assert.That(dayClips.Count, Is.GreaterThan(2));
            Assert.That(dayClips.All(c => !c.Loop));
            for (var i = 1; i < dayClips.Count; i++)
            {
                Assert.That(dayClips[i].StartMs - dayClips[i - 1].StartMs, Is.EqualTo(172_000), "180 秒の曲を 8 秒重ねて繰り返す");
                Assert.That(dayClips[i - 1].EndMs - dayClips[i].StartMs, Is.EqualTo(8000));
            }
            // 今鳴っているクリップの再生位置は 0〜曲の長さ。
            var playing = plan.ClipsAt(Ms(now)).First();
            Assert.That(playing.PositionAt(Ms(now), 180_000), Is.InRange(0, 180_000));
            Assert.That(plan.Durations["m/day_clear.ogg"], Is.EqualTo(180_000));
            // 終わった回は予定表に入らない。
            Assert.That(dayClips.All(c => c.EndMs > Ms(now)));
            // 最後の回は次の時間帯の頭とクロスフェードして終わる。
            Assert.That(dayClips.Last().EndMs, Is.LessThanOrEqualTo(Ms(Jst(11, 13, 16)) + 8000));
        }

        [Test]
        public void Rebuilding_KeepsTheSameClipIdsAndTimes()
        {
            var program = Program();
            var a = Ctx(Jst(11, 13, 15, 30));
            a.Audio = Durations(("m/day_clear.ogg", 180_000));
            var b = Ctx(Jst(11, 13, 15, 47));
            b.Audio = a.Audio;
            var planA = RadioScheduler.BuildPlan(program, a);
            var planB = RadioScheduler.BuildPlan(program, b);

            var t = Ms(Jst(11, 13, 15, 50));
            var clipsA = planA.ClipsAt(t).Select(c => c.Id + "/" + c.PositionAt(t, 180_000));
            var clipsB = planB.ClipsAt(t).Select(c => c.Id + "/" + c.PositionAt(t, 180_000));
            Assert.That(clipsB, Is.EqualTo(clipsA));
        }

        [Test]
        public void GapSeconds_LeavesSilenceBetweenRepeats()
        {
            var json = ProgramJson.Replace(
                @"{ ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_clear.ogg"" }",
                @"{ ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_clear.ogg"", ""gapSeconds"": 20 }");
            var ctx = Ctx(Jst(11, 13, 12));
            ctx.Audio = Durations(("m/day_clear.ogg", 100_000));
            var plan = RadioScheduler.BuildPlan(RadioProgram.Parse(json), ctx);
            var dayClips = plan.Music.Where(c => c.File == "m/day_clear.ogg" && c.StartMs < Ms(Jst(11, 13, 16))).ToList();
            Assert.That(dayClips[1].StartMs - dayClips[0].EndMs, Is.EqualTo(20_000));
            Assert.That(dayClips[1].FadeInMs, Is.EqualTo(0), "間を空けるときは曲のイントロをそのまま使う");
        }

        [Test]
        public void SameTrackInConsecutivePhases_IsOneRun()
        {
            // night (19:00) と late_night (23:00) は同じ曲なので 23:00 で切れない。
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 20)));
            var night = plan.ClipsAt(Ms(Jst(11, 13, 20))).Single();
            Assert.That(night.File, Is.EqualTo("m/night_clear.ogg"));
            Assert.That(night.EndMs, Is.EqualTo(Ms(Jst(11, 14, 4)) + 8000));
        }

        [Test]
        public void Rain_UsesRainTrack_WhenAvailable_ElseFallsBackToClear()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 8), WeatherIds.Rain));
            var morning = plan.ClipsAt(Ms(Jst(11, 13, 8))).Single();
            Assert.That(morning.File, Is.EqualTo("m/morning_rain.ogg"));
            Assert.That(morning.Volume, Is.EqualTo(0.8f));
            Assert.That(plan.ClipsAt(Ms(Jst(11, 13, 12))).Single().File, Is.EqualTo("m/day_clear.ogg"), "day の雨曲はないので晴れ曲");
        }

        [Test]
        public void Chimes_AreOnEveryLocalHour_InThemeMode()
        {
            var now = Jst(11, 13, 15, 30);
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(now, hours: 5));
            var chimes = plan.OneShots.Where(s => s.Kind == OneShotKinds.Chime).Select(s => s.AtMs).ToArray();
            var expected = new[] { 16, 17, 18, 19, 20 }.Select(h => Jst(11, 13, h).ToUnixTimeMilliseconds()).ToArray();
            Assert.That(chimes, Is.EqualTo(expected));
        }

        [Test]
        public void QuietOverride_SilencesMusic_AndSchedulesPhrases_WithoutChimes()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 22), quiet: true, hours: 3));
            Assert.That(plan.Music, Is.Empty);
            Assert.That(plan.Phases.All(p => p.Mode == RadioMode.Quiet));
            Assert.That(plan.OneShots.Any(s => s.Kind == OneShotKinds.Chime), Is.False);

            var phrases = plan.OneShots.Where(s => s.Kind == OneShotKinds.Phrase).ToList();
            Assert.That(phrases.Count, Is.GreaterThan(0));
            for (var i = 1; i < phrases.Count; i++)
            {
                var gap = phrases[i].AtMs - phrases[i - 1].AtMs;
                Assert.That(gap, Is.GreaterThanOrEqualTo(60_000L));
                // 日付が変わるところ(0:00)以外は間隔が maxGap 以内で、同じフレーズが続かない。
                var crossesMidnight = phrases[i - 1].AtMs < Jst(11, 14, 0).ToUnixTimeMilliseconds()
                                      && phrases[i].AtMs >= Jst(11, 14, 0).ToUnixTimeMilliseconds();
                if (crossesMidnight) continue;
                Assert.That(gap, Is.LessThanOrEqualTo(240_000L));
                Assert.That(phrases[i].File, Is.Not.EqualTo(phrases[i - 1].File), "同じフレーズを続けない");
            }
            Assert.That(phrases.All(s => s.AtMs > plan.GeneratedAtMs && s.AtMs <= plan.ValidUntilMs));
        }

        [Test]
        public void QuietPhrases_AreStable_WhenPlanIsRebuiltLater()
        {
            var program = Program();
            var a = RadioScheduler.BuildPlan(program, Ctx(Jst(11, 13, 22), quiet: true, hours: 4));
            var b = RadioScheduler.BuildPlan(program, Ctx(Jst(11, 13, 22, 40), quiet: true, hours: 4));
            var laterInA = a.OneShots.Where(s => s.AtMs > b.GeneratedAtMs && s.AtMs <= Jst(11, 14, 2).ToUnixTimeMilliseconds()).Select(s => s.AtMs);
            var inB = b.OneShots.Where(s => s.AtMs <= Jst(11, 14, 2).ToUnixTimeMilliseconds()).Select(s => s.AtMs);
            Assert.That(inB, Is.EqualTo(laterInA));
        }

        [Test]
        public void SamePhaseWithMultipleTracks_RotatesByDay()
        {
            var json = ProgramJson.Replace(
                @"{ ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_clear.ogg"" },",
                @"{ ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_a.ogg"" }, { ""phase"": ""day"", ""weather"": ""clear"", ""file"": ""m/day_b.ogg"" },");
            var plan = RadioScheduler.BuildPlan(RadioProgram.Parse(json), Ctx(Jst(11, 13, 12)));
            var today = plan.ClipsAt(Ms(Jst(11, 13, 12))).Single().File;
            var tomorrow = plan.ClipsAt(Ms(Jst(11, 14, 12))).Single().File;
            Assert.That(new[] { today, tomorrow }, Is.EquivalentTo(new[] { "m/day_a.ogg", "m/day_b.ogg" }));
        }

        [Test]
        public void Ambient_RainLayerOnlyWhenRaining()
        {
            var clear = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 12)));
            var rain = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 12), WeatherIds.Rain));
            Assert.That(clear.Ambient.Single(a => a.Id == "rain").Volume, Is.EqualTo(0f));
            Assert.That(rain.Ambient.Single(a => a.Id == "rain").Volume, Is.EqualTo(0.7f).Within(1e-6));
            Assert.That(clear.Ambient.Single(a => a.Id == "fire").Volume, Is.EqualTo(0.8f).Within(1e-6));
        }

        [Test]
        public void Plan_RoundTripsThroughJson()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 22), quiet: false));
            plan.ClockOffsetMs = 123456;
            var back = PlaybackPlan.FromJson(plan.ToJson());
            Assert.That(back.ToJson(), Is.EqualTo(plan.ToJson()));
            Assert.That(back.Music.Count, Is.EqualTo(plan.Music.Count));
            Assert.That(back.ClockOffsetMs, Is.EqualTo(123456));
        }

        [Test]
        public void ClipFades_AreEqualPower()
        {
            var clip = new MusicClip { StartMs = 0, EndMs = 100_000, FadeInMs = 10_000, FadeOutMs = 10_000 };
            Assert.That(clip.FadeGainAt(0), Is.EqualTo(0f).Within(1e-6));
            Assert.That(clip.FadeGainAt(5_000), Is.EqualTo((float)Math.Sqrt(0.5)).Within(1e-4));
            Assert.That(clip.FadeGainAt(50_000), Is.EqualTo(1f));
            Assert.That(clip.FadeGainAt(95_000), Is.EqualTo((float)Math.Sqrt(0.5)).Within(1e-4));
            Assert.That(clip.FadeGainAt(100_000), Is.EqualTo(0f));
        }

        [Test]
        public void ExpiredPlan_RepeatsTheLastDay()
        {
            var plan = RadioScheduler.BuildPlan(Program(), Ctx(Jst(11, 13, 15)));
            Assert.That(plan.ToPlanTime(Ms(Jst(11, 14, 15))), Is.EqualTo(Ms(Jst(11, 14, 15))));
            // 予定表の終わり(11/15 15:00)の 3 時間後は、最後の 1 日の同じ時刻に折り返す。
            Assert.That(plan.ToPlanTime(Ms(Jst(11, 15, 18))), Is.EqualTo(Ms(Jst(11, 14, 18))));
        }

        [Test]
        public void TimezoneWithDst_SkipsNonexistentLocalHour()
        {
            // 夏時間の切り替えがあるタイムゾーンでも区間が途切れないこと。
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1), new DateTime(2100, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 10, 5, DayOfWeek.Sunday));
            var tz = TimeZoneInfo.CreateCustomTimeZone("DST", TimeSpan.Zero, "DST", "STD", "DST", new[] { rule });
            var now = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);
            var plan = RadioScheduler.BuildPlan(Program(), new RadioContext { Now = now, TimeZone = tz, Horizon = TimeSpan.FromHours(24) });
            for (var i = 1; i < plan.Phases.Count; i++)
            {
                Assert.That(plan.Phases[i].StartMs, Is.EqualTo(plan.Phases[i - 1].EndMs));
                Assert.That(plan.Phases[i].StartMs, Is.GreaterThan(plan.Phases[i - 1].StartMs));
            }
        }

        [Test]
        public void InvalidProgram_IsRejected()
        {
            Assert.Throws<FormatException>(() => RadioProgram.Parse(@"{ ""phases"": [] }"));
            Assert.Throws<FormatException>(() => RadioProgram.Parse(@"{ ""phases"": [ { ""id"": ""a"", ""start"": ""25:00"" } ] }"));
            Assert.Throws<FormatException>(() => RadioProgram.Parse(
                @"{ ""phases"": [ { ""id"": ""a"", ""start"": ""01:00"" } ], ""tracks"": [ { ""phase"": ""zzz"", ""file"": ""x"" } ] }"));
        }
    }
}
