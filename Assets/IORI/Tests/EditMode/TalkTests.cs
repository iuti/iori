using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class TalkTests
    {
        const string BookJson = @"{
  ""llm"": { ""model"": ""claude-haiku-5-5"", ""maxTokens"": 300, ""thinking"": ""disabled"", ""effort"": ""low"" },
  ""voice"": { ""voiceName"": ""Chinyaku"", ""models"": [""v4 turbo"", ""v3""], ""format"": ""pcm_22050"" },
  ""reply"": { ""maxSentences"": 2, ""maxChars"": 30 },
  ""memory"": { ""recentTurns"": 2, ""recentHours"": 1, ""maxNotes"": 3, ""noteChars"": 12 },
  ""persona"": [""あなたはイオリ。""],
  ""examples"": [ { ""user"": ""ただいま"", ""iori"": ""ん。"" } ],
  ""failLines"": [""……""]
}";

        static TalkBook Book() => TalkBook.Parse(BookJson);

        static TalkReplyText Tidy(string raw) => TalkReply.Tidy(raw, 2, 30, 12);

        [Test]
        public void Book_ReadsSettings()
        {
            var b = Book();
            Assert.That(b.Llm.Model, Is.EqualTo("claude-haiku-5-5"));
            Assert.That(b.Llm.MaxTokens, Is.EqualTo(300));
            Assert.That(b.Voice.SampleRate, Is.EqualTo(22050));
            Assert.That(b.Voice.Models, Is.EqualTo(new[] { "v4 turbo", "v3" }));
            Assert.That(b.MaxChars, Is.EqualTo(30));
            Assert.That(b.RecentTurns, Is.EqualTo(2));
            Assert.That(b.Examples[0].Iori, Is.EqualTo("ん。"));
        }

        [Test]
        public void Book_RejectsBadData()
        {
            Assert.Throws<FormatException>(() => TalkBook.Parse(@"{ ""persona"": [] }"));
            Assert.Throws<FormatException>(() => TalkBook.Parse(@"{ ""persona"": [""a""], ""voice"": { ""format"": ""mp3_44100_128"" } }"));
            Assert.Throws<FormatException>(() => TalkBook.Parse(@"{ ""persona"": [""a""], ""examples"": [ { ""user"": ""x"" } ] }"));
        }

        [Test]
        public void ShippedBook_IsValid()
        {
            var dir = AppContext.BaseDirectory;
            string path = null;
            for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var p = Path.Combine(dir, "Assets", "StreamingAssets", "IORI", "talk.json");
                if (File.Exists(p)) { path = p; break; }
            }
            if (path == null) Assert.Ignore("talk.json が見つからない場所で動いています");
            var b = TalkBook.Parse(File.ReadAllText(path));
            Assert.That(b.Persona.Count, Is.GreaterThan(0));
            Assert.That(b.FailLines.Count, Is.GreaterThan(0));
            // 見本は自分の決まり(文の数と長さ)を守っている
            foreach (var e in b.Examples)
                Assert.That(TalkReply.Tidy(e.Iori, b.MaxSentences, b.MaxChars, b.NoteChars).Say, Is.EqualTo(e.Iori));
        }

        [Test]
        public void Prompt_HoldsPersonaSceneAndNotes()
        {
            var scene = new TalkScene
            {
                Year = 2026, Month = 11, Day = 14, Hour = 23, Minute = 5, Weekday = DayOfWeek.Saturday,
                Phase = "late_night", Weather = WeatherIds.Rain, FireLit = true,
                Source = "player", SongTitle = "Blue in Green", SongArtist = "Miles Davis",
            };
            var notes = new List<TalkNote> { new TalkNote { Text = "キミは夜更かし" } };
            var s = TalkPrompt.System(Book(), scene, notes);
            Assert.That(s, Does.StartWith("あなたはイオリ。"));
            Assert.That(s, Does.Contain("キミ「ただいま」"));
            Assert.That(s, Does.Contain("2026 年 11 月 14 日(土) 23 時 5 分、深夜"));
            Assert.That(s, Does.Contain("天気: 雨"));
            Assert.That(s, Does.Contain("「Blue in Green」(Miles Davis)"));
            Assert.That(s, Does.Contain("- キミは夜更かし"));
            Assert.That(s, Does.Contain("MEMO:"));
            // 変わらない部分が先、変わる部分が後ろ
            Assert.That(s.IndexOf("返事の決まり"), Is.LessThan(s.IndexOf("いまの様子")));

            scene.Source = "";
            scene.FireLit = false;
            s = TalkPrompt.System(Book(), scene, null);
            Assert.That(s, Does.Contain("曲は流れていない"));
            Assert.That(s, Does.Contain("焚き火: 消えている"));
            Assert.That(s, Does.Not.Contain("覚えていること"));
        }

        [Test]
        public void Reply_SplitsMemoAndStripsDressing()
        {
            var r = Tidy("イオリ:「いいね。夜に合う。」\nMEMO: キミはジャズが好き");
            Assert.That(r.Say, Is.EqualTo("いいね。夜に合う。"));
            Assert.That(r.Memo, Is.EqualTo("キミはジャズが好き"));
            Assert.That(r.HasVoice, Is.True);

            Assert.That(Tidy("(少し笑って)そっか。").Say, Is.EqualTo("そっか。"));
            Assert.That(Tidy("*うなずく* ん。").Say, Is.EqualTo("ん。"));
            Assert.That(Tidy("「そう」って言ったよね。").Say, Is.EqualTo("「そう」って言ったよね。"));
            Assert.That(Tidy("memo: キミは朝が苦手で、いつも二度寝する").Memo, Is.EqualTo("キミは朝が苦手で、いつ…"));
            Assert.That(Tidy("ん。\nMEMO:").Memo, Is.Null);
        }

        [Test]
        public void Reply_KeepsWithinSentencesAndChars()
        {
            Assert.That(Tidy("一つ。二つ!三つ?四つ。").Say, Is.EqualTo("一つ。二つ!"));
            // 文の途中の「……」は文の終わりではない
            Assert.That(Tidy("そっか。……座ってなよ、何もしなくていいから。").Say, Is.EqualTo("そっか。……座ってなよ、何もしなくていいから。"));
            // 長すぎたら、収まる文の終わりで切る
            var longTwo = "火がちょうどいい頃だよ。" + new string('あ', 40) + "。";
            Assert.That(Tidy(longTwo).Say, Is.EqualTo("火がちょうどいい頃だよ。"));
            // 収まる文の終わりが無ければ、文字数で切る
            var cut = Tidy(new string('あ', 50)).Say;
            Assert.That(cut.Length, Is.EqualTo(30));
            Assert.That(cut, Does.EndWith("…"));
        }

        [Test]
        public void Reply_SilenceHasNoVoice()
        {
            var r = Tidy("……");
            Assert.That(r.Say, Is.EqualTo("……"));
            Assert.That(r.HasVoice, Is.False);
            Assert.That(Tidy("").Say, Is.EqualTo(""));
            Assert.That(Tidy(null).HasVoice, Is.False);
        }

        [Test]
        public void Memory_KeepsRecentTurns()
        {
            var m = new TalkMemory();
            const long hour = 3600_000;
            m.AddTurn(0, "a", "A");
            m.AddTurn(5 * hour, "b", "B");
            m.AddTurn(5 * hour + 1, "c", "C");
            m.AddTurn(5 * hour + 2, "d", "D");
            var recent = m.Recent(5 * hour + 10, 2, 1);
            Assert.That(recent.ConvertAll(t => t.User), Is.EqualTo(new[] { "c", "d" }));
            Assert.That(m.Recent(5 * hour + 10, 10, 1).Count, Is.EqualTo(3));   // 古すぎるものは渡さない
            Assert.That(m.Recent(8 * hour, 10, 1).Count, Is.EqualTo(0));
            for (int i = 0; i < 40; i++) m.AddTurn(i, "x", "y");
            Assert.That(m.Turns.Count, Is.EqualTo(TalkMemory.KeptTurns));
        }

        [Test]
        public void Memory_NotesDedupeAndForgetOldest()
        {
            var m = new TalkMemory();
            Assert.That(m.AddNote(1, "キミは Jazz が好き", 3, 20), Is.True);
            Assert.That(m.AddNote(2, "キミは朝が苦手", 3, 20), Is.True);
            Assert.That(m.AddNote(3, "キミは jazz が好き", 3, 20), Is.True);   // 同じ中身: 後ろへ回る
            Assert.That(m.Notes.Count, Is.EqualTo(2));
            Assert.That(m.Notes[1].AtMs, Is.EqualTo(3));
            m.AddNote(4, "c", 3, 20);
            m.AddNote(5, "d", 3, 20);
            Assert.That(m.Notes.Count, Is.EqualTo(3));
            Assert.That(m.Notes[0].Text, Is.EqualTo("キミは jazz が好き"));
            Assert.That(m.AddNote(6, "  ", 3, 20), Is.False);
            m.RemoveNoteAt(0);
            Assert.That(m.Notes[0].Text, Is.EqualTo("c"));
        }

        [Test]
        public void Memory_SavesAndLoads()
        {
            var m = new TalkMemory();
            m.AddTurn(10, "こんばんは", "ん。");
            m.AddNote(11, "キミは \"夜型\"", 5, 20);
            var again = new TalkMemory();
            Assert.That(again.Load(m.Save()), Is.True);
            Assert.That(again.Turns[0].Iori, Is.EqualTo("ん。"));
            Assert.That(again.Notes[0].Text, Is.EqualTo("キミは \"夜型\""));
            Assert.That(again.Load("{ broken"), Is.False);
            Assert.That(again.Turns.Count + again.Notes.Count, Is.EqualTo(0));
            Assert.That(again.Load(""), Is.False);
        }

        [Test]
        public void Anthropic_RequestHasHistoryThenTheNewWords()
        {
            var turns = new List<TalkTurn>
            {
                new TalkTurn { User = "ただいま", Iori = "ん。" },
                new TalkTurn { User = "…", Iori = "" },
            };
            var json = TalkApi.AnthropicRequest(Book().Llm, "system text", turns, "この曲どう?");
            var root = (Dictionary<string, object>)Json.Parse(json);
            Assert.That(root["model"], Is.EqualTo("claude-haiku-5-5"));
            Assert.That(root["max_tokens"], Is.EqualTo(300d));
            Assert.That(root["system"], Is.EqualTo("system text"));
            Assert.That(((Dictionary<string, object>)root["thinking"])["type"], Is.EqualTo("disabled"));
            Assert.That(((Dictionary<string, object>)root["output_config"])["effort"], Is.EqualTo("low"));
            var messages = (List<object>)root["messages"];
            Assert.That(messages.Count, Is.EqualTo(5));
            var last = (Dictionary<string, object>)messages[4];
            Assert.That(last["role"], Is.EqualTo("user"));
            Assert.That(last["content"], Is.EqualTo("この曲どう?"));
            Assert.That(((Dictionary<string, object>)messages[3])["content"], Is.EqualTo("……"));   // 黙った返事も空では送らない

            // 設定が空なら、その項目は送らない
            json = TalkApi.AnthropicRequest(new TalkLlm { Thinking = "", Effort = "" }, "s", null, "x");
            root = (Dictionary<string, object>)Json.Parse(json);
            Assert.That(root.ContainsKey("thinking"), Is.False);
            Assert.That(root.ContainsKey("output_config"), Is.False);
        }

        [Test]
        public void Anthropic_ReplyReadsTextBlocksOnly()
        {
            var ok = @"{ ""type"": ""message"", ""stop_reason"": ""end_turn"", ""content"": [
                { ""type"": ""thinking"", ""thinking"": """", ""signature"": ""x"" },
                { ""type"": ""text"", ""text"": ""いいね。"" }, { ""type"": ""text"", ""text"": ""夜に合う。"" } ] }";
            Assert.That(TalkApi.AnthropicReply(ok, out var error), Is.EqualTo("いいね。夜に合う。"));
            Assert.That(error, Is.Null);

            var refused = @"{ ""type"": ""message"", ""stop_reason"": ""refusal"", ""content"": [] }";
            Assert.That(TalkApi.AnthropicReply(refused, out error), Is.Null);
            Assert.That(error, Is.EqualTo("refusal"));

            var failed = @"{ ""type"": ""error"", ""error"": { ""type"": ""authentication_error"", ""message"": ""invalid x-api-key"" } }";
            Assert.That(TalkApi.AnthropicReply(failed, out error), Is.Null);
            Assert.That(error, Is.EqualTo("authentication_error: invalid x-api-key"));

            Assert.That(TalkApi.AnthropicReply("<html>", out error), Is.Null);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void Eleven_PicksVoiceAndModel()
        {
            var voices = @"{ ""voices"": [ { ""voice_id"": ""aaa"", ""name"": ""Rachel"" }, { ""voice_id"": ""bbb"", ""name"": ""Chinyaku - Cute Japanese Actor"" } ] }";
            Assert.That(TalkApi.PickVoice(voices, "chinyaku"), Is.EqualTo("bbb"));
            Assert.That(TalkApi.PickVoice(voices, "nobody"), Is.Null);
            Assert.That(TalkApi.PickVoice("oops", "x"), Is.Null);

            var models = @"[
              { ""model_id"": ""eleven_multilingual_v2"", ""name"": ""Eleven Multilingual v2"", ""can_do_text_to_speech"": true },
              { ""model_id"": ""eleven_turbo_v2_5"", ""name"": ""Eleven Turbo v2.5"", ""can_do_text_to_speech"": true },
              { ""model_id"": ""eleven_v4_sts"", ""name"": ""Eleven v4 Turbo Speech to Speech"", ""can_do_text_to_speech"": false },
              { ""model_id"": ""eleven_turbo_v4"", ""name"": ""Eleven v4 Turbo"", ""can_do_text_to_speech"": true } ]";
            Assert.That(TalkApi.PickModel(models, new[] { "v4 turbo", "v3" }), Is.EqualTo("eleven_turbo_v4"));
            Assert.That(TalkApi.PickModel(models, new[] { "v9", "turbo v2 5" }), Is.EqualTo("eleven_turbo_v2_5"));
            Assert.That(TalkApi.PickModel(models, new[] { "v9" }), Is.Null);
            Assert.That(TalkApi.PickModel("{}", new[] { "v3" }), Is.Null);
        }

        [Test]
        public void Eleven_RequestAndPcm()
        {
            var root = (Dictionary<string, object>)Json.Parse(TalkApi.ElevenRequest("ん。", "eleven_v3", ""));
            Assert.That(root["text"], Is.EqualTo("ん。"));
            Assert.That(root["model_id"], Is.EqualTo("eleven_v3"));
            Assert.That(root.ContainsKey("language_code"), Is.False);
            Assert.That(TalkApi.ElevenRequest("a", "m", "ja"), Does.Contain("\"language_code\":\"ja\""));
            Assert.That(TalkApi.ElevenSpeechUrl("abc", "pcm_24000"), Is.EqualTo("https://api.elevenlabs.io/v1/text-to-speech/abc?output_format=pcm_24000"));

            Assert.That(TalkApi.ElevenVoicesUrl("Chin yaku"), Is.EqualTo("https://api.elevenlabs.io/v2/voices?page_size=100&search=Chin%20yaku"));
            Assert.That(TalkApi.ElevenSharedVoicesUrl("Chinyaku"), Is.EqualTo("https://api.elevenlabs.io/v1/shared-voices?page_size=30&search=Chinyaku"));
            Assert.That(TalkApi.CleanKey(" \"sk_abc 123\n\" "), Is.EqualTo("sk_abc123"));
            Assert.That(TalkApi.LooksLikeVoiceId("EbuvaInXUGWtpYRUnKLQ"), Is.True);
            Assert.That(TalkApi.LooksLikeVoiceId("sk_0123456789abcdefg"), Is.False);
            Assert.That(TalkApi.LooksLikeVoiceId("short"), Is.False);

            var f = TalkApi.PcmToFloats(new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80, 0x01 });
            Assert.That(f.Length, Is.EqualTo(3));
            Assert.That(f[0], Is.EqualTo(0f));
            Assert.That(f[1], Is.EqualTo(32767 / 32768f));
            Assert.That(f[2], Is.EqualTo(-1f));
        }
    }
}
