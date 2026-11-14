using System;
using System.Collections.Generic;
using System.Text;

namespace Iori.Audio
{
    /// <summary>LLM の設定(talk.json の llm)。</summary>
    public sealed class TalkLlm
    {
        public string Model = "claude-haiku-5-5";
        public int MaxTokens = 400;
        /// <summary>"disabled" なら考える時間を切る(速い)。空なら何も送らない(モデルの既定)。モデルによっては "disabled" を受け付けない。</summary>
        public string Thinking = "disabled";
        /// <summary>low / medium / high。空なら送らない。</summary>
        public string Effort = "low";
    }

    /// <summary>声の設定(talk.json の voice)。</summary>
    public sealed class TalkVoice
    {
        /// <summary>ElevenLabs の自分の声の一覧から、この名前を含む声を使う。</summary>
        public string VoiceName = "Chinyaku";
        /// <summary>声の id を直接書くとき(空なら名前で探す)。書いてあれば一覧を読まないので、一覧を読む権限の無いキーでも使える。</summary>
        public string VoiceId = "";
        /// <summary>使いたいモデルの順。「v4 turbo」のような語の並びで、モデルの id か名前にその語がすべてあるものを選ぶ。</summary>
        public List<string> Models = new List<string>();
        /// <summary>どれも見つからないときのモデルの id。</summary>
        public string FallbackModel = "eleven_multilingual_v2";
        /// <summary>角かっこのタグを読めるモデル(「v3」のような語の並び。モデルの id にその語がすべてあれば読める)。ほかのモデルにはタグを渡さない。</summary>
        public List<string> TagModels = new List<string>();
        /// <summary>声の調子のタグとして使ってよいもの("[softly]" の形)。LLM が付けたタグのうち、ここに無いものは落とす。</summary>
        public List<string> Tags = new List<string>();
        /// <summary>LLM がタグを付けなかったときの、時間帯ごとの既定のタグ(上から最初に合うもの)。</summary>
        public List<TalkHourTag> HourTags = new List<TalkHourTag>();
        /// <summary>言語を決め打ちするときの符号(ja など)。空なら送らない(受け付けないモデルがある)。</summary>
        public string Language = "";
        /// <summary>返ってくる音の形。pcm_ のものだけ(16 ビット、モノラル)。</summary>
        public string Format = "pcm_24000";
        public float Volume = 1f;

        public int SampleRate
        {
            get
            {
                var at = Format.IndexOf('_');
                return at >= 0 && int.TryParse(Format.Substring(at + 1), out var hz) && hz > 0 ? hz : 24000;
            }
        }
    }

    /// <summary>見本のやり取り。</summary>
    public sealed class TalkExample
    {
        public string User = "";
        public string Iori = "";
    }

    /// <summary>
    /// イオリと話すときの決まり(StreamingAssets/IORI/talk.json)。人となり、見本、返事の長さ、覚えておく量。
    /// 何をどう言わせるか(指示の文)を組むのは <see cref="TalkPrompt"/>、返ってきた文を整えるのは <see cref="TalkReply"/>。
    /// </summary>
    public sealed class TalkBook
    {
        public int Version = 1;
        public TalkLlm Llm = new TalkLlm();
        public TalkVoice Voice = new TalkVoice();
        /// <summary>返事は何文まで、何文字まで。</summary>
        public int MaxSentences = 2;
        public int MaxChars = 70;
        /// <summary>キミが打てる文字数。</summary>
        public int MaxInputChars = 200;
        /// <summary>毎回渡す直近のやり取りの数と、どれだけ前のものまで渡すか。</summary>
        public int RecentTurns = 6;
        public double RecentHours = 12;
        /// <summary>覚え書きの数と 1 つの長さ。</summary>
        public int MaxNotes = 30;
        public int NoteChars = 40;
        public List<string> Persona = new List<string>();
        public List<TalkExample> Examples = new List<TalkExample>();
        /// <summary>通信できなかったときに文字で見せる一言。</summary>
        public List<string> FailLines = new List<string>();

        public static TalkBook Parse(string json)
        {
            var root = JsonRead.Obj(Json.Parse(json), "talk.json");
            var b = new TalkBook { Version = (int)JsonRead.Num(root, "version", 1) };
            if (root.TryGetValue("llm", out var lo) && lo != null)
            {
                var l = JsonRead.Obj(lo, "llm");
                b.Llm.Model = JsonRead.Str(l, "model", b.Llm.Model);
                b.Llm.MaxTokens = (int)JsonRead.Num(l, "maxTokens", b.Llm.MaxTokens);
                b.Llm.Thinking = JsonRead.Str(l, "thinking", "");
                b.Llm.Effort = JsonRead.Str(l, "effort", "");
            }
            if (root.TryGetValue("voice", out var vo) && vo != null)
            {
                var v = JsonRead.Obj(vo, "voice");
                b.Voice.VoiceName = JsonRead.Str(v, "voiceName", b.Voice.VoiceName);
                b.Voice.VoiceId = JsonRead.Str(v, "voiceId", "").Trim();
                b.Voice.Models = JsonRead.StrList(v, "models");
                b.Voice.FallbackModel = JsonRead.Str(v, "fallbackModel", b.Voice.FallbackModel);
                b.Voice.TagModels = JsonRead.StrList(v, "tagModels");
                b.Voice.Tags = JsonRead.StrList(v, "tags");
                foreach (var ho in JsonRead.Arr(v, "hourTags"))
                {
                    var h = JsonRead.Obj(ho, "hourTags の要素");
                    b.Voice.HourTags.Add(new TalkHourTag
                    {
                        Hours = HourRange.Parse(JsonRead.Str(h, "hours", "", true)),
                        Tag = JsonRead.Str(h, "tag", "", true).Trim(),
                    });
                }
                b.Voice.Language = JsonRead.Str(v, "language", "");
                b.Voice.Format = JsonRead.Str(v, "format", b.Voice.Format);
                b.Voice.Volume = (float)JsonRead.Num(v, "volume", 1);
            }
            if (root.TryGetValue("reply", out var ro) && ro != null)
            {
                var r = JsonRead.Obj(ro, "reply");
                b.MaxSentences = (int)JsonRead.Num(r, "maxSentences", b.MaxSentences);
                b.MaxChars = (int)JsonRead.Num(r, "maxChars", b.MaxChars);
                b.MaxInputChars = (int)JsonRead.Num(r, "maxInputChars", b.MaxInputChars);
            }
            if (root.TryGetValue("memory", out var mo) && mo != null)
            {
                var m = JsonRead.Obj(mo, "memory");
                b.RecentTurns = (int)JsonRead.Num(m, "recentTurns", b.RecentTurns);
                b.RecentHours = JsonRead.Num(m, "recentHours", b.RecentHours);
                b.MaxNotes = (int)JsonRead.Num(m, "maxNotes", b.MaxNotes);
                b.NoteChars = (int)JsonRead.Num(m, "noteChars", b.NoteChars);
            }
            b.Persona = JsonRead.StrList(root, "persona");
            foreach (var eo in JsonRead.Arr(root, "examples") ?? new List<object>())
            {
                var e = JsonRead.Obj(eo, "examples の要素");
                b.Examples.Add(new TalkExample { User = JsonRead.Str(e, "user", "", true), Iori = JsonRead.Str(e, "iori", "", true) });
            }
            b.FailLines = JsonRead.StrList(root, "failLines");
            b.Validate();
            return b;
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Llm.Model)) throw new FormatException("talk.json: llm.model がありません");
            if (Llm.MaxTokens < 16) throw new FormatException("talk.json: llm.maxTokens が小さすぎます");
            if (Persona.Count == 0) throw new FormatException("talk.json: persona がありません");
            if (MaxSentences < 1 || MaxChars < 10) throw new FormatException("talk.json: reply の長さが小さすぎます");
            if (RecentTurns < 0 || MaxNotes < 0 || NoteChars < 4) throw new FormatException("talk.json: memory の値がおかしいです");
            if (!Voice.Format.StartsWith("pcm_", StringComparison.Ordinal)) throw new FormatException("talk.json: voice.format は pcm_ で始まるものだけです");
        }
    }

    /// <summary>話しかけられたときの、まわりの様子。</summary>
    public struct TalkScene
    {
        public int Year, Month, Day, Hour, Minute;
        public DayOfWeek Weekday;
        /// <summary>時間帯(radio.json の phase の id)。</summary>
        public string Phase;
        /// <summary>天気(<see cref="WeatherIds"/>)。</summary>
        public string Weather;
        public bool FireLit;
        /// <summary>鳴っているもの: "radio"(ラジオの曲)、"player"(キミの選んだ曲)、""(曲は鳴っていない)。</summary>
        public string Source;
        public string SongTitle;
        public string SongArtist;
    }

    /// <summary>LLM に渡す指示の文を組む。</summary>
    public static class TalkPrompt
    {
        public const string MemoMark = "MEMO:";
        /// <summary>声に出して読む形(読みを開いた文と調子のタグ)の行の印。見せる文、記憶、履歴には入れない。</summary>
        public const string VoiceMark = "VOICE:";

        static readonly string[] Weekdays = { "日", "月", "火", "水", "木", "金", "土" };

        public static string PhaseName(string phase)
        {
            switch (phase)
            {
                case "early_morning": return "早朝";
                case "morning": return "朝";
                case "day": return "昼";
                case "evening": return "夕方";
                case "night": return "夜";
                case "late_night": return "深夜";
                default: return "";
            }
        }

        /// <summary>変わらない部分(人となり、見本、返事の決まり)を先に、変わる部分(いまの様子、覚え書き)を後ろに置く。</summary>
        public static string System(TalkBook book, TalkScene scene, IList<TalkNote> notes)
        {
            var sb = new StringBuilder();
            foreach (var line in book.Persona) sb.Append(line).Append('\n');

            if (book.Examples.Count > 0)
            {
                sb.Append("\n話し方の見本:\n");
                foreach (var e in book.Examples)
                    sb.Append("キミ「").Append(e.User).Append("」\nイオリ「").Append(e.Iori).Append("」\n");
            }

            sb.Append("\n返事の決まり:\n");
            sb.Append("- イオリの言葉だけを書く。かぎかっこ、名前、動作や表情の説明は付けない。\n");
            sb.Append("- ").Append(book.MaxSentences).Append(" 文まで、全部で ").Append(book.MaxChars)
              .Append(" 文字まで。声に出して読まれるので、絵文字や記号は使わない。\n");
            sb.Append("- 返事のあとに改行して、「").Append(VoiceMark).Append(" 」で始まる 1 行を必ず足す。返事と同じ言葉を、声に出して読む形で書いたもの。")
              .Append("読み間違えやすい漢字(人名、曲名、地名、数字、当て字、むずかしい読み)はひらがなに、英語の曲名や名前はカタカナに開く。ほかはそのままでいい。\n");
            if (book.Voice.Tags.Count > 0)
                sb.Append("- 声の調子をはっきり変えたいときだけ、").Append(VoiceMark).Append(" の行の頭に次のタグから 1 つを付ける: ")
                  .Append(string.Join(" ", book.Voice.Tags)).Append("。たいていは付けない(時間に合った調子は自動で付く)。\n");
            sb.Append("- 例: 返事が「春泥、いい曲だね。」なら、次の行は「").Append(VoiceMark).Append(" しゅんでい、いい曲だね。」。\n");
            if (book.MaxNotes > 0)
                sb.Append("- キミの言ったことの中に、次に会ったときも覚えていたいことがあったときだけ、").Append(VoiceMark).Append(" の行のあとに改行して「")
                  .Append(MemoMark).Append(" 」で始まる 1 行を足す(").Append(book.NoteChars)
                  .Append(" 文字まで、「キミは〜」の形)。たいていは足さない。\n");

            sb.Append("\nいまの様子:\n");
            sb.Append("- ").Append(scene.Year).Append(" 年 ").Append(scene.Month).Append(" 月 ").Append(scene.Day).Append(" 日(")
              .Append(Weekdays[(int)scene.Weekday]).Append(") ").Append(scene.Hour).Append(" 時 ").Append(scene.Minute).Append(" 分");
            var phase = PhaseName(scene.Phase);
            if (phase.Length > 0) sb.Append("、").Append(phase);
            sb.Append("\n- 天気: ").Append(scene.Weather == WeatherIds.Rain ? "雨" : "晴れ").Append('\n');
            sb.Append("- 焚き火: ").Append(scene.FireLit ? "ついている" : "消えている").Append('\n');
            if (scene.Source == "player")
            {
                sb.Append("- キミが選んだ曲が流れている");
                if (!string.IsNullOrEmpty(scene.SongTitle))
                {
                    sb.Append(": 「").Append(scene.SongTitle).Append('」');
                    if (!string.IsNullOrEmpty(scene.SongArtist)) sb.Append("(").Append(scene.SongArtist).Append(")");
                }
                sb.Append('\n');
            }
            else if (scene.Source == "radio") sb.Append("- ラジオの曲が流れている\n");
            else sb.Append("- 曲は流れていない(焚き火やまわりの音だけ)\n");

            if (notes != null && notes.Count > 0)
            {
                sb.Append("\nキミについて覚えていること:\n");
                foreach (var n in notes) sb.Append("- ").Append(n.Text).Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>LLM の返事を整えたもの。</summary>
    public struct TalkReplyText
    {
        /// <summary>イオリの言葉(黙るなら空)。</summary>
        public string Say;
        /// <summary>覚えておくこと(無ければ null)。</summary>
        public string Memo;
        /// <summary>声に出して読む形(「VOICE:」の行の中身。調子のタグも付いたまま。無ければ null)。声に渡す文は <see cref="TalkSpeech.Compose"/> で組む。</summary>
        public string Voice;
        /// <summary>声にする中身があるか(「……」だけなら無い)。</summary>
        public bool HasVoice;
    }

    /// <summary>
    /// LLM の返事をそのまま信じず、決まりの形に整える: 覚え書きの行を分ける、かぎかっこや名前やト書きを外す、
    /// 文の数と文字数を越えた分は切る。
    /// </summary>
    public static class TalkReply
    {
        // (全角の記号は \u で書く: 。!?!? と 」』)) のうち全角のもの)
        const string Enders = "。！？!?";
        const string Closers = "」』)）";
        const char WideSpace = '　';

        public static TalkReplyText Tidy(string raw, int maxSentences, int maxChars, int noteChars)
        {
            var result = new TalkReplyText { Say = "" };
            if (string.IsNullOrEmpty(raw)) return result;
            var say = new StringBuilder();
            string voice = null;
            foreach (var rawLine in raw.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.Trim().Trim(WideSpace);
                if (line.Length == 0) continue;
                var memo = MarkedOf(line, TalkPrompt.MemoMark);
                if (memo != null)
                {
                    if (result.Memo == null && memo.Length > 0) result.Memo = Cut(memo, noteChars);
                    continue;
                }
                var spoken = MarkedOf(line, TalkPrompt.VoiceMark);
                if (spoken != null)
                {
                    if (voice == null && spoken.Length > 0) voice = spoken;
                    continue;
                }
                say.Append(line);
            }
            // (見せる文には調子のタグを入れない)
            var text = TalkSpeech.StripTags(say.ToString(), null, out _);
            text = Clean(text);
            text = LimitSentences(text, maxSentences);
            text = LimitChars(text, maxChars);
            result.Say = text;
            foreach (var c in text)
                if (char.IsLetterOrDigit(c)) { result.HasVoice = true; break; }

            if (voice != null)
            {
                // 見せる文と同じ数の文までにそろえる(タグは文に数えない)
                voice = Clean(voice);
                voice = LimitSentences(voice, Math.Max(1, SentenceEnds(text).Count));
                result.Voice = voice.Length > 0 ? voice : null;
            }
            return result;
        }

        static string Clean(string s)
        {
            s = StripAsides(s);
            s = StripName(s);
            return StripQuotes(s).Trim().Trim(WideSpace);
        }

        /// <summary>「MEMO:」「VOICE:」のような印で始まる行なら中身を返す(印の前の記号、全角のコロン、囲みは見ない)。違えば null。</summary>
        static string MarkedOf(string line, string mark)
        {
            var folded = line.Replace('：', ':').TrimStart('-', '・', ' ', WideSpace, '「', '〔', '［', '(', '（');
            if (!folded.StartsWith(mark, StringComparison.OrdinalIgnoreCase)) return null;
            return folded.Substring(mark.Length).Trim().Trim(WideSpace).TrimEnd('」', '〕', '］', ')', '）').Trim();
        }

        /// <summary>(笑う)や *うなずく* のような説明を外す。</summary>
        static string StripAsides(string s)
        {
            var sb = new StringBuilder(s.Length);
            int depth = 0; bool star = false;
            foreach (var c in s)
            {
                if (c == '(' || c == '\uFF08') { depth++; continue; }
                if (c == ')' || c == '\uFF09') { if (depth > 0) depth--; continue; }
                if (c == '*' || c == '\uFF0A') { star = !star; continue; }
                if (depth == 0 && !star) sb.Append(c);
            }
            // (閉じていない * は、ただの記号として中身を残す)
            return star ? s.Replace("*", "").Replace("\uFF0A", "") : sb.ToString();
        }

        static string StripName(string s)
        {
            s = s.TrimStart();
            foreach (var name in new[] { "イオリ", "IORI", "Iori" })
            {
                if (!s.StartsWith(name, StringComparison.Ordinal)) continue;
                var rest = s.Substring(name.Length).TrimStart();
                if (rest.Length > 0 && (rest[0] == ':' || rest[0] == '\uFF1A')) return rest.Substring(1).TrimStart();
                if (rest.Length > 0 && rest[0] == '「') return rest;
            }
            return s;
        }

        /// <summary>全体を囲むかぎかっこや引用符を外す(途中にあるものはそのまま)。</summary>
        static string StripQuotes(string s)
        {
            s = s.Trim();
            while (s.Length >= 2)
            {
                char a = s[0], z = s[s.Length - 1];
                bool pair = (a == '「' && z == '」') || (a == '『' && z == '』') || (a == '"' && z == '"') || (a == '\uFF02' && z == '\uFF02') || (a == '“' && z == '”');
                if (!pair) break;
                var inner = s.Substring(1, s.Length - 2);
                // 「A」と「B」のように別々のかっこなら外さない
                if (a == '「' && inner.IndexOf('」') >= 0 && inner.IndexOf('「') > inner.IndexOf('」')) break;
                s = inner.Trim();
            }
            return s;
        }

        /// <summary>文の終わりの位置(その次の文字の位置)を順に返す。</summary>
        static List<int> SentenceEnds(string s)
        {
            var ends = new List<int>();
            for (int i = 0; i < s.Length; i++)
            {
                if (Enders.IndexOf(s[i]) < 0) continue;
                int j = i + 1;
                while (j < s.Length && (Enders.IndexOf(s[j]) >= 0 || Closers.IndexOf(s[j]) >= 0)) j++;
                ends.Add(j);
                i = j - 1;
            }
            return ends;
        }

        static string LimitSentences(string s, int max)
        {
            var ends = SentenceEnds(s);
            return ends.Count > max ? s.Substring(0, ends[max - 1]).TrimEnd() : s;
        }

        static string LimitChars(string s, int max)
        {
            if (s.Length <= max) return s;
            int best = 0;
            foreach (var e in SentenceEnds(s)) if (e <= max) best = e;
            return best > 0 ? s.Substring(0, best).TrimEnd() : Cut(s, max);
        }

        /// <summary>max 文字までに切る(切ったら最後は「…」)。文字の途中(サロゲートの片割れ)では切らない。</summary>
        public static string Cut(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            int n = Math.Max(1, max - 1);
            if (char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n).TrimEnd() + "…";
        }
    }
}
