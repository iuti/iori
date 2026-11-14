using System.Text;

namespace Iori.Audio
{
    /// <summary>
    /// 曲をさがすときの文字のそろえ方。打った言葉と曲名を同じ形にそろえてから比べるので、
    /// 大文字と小文字、全角と半角(英数字、カタカナ)、ひらがなとカタカナ、空白のあるなしの違いでは外れない。
    /// どの端末でも同じ結果になるように、言語の設定は使わない。
    /// </summary>
    public static class TextFold
    {
        /// <summary>そろえた形(空白は抜く)。</summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // 半角カタカナ: 全角にして、続く濁点、半濁点を合わせる
                if (c >= 'ｦ' && c <= 'ﾝ')
                {
                    char full = HalfKana[c - 'ｦ'];
                    if (i + 1 < text.Length)
                    {
                        char next = text[i + 1];
                        if (next == 'ﾞ' && CanVoice(full)) { full = full == 'ウ' ? 'ヴ' : (char)(full + 1); i++; }
                        else if (next == 'ﾟ' && CanHalfVoice(full)) { full = (char)(full + 2); i++; }
                    }
                    c = full;
                }
                else if (c == 'ﾞ') c = '゛';
                else if (c == 'ﾟ') c = '゜';
                else if (c == 'ｰ') c = 'ー';
                // 全角の英数字と記号は半角に
                else if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);

                if (char.IsWhiteSpace(c) || c == '　') continue;
                // カタカナはひらがなに(ヴ、ヵ、ヶ も)
                if (c >= 'ァ' && c <= 'ヶ') c = (char)(c - 0x60);
                c = char.ToLowerInvariant(c);
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 打った言葉(空白で区切ると、どれも含むものだけ)が text に入っているか。text はそろえた形で渡す。
        /// 空の言葉なら何にでも当たる。
        /// </summary>
        public static bool Matches(string foldedText, string[] foldedWords)
        {
            if (foldedWords == null) return true;
            foreach (var w in foldedWords)
                if (w.Length > 0 && (foldedText == null || foldedText.IndexOf(w, System.StringComparison.Ordinal) < 0))
                    return false;
            return true;
        }

        /// <summary>打った言葉を空白で区切り、それぞれをそろえた形にする(空のものは抜く)。</summary>
        public static string[] Words(string query)
        {
            if (string.IsNullOrEmpty(query)) return new string[0];
            var parts = query.Split(new[] { ' ', '　', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var p in parts)
            {
                var f = Fold(p);
                if (f.Length > 0) list.Add(f);
            }
            return list.ToArray();
        }

        // (濁点が付く字は、その字の次の番号が濁った字。ウだけは別)
        static bool CanVoice(char k) => k == 'ウ' || "カキクケコサシスセソタチツテトハヒフヘホ".IndexOf(k) >= 0;

        static bool CanHalfVoice(char k) => k >= 'ハ' && k <= 'ホ' && (k - 'ハ') % 3 == 0;

        // U+FF66 から U+FF9D の半角カタカナに当たる全角
        const string HalfKana = "ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン";
    }
}
