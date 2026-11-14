using System;
using System.Collections.Generic;
using System.Text;

namespace Iori.Audio
{
    /// <summary>時間帯ごとの既定の声の調子(talk.json の voice.hourTags の 1 つ)。</summary>
    public sealed class TalkHourTag
    {
        public HourRange Hours;
        /// <summary>"[softly]" のような角かっこのタグ。</summary>
        public string Tag = "";
    }

    /// <summary>
    /// 声(ElevenLabs)に渡す文を組む。ここだけが決める:
    /// - 読み: LLM が返した「VOICE:」の行(読み間違えやすい漢字をひらがなに開いたもの)を使う。無い、または見せる文と長さがかけ離れているなら見せる文をそのまま。
    /// - 調子: 角かっこのタグ。LLM が付けたタグのうち一覧(voice.tags)にあるものだけを残し、頭に一覧のタグが無ければ時間帯の既定(voice.hourTags)を付ける。
    /// - タグを読めるモデル(voice.tagModels)でなければ、タグは全部外す(声に出して読まれてしまうため)。
    /// </summary>
    public static class TalkSpeech
    {
        /// <summary>読みの文が、見せる文の何倍の長さまでなら信じるか(漢字をひらがなに開くと長くなる)。</summary>
        public const double MinLengthRatio = 0.5;
        public const double MaxLengthRatio = 3.0;

        public static string Compose(string say, string voiceLine, string modelId, int localHour, TalkVoice voice)
        {
            say = StripTags(say ?? "", null, out _).Trim();
            var allowed = voice?.Tags ?? new List<string>();

            string body = say;
            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(voiceLine))
            {
                var spoken = StripTags(voiceLine, allowed, out var kept).Trim();
                if (LengthFits(Plain(spoken), Plain(say)))
                {
                    body = spoken;
                    tags = kept;
                }
            }

            if (!TakesTags(modelId, voice?.TagModels)) return body;

            if (tags.Count == 0 && voice != null)
                foreach (var h in voice.HourTags)
                    if (h.Hours != null && h.Hours.Contains(localHour) && h.Tag.Length > 0) { tags.Add(h.Tag); break; }

            if (tags.Count == 0) return body;
            var sb = new StringBuilder();
            foreach (var t in tags) sb.Append(t).Append(' ');
            return sb.Append(body).ToString();
        }

        /// <summary>このモデルが角かっこのタグを読めるか(id を語に分けたものに、patterns のどれかの語がすべてある)。</summary>
        public static bool TakesTags(string modelId, IList<string> patterns)
        {
            if (string.IsNullOrEmpty(modelId) || patterns == null) return false;
            var have = TalkApi.Tokens(modelId);
            foreach (var p in patterns)
            {
                var need = TalkApi.Tokens(p);
                if (need.Count > 0 && need.IsSubsetOf(have)) return true;
            }
            return false;
        }

        /// <summary>
        /// 角かっこのタグを外す。allowed が null ならすべて捨てる。allowed にあるタグ(大文字小文字は見ない)は、
        /// 一覧の書き方で kept に入れる(同じものは 1 つ)。閉じていない「[」はそのまま残す。
        /// </summary>
        public static string StripTags(string s, IList<string> allowed, out List<string> kept)
        {
            kept = new List<string>();
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '[' || c == '\uFF3B')
                {
                    int close = IndexOfClose(s, i + 1);
                    if (close > i)
                    {
                        var inner = s.Substring(i + 1, close - i - 1).Trim();
                        var match = Find(allowed, inner);
                        if (match != null && !kept.Contains(match)) kept.Add(match);
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            // (タグを外したあとに残る二重の空白をつめる)
            return sb.ToString().Replace("  ", " ");
        }

        static int IndexOfClose(string s, int from)
        {
            for (int j = from; j < s.Length; j++)
            {
                if (s[j] == ']' || s[j] == '\uFF3D') return j;
                if (s[j] == '[' || s[j] == '\uFF3B' || s[j] == '\n') return -1;
            }
            return -1;
        }

        static string Find(IList<string> allowed, string inner)
        {
            if (allowed == null || inner.Length == 0) return null;
            foreach (var a in allowed)
            {
                var name = a.Trim().TrimStart('[').TrimEnd(']').Trim();
                if (string.Equals(name, inner, StringComparison.OrdinalIgnoreCase)) return "[" + name + "]";
            }
            return null;
        }

        static bool LengthFits(string spoken, string say)
        {
            if (spoken.Length == 0) return false;
            if (say.Length == 0) return true;
            double r = (double)spoken.Length / say.Length;
            return r >= MinLengthRatio && r <= MaxLengthRatio;
        }

        /// <summary>長さを比べるため、空白と記号を落とした文字だけにする。</summary>
        static string Plain(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
