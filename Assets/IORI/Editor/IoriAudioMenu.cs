using System.IO;
using Iori.Audio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Iori.EditorTools
{
    /// <summary>音まわりのエディタ用メニュー。</summary>
    public static class IoriAudioMenu
    {
        static string AudioRoot => Path.Combine(Application.streamingAssetsPath, StreamingAudio.AudioFolder);
        static string IndexPath => Path.Combine(Application.streamingAssetsPath, StreamingAudio.RootFolder, "audio-index.json");

        [MenuItem("IORI/Audio/仮素材を生成")]
        public static void GeneratePlaceholders()
        {
            var count = 0;
            foreach (var asset in PlaceholderAudio.All())
            {
                var path = Path.Combine(AudioRoot, asset.File);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, PlaceholderAudio.EncodeWav(asset.Samples, PlaceholderAudio.SampleRate));
                count++;
            }
            UpdateAudioIndex();
            AssetDatabase.Refresh();
            Debug.Log($"[IORI] 仮素材を {count} 個書き出しました: {AudioRoot}");
        }

        [MenuItem("IORI/Audio/曲の長さ一覧を更新")]
        public static void UpdateAudioIndex()
        {
            var index = new AudioIndex();
            if (Directory.Exists(AudioRoot))
            {
                foreach (var path in Directory.GetFiles(AudioRoot, "*.*", SearchOption.AllDirectories))
                {
                    if (path.EndsWith(".meta")) continue;
                    var rel = path.Substring(AudioRoot.Length + 1).Replace('\\', '/');
                    if (AudioFileInfo.TryReadDurationMs(path, File.ReadAllBytes(path), out var ms))
                        index.DurationsMs[rel] = ms;
                    else if (!rel.EndsWith(".txt") && !rel.EndsWith(".md"))
                        Debug.LogWarning($"[IORI] 長さを読めない音声です(OGG Vorbis か WAV にしてください): {rel}");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(IndexPath));
            File.WriteAllText(IndexPath, index.ToJson());
            Debug.Log($"[IORI] audio-index.json を更新しました({index.DurationsMs.Count} 件)");
        }

        [MenuItem("IORI/Audio/番組表をチェック")]
        public static void ValidateProgram()
        {
            var path = Path.Combine(Application.streamingAssetsPath, StreamingAudio.RootFolder, "radio.json");
            RadioProgram program;
            try { program = RadioProgram.Parse(File.ReadAllText(path)); }
            catch (System.Exception e) { Debug.LogError("[IORI] radio.json の読み込みに失敗しました: " + e.Message); return; }

            var missing = 0;
            void Check(string file)
            {
                if (File.Exists(Path.Combine(AudioRoot, file))) return;
                Debug.LogWarning("[IORI] radio.json にあるファイルが見つかりません: " + file);
                missing++;
            }
            foreach (var t in program.Tracks) Check(t.File);
            if (program.Chime != null) Check(program.Chime.File);
            if (program.Jiho != null) for (var h = 0; h < 24; h++) Check(program.Jiho.FileFor(h));
            foreach (var p in program.Quiet.Phrases) Check(p.File);
            foreach (var a in program.Ambient) Check(a.File);
            if (missing == 0) Debug.Log("[IORI] radio.json は問題ありません");

            // イオリのセリフ帳
            var voicePath = Path.Combine(Application.streamingAssetsPath, StreamingAudio.RootFolder, "voice.json");
            VoiceBook book;
            try { book = VoiceBook.Parse(File.ReadAllText(voicePath)); }
            catch (System.Exception e) { Debug.LogError("[IORI] voice.json の読み込みに失敗しました: " + e.Message); return; }
            var before = missing;
            foreach (var f in book.Files()) Check(f);
            if (missing == before) Debug.Log("[IORI] voice.json は問題ありません");
        }
    }

    /// <summary>ビルドの前に曲の長さ一覧を作り直す(素材を差し替えたまま忘れても大丈夫なように)。</summary>
    sealed class IoriAudioBuildStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => IoriAudioMenu.UpdateAudioIndex();
    }
}
