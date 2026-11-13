using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// PC(とエディタ)用の再生係。Android のネイティブサービスと同じ決まりで予定表を鳴らす。
    /// - 時刻 t のクリップの音量 = フェード × クリップ音量 × 曲の音量 × 途中参加のフェード
    /// - 再生位置 = t - StartMs + OffsetMs(Loop なら曲の長さで割った余り)
    /// - 予定表が差し替わって消えたクリップは短くフェードアウトする
    /// </summary>
    public sealed class UnityAudioBackend : MonoBehaviour, IPlaybackBackend
    {
        const int JoinFadeMs = 1500;
        /// <summary>この時間より遅れて鳴らし始めたクリップは「途中参加」としてフェードインする。</summary>
        const int JoinToleranceMs = 1000;
        const int RemovedFadeMs = 1000;
        const long OneShotLateLimitMs = 2000;
        const float AmbientSmoothing = 4f; // 1 秒あたり

        sealed class Voice
        {
            public MusicClip Clip;
            public AudioSource Source;
            public long JoinedAtMs = -1;     // 途中から鳴らし始めた時刻(-1 は頭から)
            public long RemovedAtMs = -1;    // 予定表から消えた時刻
            public float RemovedFromGain;
        }

        sealed class AmbientVoice
        {
            public AudioSource Source;
            public float Target;
        }

        PlaybackPlan _plan;
        float _musicVolume = 1f;
        readonly Dictionary<string, Voice> _voices = new Dictionary<string, Voice>();
        readonly Dictionary<string, AmbientVoice> _ambient = new Dictionary<string, AmbientVoice>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> _loading = new HashSet<string>();
        readonly List<AudioSource> _oneShotSources = new List<AudioSource>();
        long _lastOneShotT = long.MinValue;

        public string Name => "Unity Audio (PC)";

        public void ApplyPlan(PlaybackPlan plan, string planJson)
        {
            _plan = plan;
            _musicVolume = plan.MusicVolume;
            var now = PlanNow();
            if (_lastOneShotT == long.MinValue || _lastOneShotT > now) _lastOneShotT = now;

            // 予定表から消えたクリップはフェードアウトへ。
            var ids = new HashSet<string>();
            foreach (var c in plan.Music) ids.Add(c.Id);
            foreach (var v in _voices.Values)
            {
                if (ids.Contains(v.Clip.Id))
                {
                    v.Clip = plan.Music.Find(c => c.Id == v.Clip.Id);
                    v.RemovedAtMs = -1;
                }
                else if (v.RemovedAtMs < 0)
                {
                    v.RemovedAtMs = now;
                    v.RemovedFromGain = v.Source.volume;
                }
            }

            // 環境音。
            foreach (var layer in plan.Ambient)
            {
                if (!_ambient.TryGetValue(layer.Id, out var av))
                {
                    av = new AmbientVoice { Source = NewSource("Ambient " + layer.Id, true) };
                    _ambient[layer.Id] = av;
                    WithClip(layer.File, clip =>
                    {
                        av.Source.clip = clip;
                        av.Source.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
                        av.Source.Play();
                    });
                }
                av.Target = layer.Volume;
            }

            // 予定表に出てくる音を先に読み込んでおく(数が少ないうちは全部)。
            foreach (var c in plan.Music) WithClip(c.File, null);
            foreach (var s in plan.OneShots) WithClip(s.File, null);
        }

        public void SetMusicVolume(float volume) => _musicVolume = Mathf.Clamp01(volume);

        public void SetAmbientVolume(string id, float volume)
        {
            if (_ambient.TryGetValue(id, out var av)) av.Target = Mathf.Clamp01(volume);
        }

        public void Stop()
        {
            _plan = null;
            foreach (var v in _voices.Values) Destroy(v.Source);
            _voices.Clear();
            foreach (var a in _ambient.Values) Destroy(a.Source);
            _ambient.Clear();
        }

        long PlanNow()
        {
            var real = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (_plan?.ClockOffsetMs ?? 0);
            return _plan?.ToPlanTime(real) ?? real;
        }

        void Update()
        {
            if (_plan == null) return;
            var t = PlanNow();

            // 曲
            foreach (var clip in _plan.ClipsAt(t))
            {
                if (_voices.ContainsKey(clip.Id) || !_clips.TryGetValue(clip.File, out var audio) || audio == null) continue;
                var v = new Voice { Clip = clip, Source = NewSource("Music " + clip.Id, clip.Loop) };
                v.Source.clip = audio;
                var durationMs = _plan.DurationOf(clip.File);
                if (durationMs <= 0) durationMs = (long)(audio.length * 1000);
                var pos = clip.PositionAt(t, clip.Loop ? durationMs : 0);
                if (pos >= durationMs) { Destroy(v.Source); continue; } // もう曲が終わっている
                v.Source.time = pos / 1000f;
                if (t - clip.StartMs > clip.FadeInMs + JoinToleranceMs) v.JoinedAtMs = t;
                v.Source.volume = 0f;
                v.Source.Play();
                _voices[clip.Id] = v;
            }

            var finished = new List<string>();
            foreach (var kv in _voices)
            {
                var v = kv.Value;
                float gain;
                if (v.RemovedAtMs >= 0)
                {
                    var k = 1f - (t - v.RemovedAtMs) / (float)RemovedFadeMs;
                    gain = v.RemovedFromGain * Mathf.Clamp01(k);
                    if (k <= 0f) finished.Add(kv.Key);
                }
                else
                {
                    if (t >= v.Clip.EndMs) { finished.Add(kv.Key); continue; }
                    gain = v.Clip.FadeGainAt(t) * v.Clip.Volume * _musicVolume;
                    if (v.JoinedAtMs >= 0) gain *= Mathf.Sin(Mathf.Clamp01((t - v.JoinedAtMs) / (float)JoinFadeMs) * Mathf.PI / 2);
                }
                v.Source.volume = gain;
            }
            foreach (var id in finished)
            {
                Destroy(_voices[id].Source);
                _voices.Remove(id);
            }

            // 単発音
            if (t < _lastOneShotT - 1000) _lastOneShotT = t; // 時計が戻った
            foreach (var s in _plan.OneShotsBetween(_lastOneShotT, t))
            {
                if (t - s.AtMs > OneShotLateLimitMs) continue;
                if (_clips.TryGetValue(s.File, out var audio) && audio != null)
                    OneShotSource().PlayOneShot(audio, s.Volume * _plan.OneShotVolume);
            }
            _lastOneShotT = t;

            // 環境音
            var step = Time.unscaledDeltaTime * AmbientSmoothing;
            foreach (var a in _ambient.Values)
                a.Source.volume = Mathf.MoveTowards(a.Source.volume, a.Target, step);
        }

        AudioSource OneShotSource()
        {
            foreach (var s in _oneShotSources)
                if (!s.isPlaying) return s;
            var src = NewSource("OneShot " + _oneShotSources.Count, false);
            _oneShotSources.Add(src);
            return src;
        }

        AudioSource NewSource(string label, bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        void WithClip(string file, Action<AudioClip> done)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (_clips.TryGetValue(file, out var cached))
            {
                if (cached != null) done?.Invoke(cached);
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
                if (clip != null) done?.Invoke(clip);
            }));
        }

        System.Collections.IEnumerator WaitFor(string file, Action<AudioClip> done)
        {
            while (_loading.Contains(file)) yield return null;
            if (_clips.TryGetValue(file, out var clip) && clip != null) done(clip);
        }

        public string Status
        {
            get
            {
                if (_plan == null) return "停止中";
                var sb = new StringBuilder();
                foreach (var v in _voices.Values)
                    sb.AppendLine($"♪ {v.Clip.File}  vol {v.Source.volume:0.00}  {v.Source.time:0.0}s" + (v.RemovedAtMs >= 0 ? " (消去中)" : ""));
                foreach (var kv in _ambient)
                    sb.AppendLine($"~ {kv.Key}  vol {kv.Value.Source.volume:0.00}");
                return sb.ToString();
            }
        }
    }
}
