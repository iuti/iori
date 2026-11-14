using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// イオリの声の係。どのシーンでもラジオと同じ GameObject に自動で 1 つ作られる。
    /// - 場面の声(あいさつ、おかえり、プレーヤーを開いた、曲が止まった、タップ、雨、火): <see cref="Say"/> で頼まれると、
    ///   何を言うかを <see cref="VoicePicker"/>(Core)に決めてもらい、Unity のオーディオで鳴らす(アプリが前にいる間だけ)。
    /// - 時報: ラジオの予定表に入っていて、再生係(Android はサービス、PC は UnityAudioBackend)が鳴らす。
    ///   ここでは鳴らさず、予定表の時刻に合わせて口だけ動かす(再生係と同じく、2 秒より遅れたものは無かったことにする)。
    /// - 会話の返事: <see cref="TalkDirector"/> が作った声を <see cref="SpeakReply"/> で受け取って言う。時報と重なるなら時報のあとに回し、
    ///   返事を言っている間は場面の声を言わない。
    /// - 口: 声の波形から <see cref="LipSync"/> で口の動きを作り、再生位置で引いて <see cref="IoriMouth"/> に渡す。
    /// </summary>
    public sealed class VoiceDirector : MonoBehaviour
    {
        const string BookFile = StreamingAudio.RootFolder + "/voice.json";
        const long PlannedLateLimitMs = 2000;   // 再生係の単発音の遅れの許し(UnityAudioBackend、PlanEngine.java と同じ)
        const float StartGreetDelay = 1.5f;     // 起動して画面が落ち着いてから
        const float FireSettleSeconds = 0.8f;   // 火のつまみを動かし終えてから

        public static VoiceDirector Instance { get; private set; }

        /// <summary>true を返す間は場面の声を言わない(イオリが眠っているときなど)。キャンプの側が入れる。</summary>
        public static Func<bool> Hushed;

        public VoiceBook Book { get; private set; }
        public string LoadError { get; private set; }
        /// <summary>声の音量(0〜1)。場面の声に掛かる(時報は予定表の単発音の音量)。</summary>
        public float Volume = 1f;
        /// <summary>動作確認用: 最後に言った(言おうとした)ことの一言。</summary>
        public string LastEvent { get; private set; } = "";

        VoicePicker _picker;
        AudioSource _source;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, MouthTrack> _tracks = new Dictionary<string, MouthTrack>();
        readonly HashSet<string> _loading = new HashSet<string>();

        MouthTrack _eventTrack;              // いま鳴らしている場面の声の口
        ScheduledOneShot _planned;           // いま鳴っているはずの予定の声(時報)
        long _lastPlanT = long.MinValue;
        bool _greeted;
        string _lastWeather;
        bool _fireLit = true, _fireKnown;
        float _fireLitAt = -1f;
        bool _wasSpeaking;
        AudioClip _replyClip;                // 会話の返事の声(その場で作ったもの)
        Coroutine _replyWait;                // 返事を言い出すのを待っている(時報を先に通す)

        void Awake()
        {
            Instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            gameObject.AddComponent<PuppetMouthLink>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            IoriMouth.Release();
        }

        IEnumerator Start()
        {
            string json = null;
            yield return StreamingAudio.LoadText(BookFile, t => json = t);
            if (json == null)
            {
                LoadError = "StreamingAssets/" + BookFile + " が読めません";
                Debug.LogWarning("[IORI] " + LoadError + "(イオリは場面では話しません)");
                yield break;
            }
            try { Book = VoiceBook.Parse(json); }
            catch (FormatException e)
            {
                LoadError = e.Message;
                Debug.LogError("[IORI] voice.json の読み込みに失敗しました: " + e.Message);
                yield break;
            }
            _picker = new VoicePicker(Book, (ulong)DateTime.UtcNow.Ticks);
            var music = GetComponent<MusicDirector>();
            if (music) music.QueueFinished += OnQueueFinished;

            // 時間帯が分かってから、あいさつ
            while (RadioDirector.Instance == null || RadioDirector.Instance.Plan == null) yield return null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ioriVoiceTest") >= 0)
            {
                _greeted = true;
                yield return SelfTest();
                yield break;
            }
            yield return new WaitForSecondsRealtime(StartGreetDelay);
            if (!_greeted)
            {
                _greeted = true;
                Say(VoiceCues.Greet);
            }
        }

        void OnQueueFinished() => Say(VoiceCues.MusicStopped);

        void OnApplicationPause(bool paused)
        {
            // 裏に回るとき、言いかけの声は止める(戻ってきたときの「おかえり」は、家に帰ったのが分かる仕組みができたら)
            if (paused && _source.isPlaying) _source.Stop();
            if (paused) DropReply();
        }

        /// <summary>会話の返事を言おうとしている、または言っている。</summary>
        public bool ReplySpeaking => _replyWait != null || (_replyClip && _source.isPlaying && _source.clip == _replyClip);

        /// <summary>
        /// 会話の返事の声を言う(モノラルの波形)。予定された声(時報)が鳴っている、または返事の途中に始まるなら、それが終わるまで待つ。
        /// 前の返事を言っている途中なら、それは止めて新しいほうを言う。
        /// </summary>
        public void SpeakReply(float[] samples, int sampleRate, float volume)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0) return;
            DropReply();
            var clip = AudioClip.Create("talk reply", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            _replyClip = clip;
            _replyWait = StartCoroutine(PlayReply(clip, LipSync.Analyze(samples, 1, sampleRate), volume));
        }

        IEnumerator PlayReply(AudioClip clip, MouthTrack track, float volume)
        {
            long lengthMs = (long)(clip.length * 1000);
            for (float waited = 0f; waited < 20f; waited += Time.unscaledDeltaTime)
            {
                var now = PlanNow();
                var next = NearestPlannedVoice(now);
                bool soon = next != long.MaxValue && next > now - 1000 && next - now < lengthMs + 500;
                if (_planned == null && !soon) break;
                yield return null;
            }
            _replyWait = null;
            _source.Stop();
            _source.clip = clip;
            _source.volume = Mathf.Clamp01(volume * Volume);
            _source.Play();
            _eventTrack = track;
            LastEvent = "会話の返事";
            _picker?.NoteSpoken(PlanNow() + lengthMs);
        }

        void DropReply()
        {
            if (_replyWait != null) { StopCoroutine(_replyWait); _replyWait = null; }
            if (!_replyClip) return;
            if (_source.clip == _replyClip) { _source.Stop(); _source.clip = null; _eventTrack = null; }
            Destroy(_replyClip);
            _replyClip = null;
        }

        /// <summary>
        /// 場面(<see cref="VoiceCues"/>)を伝える。言うかどうか、何を言うかはセリフ帳の決まりで決まる(黙ることも多い)。
        /// 言うことにしたら true。force(動作確認用)なら、間隔や確率の決まりと眠りを無視して言う。
        /// </summary>
        public bool Say(string cue, bool force = false)
        {
            if (cue == null || _picker == null) return false;
            if (!force && Hushed != null && Hushed()) { LastEvent = cue + ": 眠っているので黙る"; return false; }
            if (!force && ReplySpeaking) { LastEvent = cue + ": 会話の返事の途中なので黙る"; return false; }
            var radio = RadioDirector.Instance;
            var now = PlanNow();
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), TimeZoneInfo.Local);
            var moment = new VoiceMoment
            {
                NowMs = now,
                Phase = radio && radio.Plan != null ? radio.Plan.SpanAt(radio.Plan.ToPlanTime(now))?.Phase : null,
                LocalHour = local.Hour,
                NextScheduledVoiceMs = NearestPlannedVoice(now),
            };
            var line = _picker.Pick(cue, moment, force);
            if (line == null) { LastEvent = cue + ": 黙る"; return false; }
            LastEvent = cue + ": " + line.File;
            WithClip(line.File, clip =>
            {
                // (読み込みの間に時報が始まっていたら、重ねない)
                if (_planned != null && !force) return;
                if (ReplySpeaking && !force) return;
                _source.Stop();
                _source.clip = clip;
                _source.volume = Mathf.Clamp01(line.Volume * Volume);
                _source.Play();
                _eventTrack = _tracks.TryGetValue(line.File, out var tr) ? tr : null;
                _picker.NoteSpoken(PlanNow() + (long)(clip.length * 1000));
            });
            return true;
        }

        long PlanNow()
        {
            var radio = RadioDirector.Instance;
            return radio ? radio.VirtualNow.ToUnixTimeMilliseconds() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>いまにいちばん近い予定の声の時刻(無ければ long.MaxValue)。</summary>
        long NearestPlannedVoice(long now)
        {
            var plan = RadioDirector.Instance ? RadioDirector.Instance.Plan : null;
            if (plan == null) return long.MaxValue;
            var t = plan.ToPlanTime(now);
            long best = long.MaxValue, bestDist = long.MaxValue;
            foreach (var s in plan.OneShots)
            {
                if (s.Kind != OneShotKinds.Voice) continue;
                var d = Math.Abs(s.AtMs - t);
                if (d < bestDist) { bestDist = d; best = s.AtMs - t + now; }
                if (s.AtMs > t) break;
            }
            return best;
        }

        void Update()
        {
            var radio = RadioDirector.Instance;
            WatchPlan(radio);
            WatchScene(radio);
            DriveMouth(radio);
        }

        /// <summary>予定表の声(時報): 近づいたら口の動きを用意し、時刻になったら口を動かし始める。</summary>
        void WatchPlan(RadioDirector radio)
        {
            var plan = radio ? radio.Plan : null;
            if (plan == null) return;
            var t = plan.ToPlanTime(PlanNow());
            if (_lastPlanT == long.MinValue || t < _lastPlanT - 1000) _lastPlanT = t; // 始めたて、時計が戻った
            foreach (var s in plan.OneShots)
            {
                if (s.Kind != OneShotKinds.Voice) continue;
                if (s.AtMs > t + 30_000) break;
                if (s.AtMs > t) { WithClip(s.File, null); continue; } // もうすぐ: 先に読んでおく
                if (s.AtMs <= _lastPlanT || t - s.AtMs > PlannedLateLimitMs) continue;
                _planned = s;
                // 場面の声が鳴っていても、時報が優先(再生係は止められないので、こちらが引く)
                if (_source.isPlaying) _source.Stop();
                _eventTrack = null;
                WithClip(s.File, clip => _picker?.NoteSpoken(s.AtMs + (long)(clip.length * 1000)));
            }
            _lastPlanT = t;
        }

        /// <summary>天気と焚き火の変わり目を見て、場面を伝える。</summary>
        void WatchScene(RadioDirector radio)
        {
            if (!radio || radio.Program == null || radio.Mixer == null) return;
            if (_lastWeather == null) _lastWeather = radio.Weather;
            else if (radio.Weather != _lastWeather)
            {
                var wasRain = _lastWeather == WeatherIds.Rain;
                _lastWeather = radio.Weather;
                var isRain = radio.Weather == WeatherIds.Rain;
                if (isRain != wasRain) Say(isRain ? VoiceCues.RainStart : VoiceCues.RainStop);
            }

            var lit = radio.Mixer.GetUserVolume("fire") > 0.001f && !radio.Mixer.IsMuted("fire");
            if (!_fireKnown) { _fireKnown = true; _fireLit = lit; }
            else if (lit != _fireLit)
            {
                _fireLit = lit;
                _fireLitAt = lit ? Time.unscaledTime : -1f;
            }
            if (_fireLitAt >= 0f && Time.unscaledTime - _fireLitAt > FireSettleSeconds && !Input.GetMouseButton(0))
            {
                _fireLitAt = -1f;
                Say(VoiceCues.Firewood);
            }
        }

        void DriveMouth(RadioDirector radio)
        {
            bool speaking = false;
            float open = 0f, vowel = 0f;
            if (_source.isPlaying && _source.clip && _eventTrack != null)
            {
                var sec = _source.timeSamples / (double)_source.clip.frequency;
                open = _eventTrack.OpenAt(sec);
                vowel = _eventTrack.VowelAt(sec);
                speaking = true;
            }
            else if (_planned != null)
            {
                var plan = radio ? radio.Plan : null;
                var sec = plan != null ? (plan.ToPlanTime(PlanNow()) - _planned.AtMs) / 1000.0 : double.MaxValue;
                if (_tracks.TryGetValue(_planned.File, out var tr) && sec >= 0 && sec < tr.DurationSeconds)
                {
                    open = tr.OpenAt(sec);
                    vowel = tr.VowelAt(sec);
                    speaking = true;
                }
                else if (sec > 10 || (tr != null && sec >= tr.DurationSeconds)) _planned = null;
            }
            if (speaking) IoriMouth.Set(open, vowel);
            else if (_wasSpeaking) IoriMouth.Release();
            _wasSpeaking = speaking;
        }

        void WithClip(string file, Action<AudioClip> done)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (_clips.TryGetValue(file, out var cached))
            {
                if (cached) done?.Invoke(cached);
                return;
            }
            if (_loading.Contains(file))
            {
                if (done != null) StartCoroutine(WaitFor(file, done));
                return;
            }
            _loading.Add(file);
            StartCoroutine(StreamingAudio.LoadClip(file, clip =>
            {
                _loading.Remove(file);
                _clips[file] = clip;
                if (!clip) return;
                _tracks[file] = Analyze(clip);
                done?.Invoke(clip);
            }));
        }

        IEnumerator WaitFor(string file, Action<AudioClip> done)
        {
            while (_loading.Contains(file)) yield return null;
            if (_clips.TryGetValue(file, out var clip) && clip) done(clip);
        }

        static MouthTrack Analyze(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0))
            {
                Debug.LogWarning("[IORI] 声の波形が読めないので口は動きません: " + clip.name);
                return new MouthTrack(new float[0], new float[0]);
            }
            return LipSync.Analyze(data, clip.channels, clip.frequency);
        }

        /// <summary>
        /// 動作確認(起動引数 -ioriVoiceTest): 場面の声をひととおり言わせ、次の正時の直前に時計を進めて時報を待ち、
        /// それぞれの口の開きの最大と、口をつないだ相手の数をログに書いて終わる。
        /// </summary>
        IEnumerator SelfTest()
        {
            yield return new WaitForSecondsRealtime(2f);
            var cues = new[] { VoiceCues.Greet, VoiceCues.Tap, VoiceCues.Tap, VoiceCues.PlayerOpen, VoiceCues.MusicStopped, VoiceCues.RainStart, VoiceCues.RainStop, VoiceCues.Firewood };
            foreach (var cue in cues)
            {
                Say(cue, true);
                float max = 0f, sum = 0f; int frames = 0, speaking = 0;
                for (var t = 0f; t < 4.5f; t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    max = Mathf.Max(max, IoriMouth.Open); sum += IoriMouth.Open; frames++;
                    if (IoriMouth.Speaking) speaking++;
                }
                Debug.Log($"[IORI] voice test {LastEvent}  mouth max {max:0.00} mean {sum / Mathf.Max(1, frames):0.00} speaking {speaking}/{frames} frames  targets {TargetCount()}");
            }
            var radio = RadioDirector.Instance;
            radio.JumpBeforeNextHour(3);
            float jmax = 0f; string heard = null;
            for (var t = 0f; t < 9f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                jmax = Mathf.Max(jmax, IoriMouth.Open);
                if (_planned != null) heard = _planned.File;
            }
            Debug.Log($"[IORI] voice test jiho {heard ?? "(none)"}  mouth max {jmax:0.00}");
            radio.SetClockOffset(0);
            Debug.Log("[IORI] voice test done");
            Application.Quit();
        }

        static int TargetCount()
        {
            var link = Instance ? Instance.GetComponent<PuppetMouthLink>() : null;
            return link ? link.Count : 0;
        }

        /// <summary>動作確認パネルに出す状態。</summary>
        public string Status
        {
            get
            {
                if (LoadError != null) return "声: " + LoadError;
                var what = _source.isPlaying && _source.clip ? _source.clip.name : _planned != null ? _planned.File + "(時報)" : "—";
                return $"声: {what}  口 {IoriMouth.Open:0.00} / 形 {IoriMouth.Vowel:+0.00;-0.00}\n最後: {LastEvent}";
            }
        }
    }
}
