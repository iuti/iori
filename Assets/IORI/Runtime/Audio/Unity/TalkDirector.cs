using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Iori.Audio
{
    /// <summary>
    /// イオリと LLM で話す係。ラジオと同じ GameObject に自動で 1 つ作られる。
    /// LLM なしで成り立つ作りの上に足したもの: キーが無い、通信できない、ならアプリは今までどおり(場面の一言と時報だけ)。
    /// - 何をどう言わせるか(人となり、返事の長さ、覚える量)は Core の <see cref="TalkBook"/>(StreamingAssets/IORI/talk.json)と
    ///   <see cref="TalkPrompt"/>、返事を整えるのは <see cref="TalkReply"/>、記憶は <see cref="TalkMemory"/>。ここは通信して鳴らすだけ。
    /// - キーは端末の中だけ(<see cref="SecretStore"/>)。記憶も端末の中だけ(persistentDataPath/talk_memory.json)。
    ///   撮影中(<see cref="MusicDirector.Persist"/> が false)は記憶を読み書きしない。
    /// - 返事は文字で <see cref="Replied"/> に渡し、声がオンなら ElevenLabs で作って <see cref="VoiceDirector.SpeakReply"/> で言う
    ///   (口は場面の声と同じ道で動く。時報と重なるなら時報のあと)。
    /// - 場面の声と時報は変えない(それを決めるのは VoicePicker とラジオの予定表のまま)。
    /// </summary>
    public sealed class TalkDirector : MonoBehaviour
    {
        const string BookFile = StreamingAudio.RootFolder + "/talk.json";
        const string LlmKeyName = "anthropic";
        const string VoiceKeyName = "elevenlabs";
        const string PrefVoiceOn = "iori.talk.voiceOn";
        const string PrefVoiceId = "iori.talk.voiceId";
        const string PrefVoiceFor = "iori.talk.voiceFor";
        const string PrefVoiceIdByHand = "iori.talk.voiceIdByHand";
        const string PrefVoiceModel = "iori.talk.voiceModel";
        const string PrefVoiceModelFor = "iori.talk.voiceModelFor";
        const int LlmTimeoutSeconds = 25;
        const int VoiceTimeoutSeconds = 20;

        public static TalkDirector Instance { get; private set; }

        public TalkBook Book { get; private set; }
        public TalkMemory Memory { get; } = new TalkMemory();
        public string LoadError { get; private set; }
        /// <summary>返事を待っている間 true。</summary>
        public bool Busy { get; private set; }
        /// <summary>動作確認用: 最後にうまくいかなかったわけ(キーは入らない)。</summary>
        public string LastError { get; private set; } = "";
        /// <summary>動作確認用: 最後の往復にかかった時間など。</summary>
        public string LastInfo { get; private set; } = "";

        /// <summary>イオリの返事(見せる文字)。通信できなかったときの一言もここに来る。</summary>
        public event Action<string> Replied;
        /// <summary>最後の返事(動作確認用)。</summary>
        public string LastReply { get; private set; } = "";

        void Tell(string text)
        {
            LastReply = text;
            Replied?.Invoke(text);
        }

        int _asking;        // 何回目の問いか(古い問いの声を鳴らさないため)
        string _voiceId, _voiceModel;
        bool _voiceModelFailed;
        System.Random _rng = new System.Random();

        static string MemoryPath => Path.Combine(Application.persistentDataPath, "talk_memory.json");

        static string LlmKey => TalkApi.CleanKey(SecretStore.Get(LlmKeyName));
        static string VoiceKey => TalkApi.CleanKey(SecretStore.Get(VoiceKeyName));

        public bool HasLlmKey => LlmKey.Length > 0;
        public bool HasVoiceKey => VoiceKey.Length > 0;
        /// <summary>話せるか(決まりが読めていて、LLM のキーがある)。false なら、イオリは今までどおり場面の一言だけ。</summary>
        public bool Ready => Book != null && HasLlmKey;

        /// <summary>返事を声でも言うか(設定。キーが無ければどのみち文字だけ)。</summary>
        public bool VoiceOn
        {
            get => PlayerPrefs.GetInt(PrefVoiceOn, 1) == 1;
            set => PlayerPrefs.SetInt(PrefVoiceOn, value ? 1 : 0);
        }

        /// <summary>LLM のキーを入れる(空で消す)。設定画面から呼ぶ。</summary>
        public void SetLlmKey(string key) => SecretStore.Set(LlmKeyName, TalkApi.CleanKey(key));

        /// <summary>声の id を手で決める(空で、名前で探すほうに戻す)。ElevenLabs の声の画面で「Copy voice ID」したもの。</summary>
        public void SetVoiceId(string id)
        {
            id = (id ?? "").Trim();
            if (id.Length == 0) PlayerPrefs.DeleteKey(PrefVoiceIdByHand);
            else PlayerPrefs.SetString(PrefVoiceIdByHand, id);
            _voiceId = null;
            PlayerPrefs.DeleteKey(PrefVoiceId);
        }

        /// <summary>手で決めた声の id(無ければ空)。</summary>
        public string VoiceIdByHand => PlayerPrefs.GetString(PrefVoiceIdByHand, "");

        /// <summary>声(ElevenLabs)のキーを入れる(空で消す)。</summary>
        public void SetVoiceKey(string key)
        {
            SecretStore.Set(VoiceKeyName, TalkApi.CleanKey(key));
            _voiceId = _voiceModel = null;
            _voiceModelFailed = false;
            PlayerPrefs.DeleteKey(PrefVoiceId);
            PlayerPrefs.DeleteKey(PrefVoiceModel);
        }

        void Awake() => Instance = this;

        void OnDestroy() { if (Instance == this) Instance = null; }

        IEnumerator Start()
        {
            string json = null;
            yield return StreamingAudio.LoadText(BookFile, t => json = t);
            if (json == null)
            {
                LoadError = "StreamingAssets/" + BookFile + " が読めません";
                Debug.LogWarning("[IORI] " + LoadError + "(イオリとは話せません)");
                yield break;
            }
            try { Book = TalkBook.Parse(json); }
            catch (FormatException e)
            {
                LoadError = e.Message;
                Debug.LogError("[IORI] talk.json の読み込みに失敗しました: " + e.Message);
                yield break;
            }
            LoadMemory();
        }

        void LoadMemory()
        {
            if (!MusicDirector.Persist) return;
            try
            {
                if (File.Exists(MemoryPath)) Memory.Load(File.ReadAllText(MemoryPath));
            }
            catch (Exception e) { Debug.LogWarning("[IORI] 会話の記憶が読めません: " + e.Message); }
        }

        void SaveMemory()
        {
            if (!MusicDirector.Persist) return;
            try { File.WriteAllText(MemoryPath, Memory.Save()); }
            catch (Exception e) { Debug.LogWarning("[IORI] 会話の記憶を書けません: " + e.Message); }
        }

        /// <summary>覚え書きを 1 つ忘れる(設定画面から)。</summary>
        public void ForgetNote(int index)
        {
            Memory.RemoveNoteAt(index);
            SaveMemory();
        }

        /// <summary>全部忘れる(設定画面から)。</summary>
        public void ForgetAll()
        {
            Memory.Clear();
            SaveMemory();
        }

        /// <summary>イオリに話しかける。受け付けたら true(返事は <see cref="Replied"/> に来る)。</summary>
        public bool Ask(string text)
        {
            text = (text ?? "").Trim();
            if (!Ready || Busy || text.Length == 0) return false;
            if (text.Length > Book.MaxInputChars) text = text.Substring(0, Book.MaxInputChars);
            StartCoroutine(AskRoutine(text, ++_asking));
            return true;
        }

        /// <summary>撮影や動作確認用: 通信せずに、この文を返事として見せる。</summary>
        public void ShowReply(string text) => Tell(text);

        TalkScene SceneNow(out long nowMs)
        {
            var radio = RadioDirector.Instance;
            var now = radio ? radio.VirtualNow : DateTimeOffset.UtcNow;
            nowMs = now.ToUnixTimeMilliseconds();
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
            var scene = new TalkScene
            {
                Year = local.Year, Month = local.Month, Day = local.Day, Hour = local.Hour, Minute = local.Minute,
                Weekday = local.DayOfWeek,
                Weather = radio ? radio.Weather : WeatherIds.Clear,
                FireLit = true,
                Source = "",
            };
            if (radio && radio.Plan != null)
            {
                var t = radio.Plan.ToPlanTime(nowMs);
                scene.Phase = radio.Plan.SpanAt(t)?.Phase;
                if (!radio.PlayerOn && radio.Plan.ClipsAt(t).Count > 0) scene.Source = "radio";
            }
            if (radio && radio.Mixer != null)
                scene.FireLit = radio.Mixer.GetUserVolume("fire") > 0.001f && !radio.Mixer.IsMuted("fire");
            var music = GetComponent<MusicDirector>();
            if (music && music.IsPlaying && music.Current != null)
            {
                scene.Source = "player";
                scene.SongTitle = music.Current.Title;
                scene.SongArtist = music.Current.Artist;
            }
            return scene;
        }

        IEnumerator AskRoutine(string text, int asking)
        {
            Busy = true;
            LastError = "";
            float started = Time.realtimeSinceStartup;
            var scene = SceneNow(out var nowMs);
            var system = TalkPrompt.System(Book, scene, new List<TalkNote>(Memory.Notes));
            var body = TalkApi.AnthropicRequest(Book.Llm, system, Memory.Recent(nowMs, Book.RecentTurns, Book.RecentHours), text);

            string raw = null, error = null;
            using (var req = new UnityWebRequest(TalkApi.AnthropicUrl, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-api-key", LlmKey);
                req.SetRequestHeader("anthropic-version", TalkApi.AnthropicVersion);
                req.timeout = LlmTimeoutSeconds;
                yield return req.SendWebRequest();
                var response = req.downloadHandler != null ? req.downloadHandler.text : null;
                // (エラーのときも本文にわけが書いてある)
                if (!string.IsNullOrEmpty(response)) raw = TalkApi.AnthropicReply(response, out error);
                if (raw == null && error == null) error = req.error ?? "返事がありません";
                if (raw == null && req.responseCode > 0) error = "HTTP " + req.responseCode + " " + error;
            }
            float llmSeconds = Time.realtimeSinceStartup - started;

            if (raw == null)
            {
                Fail("LLM: " + error);
                yield break;
            }

            var reply = TalkReply.Tidy(raw, Book.MaxSentences, Book.MaxChars, Book.NoteChars);
            if (reply.Say.Length == 0) { reply.Say = "……"; reply.HasVoice = false; }
            Memory.AddTurn(nowMs, text, reply.Say);
            if (reply.Memo != null) Memory.AddNote(nowMs, reply.Memo, Book.MaxNotes, Book.NoteChars);
            SaveMemory();

            Busy = false;
            LastInfo = $"LLM {llmSeconds:0.0} 秒";
            Tell(reply.Say);

            if (reply.HasVoice && VoiceOn && HasVoiceKey && VoiceDirector.Instance)
                yield return SpeakRoutine(reply.Say, asking);
        }

        void Fail(string why)
        {
            Busy = false;
            LastError = why;
            Debug.LogWarning("[IORI] イオリと話せませんでした: " + why);
            var lines = Book.FailLines;
            Tell(lines.Count > 0 ? lines[_rng.Next(lines.Count)] : "……");
            // (声のセリフ帳に talk_fail があれば、それも言う。無ければ文字だけ)
            if (VoiceDirector.Instance) VoiceDirector.Instance.Say(VoiceCues.TalkFail);
        }

        // ---- 声

        IEnumerator SpeakRoutine(string text, int asking)
        {
            float started = Time.realtimeSinceStartup;
            if (_voiceId == null) yield return ResolveVoice();
            if (_voiceId == null) yield break;

            byte[] pcm = null;
            string error = null;
            using (var req = new UnityWebRequest(TalkApi.ElevenSpeechUrl(_voiceId, Book.Voice.Format), UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(TalkApi.ElevenRequest(text, _voiceModel, Book.Voice.Language)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("xi-api-key", VoiceKey);
                req.timeout = VoiceTimeoutSeconds;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) pcm = req.downloadHandler.data;
                else error = "HTTP " + req.responseCode + " " + req.error + " " + Short(req.downloadHandler != null ? req.downloadHandler.text : "");
            }
            if (pcm == null || pcm.Length < 2)
            {
                LastError = "声: " + (error ?? "空の返事") + "(モデル " + _voiceModel + ")";
                Debug.LogWarning("[IORI] イオリの声を作れませんでした: " + LastError);
                // 選んだモデルで作れなかったら、次からは確実なほうのモデルにする
                if (!_voiceModelFailed && _voiceModel != Book.Voice.FallbackModel)
                {
                    _voiceModelFailed = true;
                    _voiceModel = Book.Voice.FallbackModel;
                    PlayerPrefs.DeleteKey(PrefVoiceModel);
                }
                yield break;
            }
            LastInfo += $"  声 {Time.realtimeSinceStartup - started:0.0} 秒({_voiceModel})";
            // 待っている間に次の問いが始まっていたら、古い返事は言わない
            if (asking != _asking || !VoiceDirector.Instance) yield break;
            VoiceDirector.Instance.SpeakReply(TalkApi.PcmToFloats(pcm), Book.Voice.SampleRate, Book.Voice.Volume);
        }

        /// <summary>声の id とモデルの id を、名前から探す(一度見つけたら端末に覚えておく)。</summary>
        IEnumerator ResolveVoice()
        {
            var voice = Book.Voice;
            var modelsKey = string.Join("|", voice.Models);
            if (PlayerPrefs.GetString(PrefVoiceFor, "") == voice.VoiceName) _voiceId = NullIfEmpty(PlayerPrefs.GetString(PrefVoiceId, ""));
            if (PlayerPrefs.GetString(PrefVoiceModelFor, "") == modelsKey) _voiceModel = NullIfEmpty(PlayerPrefs.GetString(PrefVoiceModel, ""));

            // 手で決めた id、talk.json に書いた id があれば、一覧は読まない
            var byHand = VoiceIdByHand;
            if (byHand.Length > 0) _voiceId = byHand;
            else if (voice.VoiceId.Length > 0) _voiceId = voice.VoiceId;

            if (_voiceId == null)
            {
                // 自分の声(ライブラリから足したものも含む)から探し、無ければみんなの声のライブラリから探す
                string json = null, error = null, sharedError = null;
                yield return ElevenGet(TalkApi.ElevenVoicesUrl(voice.VoiceName), (t, e) => { json = t; error = e; });
                _voiceId = json != null ? TalkApi.PickVoice(json, voice.VoiceName) : null;
                if (_voiceId == null)
                {
                    string shared = null;
                    yield return ElevenGet(TalkApi.ElevenSharedVoicesUrl(voice.VoiceName), (t, e) => { shared = t; sharedError = e; });
                    _voiceId = shared != null ? TalkApi.PickVoice(shared, voice.VoiceName) : null;
                }
                if (_voiceId == null)
                {
                    LastError = error != null || sharedError != null
                        ? "声: ElevenLabs の声の一覧を読めません(" + (error ?? sharedError) + ")。キーに声の一覧を読む権限が無いなら、声の ID を貼ると一覧を読まずに使える"
                        : "声: ElevenLabs に「" + voice.VoiceName + "」という名前の声が見つかりません。声の ID を貼ると名前で探さずに使える";
                    Debug.LogWarning("[IORI] " + LastError);
                    yield break;
                }
                PlayerPrefs.SetString(PrefVoiceId, _voiceId);
                PlayerPrefs.SetString(PrefVoiceFor, voice.VoiceName);
            }
            if (_voiceModel == null)
            {
                string json = null;
                yield return ElevenGet(TalkApi.ElevenBase + "/models", (t, e) => json = t);
                var picked = json != null ? TalkApi.PickModel(json, voice.Models) : null;
                _voiceModel = picked ?? voice.FallbackModel;
                Debug.Log("[IORI] イオリの声のモデル: " + _voiceModel + (picked == null ? "(一覧に望みのものが無いので予備)" : ""));
                if (picked != null)
                {
                    PlayerPrefs.SetString(PrefVoiceModel, _voiceModel);
                    PlayerPrefs.SetString(PrefVoiceModelFor, modelsKey);
                }
            }
        }

        /// <summary>ElevenLabs から読む。done(本文, 失敗のわけ): うまくいけば本文、だめなら null とわけ(キーは入らない)。</summary>
        IEnumerator ElevenGet(string url, Action<string, string> done)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("xi-api-key", VoiceKey);
                req.timeout = VoiceTimeoutSeconds;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) done(req.downloadHandler.text, null);
                else
                {
                    // (断られたわけは本文に書いてある)
                    var why = "HTTP " + req.responseCode + " " + req.error + " " + Short(req.downloadHandler != null ? req.downloadHandler.text : "");
                    Debug.LogWarning("[IORI] ElevenLabs " + url.Substring(TalkApi.ElevenHost.Length) + ": " + why);
                    done(null, why);
                }
            }
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 200 ? s.Substring(0, 200) : s;

        /// <summary>動作確認パネルに出す状態。</summary>
        public string Status
        {
            get
            {
                if (LoadError != null) return "会話: " + LoadError;
                if (Book == null) return "会話: 読み込み中";
                var s = $"会話: LLM のキー {(HasLlmKey ? "あり" : "なし")} / 声のキー {(HasVoiceKey ? "あり" : "なし")} / 声 {(VoiceOn ? "オン" : "オフ")} / 声の ID {(VoiceIdByHand.Length > 0 ? "手で決めた" : "名前で探す")}\n"
                        + $"覚え書き {Memory.Notes.Count} 個、やり取り {Memory.Turns.Count} 回  {LastInfo}";
                if (LastReply.Length > 0) s += "\n最後の返事: " + LastReply;
                if (LastError.Length > 0) s += "\n" + LastError;
                return s;
            }
        }
    }
}
