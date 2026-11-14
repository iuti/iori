using System;
using System.Collections.Generic;
using System.Globalization;

namespace Iori.Audio
{
    /// <summary>イオリが話すきっかけ(場面)。voice.json の cues のキー。</summary>
    public static class VoiceCues
    {
        /// <summary>アプリを開いた(時間帯のあいさつ)。</summary>
        public const string Greet = "greet";
        /// <summary>家に帰ってきた(「おかえり」)。位置のしかけができたら使う。いまはどこからも言わない(chico の指定でとっておく)。</summary>
        public const string Return = "return";
        /// <summary>音楽プレーヤーを開いた。</summary>
        public const string PlayerOpen = "player_open";
        /// <summary>プレーヤーの曲を最後まで鳴らし終えて止まった。</summary>
        public const string MusicStopped = "music_stopped";
        /// <summary>イオリを押した。</summary>
        public const string Tap = "tap";
        public const string RainStart = "rain_start";
        public const string RainStop = "rain_stop";
        /// <summary>消えていた火をまたつけた(焚き火の音量をゼロから上げた)。</summary>
        public const string Firewood = "firewood";
        /// <summary>話しかけられたのに、言葉を返せなかった(通信できない)。セリフ帳に無い間は文字だけで返す。</summary>
        public const string TalkFail = "talk_fail";
    }

    /// <summary>セリフ 1 本。Phases と Hours が空なら、いつでも言える。</summary>
    public sealed class VoiceLine
    {
        public string File;
        public float Volume = 1f;
        /// <summary>この時間帯のときだけ(空なら問わない)。</summary>
        public List<string> Phases = new List<string>();
        /// <summary>この時刻の範囲のときだけ。From から To の前まで(From &gt; To なら 0 時をまたぐ)。null なら問わない。</summary>
        public HourRange Hours;

        public bool Fits(string phase, int localHour)
        {
            if (Phases.Count > 0 && !Phases.Contains(phase ?? "")) return false;
            return Hours == null || Hours.Contains(localHour);
        }
    }

    public sealed class HourRange
    {
        public int From;
        public int To;

        public bool Contains(int hour) => From <= To ? hour >= From && hour < To : hour >= From || hour < To;

        /// <summary>"22-4" のような書き方を読む。</summary>
        public static HourRange Parse(string s)
        {
            var parts = s.Split('-');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                || !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                || from > 23 || to > 24 || from == to % 24)
                throw new FormatException("hours は \"22-4\" のように 0〜24 の違う 2 つの時で書いてください: " + s);
            return new HourRange { From = from, To = to % 24 };
        }
    }

    public sealed class VoiceCueDef
    {
        public string Id;
        public List<VoiceLine> Lines = new List<VoiceLine>();
        /// <summary>この場面で一度話したら、次に同じ場面で話すまで空ける秒数。</summary>
        public double CooldownSeconds;
        /// <summary>話す確率(0〜1)。押すたびに毎回しゃべると、うるさいので。</summary>
        public double Chance = 1;
    }

    /// <summary>
    /// イオリのセリフ帳。StreamingAssets/IORI/voice.json から読む。どの場面でどの声を言えるかと、話す間隔の決まり。
    /// 時報(正時の声)はラジオの予定表に入るので、ここではなく radio.json の jiho に書く。
    /// </summary>
    public sealed class VoiceBook
    {
        public int Version = 1;
        /// <summary>どの場面でも、前のセリフが終わってから次を言うまで空ける秒数。</summary>
        public double MinGapSeconds = 6;
        /// <summary>時報などの予定された声がこの秒数以内に来るなら、場面の声は言わない(重ならないように)。</summary>
        public double ScheduledMarginSeconds = 6;
        public Dictionary<string, VoiceCueDef> Cues = new Dictionary<string, VoiceCueDef>();

        public VoiceCueDef Find(string cue) => cue != null && Cues.TryGetValue(cue, out var c) ? c : null;

        /// <summary>この本に出てくる声のファイル(重複なし)。</summary>
        public List<string> Files()
        {
            var result = new List<string>();
            foreach (var c in Cues.Values)
                foreach (var l in c.Lines)
                    if (!result.Contains(l.File)) result.Add(l.File);
            return result;
        }

        public static VoiceBook Parse(string json)
        {
            var root = JsonRead.Obj(Json.Parse(json), "voice.json");
            var book = new VoiceBook
            {
                Version = (int)JsonRead.Num(root, "version", 1),
                MinGapSeconds = JsonRead.Num(root, "minGapSeconds", 6),
                ScheduledMarginSeconds = JsonRead.Num(root, "scheduledMarginSeconds", 6),
            };
            if (root.TryGetValue("cues", out var cuesObj) && cuesObj != null)
            {
                foreach (var kv in JsonRead.Obj(cuesObj, "cues"))
                {
                    var o = JsonRead.Obj(kv.Value, "cues." + kv.Key);
                    var cue = new VoiceCueDef
                    {
                        Id = kv.Key,
                        CooldownSeconds = JsonRead.Num(o, "cooldownSeconds", 0),
                        Chance = JsonRead.Num(o, "chance", 1),
                    };
                    foreach (var item in JsonRead.Arr(o, "lines", required: true))
                        cue.Lines.Add(ReadLine(item, kv.Key));
                    book.Cues[kv.Key] = cue;
                }
            }
            book.Validate();
            return book;
        }

        static VoiceLine ReadLine(object item, string cue)
        {
            // 書き方は 2 通り: "voice/tap_01.ogg" か { "file": ..., "phase": ..., "hours": "22-4", "volume": ... }
            if (item is string file) return new VoiceLine { File = file };
            var o = JsonRead.Obj(item, "cues." + cue + ".lines の要素");
            var line = new VoiceLine
            {
                File = JsonRead.Str(o, "file", required: true),
                Volume = (float)JsonRead.Num(o, "volume", 1),
            };
            if (o.TryGetValue("phase", out var ph) && ph != null)
            {
                if (ph is string one) line.Phases.Add(one);
                else line.Phases = JsonRead.StrList(o, "phase");
            }
            var hours = JsonRead.Str(o, "hours", null);
            if (hours != null) line.Hours = HourRange.Parse(hours);
            return line;
        }

        public void Validate()
        {
            if (MinGapSeconds < 0) throw new FormatException("minGapSeconds は 0 以上です");
            if (ScheduledMarginSeconds < 0) throw new FormatException("scheduledMarginSeconds は 0 以上です");
            foreach (var c in Cues.Values)
            {
                if (c.Lines.Count == 0) throw new FormatException("cues." + c.Id + " にセリフがありません");
                if (c.CooldownSeconds < 0) throw new FormatException("cues." + c.Id + " の cooldownSeconds は 0 以上です");
                if (c.Chance < 0 || c.Chance > 1) throw new FormatException("cues." + c.Id + " の chance は 0〜1 です");
                foreach (var l in c.Lines)
                    if (string.IsNullOrEmpty(l.File)) throw new FormatException("cues." + c.Id + " に file の無いセリフがあります");
            }
        }
    }

    /// <summary>場面の声を選ぶときの状況。</summary>
    public struct VoiceMoment
    {
        public long NowMs;
        /// <summary>いまの時間帯(radio.json の phase の id)。</summary>
        public string Phase;
        /// <summary>現地の時(0〜23)。</summary>
        public int LocalHour;
        /// <summary>次の予定された声(時報)の時刻。無ければ long.MaxValue。</summary>
        public long NextScheduledVoiceMs;
    }

    /// <summary>
    /// 何を言うかを決める係。場面(<see cref="VoiceCues"/>)と状況から、言うセリフを 1 本選ぶか、黙る(null)。
    /// 決まり:
    /// - 前のセリフが終わってから MinGapSeconds たつまでは黙る(声が重ならない、しゃべりすぎない)。
    /// - 予定された声(時報)の前後 ScheduledMarginSeconds は黙る。
    /// - 場面ごとの cooldownSeconds の間は、その場面では黙る。chance の確率でしか話さない。
    /// - 時間帯と時刻の合うセリフから選び、同じ場面で直前と同じセリフは続けない。
    /// 乱数の種を渡せば、同じ入力から同じ答えになる(テストのため)。
    /// </summary>
    public sealed class VoicePicker
    {
        readonly VoiceBook _book;
        readonly Dictionary<string, long> _lastCueMs = new Dictionary<string, long>();
        readonly Dictionary<string, string> _lastFile = new Dictionary<string, string>();
        SplitMix64 _rng;

        /// <summary>いま話しているセリフが終わる時刻(話していなければ過去)。</summary>
        public long SpeakingUntilMs { get; private set; } = long.MinValue;

        public VoicePicker(VoiceBook book, ulong seed)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            _rng = new SplitMix64(seed);
        }

        public VoiceBook Book => _book;

        /// <summary>
        /// cue の場面で言うセリフを選ぶ。null なら黙る。選んだら言ったものとして覚えるので、
        /// 鳴らし終わる時刻を <see cref="NoteSpoken"/> で伝える(長さが分からなければ選んだ時刻から数える)。
        /// force(動作確認用)なら、間隔と確率の決まりを飛ばす(時間帯と時刻の合うセリフから選ぶのは同じ)。
        /// </summary>
        public VoiceLine Pick(string cue, VoiceMoment m, bool force = false)
        {
            var def = _book.Find(cue);
            if (def == null) return null;
            if (force) return Choose(cue, def, m);
            var gapMs = (long)(_book.MinGapSeconds * 1000);
            if (SpeakingUntilMs != long.MinValue && m.NowMs < SpeakingUntilMs + gapMs) return null;
            var marginMs = (long)(_book.ScheduledMarginSeconds * 1000);
            if (m.NextScheduledVoiceMs != long.MaxValue && Math.Abs(m.NextScheduledVoiceMs - m.NowMs) < marginMs) return null;
            if (_lastCueMs.TryGetValue(cue, out var last) && m.NowMs - last < (long)(def.CooldownSeconds * 1000)) return null;

            if (!def.Lines.Exists(l => l.Fits(m.Phase, m.LocalHour))) return null;
            // (確率は、言えるセリフがあるときだけ引く。引いて外れても場面の間隔は空けない)
            if (def.Chance < 1 && _rng.NextDouble() >= def.Chance) return null;
            return Choose(cue, def, m);
        }

        VoiceLine Choose(string cue, VoiceCueDef def, VoiceMoment m)
        {
            var fits = def.Lines.FindAll(l => l.Fits(m.Phase, m.LocalHour));
            if (fits.Count == 0) return null;
            _lastFile.TryGetValue(cue, out var previous);
            if (fits.Count > 1) fits.RemoveAll(l => l.File == previous);
            var pick = fits[_rng.NextInt(fits.Count)];

            _lastCueMs[cue] = m.NowMs;
            _lastFile[cue] = pick.File;
            SpeakingUntilMs = m.NowMs;
            return pick;
        }

        /// <summary>声が鳴り終わる時刻を伝える(場面の声も、予定された時報も)。次のセリフはそこから MinGapSeconds あける。</summary>
        public void NoteSpoken(long untilMs)
        {
            if (untilMs > SpeakingUntilMs) SpeakingUntilMs = untilMs;
        }
    }
}
