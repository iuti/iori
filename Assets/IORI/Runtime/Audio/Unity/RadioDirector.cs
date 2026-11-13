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
        const string PrefOff = "iori.radio.off";
        const string PrefMusicVolume = "iori.radio.musicVolume";
        const string PrefMixer = "iori.radio.mixer";
        static readonly TimeSpan RebuildInterval = TimeSpan.FromMinutes(30);

        public static RadioDirector Instance { get; private set; }

        public RadioProgram Program { get; private set; }
        public AmbientMixer Mixer { get; private set; }
        public IPlaybackBackend Backend { get; private set; }
        public PlaybackPlan Plan { get; private set; }
        public string LoadError { get; private set; }

        public string Weather { get; private set; } = WeatherIds.Clear;
        public bool Quiet { get; private set; }
        /// <summary>ラジオを止めているか(曲、チャイム、フレーズが鳴らない。環境音は鳴る)。</summary>
        public bool Off { get; private set; }
        public float MusicVolume { get; private set; } = 1f;
        /// <summary>動作確認用の時計のずれ。保存しない。</summary>
        public long ClockOffsetMs { get; private set; }

        AudioIndex _audioIndex = new AudioIndex();
        DateTime _lastBuiltUtc;
        bool _mixerDirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("IORI Radio");
            DontDestroyOnLoad(go);
            go.AddComponent<RadioDirector>();
            go.AddComponent<RadioDebugPanel>();
        }

        void Awake()
        {
            Instance = this;
            Weather = PlayerPrefs.GetString(PrefWeather, WeatherIds.Clear);
            Quiet = PlayerPrefs.GetInt(PrefQuiet, 0) == 1;
            Off = PlayerPrefs.GetInt(PrefOff, 0) == 1;
            MusicVolume = PlayerPrefs.GetFloat(PrefMusicVolume, 1f);
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
            Mixer.Load(PlayerPrefs.GetString(PrefMixer, ""));
            Rebuild();
        }

        void Update()
        {
            if (Program == null) return;
            if (DateTime.UtcNow - _lastBuiltUtc > RebuildInterval) Rebuild();
            if (_mixerDirty && Time.frameCount % 30 == 0)
            {
                PlayerPrefs.SetString(PrefMixer, Mixer.Save());
                _mixerDirty = false;
            }
        }

        void OnApplicationPause(bool paused)
        {
            // 裏に回るときは何もしない(Android ではサービスが鳴らし続ける)。戻ってきたら作り直す。
            if (paused)
            {
                if (_mixerDirty) { PlayerPrefs.SetString(PrefMixer, Mixer.Save()); _mixerDirty = false; }
                PlayerPrefs.Save();
            }
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
                RadioOff = Off,
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

        public void SetOff(bool off)
        {
            Off = off;
            PlayerPrefs.SetInt(PrefOff, off ? 1 : 0);
            Rebuild();
        }

        public void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefMusicVolume, MusicVolume);
            if (Plan != null) Plan.MusicVolume = MusicVolume;
            Backend.SetMusicVolume(MusicVolume);
        }

        public void SetAmbientVolume(string id, float volume)
        {
            Mixer.SetUserVolume(id, volume);
            _mixerDirty = true;
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
