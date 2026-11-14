using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace Iori.Audio
{
    /// <summary>
    /// Android 用の、音楽プレーヤーの再生係。端末の曲は MediaStore から集め、鳴らすのはネイティブの再生サービス
    /// (Assets/Plugins/Android/IoriPlayback.androidlib の IoriMusic / MusicEngine)。Unity が裏に回っても鳴り続け、
    /// 通知やロック画面から次の曲へ進めたり止めたりできる(その結果は、ここが状態を読み直して列に合わせる)。
    /// 集めるのは <see cref="Folder"/> という名前のフォルダの下の曲だけ。
    /// 曲を読む許可は、起動のときにはたのまず、音楽プレーヤーを開いて曲を集め直すときにたのむ。
    /// </summary>
    public sealed class AndroidMusicBackend : MonoBehaviour, IMusicBackend
    {
        const string BridgeClass = "com.iori.playback.IoriMusic";
        const string PrefFolder = "iori.music.folder";
        /// <summary>曲を集めるフォルダの名前(共有ストレージのどこにあってもよい)。chico のスマホでは PC から移した曲がここにある。</summary>
        public const string DefaultFolder = "PC-Music";
        const int MaxLargeCount = 4, MaxSmallCount = 240;
        /// <summary>操作のあと、ネイティブ側の状態を読み直すまで待つ時間(操作が届く前の古い状態を読まないため)。</summary>
        const float SettleSeconds = 0.4f;
        const float PollSeconds = 0.1f;

        AndroidJavaClass _bridge;
        AndroidJavaObject _activity;
        string _artFolder = "";

        readonly List<MusicTrack> _order = new List<MusicTrack>();
        RepeatMode _repeat;
        float _volume = -1f;

        // ネイティブ側から読んだ(または、操作した直後に見込んだ)状態
        int _index = -1, _lastIndex;
        long _positionMs, _durationMs, _lastPositionMs;
        bool _playing, _finished, _lost;
        float _readAt, _nextPoll;

        Action<List<MusicTrack>> _scanDone;
        Task<List<MusicTrack>> _parse;
        bool _scanAsked, _scanRunning, _waitingPermission, _denied;
        float _permissionTimeout;

        public string Name => "Android ネイティブサービス";
        public bool IsPlaying => _playing;
        public int Index => _index;
        public bool Finished => _finished;
        public long DurationMs => _durationMs;
        public long PositionMs
        {
            get
            {
                long p = _positionMs;
                if (_playing) p += (long)((Time.realtimeSinceStartup - _readAt) * 1000f);
                return _durationMs > 0 ? Math.Min(p, _durationMs) : p;
            }
        }
        public string Status => $"{(_playing ? "play" : "pause")} {_index + 1}/{_order.Count} {PositionMs / 1000}s/{_durationMs / 1000}s{(_lost ? " (サービスなし)" : "")}";
        public string EmptyHint => _denied ? "曲を読む許可がありません" : Folder;

        /// <summary>この名前のフォルダの下の曲だけ集める(空なら端末の曲を全部)。PlayerPrefs の iori.music.folder で変えられる。</summary>
        public static string Folder => PlayerPrefs.GetString(PrefFolder, DefaultFolder);

        static string ReadPermission
        {
            get
            {
                int sdk;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                return sdk >= 33 ? "android.permission.READ_MEDIA_AUDIO" : "android.permission.READ_EXTERNAL_STORAGE";
            }
        }

        void Awake()
        {
            _bridge = new AndroidJavaClass(BridgeClass);
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            _artFolder = _bridge.CallStatic<string>("artFolder", _activity);
        }

        void OnDestroy()
        {
            foreach (var art in _art.Values) if (art) Destroy(art);
            _activity?.Dispose();
            _bridge?.Dispose();
        }

        // ---- 曲を集める

        public void Scan(Action<List<MusicTrack>> done)
        {
            if (_scanDone != null) return; // 集めている最中
            _scanDone = done;
            string permission = ReadPermission;
            if (Permission.HasUserAuthorizedPermission(permission))
            {
                StartScan();
                return;
            }
            // 起動のとき(最初の 1 回)は許可をたのまない。音楽プレーヤーを開いて集め直すときにたのむ。
            if (!_scanAsked)
            {
                _scanAsked = true;
                FinishScan(new List<MusicTrack>());
                return;
            }
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { if (_waitingPermission) StartScan(); };
            callbacks.PermissionDenied += _ => { if (_waitingPermission) { _denied = true; FinishScan(new List<MusicTrack>()); } };
            _waitingPermission = true;
            _permissionTimeout = Time.realtimeSinceStartup + 120f;
            Permission.RequestUserPermission(permission, callbacks);
        }

        void StartScan()
        {
            _scanAsked = true;
            _waitingPermission = false;
            _denied = false;
            _scanRunning = true;
            _bridge.CallStatic("scan", _activity, Folder);
        }

        void FinishScan(List<MusicTrack> tracks)
        {
            _waitingPermission = false;
            _scanRunning = false;
            var done = _scanDone;
            _scanDone = null;
            done?.Invoke(tracks);
        }

        // ---- 列を鳴らす

        /// <summary>操作した直後: 見込みの状態を入れて、ネイティブ側が追いつくまで読み直しを待つ。</summary>
        void Expect(int index, long positionMs, bool playing)
        {
            _index = index;
            _positionMs = Math.Max(0, positionMs);
            _playing = playing;
            _finished = false;
            _lost = false;
            _readAt = Time.realtimeSinceStartup;
            _nextPoll = _readAt + SettleSeconds;
        }

        public void SetQueue(IReadOnlyList<MusicTrack> order, int index, long positionMs, RepeatMode repeat, bool play)
        {
            string before = _index >= 0 && _index < _order.Count ? _order[_index].Uri : null;
            long position = PositionMs;
            _order.Clear();
            if (order != null) _order.AddRange(order);
            _repeat = repeat;
            int at = _order.Count == 0 ? -1 : Mathf.Clamp(index, 0, _order.Count - 1);
            var uris = new StringBuilder(_order.Count * 48);
            foreach (var t in _order) uris.Append(t.Uri).Append('\n');
            _bridge.CallStatic("setQueue", _activity, uris.ToString(), Math.Max(0, at), positionMs, (int)repeat, play && at >= 0);
            // (同じ曲はそのまま続くので、位置も変わらない)
            bool same = at >= 0 && before == _order[at].Uri && !_lost && !_finished;
            if (!same) _durationMs = at >= 0 ? _order[at].DurationMs : 0;
            Expect(at, same ? position : positionMs, play && at >= 0);
        }

        public void SetRepeat(RepeatMode repeat)
        {
            _repeat = repeat;
            _bridge.CallStatic("setRepeat", (int)repeat);
        }

        public void Play()
        {
            if (_order.Count == 0) return;
            if (_lost)
            {
                // サービスが落ちて列が無くなっていた: 覚えている所から渡し直す
                SetQueue(new List<MusicTrack>(_order), _lastIndex, _lastPositionMs, _repeat, true);
                return;
            }
            _bridge.CallStatic("play", _activity);
            Expect(Mathf.Max(0, _index), _finished ? 0 : PositionMs, true);
        }

        public void Pause()
        {
            long position = PositionMs;
            _bridge.CallStatic("pause");
            Expect(_index, position, false);
        }

        public void Seek(long positionMs)
        {
            _bridge.CallStatic("seek", Math.Max(0L, positionMs));
            Expect(_index, positionMs, _playing);
        }

        public void SetVolume(float volume)
        {
            volume = Mathf.Clamp01(volume);
            if (Mathf.Approximately(volume, _volume)) return;
            _volume = volume;
            _bridge.CallStatic("setVolume", volume);
        }

        public void SetActive(bool active) => _bridge.CallStatic("setActive", active);

        void Update()
        {
            float now = Time.realtimeSinceStartup;

            // 曲を集める
            if (_waitingPermission && now > _permissionTimeout) FinishScan(new List<MusicTrack>());
            if (_scanRunning && _parse == null && Time.frameCount % 10 == 0)
            {
                string json = _bridge.CallStatic<string>("takeScanResult");
                if (json != null) _parse = Task.Run(() => MediaStoreJson.Parse(json));
            }
            if (_parse != null && _parse.IsCompleted)
            {
                var tracks = _parse.Status == TaskStatus.RanToCompletion ? _parse.Result : new List<MusicTrack>();
                if (_parse.IsFaulted) Debug.LogWarning("[IORI] 曲の一覧を読めませんでした: " + _parse.Exception?.GetBaseException().Message);
                _parse = null;
                FinishScan(tracks);
            }

            // いまの状態(通知やロック画面からの操作、曲の終わりで変わる)
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;
            long[] s = _bridge.CallStatic<long[]>("state");
            if (s == null || s.Length < 5) return;
            if (s[0] < 0)
            {
                // サービスが無い、または列を持っていない
                if (_order.Count > 0 && !_lost)
                {
                    _lost = true;
                    _lastIndex = Mathf.Max(0, _index);
                    _lastPositionMs = PositionMs;
                    _playing = false;
                    _positionMs = _lastPositionMs;
                }
                return;
            }
            _lost = false;
            _index = (int)s[0];
            _positionMs = s[1];
            if (s[2] > 0) _durationMs = s[2];
            else if (_index < _order.Count) _durationMs = _order[_index].DurationMs;
            _playing = s[3] != 0;
            _finished = s[4] != 0;
            _readAt = now;
        }

        void OnApplicationPause(bool paused)
        {
            // 戻ってきたら、すぐ読み直す(裏にいる間に曲が進んでいる)
            if (!paused) _nextPoll = 0f;
        }

        // ---- ジャケットの絵

        readonly Dictionary<string, Texture2D> _art = new Dictionary<string, Texture2D>();
        readonly List<string> _artSmall = new List<string>(), _artLarge = new List<string>();
        readonly HashSet<string> _artNone = new HashSet<string>();
        readonly Dictionary<string, List<Action<Texture2D>>> _artWaiting = new Dictionary<string, List<Action<Texture2D>>>();

        public void LoadArt(MusicTrack track, int size, Action<Texture2D> done)
        {
            if (track == null || string.IsNullOrEmpty(track.ArtUri) || !long.TryParse(track.ArtUri, out long albumId))
            {
                done?.Invoke(null);
                return;
            }
            bool large = size > MusicArt.Small;
            int pixels = large ? MusicArt.Large : MusicArt.Small;
            string key = albumId + "_" + pixels;
            if (_artNone.Contains(key)) { done?.Invoke(null); return; }
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
            StartCoroutine(LoadArtFile(key, albumId, track.Uri, pixels, large));
        }

        /// <summary>ネイティブ側が書いた絵のファイル(無ければ書いてもらう)を読む。</summary>
        IEnumerator LoadArtFile(string key, long albumId, string trackUri, int pixels, bool large)
        {
            string path = Path.Combine(_artFolder, key + ".jpg"), none = path + ".none";
            if (!File.Exists(path) && !File.Exists(none))
            {
                _bridge.CallStatic("requestArt", _activity, albumId, trackUri, pixels);
                float until = Time.realtimeSinceStartup + 20f;
                while (!File.Exists(path) && !File.Exists(none) && Time.realtimeSinceStartup < until)
                    yield return new WaitForSecondsRealtime(0.1f);
            }

            Texture2D tex = null;
            if (File.Exists(path))
            {
                var read = Task.Run(() =>
                {
                    try { return File.ReadAllBytes(path); }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                });
                while (!read.IsCompleted) yield return null;
                var data = read.Status == TaskStatus.RanToCompletion ? read.Result : null;
                if (data != null)
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (tex.LoadImage(data, false) && tex.width > 2) tex.Apply(true, true);
                    else { Destroy(tex); tex = null; }
                }
            }
            if (tex)
            {
                tex.name = "Jacket";
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                _art[key] = tex;
                var recent = large ? _artLarge : _artSmall;
                recent.Add(key);
                // 古い絵から捨てる
                int max = large ? MaxLargeCount : MaxSmallCount;
                while (recent.Count > max)
                {
                    var old = recent[0];
                    recent.RemoveAt(0);
                    if (_art.TryGetValue(old, out var gone)) { _art.Remove(old); if (gone) Destroy(gone); }
                }
            }
            else if (File.Exists(none)) _artNone.Add(key);
            if (_artWaiting.TryGetValue(key, out var waiting))
            {
                _artWaiting.Remove(key);
                foreach (var done in waiting) done?.Invoke(tex);
            }
        }
    }
}
