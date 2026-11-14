using System;
using System.IO;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// 設定(<see cref="SettingsStore"/>)を端末に保存する係。中身は <see cref="Store"/> で、最初に触った所で
    /// 端末の settings.json から読む(無ければ、まとめる前の PlayerPrefs の音量を移す)。値が変わったら少し待ってから書く
    /// (つまみを動かしている間に何度も書かないため)。裏に回るときと終わるときにも書く。
    /// ラジオと同じ GameObject に自動で 1 つ作られる。
    ///
    /// 読み書きの入口はここ 1 つ。音量を変えるときは <c>Store.SetNumber(IoriSettings.RadioVolume, v)</c>、
    /// 変わったのを知りたいときは <c>Store.Watch(鍵, …)</c>(ラジオ、プレーヤー、声、シートのつまみが同じ鍵を見る)。
    /// </summary>
    public sealed class SettingsDirector : MonoBehaviour
    {
        const string FileName = "settings.json";
        const float SaveDelay = 0.6f;

        /// <summary>動作確認用: false の間は settings.json に書かない(画面の撮影で、chico の設定を上書きしないため)。</summary>
        public static bool Persist = true;

        static SettingsStore _store;
        static int _savedVersion;
        float _changedAt = -1f;
        int _seenVersion;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>アプリの設定。最初に触ったときに端末から読む。</summary>
        public static SettingsStore Store
        {
            get
            {
                if (_store == null) Open();
                return _store;
            }
        }

        // (再生のたびにドメインを読み直さない設定のエディタでも、前の再生の値を持ち越さない)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _store = null;
            _savedVersion = 0;
            Persist = true;
        }

        static void Open()
        {
            _store = new SettingsStore();
            _store.Define(IoriSettings.Basics());
            bool loaded = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    loaded = _store.Load(File.ReadAllText(FilePath));
                    if (!loaded) Debug.LogWarning("[IORI] 設定の保存を読めませんでした(既定の値で始めます): " + FilePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IORI] 設定の保存を読めませんでした: " + e.Message);
            }
            if (!loaded && !File.Exists(FilePath)) ImportLegacy();
            _savedVersion = loaded ? _store.Version : -1; // 移しただけのときは、次の機会に書く
        }

        /// <summary>まとめる前の版が PlayerPrefs に残した音量を移す(PlayerPrefs の方は消さないので、前の版に戻しても困らない)。</summary>
        static void ImportLegacy()
        {
            double? radio = PlayerPrefs.HasKey("iori.radio.musicVolume") ? PlayerPrefs.GetFloat("iori.radio.musicVolume") : (double?)null;
            double? player = PlayerPrefs.HasKey("iori.music.volume") ? PlayerPrefs.GetFloat("iori.music.volume") : (double?)null;
            string mixer = PlayerPrefs.GetString("iori.radio.mixer", "");
            if (IoriSettings.ImportLegacy(_store, radio, player, mixer))
                Debug.Log("[IORI] 前の版の音量を設定に移しました");
        }

        /// <summary>いますぐ書く(変わっていなければ何もしない)。</summary>
        public static void SaveNow()
        {
            if (_store == null || !Persist || _store.Version == _savedVersion) return;
            try
            {
                // (書きかけで落ちても前の保存が残るように、別のファイルに書いてから置き換える)
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, _store.Save());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                _savedVersion = _store.Version;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IORI] 設定を保存できませんでした: " + e.Message);
            }
        }

        void Awake()
        {
            _seenVersion = Store.Version;
        }

        void Update()
        {
            if (_store == null) return;
            if (_store.Version != _seenVersion)
            {
                _seenVersion = _store.Version;
                _changedAt = Time.unscaledTime;
            }
            if (_changedAt >= 0f && Time.unscaledTime - _changedAt > SaveDelay)
            {
                _changedAt = -1f;
                SaveNow();
            }
            else if (_changedAt < 0f && _savedVersion < 0) SaveNow(); // 移しただけの値
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveNow();
        }

        void OnApplicationQuit() => SaveNow();
    }
}
