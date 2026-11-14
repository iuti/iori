using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// 音楽プレーヤーの司令塔。端末の曲の一覧(<see cref="MusicLibrary"/>)と再生の列(<see cref="PlayQueue"/>)を持ち、
    /// 再生係に列を渡す。ラジオと同じ GameObject に自動で 1 つ作られる。
    ///
    /// ラジオとの関係: 鳴らすのはどちらか片方。プレーヤーで曲を鳴らすと「プレーヤーの番」(<see cref="Active"/>)になり、
    /// ラジオは黙る(環境音は鳴り続ける)。一時停止してもプレーヤーの番のまま。列を最後まで鳴らし終えるか、
    /// <see cref="Release"/>(ラジオの再生ボタン)でラジオの番に戻る。
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        const string PrefQueue = "iori.music.queue";
        const string PrefPosition = "iori.music.position";

        public static MusicDirector Instance { get; private set; }
        /// <summary>動作確認用: false にすると、列と位置を保存も読み出しもしない(画面の撮影で、聴いていた列を上書きしないため)。</summary>
        public static bool Persist = true;

        public MusicLibrary Library { get; private set; } = new MusicLibrary();
        public PlayQueue Queue { get; } = new PlayQueue();
        public IMusicBackend Backend { get; private set; }
        /// <summary>端末の曲を集めている最中か。</summary>
        public bool Scanning { get; private set; }
        /// <summary>一度でも曲を集め終えたか。</summary>
        public bool Scanned { get; private set; }
        /// <summary>プレーヤーの番か(ラジオは黙っている)。</summary>
        public bool Active { get; private set; }
        /// <summary>曲、列、鳴っているかどうか、一覧のどれかが変わるたびに増える。画面はこれを見て描き直す。</summary>
        public int Version { get; private set; }

        public MusicTrack Current => Library.Find(Queue.Current);
        public bool IsPlaying => Active && Backend != null && Backend.IsPlaying;
        public long PositionMs => _pushed && Backend != null ? Backend.PositionMs : _savedPositionMs;
        public long DurationMs
        {
            get
            {
                long d = _pushed && Backend != null ? Backend.DurationMs : 0;
                if (d > 0) return d;
                var t = Current;
                return t != null ? t.DurationMs : 0;
            }
        }

        bool _pushed;            // 再生係がいまの列を持っているか
        bool _wasPlaying;
        long _savedPositionMs;
        float _saveTimer;
        int _seed;

        void Awake()
        {
            Instance = this;
            _seed = Environment.TickCount;
#if UNITY_ANDROID && !UNITY_EDITOR
            Backend = null; // Android の再生係はまだ無い(端末の曲を読む所から、これから作る)
#else
            Backend = gameObject.AddComponent<UnityMusicBackend>();
#endif
        }

        void Start() => Rescan();

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Touch() => Version++;

        /// <summary>端末の曲を集め直す。</summary>
        public void Rescan()
        {
            if (Backend == null || Scanning) return;
            Scanning = true;
            Touch();
            Backend.Scan(tracks =>
            {
                Scanning = false;
                Scanned = true;
                var first = Library.Count == 0 && Queue.Count == 0;
                Library = new MusicLibrary(tracks);
                if (first && Persist)
                {
                    // 前に聴いていた列と位置を思い出す(鳴らし始めはしない)
                    Queue.Load(PlayerPrefs.GetString(PrefQueue, ""));
                    _savedPositionMs = Math.Max(0L, (long)(PlayerPrefs.GetFloat(PrefPosition, 0f) * 1000f));
                }
                // 無くなった曲は列から外す
                var before = Queue.Current;
                Queue.Remove(id => Library.Find(id) == null);
                if (_pushed && Queue.Current != before) Push(IsPlaying, 0);
                Touch();
            });
        }

        // ---- 画面から呼ぶ操作

        /// <summary>list の start 番目から鳴らす(list は選んだ並び: 一覧の順、アルバムの曲順)。</summary>
        public void PlayList(IReadOnlyList<MusicTrack> list, int start, bool? shuffle = null)
        {
            if (Backend == null || list == null || list.Count == 0) return;
            var ids = new List<string>(list.Count);
            foreach (var t in list) ids.Add(t.Id);
            Queue.Set(ids, start, shuffle ?? Queue.Shuffle, ++_seed);
            Push(true, 0);
            SetActive(true);
            SaveQueue();
        }

        /// <summary>全部の曲をまぜて鳴らす。</summary>
        public void ShuffleAll()
        {
            if (Library.Count == 0) return;
            PlayList(Library.Songs, UnityEngine.Random.Range(0, Library.Count), true);
        }

        public void TogglePlay()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Play()
        {
            if (Backend == null || Current == null) return;
            if (!_pushed) Push(true, _savedPositionMs);
            else Backend.Play();
            SetActive(true);
            Touch();
        }

        public void Pause()
        {
            if (Backend == null) return;
            Backend.Pause();
            SavePosition();
            Touch();
        }

        public void Next()
        {
            if (Backend == null || Queue.Count == 0) return;
            var playing = IsPlaying || !Active;
            Queue.Next(true);
            Push(playing, 0);
            if (playing) SetActive(true);
            SaveQueue();
        }

        public void Previous()
        {
            if (Backend == null || Queue.Count == 0) return;
            if (Queue.Previous(PositionMs))
            {
                Push(IsPlaying, 0);
                SaveQueue();
            }
            else SeekMs(0);
        }

        /// <summary>曲の中の位置へ(0 が頭、1 が終わり)。</summary>
        public void Seek(float fraction)
        {
            var d = DurationMs;
            if (d > 0) SeekMs((long)(Mathf.Clamp01(fraction) * d));
        }

        void SeekMs(long ms)
        {
            if (Backend == null || Current == null) return;
            if (_pushed) Backend.Seek(ms);
            _savedPositionMs = ms;
            Touch();
        }

        public void SetShuffle(bool shuffle)
        {
            if (Queue.Shuffle == shuffle) return;
            Queue.SetShuffle(shuffle, ++_seed);
            // (いまの曲は同じなので、再生係は途切れずに続ける)
            if (_pushed) Push(Backend.IsPlaying, PositionMs);
            SaveQueue();
            Touch();
        }

        /// <summary>くり返しを切り替える: なし、全部、1 曲。</summary>
        public void CycleRepeat()
        {
            Queue.SetRepeat(Queue.Repeat == RepeatMode.Off ? RepeatMode.All : Queue.Repeat == RepeatMode.All ? RepeatMode.One : RepeatMode.Off);
            if (Backend != null) Backend.SetRepeat(Queue.Repeat);
            SaveQueue();
            Touch();
        }

        /// <summary>プレーヤーを止めて、ラジオの番に戻す。</summary>
        public void Release()
        {
            if (Backend != null) Backend.Pause();
            SavePosition();
            SetActive(false);
        }

        /// <summary>曲のジャケットの絵(無ければ null を渡す)。size は <see cref="MusicArt"/> の大きさ。</summary>
        public void LoadArt(MusicTrack track, int size, Action<Texture2D> done)
        {
            if (Backend == null || track == null) done?.Invoke(null);
            else Backend.LoadArt(track, size, done);
        }

        // ---- 中の仕事

        void Push(bool play, long positionMs)
        {
            var order = new List<MusicTrack>(Queue.Count);
            foreach (var id in Queue.Order) order.Add(Library.Find(id));
            if (order.Contains(null))
            {
                // (集め直しで外しているので、ここには来ないはず)
                Queue.Remove(id => Library.Find(id) == null);
                order.RemoveAll(t => t == null);
            }
            Backend.SetQueue(order, Queue.Index, positionMs, Queue.Repeat, play);
            _pushed = order.Count > 0;
            _savedPositionMs = positionMs;
            Touch();
        }

        void SetActive(bool active)
        {
            if (Active == active) return;
            Active = active;
            var radio = RadioDirector.Instance;
            if (radio) radio.SetPlayerOn(active);
            Touch();
        }

        void SaveQueue()
        {
            if (Persist) PlayerPrefs.SetString(PrefQueue, Queue.Save());
            SavePosition();
        }

        void SavePosition()
        {
            _savedPositionMs = PositionMs;
            if (Persist) PlayerPrefs.SetFloat(PrefPosition, _savedPositionMs / 1000f);
        }

        void Update()
        {
            if (Backend == null) return;
            var radio = RadioDirector.Instance;
            if (radio)
            {
                Backend.SetVolume(radio.MusicVolume);
                // (ラジオが後から用意できたときのため、番を伝え直す)
                if (radio.PlayerOn != Active) radio.SetPlayerOn(Active);
            }
            if (!_pushed) return;

            // 再生係が自分で次の曲へ進んだら、列の位置を合わせる
            if (Backend.Index != Queue.Index && Backend.Index >= 0)
            {
                Queue.SyncIndex(Backend.Index);
                SaveQueue();
                Touch();
            }
            // 列を最後まで鳴らし終えたら、ラジオの番に戻る
            if (Active && Backend.Finished)
            {
                _savedPositionMs = 0;
                if (Persist) PlayerPrefs.SetFloat(PrefPosition, 0f);
                SetActive(false);
            }
            if (Backend.IsPlaying != _wasPlaying)
            {
                _wasPlaying = Backend.IsPlaying;
                Touch();
            }
            if (Backend.IsPlaying)
            {
                _saveTimer += Time.unscaledDeltaTime;
                if (_saveTimer > 5f) { _saveTimer = 0f; SavePosition(); }
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _pushed && Persist) { SaveQueue(); PlayerPrefs.Save(); }
        }
    }
}
