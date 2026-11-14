using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Iori.Audio
{
    /// <summary>
    /// PC(とエディタ)用の、音楽プレーヤーの再生係。音楽のフォルダ(既定は「ミュージック」)の MP3 / OGG / WAV を集めて、
    /// 渡された列を Unity の AudioSource で鳴らす。フォルダは起動の引数 -ioriMusicDir か、PlayerPrefs の iori.music.folder で変えられる。
    /// </summary>
    public sealed class UnityMusicBackend : MonoBehaviour, IMusicBackend
    {
        public const string PrefFolder = "iori.music.folder";
        /// <summary>止める、鳴らすときの短いフェード(プツッという音を避ける)。</summary>
        const float FadeSeconds = 0.16f;
        const int MaxLargeSize = MusicArt.Large;
        /// <summary>覚えておく絵の数(大きい絵は 1 枚 5MB ほど、小さい絵は 0.1MB ほど)。</summary>
        const int MaxLargeCount = 4, MaxSmallCount = 240;

        AudioSource _source;
        readonly List<MusicTrack> _order = new List<MusicTrack>();
        int _index = -1;
        RepeatMode _repeat;
        bool _wantPlay, _finished, _started, _paused, _loading;
        float _volume = 1f, _gain;
        AudioClip _clip;
        string _clipUri;
        int _loadSerial, _failures;
        long _pendingMs;
        float _lastTime;

        Task<List<MusicTrack>> _scan;
        Action<List<MusicTrack>> _scanDone;
        volatile bool _cancelScan;

        public string Name => "Unity Audio (PC)";
        public bool IsPlaying => _wantPlay;
        public int Index => _index;
        public bool Finished => _finished;
        public long PositionMs => _clip && !_loading ? (long)(_source.time * 1000f) : _pendingMs;
        public long DurationMs => _clip && !_loading ? (long)(_clip.length * 1000f) : (Current != null ? Current.DurationMs : 0);
        MusicTrack Current => _index >= 0 && _index < _order.Count ? _order[_index] : null;

        public string Status =>
            $"{(_wantPlay ? "play" : "pause")} {_index + 1}/{_order.Count} {(_loading ? "loading" : _clip ? "ready" : "none")} {PositionMs / 1000}s/{DurationMs / 1000}s";

        /// <summary>動作確認用: 入れておくと、このフォルダの曲を集める(画面の撮影で見本の曲を使う)。</summary>
        public static string FolderOverride;

        public static string MusicFolderPath
        {
            get
            {
                if (!string.IsNullOrEmpty(FolderOverride)) return FolderOverride;
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-ioriMusicDir") return args[i + 1];
                var saved = PlayerPrefs.GetString(PrefFolder, "");
                return string.IsNullOrEmpty(saved) ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) : saved;
            }
        }

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.priority = 0;
            _source.volume = 0f;
        }

        void OnDestroy()
        {
            _cancelScan = true;
            if (_clip) Destroy(_clip);
            foreach (var art in _art.Values) if (art) Destroy(art);
        }

        // ---- 曲を集める

        public void Scan(Action<List<MusicTrack>> done)
        {
            if (_scan != null) return; // 探している最中
            var folder = MusicFolderPath;
            _cancelScan = false;
            _scanDone = done;
            _scan = Task.Run(() => MusicFolder.Scan(folder, () => _cancelScan));
        }

        // ---- 列を鳴らす

        public void SetQueue(IReadOnlyList<MusicTrack> order, int index, long positionMs, RepeatMode repeat, bool play)
        {
            _order.Clear();
            if (order != null) _order.AddRange(order);
            _repeat = repeat;
            _finished = false;
            _failures = 0;
            _index = _order.Count == 0 ? -1 : Mathf.Clamp(index, 0, _order.Count - 1);
            var t = Current;
            if (t == null)
            {
                Unload();
                _wantPlay = false;
                return;
            }
            _wantPlay = play;
            if (_clipUri == t.Uri && (_clip || _loading)) return; // 同じ曲はそのまま続ける
            Load(positionMs);
        }

        public void SetRepeat(RepeatMode repeat) => _repeat = repeat;

        public void Play()
        {
            if (Current == null) return;
            if (_finished)
            {
                // 最後まで鳴らし終えていたら、いまの曲の頭から
                _finished = false;
                Seek(0);
            }
            _wantPlay = true;
        }

        public void Pause() => _wantPlay = false;

        public void Seek(long positionMs)
        {
            _pendingMs = Math.Max(0, positionMs);
            if (!_clip || _loading) return;
            _source.time = Mathf.Clamp(_pendingMs / 1000f, 0f, Mathf.Max(0f, _clip.length - 0.05f));
            _lastTime = _source.time;
        }

        public void SetVolume(float volume) => _volume = Mathf.Clamp01(volume);

        void Unload()
        {
            _loadSerial++;
            _loading = false;
            _source.Stop();
            _source.clip = null;
            if (_clip) Destroy(_clip);
            _clip = null;
            _clipUri = null;
            _started = false;
            _paused = false;
            _gain = 0f;
            _lastTime = 0f;
        }

        void Load(long positionMs)
        {
            Unload();
            var t = Current;
            if (t == null) return;
            _pendingMs = Math.Max(0, positionMs);
            _clipUri = t.Uri;
            _loading = true;
            StartCoroutine(LoadClip(t, ++_loadSerial));
        }

        static AudioType TypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }

        IEnumerator LoadClip(MusicTrack t, int serial)
        {
            string uri;
            try { uri = new Uri(t.Uri).AbsoluteUri; }
            catch (UriFormatException) { uri = "file://" + t.Uri; }
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, TypeOf(t.Uri)))
            {
                // (全部を先にほどかず、鳴らしながら読む: 長い曲でも待たせない)
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                yield return req.SendWebRequest();
                if (serial != _loadSerial) yield break; // 待っている間に別の曲になった
                AudioClip clip = null;
                if (req.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(req);
                if (!clip || clip.loadState == AudioDataLoadState.Failed || clip.length <= 0f)
                {
                    Debug.LogWarning($"[IORI] 曲を読めませんでした: {t.Uri} ({req.error})");
                    if (clip) Destroy(clip);
                    _loading = false;
                    _clipUri = null;
                    // 読めない曲は飛ばす(全部読めなければ止まる)
                    if (++_failures >= _order.Count) { _wantPlay = false; _finished = true; }
                    else Advance(true);
                    yield break;
                }
                _failures = 0;
                clip.name = t.Title;
                _clip = clip;
                _source.clip = clip;
                _loading = false;
                _source.time = Mathf.Clamp(_pendingMs / 1000f, 0f, Mathf.Max(0f, clip.length - 0.05f));
                _lastTime = _source.time;
            }
        }

        /// <summary>曲が終わったとき(skip は読めない曲を飛ばすとき)。PlayQueue.Next(false) と同じ決まり。</summary>
        void Advance(bool skip)
        {
            if (_order.Count == 0) return;
            if (_repeat == RepeatMode.One && !skip)
            {
                Seek(0);
                _started = false;
                _paused = false;
                return;
            }
            if (_index + 1 < _order.Count) _index++;
            else if (_repeat == RepeatMode.All || (_repeat == RepeatMode.One && skip)) _index = 0;
            else
            {
                _wantPlay = false;
                _finished = true;
                return;
            }
            Load(0);
        }

        void Update()
        {
            if (_scan != null && _scan.IsCompleted)
            {
                var result = _scan.Status == TaskStatus.RanToCompletion ? _scan.Result : new List<MusicTrack>();
                if (_scan.IsFaulted) Debug.LogWarning("[IORI] 曲を探せませんでした: " + _scan.Exception?.GetBaseException().Message);
                _scan = null;
                var done = _scanDone;
                _scanDone = null;
                done?.Invoke(result);
            }

            if (!_clip || _loading)
            {
                _source.volume = 0f;
                return;
            }

            // 鳴らす、止めるは短いフェードで
            _gain = Mathf.MoveTowards(_gain, _wantPlay ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            _source.volume = _volume * _gain;
            if (_wantPlay)
            {
                if (_source.isPlaying) _lastTime = _source.time;
                else if (AudioListener.pause) { }
                else if (_started && !_paused)
                {
                    // 自分で止めていないのに止まった: 曲の終わりまで鳴らし終えた
                    _started = false;
                    _lastTime = 0f;
                    Advance(false);
                }
                else
                {
                    if (_paused) _source.UnPause();
                    if (!_source.isPlaying) _source.Play();
                    _started = true;
                    _paused = false;
                }
            }
            else if (_gain <= 0f && _source.isPlaying)
            {
                _source.Pause();
                _paused = true;
            }
        }

        // ---- ジャケットの絵

        struct ArtJob
        {
            public MusicTrack track;
            public string key;
            public bool large;
        }

        readonly Dictionary<string, Texture2D> _art = new Dictionary<string, Texture2D>();
        readonly List<string> _artSmall = new List<string>(), _artLarge = new List<string>();
        readonly HashSet<string> _artNone = new HashSet<string>();
        readonly Dictionary<string, List<Action<Texture2D>>> _artWaiting = new Dictionary<string, List<Action<Texture2D>>>();
        readonly List<ArtJob> _artJobs = new List<ArtJob>();
        bool _artBusy;

        /// <summary>同じ絵になる曲が同じ値になる鍵(絵が無ければ null)。ファイルに入った絵は、アルバムの中で同じとみなす。</summary>
        static string ArtKey(MusicTrack t)
        {
            if (t == null) return null;
            if (t.ArtLength > 0) return "in:" + (string.IsNullOrEmpty(t.Album) ? t.Uri : t.AlbumKey);
            return string.IsNullOrEmpty(t.ArtUri) ? null : "file:" + t.ArtUri;
        }

        public void LoadArt(MusicTrack track, int size, Action<Texture2D> done)
        {
            var source = ArtKey(track);
            if (source == null || _artNone.Contains(source)) { done?.Invoke(null); return; }
            bool large = size > MusicArt.Small;
            var key = (large ? "L|" : "S|") + source;
            if (_art.TryGetValue(key, out var have) && have)
            {
                var recent = large ? _artLarge : _artSmall;
                recent.Remove(key);
                recent.Add(key);
                done?.Invoke(have);
                return;
            }
            if (_artWaiting.TryGetValue(key, out var waiting)) { waiting.Add(done); return; }
            _artWaiting[key] = new List<Action<Texture2D>> { done };
            _artJobs.Add(new ArtJob { track = track, key = key, large = large });
            if (!_artBusy) StartCoroutine(ArtWorker());
        }

        /// <summary>たのまれた絵を 1 枚ずつ用意する(絵をほどくのは重いので、1 フレームに 1 枚まで)。後からたのまれたものが先。</summary>
        IEnumerator ArtWorker()
        {
            _artBusy = true;
            while (_artJobs.Count > 0)
            {
                var job = _artJobs[_artJobs.Count - 1];
                _artJobs.RemoveAt(_artJobs.Count - 1);
                var t = job.track;
                string path = t.ArtLength > 0 ? t.Uri : t.ArtUri;
                long offset = t.ArtLength > 0 ? t.ArtOffset : 0;
                int length = t.ArtLength;
                var read = Task.Run(() => ReadBytes(path, offset, length));
                while (!read.IsCompleted) yield return null;

                Texture2D tex = null;
                var data = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
                if (data != null)
                {
                    var raw = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (raw.LoadImage(data, false) && raw.width > 2 && raw.height > 2)
                    {
                        raw.Apply(true, false);
                        tex = Square(raw, job.large ? MaxLargeSize : MusicArt.Small);
                    }
                    Destroy(raw);
                }
                if (tex)
                {
                    tex.name = "Jacket";
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Trilinear;
                    _art[job.key] = tex;
                    var recent = job.large ? _artLarge : _artSmall;
                    recent.Add(job.key);
                    // 古い絵から捨てる
                    int max = job.large ? MaxLargeCount : MaxSmallCount;
                    while (recent.Count > max)
                    {
                        var old = recent[0];
                        recent.RemoveAt(0);
                        if (_art.TryGetValue(old, out var gone)) { _art.Remove(old); if (gone) Destroy(gone); }
                    }
                }
                else _artNone.Add(job.key.Substring(2));
                if (_artWaiting.TryGetValue(job.key, out var waiting))
                {
                    _artWaiting.Remove(job.key);
                    foreach (var done in waiting) done?.Invoke(tex);
                }
                yield return null;
            }
            _artBusy = false;
        }

        static byte[] ReadBytes(string path, long offset, int length)
        {
            try
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (length <= 0) length = (int)Math.Min(s.Length, 32 * 1024 * 1024);
                    var bytes = new byte[length];
                    s.Position = offset;
                    var got = 0;
                    while (got < length)
                    {
                        var n = s.Read(bytes, got, length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    return got == length ? bytes : null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>絵の真ん中を真四角に切り出して、一辺が max 以下の絵を作る(元の絵は壊さない)。</summary>
        static Texture2D Square(Texture2D src, int max)
        {
            int side = Mathf.Min(src.width, src.height);
            int size = Mathf.Max(1, Mathf.Min(side, max));
            var scale = new Vector2((float)side / src.width, (float)side / src.height);
            var offset = new Vector2((1f - scale.x) * 0.5f, (1f - scale.y) * 0.5f);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            src.filterMode = FilterMode.Trilinear;
            src.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(src, rt, scale, offset);
            var before = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(size, size, TextureFormat.RGBA32, true);
            dst.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            // (画面に出すだけなので、手元の写しは持たない)
            dst.Apply(true, true);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
