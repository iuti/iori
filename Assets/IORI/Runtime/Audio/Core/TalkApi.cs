using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// 外のサービスとやり取りする文の形(送る JSON を作る、返ってきた JSON を読む)。通信そのものは Unity 側(TalkDirector)。
    /// LLM は Claude の Messages API(Unity 向けの公式 SDK が無いので、HTTP の形をここで組む)。声は ElevenLabs。
    /// </summary>
    public static class TalkApi
    {
        public const string AnthropicUrl = "https://api.anthropic.com/v1/messages";
        public const string AnthropicVersion = "2026-10-21";
        public const string ElevenHost = "https://api.elevenlabs.io";
        public const string ElevenBase = ElevenHost + "/v1";

        // ---- LLM

        /// <summary>Messages API に送る本文。turns は古い順、最後に今回のキミの言葉。</summary>
        public static string AnthropicRequest(TalkLlm llm, string system, IList<TalkTurn> turns, string userText)
        {
            var messages = new List<object>();
            if (turns != null)
                foreach (var t in turns)
                {
                    // (空の発言は送れない。黙った返事は「……」として渡す)
                    if (string.IsNullOrEmpty(t.User)) continue;
                    messages.Add(Message("user", t.User));
                    messages.Add(Message("assistant", string.IsNullOrEmpty(t.Iori) ? "……" : t.Iori));
                }
            messages.Add(Message("user", userText));
            var body = new Dictionary<string, object>
            {
                ["model"] = llm.Model,
                ["max_tokens"] = llm.MaxTokens,
                ["system"] = system,
                ["messages"] = messages,
            };
            if (!string.IsNullOrEmpty(llm.Thinking)) body["thinking"] = new Dictionary<string, object> { ["type"] = llm.Thinking };
            if (!string.IsNullOrEmpty(llm.Effort)) body["output_config"] = new Dictionary<string, object> { ["effort"] = llm.Effort };
            return Json.Write(body);
        }

        static Dictionary<string, object> Message(string role, string text) =>
            new Dictionary<string, object> { ["role"] = role, ["content"] = text };

        /// <summary>
        /// Messages API の返事から文字を取り出す(種類が text のかたまりだけをつなぐ。考えごとのかたまりは飛ばす)。
        /// 断られた、エラーだった、形が読めないときは null を返し、error にわけを入れる。
        /// </summary>
        public static string AnthropicReply(string json, out string error)
        {
            error = null;
            Dictionary<string, object> root;
            try { root = JsonRead.Obj(Json.Parse(json ?? ""), "返事"); }
            catch (FormatException e) { error = "返事が読めません: " + e.Message; return null; }

            if (root.TryGetValue("type", out var type) && type as string == "error")
            {
                error = ErrorOf(root) ?? "エラー";
                return null;
            }
            if (root.TryGetValue("stop_reason", out var stop) && stop as string == "refusal")
            {
                error = "refusal";
                return null;
            }
            if (!(root.TryGetValue("content", out var co) && co is List<object> content))
            {
                error = ErrorOf(root) ?? "返事に content がありません";
                return null;
            }
            var text = "";
            foreach (var bo in content)
                if (bo is Dictionary<string, object> b && b.TryGetValue("type", out var bt) && bt as string == "text"
                    && b.TryGetValue("text", out var t) && t is string s)
                    text += s;
            return text;
        }

        static string ErrorOf(Dictionary<string, object> root)
        {
            if (!(root.TryGetValue("error", out var eo) && eo is Dictionary<string, object> e)) return null;
            var kind = e.TryGetValue("type", out var k) ? k as string : null;
            var message = e.TryGetValue("message", out var m) ? m as string : null;
            return (kind ?? "error") + (string.IsNullOrEmpty(message) ? "" : ": " + message);
        }

        // ---- 声

        public static string ElevenSpeechUrl(string voiceId, string format) =>
            ElevenBase + "/text-to-speech/" + Uri.EscapeDataString(voiceId) + "?output_format=" + Uri.EscapeDataString(format);

        public static string ElevenRequest(string text, string modelId, string language)
        {
            var body = new Dictionary<string, object> { ["text"] = text, ["model_id"] = modelId };
            if (!string.IsNullOrEmpty(language)) body["language_code"] = language;
            return Json.Write(body);
        }

        /// <summary>自分の声(ライブラリから足した声も含む)を名前で探す URL。古い /v1/voices は廃止予定なので使わない。</summary>
        public static string ElevenVoicesUrl(string name) =>
            ElevenHost + "/v2/voices?page_size=100&search=" + Uri.EscapeDataString(name ?? "");

        /// <summary>みんなの声のライブラリを名前で探す URL(自分の声に足していないとき用)。</summary>
        public static string ElevenSharedVoicesUrl(string name) =>
            ElevenBase + "/shared-voices?page_size=30&search=" + Uri.EscapeDataString(name ?? "");

        /// <summary>
        /// 貼られたキーから、キーに入らない文字(空白、改行、引用符、全角など)を落とす。
        /// ヘッダーに入れられない文字が混じると、サービスに届く前に断られる。
        /// </summary>
        public static string CleanKey(string key)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in key ?? "")
                if (c > 0x20 && c < 0x7F && c != '"' && c != '\'') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>ElevenLabs の声の id らしい形か(英数字 20 文字)。</summary>
        public static bool LooksLikeVoiceId(string s)
        {
            if (s == null || s.Length != 20) return false;
            foreach (var c in s) if (!(c < 0x7F && char.IsLetterOrDigit(c))) return false;
            return true;
        }

        /// <summary>声の一覧(GET /v2/voices や /v1/shared-voices)から、名前に name を含む声の id を探す(大文字小文字は見ない)。無ければ null。</summary>
        public static string PickVoice(string voicesJson, string name)
        {
            try
            {
                var root = JsonRead.Obj(Json.Parse(voicesJson ?? ""), "voices");
                foreach (var vo in JsonRead.Arr(root, "voices"))
                {
                    if (!(vo is Dictionary<string, object> v)) continue;
                    var n = v.TryGetValue("name", out var no) ? no as string : null;
                    var id = v.TryGetValue("voice_id", out var io) ? io as string : null;
                    if (n != null && !string.IsNullOrEmpty(id) && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return id;
                }
            }
            catch (FormatException) { }
            return null;
        }

        /// <summary>
        /// モデルの一覧(GET /models)から、wanted の順で最初に合うモデルの id を選ぶ。
        /// 「v4 turbo」は、id か名前を語に分けたものに v4 と turbo の両方があるモデルに合う。合うものが無ければ null。
        /// </summary>
        public static string PickModel(string modelsJson, IList<string> wanted)
        {
            var models = new List<KeyValuePair<string, HashSet<string>>>();
            try
            {
                if (!(Json.Parse(modelsJson ?? "") is List<object> list)) return null;
                foreach (var mo in list)
                {
                    if (!(mo is Dictionary<string, object> m)) continue;
                    var id = m.TryGetValue("model_id", out var io) ? io as string : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (m.TryGetValue("can_do_text_to_speech", out var can) && can is bool b && !b) continue;
                    var name = m.TryGetValue("name", out var no) ? no as string : "";
                    models.Add(new KeyValuePair<string, HashSet<string>>(id, Tokens(id + " " + name)));
                }
            }
            catch (FormatException) { return null; }

            foreach (var want in wanted)
            {
                var need = Tokens(want);
                if (need.Count == 0) continue;
                foreach (var m in models)
                    if (need.IsSubsetOf(m.Value)) return m.Key;
            }
            return null;
        }

        static HashSet<string> Tokens(string s)
        {
            var set = new HashSet<string>();
            var word = "";
            foreach (var c in (s ?? "").ToLowerInvariant() + " ")
            {
                if (char.IsLetterOrDigit(c)) { word += c; continue; }
                if (word.Length > 0) set.Add(word);
                word = "";
            }
            return set;
        }

        /// <summary>16 ビットの PCM(リトルエンディアン)を -1〜1 の値に直す。</summary>
        public static float[] PcmToFloats(byte[] pcm)
        {
            if (pcm == null) return new float[0];
            var n = pcm.Length / 2;
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
            return data;
        }
    }
}
