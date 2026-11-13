using System.Collections;
using System.Collections.Generic;
using Iori.Audio;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>The small sounds of the UI (a press, a switch, a panel opening). They are in StreamingAssets/IORI/Audio/ui.</summary>
    public sealed class UiSounds : MonoBehaviour
    {
        public const string Tap = "tap", Toggle = "toggle", Slider = "slider", Open = "open", Close = "close", Tune = "radio_tune";
        static readonly string[] Names = { Tap, Toggle, Slider, Open, Close, Tune };

        static UiSounds _instance;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        AudioSource _source;
        public float volume = 0.55f;

        void Awake()
        {
            _instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        IEnumerator Start()
        {
            foreach (var n in Names)
            {
                string name = n;
                yield return StreamingAudio.LoadClip("ui/" + name + ".wav", c => { if (c) _clips[name] = c; });
            }
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        public static void Play(string name, float gain = 1f)
        {
            if (!_instance || string.IsNullOrEmpty(name)) return;
            if (_instance._clips.TryGetValue(name, out var clip) && clip) _instance._source.PlayOneShot(clip, _instance.volume * gain);
        }
    }
}
