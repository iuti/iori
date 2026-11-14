using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Iori.Audio
{
    /// <summary>
    /// 音楽ファイルから曲名、演奏者、アルバム、曲番号、長さ、ジャケットの絵の位置を読む(PC 用。Android は端末の一覧から受け取る)。
    /// 読めるのは PC の Unity が鳴らせる形式: MP3(ID3v2 のタグ)、OGG Vorbis(コメント)、WAV(LIST/INFO)。
    /// タグが無ければ、曲名はファイル名、アルバムはフォルダ名にする。壊れたファイルでも例外は出さず、読めた分だけ返す。
    /// </summary>
    public static class MusicFileInfo
    {
        /// <summary>タグとして読み込む大きさの上限(絵の入った大きなタグでもここまで)。</summary>
        const int MaxTagBytes = 24 * 1024 * 1024;

        public static bool IsSupported(string path)
        {
            switch (ExtensionOf(path))
            {
                case ".mp3": case ".ogg": case ".wav": return true;
                default: return false;
            }
        }

        static string ExtensionOf(string path)
        {
            try { return (Path.GetExtension(path) ?? "").ToLowerInvariant(); }
            catch (ArgumentException) { return ""; }
        }

        public static MusicTrack Read(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Read(s, path);
        }

        /// <summary>stream は先頭から読める、位置を動かせるもの。path は名前と場所として使うだけで、開かない。</summary>
        public static MusicTrack Read(Stream stream, string path)
        {
            var t = new MusicTrack { Id = path, Uri = path };
            try
            {
                switch (ExtensionOf(path))
                {
                    case ".mp3": ReadMp3(stream, t); break;
                    case ".ogg": ReadOgg(stream, t); break;
                    case ".wav": ReadWav(stream, t); break;
                }
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (IndexOutOfRangeException) { }
            catch (OverflowException) { }
            catch (NotSupportedException) { }

            if (string.IsNullOrWhiteSpace(t.Title)) t.Title = NameOf(path, false);
            if (string.IsNullOrWhiteSpace(t.Album)) t.Album = NameOf(path, true);
            t.Title = t.Title.Trim();
            t.Artist = (t.Artist ?? "").Trim();
            t.Album = t.Album.Trim();
            t.AlbumArtist = (t.AlbumArtist ?? "").Trim();
            return t;
        }

        static string NameOf(string path, bool folder)
        {
            var p = (path ?? "").Replace('\\', '/').TrimEnd('/');
            var slash = p.LastIndexOf('/');
            if (folder)
            {
                if (slash <= 0) return "";
                var dir = p.Substring(0, slash);
                var up = dir.LastIndexOf('/');
                return up >= 0 ? dir.Substring(up + 1) : dir;
            }
            var name = slash >= 0 ? p.Substring(slash + 1) : p;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        static void SetText(MusicTrack t, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            switch (key)
            {
                case "title": t.Title = value; break;
                case "artist": t.Artist = value; break;
                case "album": t.Album = value; break;
                case "albumartist": t.AlbumArtist = value; break;
                case "track": t.Number = LeadingNumber(value); break;
                case "disc": t.Disc = LeadingNumber(value); break;
            }
        }

        /// <summary>「3」「3/12」「03」の先頭の数。</summary>
        static int LeadingNumber(string s)
        {
            var n = 0;
            var any = false;
            foreach (var c in s.Trim())
            {
                if (c < '0' || c > '9') break;
                if (n > 100000) break;
                n = n * 10 + (c - '0');
                any = true;
            }
            return any ? n : 0;
        }

        static int ReadFully(Stream s, byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var n = s.Read(buffer, offset + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        // ------------------------------------------------------------ MP3

        static void ReadMp3(Stream s, MusicTrack t)
        {
            var head = new byte[10];
            s.Position = 0;
            long audioStart = 0;
            if (ReadFully(s, head, 0, 10) == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
            {
                var size = Synchsafe(head, 6);
                audioStart = 10 + size + ((head[5] & 0x10) != 0 ? 10 : 0);
                if (size > 0 && size <= MaxTagBytes)
                {
                    var tag = new byte[size];
                    var got = ReadFully(s, tag, 0, size);
                    ReadId3v2(tag, got, head[3], head[5], 10, t);
                }
            }
            long audioEnd = s.Length;
            if (audioEnd >= 128)
            {
                // 末尾の ID3v1(タグが無いときの頼み。長さの計算からも外す)
                var v1 = new byte[128];
                s.Position = audioEnd - 128;
                if (ReadFully(s, v1, 0, 128) == 128 && v1[0] == 'T' && v1[1] == 'A' && v1[2] == 'G')
                {
                    audioEnd -= 128;
                    if (string.IsNullOrWhiteSpace(t.Title)) t.Title = Loose(v1, 3, 30);
                    if (string.IsNullOrWhiteSpace(t.Artist)) t.Artist = Loose(v1, 33, 30);
                    if (string.IsNullOrWhiteSpace(t.Album)) t.Album = Loose(v1, 63, 30);
                    if (t.Number == 0 && v1[125] == 0) t.Number = v1[126];
                }
            }
            if (t.DurationMs <= 0) t.DurationMs = Mp3Duration(s, audioStart, audioEnd);
        }

        static int Synchsafe(byte[] b, int o) => ((b[o] & 0x7F) << 21) | ((b[o + 1] & 0x7F) << 14) | ((b[o + 2] & 0x7F) << 7) | (b[o + 3] & 0x7F);
        static int Be32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        static int Be24(byte[] b, int o) => (b[o] << 16) | (b[o + 1] << 8) | b[o + 2];

        /// <summary>tag は ID3v2 の頭 10 バイトの後ろ。fileOffset は tag[0] がファイルのどこにあるか。</summary>
        static void ReadId3v2(byte[] tag, int length, int major, int flags, long fileOffset, MusicTrack t)
        {
            if (major < 2 || major > 4) return;
            // v2.2 と v2.3 はタグ全体の FF 00 を FF に戻す(こうなるとファイルの中の絵の位置は分からないので、絵は読まない)。
            // v2.4 はフレームごとの印で戻す。
            if ((flags & 0x80) != 0 && major < 4)
            {
                length = Deunsync(tag, 0, length);
                fileOffset = -1;
            }
            var haveFront = false;
            var p = 0;
            if (major >= 3 && (flags & 0x40) != 0)
            {
                // 拡張ヘッダーを飛ばす
                if (length < 4) return;
                p = major == 4 ? Synchsafe(tag, 0) : Be32(tag, 0) + 4;
                if (p < 0) return;
            }
            var idSize = major == 2 ? 3 : 4;
            var headSize = major == 2 ? 6 : 10;
            while (p + headSize <= length)
            {
                if (tag[p] == 0) break; // 詰め物
                var id = Encoding.ASCII.GetString(tag, p, idSize);
                int size = major == 2 ? Be24(tag, p + 3) : major == 4 ? Synchsafe(tag, p + 4) : Be32(tag, p + 4);
                var frameFlags = major == 2 ? 0 : tag[p + 9];
                p += headSize;
                if (size <= 0 || p + size > length) break;
                var start = p;
                var end = p + size;
                p = end;
                var plain = true;
                if (major == 4)
                {
                    if ((frameFlags & 0x0C) != 0) continue; // 圧縮、暗号化は読まない
                    if ((frameFlags & 0x01) != 0) start += 4; // データ長の表示
                    if ((frameFlags & 0x02) != 0) plain = false; // このフレームには FF 00 が入っている
                }
                else if (major == 3 && (frameFlags & 0xC0) != 0) continue;
                if (start >= end) continue;

                string key = null;
                switch (id)
                {
                    case "TIT2": case "TT2": key = "title"; break;
                    case "TPE1": case "TP1": key = "artist"; break;
                    case "TALB": case "TAL": key = "album"; break;
                    case "TPE2": case "TP2": key = "albumartist"; break;
                    case "TRCK": case "TRK": key = "track"; break;
                    case "TPOS": case "TPA": key = "disc"; break;
                    case "TLEN": case "TLE": key = "length"; break;
                }
                if (key != null)
                {
                    var bytes = tag;
                    int from = start, to = end;
                    if (!plain)
                    {
                        bytes = new byte[end - start];
                        Buffer.BlockCopy(tag, start, bytes, 0, bytes.Length);
                        to = Deunsync(bytes, 0, bytes.Length);
                        from = 0;
                    }
                    var text = Id3Text(bytes, from, to);
                    if (key == "length")
                    {
                        if (long.TryParse(text.Trim(), out var ms) && ms > 0 && ms < 24L * 3600 * 1000) t.DurationMs = ms;
                    }
                    else SetText(t, key, text);
                    continue;
                }
                if ((id == "APIC" || id == "PIC") && plain && fileOffset >= 0 && !haveFront)
                {
                    // 表紙の絵(種類 3)を選ぶ。無ければ最初の絵。
                    var data = PictureStart(tag, start, end, id == "PIC", out var front);
                    if (data > 0 && LooksLikePicture(tag, data, end) && (front || t.ArtLength == 0))
                    {
                        t.ArtOffset = fileOffset + data;
                        t.ArtLength = end - data;
                        haveFront = front;
                    }
                }
            }
        }

        static int Deunsync(byte[] b, int start, int end)
        {
            var w = start;
            for (var r = start; r < end; r++)
            {
                b[w++] = b[r];
                if (b[r] == 0xFF && r + 1 < end && b[r + 1] == 0) r++;
            }
            return w;
        }

        /// <summary>絵のフレームの中で、絵のデータが始まる位置。front は表紙の絵(種類 3)か。</summary>
        static int PictureStart(byte[] b, int start, int end, bool v22, out bool front)
        {
            front = false;
            var p = start;
            if (p >= end) return -1;
            int enc = b[p++];
            if (v22) p += 3; // 画像の形式 3 文字
            else
            {
                while (p < end && b[p] != 0) p++; // MIME
                p++;
            }
            if (p >= end) return -1;
            front = b[p++] == 3;
            // 説明(文字コードごとの終わりの印まで)
            if (enc == 1 || enc == 2)
            {
                while (p + 1 < end && !(b[p] == 0 && b[p + 1] == 0)) p += 2;
                p += 2;
            }
            else
            {
                while (p < end && b[p] != 0) p++;
                p++;
            }
            return p < end ? p : -1;
        }

        static bool LooksLikePicture(byte[] b, int p, int end)
        {
            if (end - p < 8) return false;
            if (b[p] == 0xFF && b[p + 1] == 0xD8) return true; // JPEG
            return b[p] == 0x89 && b[p + 1] == 'P' && b[p + 2] == 'N' && b[p + 3] == 'G';
        }

        static string Id3Text(byte[] b, int start, int end)
        {
            if (start >= end) return "";
            int enc = b[start++];
            string s;
            switch (enc)
            {
                case 1:
                    if (end - start >= 2 && b[start] == 0xFE && b[start + 1] == 0xFF) s = Encoding.BigEndianUnicode.GetString(b, start + 2, end - start - 2);
                    else if (end - start >= 2 && b[start] == 0xFF && b[start + 1] == 0xFE) s = Encoding.Unicode.GetString(b, start + 2, end - start - 2);
                    else s = Encoding.Unicode.GetString(b, start, end - start);
                    break;
                case 2: s = Encoding.BigEndianUnicode.GetString(b, start, end - start); break;
                case 3: s = Encoding.UTF8.GetString(b, start, end - start); break;
                default: s = Loose(b, start, end - start); break;
            }
            // 終わりの印より後ろは捨てる(v2.4 の複数の値は最初の 1 つだけ使う)
            var zero = s.IndexOf('\0');
            return zero >= 0 ? s.Substring(0, zero) : s;
        }

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// 文字コードの決まりが守られていないことが多い所(ID3 の「Latin-1」、ID3v1)の文字を読む。
        /// ASCII だけならそのまま、UTF-8 として正しければ UTF-8、だめならシフト JIS、それも無ければ Latin-1。
        /// </summary>
        static string Loose(byte[] b, int start, int count)
        {
            var end = start;
            var high = false;
            while (end < start + count && b[end] != 0)
            {
                if (b[end] >= 0x80) high = true;
                end++;
            }
            count = end - start;
            if (count <= 0) return "";
            if (!high) return Encoding.ASCII.GetString(b, start, count).Trim();
            try { return StrictUtf8.GetString(b, start, count).Trim(); }
            catch (ArgumentException) { }
            try
            {
                var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                return sjis.GetString(b, start, count).Trim();
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            var chars = new char[count];
            for (var i = 0; i < count; i++) chars[i] = (char)b[start + i];
            return new string(chars).Trim();
        }

        static readonly int[] BitratesV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
        static readonly int[] BitratesV2L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
        static readonly int[] RatesV1 = { 44100, 48000, 32000, 0 };

        /// <summary>MP3 の長さ(ミリ秒)。Xing/Info か VBRI の表があればそのフレーム数から、無ければ最初のフレームのビットレートから見積もる。</summary>
        static long Mp3Duration(Stream s, long start, long end)
        {
            if (start < 0 || start >= end) return 0;
            var buf = new byte[(int)Math.Min(64 * 1024, end - start)];
            s.Position = start;
            var n = ReadFully(s, buf, 0, buf.Length);
            for (var i = 0; i + 4 <= n; i++)
            {
                if (buf[i] != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
                int version = (buf[i + 1] >> 3) & 3; // 0: 2.5, 2: 2, 3: 1
                int layer = (buf[i + 1] >> 1) & 3;   // 1: Layer III
                int bitrateIndex = (buf[i + 2] >> 4) & 15;
                int rateIndex = (buf[i + 2] >> 2) & 3;
                int channelMode = (buf[i + 3] >> 6) & 3;
                if (version == 1 || layer != 1 || bitrateIndex == 0 || bitrateIndex == 15 || rateIndex == 3) continue;
                int rate = RatesV1[rateIndex] >> (version == 3 ? 0 : version == 2 ? 1 : 2);
                int kbps = (version == 3 ? BitratesV1L3 : BitratesV2L3)[bitrateIndex];
                int samples = version == 3 ? 1152 : 576;
                int padding = (buf[i + 2] >> 1) & 1;
                int frameSize = (version == 3 ? 144000 : 72000) * kbps / rate + padding;
                // 見つけた所が本当にフレームの頭か、次のフレームの頭で確かめる
                if (i + frameSize + 2 <= n && (buf[i + frameSize] != 0xFF || (buf[i + frameSize + 1] & 0xE0) != 0xE0)) continue;

                int side = version == 3 ? (channelMode == 3 ? 17 : 32) : (channelMode == 3 ? 9 : 17);
                int x = i + 4 + side;
                if (x + 12 <= n && (Is(buf, x, "Xing") || Is(buf, x, "Info")) && (buf[x + 7] & 1) != 0)
                {
                    long frames = (uint)Be32(buf, x + 8);
                    if (frames > 0) return frames * samples * 1000 / rate;
                }
                int v = i + 4 + 32;
                if (v + 18 <= n && Is(buf, v, "VBRI"))
                {
                    long frames = (uint)Be32(buf, v + 14);
                    if (frames > 0) return frames * samples * 1000 / rate;
                }
                return (end - (start + i)) * 8 / kbps;
            }
            return 0;
        }

        static bool Is(byte[] b, int o, string four) => b[o] == four[0] && b[o + 1] == four[1] && b[o + 2] == four[2] && b[o + 3] == four[3];

        // ------------------------------------------------------------ OGG Vorbis

        static void ReadOgg(Stream s, MusicTrack t)
        {
            s.Position = 0;
            var packets = new List<byte[]>();
            var current = new MemoryStream();
            var head = new byte[27];
            var segments = new byte[255];
            // 最初の 2 つのパケット(識別、コメント)を、ページをまたいで集める
            while (packets.Count < 2)
            {
                if (ReadFully(s, head, 0, 27) != 27 || !Is(head, 0, "OggS")) break;
                int count = head[26];
                if (ReadFully(s, segments, 0, count) != count) break;
                var stop = false;
                for (var i = 0; i < count && packets.Count < 2; i++)
                {
                    int len = segments[i];
                    if (current.Length + len > MaxTagBytes) { stop = true; break; }
                    var chunk = new byte[len];
                    if (ReadFully(s, chunk, 0, len) != len) { stop = true; break; }
                    current.Write(chunk, 0, len);
                    if (len < 255)
                    {
                        packets.Add(current.ToArray());
                        current = new MemoryStream();
                    }
                }
                if (stop) break;
                // (2 つ集まったら、このページの残りは読まなくてよい)
            }
            var rate = 0;
            if (packets.Count >= 1 && packets[0].Length >= 16 && packets[0][0] == 1 && IsVorbis(packets[0]))
                rate = Le32(packets[0], 12);
            if (packets.Count >= 2 && packets[1].Length >= 15 && packets[1][0] == 3 && IsVorbis(packets[1]))
                ReadVorbisComments(packets[1], 7, t);

            // 長さ: 最後のページの位置(サンプル数)から
            if (rate > 0)
            {
                var tail = new byte[(int)Math.Min(128 * 1024, s.Length)];
                s.Position = s.Length - tail.Length;
                var n = ReadFully(s, tail, 0, tail.Length);
                for (var i = n - 14; i >= 0; i--)
                {
                    if (!Is(tail, i, "OggS")) continue;
                    long granule = 0;
                    for (var k = 7; k >= 0; k--) granule = (granule << 8) | tail[i + 6 + k];
                    if (granule <= 0) continue; // パケットの終わりを含まないページ
                    t.DurationMs = granule * 1000 / rate;
                    break;
                }
            }
        }

        static bool IsVorbis(byte[] p) => p[1] == 'v' && p[2] == 'o' && p[3] == 'r' && p[4] == 'b' && p[5] == 'i' && p[6] == 's';
        static int Le32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

        static void ReadVorbisComments(byte[] b, int p, MusicTrack t)
        {
            if (p + 4 > b.Length) return;
            var vendor = Le32(b, p);
            p += 4;
            if (vendor < 0 || p + vendor + 4 > b.Length) return;
            p += vendor;
            var count = Le32(b, p);
            p += 4;
            for (var i = 0; i < count && p + 4 <= b.Length; i++)
            {
                var len = Le32(b, p);
                p += 4;
                if (len < 0 || p + len > b.Length) return;
                // (埋め込みの絵のような長い値は、名前だけ見て飛ばす)
                var eq = Array.IndexOf(b, (byte)'=', p, Math.Min(len, 64));
                if (eq > p)
                {
                    var name = Encoding.ASCII.GetString(b, p, eq - p).ToLowerInvariant();
                    string key = null;
                    switch (name)
                    {
                        case "title": key = "title"; break;
                        case "artist": key = "artist"; break;
                        case "album": key = "album"; break;
                        case "albumartist": case "album artist": key = "albumartist"; break;
                        case "tracknumber": key = "track"; break;
                        case "discnumber": key = "disc"; break;
                    }
                    // 同じ名前が 2 つあれば最初の 1 つを使う
                    if (key != null && !Already(t, key)) SetText(t, key, Encoding.UTF8.GetString(b, eq + 1, p + len - eq - 1));
                }
                p += len;
            }
        }

        static bool Already(MusicTrack t, string key)
        {
            switch (key)
            {
                case "title": return !string.IsNullOrEmpty(t.Title);
                case "artist": return !string.IsNullOrEmpty(t.Artist);
                case "album": return !string.IsNullOrEmpty(t.Album);
                case "albumartist": return !string.IsNullOrEmpty(t.AlbumArtist);
                case "track": return t.Number != 0;
                case "disc": return t.Disc != 0;
                default: return false;
            }
        }

        // ------------------------------------------------------------ WAV

        static void ReadWav(Stream s, MusicTrack t)
        {
            s.Position = 0;
            var head = new byte[12];
            if (ReadFully(s, head, 0, 12) != 12 || !Is(head, 0, "RIFF") || !Is(head, 8, "WAVE")) return;
            var chunk = new byte[8];
            long byteRate = 0, dataSize = 0;
            while (ReadFully(s, chunk, 0, 8) == 8)
            {
                long size = (uint)Le32(chunk, 4);
                var next = s.Position + size + (size & 1);
                if (Is(chunk, 0, "fmt ") && size >= 16)
                {
                    var fmt = new byte[16];
                    if (ReadFully(s, fmt, 0, 16) != 16) break;
                    byteRate = (uint)Le32(fmt, 8);
                }
                else if (Is(chunk, 0, "data"))
                {
                    dataSize = Math.Min(size, s.Length - s.Position);
                }
                else if (Is(chunk, 0, "LIST") && size >= 4 && size <= 1024 * 1024)
                {
                    var list = new byte[size];
                    if (ReadFully(s, list, 0, (int)size) != size) break;
                    if (Is(list, 0, "INFO"))
                    {
                        var p = 4;
                        while (p + 8 <= list.Length)
                        {
                            var len = Le32(list, p + 4);
                            if (len < 0 || p + 8 + len > list.Length) break;
                            string key = Is(list, p, "INAM") ? "title" : Is(list, p, "IART") ? "artist" : Is(list, p, "IPRD") ? "album" : Is(list, p, "ITRK") ? "track" : null;
                            if (key != null) SetText(t, key, Loose(list, p + 8, len));
                            p += 8 + len + (len & 1);
                        }
                    }
                }
                if (next >= s.Length) break;
                s.Position = next;
            }
            if (byteRate > 0 && dataSize > 0) t.DurationMs = dataSize * 1000 / byteRate;
        }
    }
}
