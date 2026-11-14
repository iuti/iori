using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class VoiceTests
    {
        static readonly TimeZoneInfo JstZone = TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST");

        const string BookJson = @"{
  ""minGapSeconds"": 5,
  ""scheduledMarginSeconds"": 6,
  ""returnAfterMinutes"": 120,
  ""cues"": {
    ""greet"": { ""lines"": [
      { ""file"": ""voice/greet_morning.ogg"", ""phase"": ""morning"" },
      { ""file"": ""voice/greet_night.ogg"", ""phase"": [""night"", ""late_night""] }
    ] },
    ""return"": { ""lines"": [
      { ""file"": ""voice/okaeri.ogg"", ""hours"": ""5-22"" },
      { ""file"": ""voice/okaeri_late.ogg"", ""hours"": ""22-5"", ""volume"": 0.8 }
    ] },
    ""tap"": { ""cooldownSeconds"": 10, ""lines"": [ ""voice/tap_01.ogg"", ""voice/tap_02.ogg"", ""voice/tap_03.ogg"" ] },
    ""maybe"": { ""chance"": 0.5, ""lines"": [ ""voice/maybe.ogg"" ] }
  }
}";

        static VoiceBook Book() => VoiceBook.Parse(BookJson);

        static VoiceMoment At(long ms, string phase = "day", int hour = 12, long nextVoice = long.MaxValue) =>
            new VoiceMoment { NowMs = ms, Phase = phase, LocalHour = hour, NextScheduledVoiceMs = nextVoice };

        [Test]
        public void Book_ReadsCuesLinesAndConditions()
        {
            var b = Book();
            Assert.That(b.MinGapSeconds, Is.EqualTo(5));
            Assert.That(b.Find("greet").Lines[1].Phases, Is.EqualTo(new[] { "night", "late_night" }));
            Assert.That(b.Find("return").Lines[1].Volume, Is.EqualTo(0.8f));
            Assert.That(b.Find("tap").Lines.Select(l => l.File), Is.EqualTo(new[] { "voice/tap_01.ogg", "voice/tap_02.ogg", "voice/tap_03.ogg" }));
            Assert.That(b.Files().Count, Is.EqualTo(8));
            Assert.That(b.Find("nothing"), Is.Null);
        }

        [Test]
        public void Book_RejectsBadData()
        {
            Assert.Throws<FormatException>(() => VoiceBook.Parse(@"{ ""cues"": { ""a"": { ""lines"": [] } } }"));
            Assert.Throws<FormatException>(() => VoiceBook.Parse(@"{ ""cues"": { ""a"": { ""chance"": 2, ""lines"": [""x.ogg""] } } }"));
            Assert.Throws<FormatException>(() => VoiceBook.Parse(@"{ ""cues"": { ""a"": { ""lines"": [{ ""file"": ""x.ogg"", ""hours"": ""5"" }] } } }"));
            Assert.Throws<FormatException>(() => VoiceBook.Parse(@"{ ""cues"": { ""a"": { ""lines"": [{ ""file"": ""x.ogg"", ""hours"": ""3-3"" }] } } }"));
        }

        [Test]
        public void HourRange_WrapsPastMidnight()
        {
            var late = HourRange.Parse("22-5");
            Assert.That(new[] { 22, 23, 0, 4 }.All(late.Contains));
            Assert.That(new[] { 5, 12, 21 }.Any(late.Contains), Is.False);
            var day = HourRange.Parse("5-22");
            Assert.That(day.Contains(5) && day.Contains(21) && !day.Contains(22) && !day.Contains(4));
            Assert.That(HourRange.Parse("18-24").Contains(23) && !HourRange.Parse("18-24").Contains(0));
        }

        [Test]
        public void Greet_PicksTheLineOfThePhase()
        {
            var p = new VoicePicker(Book(), 1);
            Assert.That(p.Pick(VoiceCues.Greet, At(0, "morning", 8)).File, Is.EqualTo("voice/greet_morning.ogg"));
            var q = new VoicePicker(Book(), 1);
            Assert.That(q.Pick(VoiceCues.Greet, At(0, "late_night", 1)).File, Is.EqualTo("voice/greet_night.ogg"));
            Assert.That(new VoicePicker(Book(), 1).Pick(VoiceCues.Greet, At(0, "day", 12)), Is.Null, "昼のあいさつが無ければ黙る");
        }

        [Test]
        public void Return_PicksByHour_AndArrivalDecidesTheCue()
        {
            Assert.That(new VoicePicker(Book(), 1).Pick(VoiceCues.Return, At(0, hour: 23)).File, Is.EqualTo("voice/okaeri_late.ogg"));
            Assert.That(new VoicePicker(Book(), 1).Pick(VoiceCues.Return, At(0, hour: 18)).File, Is.EqualTo("voice/okaeri.ogg"));
            var p = new VoicePicker(Book(), 1);
            Assert.That(p.CueForArrival(-1), Is.EqualTo(VoiceCues.Greet));
            Assert.That(p.CueForArrival(119 * 60_000L), Is.Null);
            Assert.That(p.CueForArrival(120 * 60_000L), Is.EqualTo(VoiceCues.Return));
        }

        [Test]
        public void Lines_DoNotOverlap_AndKeepTheGap()
        {
            var p = new VoicePicker(Book(), 7);
            Assert.That(p.Pick(VoiceCues.Greet, At(0, "morning")), Is.Not.Null);
            p.NoteSpoken(2_000);   // 2 秒の声
            Assert.That(p.Pick(VoiceCues.Tap, At(1_000)), Is.Null, "話している間は黙る");
            Assert.That(p.Pick(VoiceCues.Tap, At(6_999)), Is.Null, "終わってから 5 秒は黙る");
            Assert.That(p.Pick(VoiceCues.Tap, At(7_000)), Is.Not.Null);
        }

        [Test]
        public void Cooldown_IsPerCue_AndTheSameLineDoesNotRepeat()
        {
            var p = new VoicePicker(Book(), 3);
            var first = p.Pick(VoiceCues.Tap, At(0));
            Assert.That(first, Is.Not.Null);
            Assert.That(p.Pick(VoiceCues.Tap, At(9_000)), Is.Null, "タップは 10 秒あける");
            var last = first.File;
            for (var i = 1; i <= 30; i++)
            {
                var line = p.Pick(VoiceCues.Tap, At(i * 10_000L));
                Assert.That(line, Is.Not.Null);
                Assert.That(line.File, Is.Not.EqualTo(last), "同じセリフを続けない");
                last = line.File;
            }
        }

        [Test]
        public void ScheduledVoice_NearBy_KeepsHerQuiet()
        {
            var p = new VoicePicker(Book(), 1);
            Assert.That(p.Pick(VoiceCues.Tap, At(100_000, nextVoice: 105_000)), Is.Null, "時報の 5 秒前");
            Assert.That(p.Pick(VoiceCues.Tap, At(100_000, nextVoice: 96_000)), Is.Null, "時報の 4 秒後");
            Assert.That(p.Pick(VoiceCues.Tap, At(100_000, nextVoice: 107_000)), Is.Not.Null);
        }

        [Test]
        public void Chance_SometimesSilent_ButForceAlwaysSpeaks()
        {
            var p = new VoicePicker(Book(), 11);
            var spoken = Enumerable.Range(0, 200).Count(i => p.Pick("maybe", At(i * 60_000L)) != null);
            Assert.That(spoken, Is.InRange(60, 140));
            var f = new VoicePicker(Book(), 11);
            f.NoteSpoken(1_000_000);
            Assert.That(Enumerable.Range(0, 20).All(i => f.Pick("maybe", At(i), force: true) != null));
        }

        [Test]
        public void Picker_IsDeterministicForASeed()
        {
            string[] Run(ulong seed)
            {
                var p = new VoicePicker(Book(), seed);
                return Enumerable.Range(0, 20).Select(i => p.Pick(VoiceCues.Tap, At(i * 20_000L))?.File).ToArray();
            }
            Assert.That(Run(42), Is.EqualTo(Run(42)));
        }

        // ---- 時報(予定表)

        const string ProgramJson = @"{
  ""phases"": [
    { ""id"": ""day"", ""start"": ""07:00"" },
    { ""id"": ""night"", ""start"": ""22:00"", ""mode"": ""quiet"" }
  ],
  ""tracks"": [ { ""phase"": ""day"", ""file"": ""m/day.ogg"", ""loop"": ""seamless"" } ],
  ""chime"": { ""file"": ""fx/chime.ogg"", ""volume"": 0.5 },
  ""jiho"": { ""file"": ""voice/jiho_{hh}.ogg"", ""delaySeconds"": 1.5, ""volume"": 0.9 }
}";

        static DateTimeOffset Jst(int day, int hour, int minute = 0, int second = 0) =>
            new DateTimeOffset(2026, 11, day, hour, minute, second, TimeSpan.FromHours(9));

        static RadioContext Ctx(DateTimeOffset now, double hours) =>
            new RadioContext { Now = now, TimeZone = JstZone, Horizon = TimeSpan.FromHours(hours) };

        [Test]
        public void Jiho_FollowsEachChime_InThemeHoursOnly()
        {
            var plan = RadioScheduler.BuildPlan(RadioProgram.Parse(ProgramJson), Ctx(Jst(14, 19, 30), 5));
            var chimes = plan.OneShots.Where(s => s.Kind == OneShotKinds.Chime).Select(s => s.AtMs).ToArray();
            var voices = plan.OneShots.Where(s => s.Kind == OneShotKinds.Voice).ToArray();
            Assert.That(chimes, Is.EqualTo(new[] { 20, 21 }.Select(h => Jst(14, h).ToUnixTimeMilliseconds())), "22 時からは静けさの時間");
            Assert.That(voices.Select(v => v.AtMs), Is.EqualTo(chimes.Select(c => c + 1500)));
            Assert.That(voices.Select(v => v.File), Is.EqualTo(new[] { "voice/jiho_20.ogg", "voice/jiho_21.ogg" }));
            Assert.That(voices.All(v => v.Volume == 0.9f));
            // 時刻順に並んでいる(再生係は前から順に見る)
            Assert.That(plan.OneShots.Select(s => s.AtMs), Is.Ordered);
        }

        [Test]
        public void Jiho_RightAfterTheHour_IsStillAhead()
        {
            // 20:00:00.5 に作り直した予定表: チャイムは過ぎたが、20 時の声はまだ先
            var now = Jst(14, 20).AddMilliseconds(500);
            var plan = RadioScheduler.BuildPlan(RadioProgram.Parse(ProgramJson), Ctx(now, 1.5));
            var first = plan.OneShots.First();
            Assert.That(first.Kind, Is.EqualTo(OneShotKinds.Voice));
            Assert.That(first.File, Is.EqualTo("voice/jiho_20.ogg"));
            Assert.That(first.AtMs, Is.EqualTo(Jst(14, 20).ToUnixTimeMilliseconds() + 1500));
        }

        [Test]
        public void Jiho_Silent_WhilePlayerOn()
        {
            var ctx = Ctx(Jst(14, 9), 5);
            ctx.PlayerOn = true;
            Assert.That(RadioScheduler.BuildPlan(RadioProgram.Parse(ProgramJson), ctx).OneShots, Is.Empty);
        }

        [Test]
        public void Jiho_SurvivesThePlanJson()
        {
            var plan = RadioScheduler.BuildPlan(RadioProgram.Parse(ProgramJson), Ctx(Jst(14, 9), 3));
            var back = PlaybackPlan.FromJson(plan.ToJson());
            Assert.That(back.OneShots.Select(s => (s.AtMs, s.File, s.Kind)), Is.EqualTo(plan.OneShots.Select(s => (s.AtMs, s.File, s.Kind))));
        }

        [Test]
        public void Jiho_NeedsTheHourInItsFileName()
        {
            Assert.Throws<FormatException>(() => RadioProgram.Parse(ProgramJson.Replace("jiho_{hh}", "jiho")));
            Assert.That(RadioProgram.Parse(ProgramJson).Jiho.FileFor(7), Is.EqualTo("voice/jiho_07.ogg"));
        }

        // ---- 口の動き

        const int Rate = 44100;

        /// <summary>無音 0.3 秒、音 len 秒、無音 0.3 秒。</summary>
        static float[] Burst(double hz, double len, double amp = 0.3, int channels = 1)
        {
            var n = (int)((0.6 + len) * Rate);
            var x = new float[n * channels];
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)Rate;
                var v = t >= 0.3 && t < 0.3 + len ? (float)(amp * Math.Sin(2 * Math.PI * hz * t)) : 0f;
                for (var c = 0; c < channels; c++) x[i * channels + c] = v;
            }
            return x;
        }

        [Test]
        public void Mouth_OpensWithTheSound_AndClosesInSilence()
        {
            var track = LipSync.Analyze(Burst(220, 0.5), 1, Rate);
            Assert.That(track.DurationSeconds, Is.EqualTo(1.1).Within(0.03));
            Assert.That(track.OpenAt(0.1), Is.EqualTo(0f));
            Assert.That(track.OpenAt(0.55), Is.GreaterThan(0.9f));
            Assert.That(track.OpenAt(1.05), Is.LessThan(0.05f), "消えたら閉じる");
            Assert.That(track.OpenAt(5), Is.EqualTo(0f), "声のあとは閉じたまま");
            Assert.That(track.Open.All(v => v >= 0 && v <= 1));
        }

        [Test]
        public void Mouth_QuieterSound_OpensLess()
        {
            // 前半は大きく、後半は 12dB 小さい
            var loud = Burst(220, 0.4, 0.4);
            var x = new float[loud.Length + (int)(0.4 * Rate)];
            Array.Copy(loud, x, (int)(0.7 * Rate));
            for (var i = 0; i < (int)(0.4 * Rate); i++)
                x[(int)(0.7 * Rate) + i] = (float)(0.1 * Math.Sin(2 * Math.PI * 220 * i / Rate));
            var track = LipSync.Analyze(x, 1, Rate);
            Assert.That(track.OpenAt(0.6), Is.GreaterThan(0.9f));
            Assert.That(track.OpenAt(0.95), Is.InRange(0.3f, 0.7f));
        }

        [Test]
        public void Mouth_Shape_FollowsHowBrightTheSoundIs()
        {
            // 低い響き、真ん中の響き、高い響きを 0.4 秒ずつ: 真ん中を 0 に、低いのは「お」寄り(マイナス)、高いのは「い」寄り(プラス)
            var x = new float[(int)(1.8 * Rate)];
            var hz = new[] { 200.0, 800.0, 2500.0 };
            for (var i = 0; i < (int)(1.2 * Rate); i++)
                x[(int)(0.3 * Rate) + i] = (float)(0.3 * Math.Sin(2 * Math.PI * hz[i / (int)(0.4 * Rate)] * i / Rate));
            var track = LipSync.Analyze(x, 1, Rate);
            Assert.That(track.VowelAt(0.5), Is.LessThan(-0.5f));
            Assert.That(track.VowelAt(0.9), Is.InRange(-0.2f, 0.2f));
            Assert.That(track.VowelAt(1.3), Is.GreaterThan(0.5f));
            Assert.That(track.VowelAt(0.1), Is.EqualTo(0f), "閉じている間は形を作らない");
        }

        [Test]
        public void Mouth_StereoIsReadAsMono()
        {
            var mono = LipSync.Analyze(Burst(300, 0.5), 1, Rate);
            var stereo = LipSync.Analyze(Burst(300, 0.5, channels: 2), 2, Rate);
            Assert.That(stereo.Open, Is.EqualTo(mono.Open).Within(1e-4f));
        }

        // ---- リポジトリのデータ

        static string StreamingAssets()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets")))
                dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, "Assets", "StreamingAssets", "IORI");
        }

        [Test]
        public void RepositoryData_VoiceFilesAllExist()
        {
            var root = StreamingAssets();
            if (root == null) Assert.Ignore("リポジトリの StreamingAssets が見つからない");
            var book = VoiceBook.Parse(File.ReadAllText(Path.Combine(root, "voice.json")));
            var program = RadioProgram.Parse(File.ReadAllText(Path.Combine(root, "radio.json")));
            var files = book.Files().Concat(Enumerable.Range(0, 24).Select(program.Jiho.FileFor));
            foreach (var f in files)
                Assert.That(File.Exists(Path.Combine(root, "Audio", f)), f + " が無い");
            // 6 つの時間帯それぞれにあいさつがある
            foreach (var ph in program.Phases)
                Assert.That(book.Find(VoiceCues.Greet).Lines.Any(l => l.Fits(ph.Id, 12)), ph.Id + " のあいさつが無い");
        }
    }
}
