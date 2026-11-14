using System;
using System.Collections;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// ラジオの司令塔。番組表を読み、状況(時刻、天気、静けさモード、ミキサー)から予定表を作って再生係に渡す。
    /// どのシーンでも自動で 1 つだけ作られる。
    /// </summary>
    public sealed class RadioDirector : MonoBehaviour
    {
        const string ProgramFile = StreamingAudio.RootFolder + "/radio.json";
        const string IndexFile = StreamingAudio.RootFolder + "/audio-index.json";
        const string PrefWeather = "iori.radio.weather";
        const string PrefQuiet = "iori.radio.quiet";
        static readonly TimeSpan RebuildInterval = TimeSpan.FromMinutes(30);

        public static RadioDirector Instance { get; private set; }

        public RadioProgram Program { get; private set; }
        public AmbientMixer Mixer { get; private set; }
        public IPlaybackBackend Backend { get; private set; }
        public PlaybackPlan Plan { get; private set; }
        public string LoadError { get; private set; }

        public string Weather { get; private set; } = WeatherIds.Clear;
        public bool Quiet { get; private set; }
        /// <summary>音楽プレーヤーが自分の曲を鳴らしている間は true。ラジオは黙り、環境音だけ鳴る。保存しない。</summary>
        public bool PlayerOn { get; private set; }
        /// <summary>ラジオの曲の音量(0〜1)。中身は設定(<see cref="IoriSettings.RadioVolume"/>)。</summary>
        public float MusicVolume => SettingsDirector.Store.GetFloat(IoriSettings.RadioVolume);
        /// <summary>動作確認用の時計のずれ。保存しない。</summary>
        public long ClockOffsetMs { get; private set; }

        AudioIndex _audioIndex = new AudioIndex();
        DateTime _lastBuiltUtc;
        IDisposable _watchMusic, _watchAmbient;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("IORI Radio");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsDirector>();
            go.AddComponent<RadioDirector>();
            go.AddComponent<MusicDirector>();
            go.AddComponent<VoiceDirector>();
            go.AddComponent<RadioDebugPanel>();
        }

        void Awake()
        {
            Instance = this;
            Weather = PlayerPrefs.GetString(PrefWeather, WeatherIds.Clear);
            Quiet = PlayerPrefs.GetInt(PrefQuiet, 0) == 1;
            // 音量は設定から読み、設定が変われば(ラジオのシートでも、設定の画面でも)鳴らす音量を合わせる
            var settings = SettingsDirector.Store;
            _watchMusic = settings.Watch(IoriSettings.RadioVolume, _ => ApplyMusicVolume());
            _watchAmbient = settings.Watch(IoriSettings.AmbientPrefix, key => ApplyAmbientVolume(IoriSettings.AmbientIdOf(key)));
#if UNITY_ANDROID && !UNITY_EDITOR
            Backend = new AndroidPlaybackBackend();
#else
            Backend = gameObject.AddComponent<UnityAudioBackend>();
#endif
        }

        IEnumerator Start()
        {
            string programJson = null, indexJson = null;
            yield return StreamingAudio.LoadText(ProgramFile, t => programJson = t);
            yield return StreamingAudio.LoadText(IndexFile, t => indexJson = t);
            if (programJson == null)
            {
                LoadError = "StreamingAssets/" + ProgramFile + " が読めません";
                Debug.LogError("[IORI] " + LoadError);
                yield break;
            }
            try
            {
                Program = RadioProgram.Parse(programJson);
                _audioIndex = AudioIndex.Parse(indexJson);
            }
            catch (FormatException e)
            {
                LoadError = e.Message;
                Debug.LogError("[IORI] 番組表の読み込みに失敗しました: " + e.Message);
                yield break;
            }
            if (indexJson == null)
                Debug.LogWarning("[IORI] audio-index.json がないので、曲はすべて継ぎ目ループとして扱います(メニュー IORI/Audio から生成できます)");
            Mixer = new AmbientMixer(Program.Ambient);
            var settings = SettingsDirector.Store;
            settings.Define(IoriSettings.Ambient(Program.Ambient));
            foreach (var layer in Program.Ambient) Mixer.SetUserVolume(layer.Id, settings.GetFloat(IoriSettings.AmbientVolume(layer.Id)));
            Rebuild();
        }

        void Update()
        {
            if (Program == null) return;
            if (DateTime.UtcNow - _lastBuiltUtc > RebuildInterval) Rebuild();
        }

        void OnDestroy()
        {
            _watchMusic?.Dispose();
            _watchAmbient?.Dispose();
            if (Instance == this) Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            // 裏に回るときは何もしない(Android ではサービスが鳴らし続ける)。戻ってきたら作り直す。
            if (paused) PlayerPrefs.Save();
            else if (Program != null) Rebuild();
        }

        public DateTimeOffset VirtualNow => DateTimeOffset.UtcNow.AddMilliseconds(ClockOffsetMs);

        public void Rebuild()
        {
            if (Program == null) return;
            var ctx = new RadioContext
            {
                Now = VirtualNow,
                TimeZone = TimeZoneInfo.Local,
                Weather = Weather,
                QuietOverride = Quiet,
                PlayerOn = PlayerOn,
                ClockOffsetMs = ClockOffsetMs,
                MusicVolume = MusicVolume,
                Audio = _audioIndex,
            };
            Plan = RadioScheduler.BuildPlan(Program, ctx, Mixer);
            _lastBuiltUtc = DateTime.UtcNow;
            Backend.ApplyPlan(Plan, Plan.ToJson());
        }

        public void SetWeather(string weather)
        {
            Weather = weather;
            PlayerPrefs.SetString(PrefWeather, weather);
            Rebuild();
        }

        public void SetQuiet(bool quiet)
        {
            Quiet = quiet;
            PlayerPrefs.SetInt(PrefQuiet, quiet ? 1 : 0);
            Rebuild();
        }

        /// <summary>音楽プレーヤーが鳴らし始めたとき true、止めたとき false にする。</summary>
        public void SetPlayerOn(bool on)
        {
            if (PlayerOn == on) return;
            PlayerOn = on;
            Rebuild();
        }

        /// <summary>ラジオの曲の音量を変える(設定に書くだけ。鳴らす音量は設定を聞いて合わせる)。</summary>
        public void SetMusicVolume(float volume) => SettingsDirector.Store.SetNumber(IoriSettings.RadioVolume, volume);

        /// <summary>環境音 1 つの音量を変える(設定に書くだけ)。</summary>
        public void SetAmbientVolume(string id, float volume) => SettingsDirector.Store.SetNumber(IoriSettings.AmbientVolume(id), volume);

        void ApplyMusicVolume()
        {
            var v = MusicVolume;
            if (Plan != null) Plan.MusicVolume = v;
            Backend.SetMusicVolume(v);
        }

        void ApplyAmbientVolume(string id)
        {
            if (Mixer == null || id == null) return;
            bool known = false;
            foreach (var l in Mixer.Layers) if (l.Id == id) known = true;
            if (!known) return;
            Mixer.SetUserVolume(id, SettingsDirector.Store.GetFloat(IoriSettings.AmbientVolume(id)));
            Backend.SetAmbientVolume(id, Mixer.EffectiveVolume(id, Weather));
        }

        public void SetClockOffset(long offsetMs)
        {
            ClockOffsetMs = offsetMs;
            Rebuild();
        }

        /// <summary>動作確認用: 次の時間帯の切り替わりの少し前まで時計を進める。</summary>
        public void JumpBeforeNextPhase(int secondsBefore)
        {
            if (Plan == null) return;
            var now = VirtualNow.ToUnixTimeMilliseconds();
            var next = Plan.Phases.Find(p => p.StartMs > now);
            if (next == null) return;
            SetClockOffset(ClockOffsetMs + (next.StartMs - now) - secondsBefore * 1000L);
        }

        /// <summary>動作確認用: 次の正時の少し前まで時計を進める。</summary>
        public void JumpBeforeNextHour(int secondsBefore)
        {
            var local = TimeZoneInfo.ConvertTime(VirtualNow, TimeZoneInfo.Local);
            var next = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).AddHours(1);
            SetClockOffset(ClockOffsetMs + (long)(next - local).TotalMilliseconds - secondsBefore * 1000L);
        }
    }
}
