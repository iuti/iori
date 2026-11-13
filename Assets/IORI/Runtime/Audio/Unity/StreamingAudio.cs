using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Iori.Audio
{
    /// <summary>StreamingAssets/IORI/ 以下のファイルの読み込み(Android では APK 内なので UnityWebRequest を使う)。</summary>
    public static class StreamingAudio
    {
        public const string RootFolder = "IORI";
        public const string AudioFolder = "IORI/Audio";

        public static string PathOf(string relative) =>
            Path.Combine(Application.streamingAssetsPath, relative).Replace('\\', '/');

        public static string UriOf(string relative)
        {
            var path = PathOf(relative);
            return path.Contains("://") ? path : "file://" + path;
        }

        public static IEnumerator LoadText(string relative, Action<string> done)
        {
            using (var req = UnityWebRequest.Get(UriOf(relative)))
            {
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null);
            }
        }

        public static IEnumerator LoadClip(string audioRelative, Action<AudioClip> done)
        {
            var rel = AudioFolder + "/" + audioRelative;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(UriOf(rel), TypeOf(audioRelative)))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[IORI] 音声を読めませんでした: {rel} ({req.error})");
                    done(null);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                clip.name = audioRelative;
                done(clip);
            }
        }

        static AudioType TypeOf(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
