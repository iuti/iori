using System;
using System.Collections.Generic;
using System.IO;

namespace Iori.Audio
{
    /// <summary>
    /// 曲ファイルの長さの一覧(StreamingAssets/IORI/audio-index.json)。
    /// 曲を最後まで流してから頭とクロスフェードさせるには長さが要るので、エディタで自動生成しておく。
    /// </summary>
    public sealed class AudioIndex
    {
        public readonly Dictionary<string, long> DurationsMs = new Dictionary<string, long>();

        public long DurationOf(string file) => file != null && DurationsMs.TryGetValue(file, out var d) ? d : 0;

        public string ToJson()
        {
            var files = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in DurationsMs) files[kv.Key] = kv.Value;
            return Json.Write(new Dictionary<string, object>
            {
                ["version"] = 1,
                ["durationsMs"] = new Dictionary<string, object>(files),
            });
        }

        public static AudioIndex Parse(string json)
        {
            var index = new AudioIndex();
            if (string.IsNullOrEmpty(json)) return index;
            var root = JsonRead.Obj(Json.Parse(json), "audio-index.json");
            if (root.TryGetValue("durationsMs", out var d) && d is Dictionary<string, object> files)
                foreach (var kv in files)
                    if (kv.Value is double ms && ms > 0) index.DurationsMs[kv.Key] = (long)ms;
            return index;
        }
    }

    /// <summary>音声ファイルのヘッダから長さを読む。対応は WAV(PCM)と OGG Vorbis。</summary>
    public static class AudioFileInfo
    {
        /// <summary>長さ(ミリ秒)。読めない形式なら false。</summary>
        public static bool TryReadDurationMs(string path, byte[] data, out long durationMs)
        {
            durationMs = 0;
            if (data == null || data.Length < 12) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".wav") return TryWav(data, out durationMs);
                if (ext == ".ogg") return TryOggVorbis(data, out durationMs);
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException)
            {
                return false;
            }
            return false;
        }

        static bool TryWav(byte[] d, out long durationMs)
        {
            durationMs = 0;
            if (Ascii(d, 0, 4) != "RIFF" || Ascii(d, 8, 4) != "WAVE") return false;
            int sampleRate = 0, blockAlign = 0;
            long dataSize = -1;
            var pos = 12;
            while (pos + 8 <= d.Length)
            {
                var id = Ascii(d, pos, 4);
                var size = (long)BitConverter.ToUInt32(d, pos + 4);
                var body = pos + 8;
                if (id == "fmt ")
                {
                    sampleRate = BitConverter.ToInt32(d, body + 4);
                    blockAlign = BitConverter.ToInt16(d, body + 12);
                }
                else if (id == "data")
                {
                    dataSize = Math.Min(size, d.Length - body);
                }
                pos = body + (int)size + (int)(size & 1);
            }
            if (sampleRate <= 0 || blockAlign <= 0 || dataSize < 0) return false;
            durationMs = dataSize / blockAlign * 1000L / sampleRate;
            return true;
        }

        static bool TryOggVorbis(byte[] d, out long durationMs)
        {
            durationMs = 0;
            if (Ascii(d, 0, 4) != "OggS") return false;
            // 最初のページの最初のパケットが Vorbis の識別ヘッダ。
            var segments = d[26];
            var packet = 27 + segments;
            if (d[packet] != 1 || Ascii(d, packet + 1, 6) != "vorbis") return false;
            var sampleRate = BitConverter.ToInt32(d, packet + 12);
            if (sampleRate <= 0) return false;
            // 最後のページの granule position が総サンプル数。
            for (var i = d.Length - 27; i >= 0; i--)
            {
                if (d[i] != 'O' || d[i + 1] != 'g' || d[i + 2] != 'g' || d[i + 3] != 'S') continue;
                var granule = BitConverter.ToInt64(d, i + 6);
                if (granule <= 0) continue;
                durationMs = granule * 1000L / sampleRate;
                return true;
            }
            return false;
        }

        static string Ascii(byte[] d, int offset, int length)
        {
            if (offset + length > d.Length) return "";
            var chars = new char[length];
            for (var i = 0; i < length; i++) chars[i] = (char)d[offset + i];
            return new string(chars);
        }
    }
}
