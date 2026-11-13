using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class AudioFileInfoTests
    {
        // ---- 小さな音楽ファイルを組み立てる道具

        static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
        static byte[] A(string s) => Encoding.ASCII.GetBytes(s);
        static byte[] Be32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
        static byte[] Le32(int v) => new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };
        static byte[] Synchsafe(int v) => new[] { (byte)((v >> 21) & 0x7F), (byte)((v >> 14) & 0x7F), (byte)((v >> 7) & 0x7F), (byte)(v & 0x7F) };

        static byte[] Frame23(string id, byte[] body) => Cat(A(id), Be32(body.Length), new byte[2], body);
        static byte[] Frame24(string id, byte[] body) => Cat(A(id), Synchsafe(body.Length), new byte[2], body);
        static byte[] Utf16(string s) => Cat(new byte[] { 1, 0xFF, 0xFE }, Encoding.Unicode.GetBytes(s));
        static byte[] Utf8(string s) => Cat(new byte[] { 3 }, Encoding.UTF8.GetBytes(s));
        static byte[] Id3(int major, params byte[][] frames)
        {
            var body = Cat(Cat(frames), new byte[32]); // 後ろに詰め物
            return Cat(A("ID3"), new byte[] { (byte)major, 0, 0 }, Synchsafe(body.Length), body);
        }

        /// <summary>MPEG1 Layer III、128kbps、44.1kHz、ステレオのフレーム(中身は空)。1 フレーム 417 バイト、1152 サンプル。</summary>
        static byte[] Mp3Frames(int count, bool xing = false)
        {
            var all = new List<byte>();
            for (var i = 0; i < count + (xing ? 1 : 0); i++)
            {
                var f = new byte[417];
                f[0] = 0xFF; f[1] = 0xFB; f[2] = 0x90; f[3] = 0x00;
                if (xing && i == 0)
                {
                    Array.Copy(A("Xing"), 0, f, 4 + 32, 4);
                    f[4 + 32 + 7] = 1;
                    Array.Copy(Be32(1000), 0, f, 4 + 32 + 8, 4);
                }
                all.AddRange(f);
            }
            return all.ToArray();
        }

        static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 0xFF, 0xD9 };

        static MusicTrack Read(byte[] file, string path) => AudioFileInfo.Read(new MemoryStream(file), path);

        // ---- MP3

        [Test]
        public void Mp3_Id3v23_ReadsUtf16Text_TrackNumber_AndEstimatesLengthFromBitrate()
        {
            var file = Cat(
                Id3(3,
                    Frame23("TIT2", Utf16("遠まわりの帰り道")),
                    Frame23("TPE1", Utf16("みなと通りトリオ")),
                    Frame23("TALB", Utf16("港の朝")),
                    Frame23("TPE2", Utf16("みなと通り")),
                    Frame23("TRCK", Cat(new byte[] { 0 }, A("3/12"))),
                    Frame23("TPOS", Cat(new byte[] { 0 }, A("2")))),
                Mp3Frames(100));
            var t = Read(file, "C:/Music/x/song.mp3");
            Assert.That(t.Title, Is.EqualTo("遠まわりの帰り道"));
            Assert.That(t.Artist, Is.EqualTo("みなと通りトリオ"));
            Assert.That(t.Album, Is.EqualTo("港の朝"));
            Assert.That(t.AlbumArtist, Is.EqualTo("みなと通り"));
            Assert.That(t.Number, Is.EqualTo(3));
            Assert.That(t.Disc, Is.EqualTo(2));
            // 100 フレーム × 417 バイト × 8 ÷ 128kbps = 2606 ミリ秒(本当の長さ 2612 に近い)
            Assert.That(t.DurationMs, Is.EqualTo(2606).Within(10));
            Assert.That(t.Id, Is.EqualTo("C:/Music/x/song.mp3"));
        }

        [Test]
        public void Mp3_Id3v24_ReadsUtf8_AndFindsTheFrontCoverInsideTheFile()
        {
            var back = Cat(new byte[] { 0 }, A("image/jpeg"), new byte[] { 0, 4, 0 }, Jpeg);
            var front = Cat(new byte[] { 3 }, A("image/jpeg"), new byte[] { 0, 3 }, Encoding.UTF8.GetBytes("表紙"), new byte[] { 0 }, Jpeg);
            var file = Cat(
                Id3(4,
                    Frame24("TIT2", Utf8("夜行バスの窓\0二つ目の値")),
                    Frame24("APIC", back),
                    Frame24("APIC", front),
                    Frame24("TLEN", Utf8("228900"))),
                Mp3Frames(10));
            var t = Read(file, "a.mp3");
            Assert.That(t.Title, Is.EqualTo("夜行バスの窓"));
            Assert.That(t.DurationMs, Is.EqualTo(228900));
            Assert.That(t.ArtLength, Is.EqualTo(Jpeg.Length));
            // 示された位置に、表紙の絵(2 つ目)のデータがある
            var at = (int)t.ArtOffset;
            Assert.That(file.Skip(at).Take(t.ArtLength), Is.EqualTo(Jpeg));
            var second = file.Length - Mp3Frames(10).Length - 32 - Frame24("TLEN", Utf8("228900")).Length - Jpeg.Length;
            Assert.That(at, Is.EqualTo(second));
        }

        [Test]
        public void Mp3_Id3v22_And_XingHeader()
        {
            byte[] Frame22(string id, byte[] body) => Cat(A(id), new[] { (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length }, body);
            var file = Cat(
                Id3(2, Frame22("TT2", Cat(new byte[] { 0 }, A("Old Song"))), Frame22("TP1", Cat(new byte[] { 0 }, A("Old Band")))),
                Mp3Frames(5, xing: true));
            var t = Read(file, "old.mp3");
            Assert.That(t.Title, Is.EqualTo("Old Song"));
            Assert.That(t.Artist, Is.EqualTo("Old Band"));
            // Xing の表: 1000 フレーム × 1152 ÷ 44100 = 26.122 秒
            Assert.That(t.DurationMs, Is.EqualTo(26122).Within(2));
        }

        [Test]
        public void Mp3_WithoutTags_UsesFileAndFolderNames_AndId3v1AtTheEnd()
        {
            var t = Read(Mp3Frames(20), @"C:\Music\夏のアルバム\01 はじまり.mp3");
            Assert.That(t.Title, Is.EqualTo("01 はじまり"));
            Assert.That(t.Album, Is.EqualTo("夏のアルバム"));
            Assert.That(t.Artist, Is.EqualTo(""));

            var v1 = new byte[128];
            Array.Copy(A("TAG"), v1, 3);
            Array.Copy(A("Last Song"), 0, v1, 3, 9);
            Array.Copy(A("The Band"), 0, v1, 33, 8);
            v1[126] = 7;
            var u = Read(Cat(Mp3Frames(20), v1), "x.mp3");
            Assert.That(u.Title, Is.EqualTo("Last Song"));
            Assert.That(u.Artist, Is.EqualTo("The Band"));
            Assert.That(u.Number, Is.EqualTo(7));
        }

        [Test]
        public void Mp3_TextMarkedLatin1_ButWrittenAsUtf8_IsReadAsUtf8()
        {
            var file = Cat(Id3(3, Frame23("TIT2", Cat(new byte[] { 0 }, Encoding.UTF8.GetBytes("雨あがりの庭")))), Mp3Frames(3));
            Assert.That(Read(file, "a.mp3").Title, Is.EqualTo("雨あがりの庭"));
        }

        [Test]
        public void BrokenFiles_DoNotThrow()
        {
            Assert.That(Read(new byte[0], "empty.mp3").Title, Is.EqualTo("empty"));
            Assert.That(Read(Cat(A("ID3"), new byte[] { 3, 0, 0 }, Synchsafe(5000), new byte[20]), "cut.mp3").Title, Is.EqualTo("cut"));
            Assert.That(Read(Cat(Id3(3, Cat(A("TIT2"), Be32(999999), new byte[2], new byte[4])), Mp3Frames(2)), "bad.mp3").Title, Is.EqualTo("bad"));
            Assert.That(Read(A("OggS but not really an ogg file at all"), "bad.ogg").Title, Is.EqualTo("bad"));
            Assert.That(Read(A("RIFFxxxxWAVEfmt "), "bad.wav").Title, Is.EqualTo("bad"));
            Assert.That(Read(new byte[10], "noext").Title, Is.EqualTo("noext"));
        }

        // ---- OGG Vorbis

        static byte[] OggPage(long granule, params byte[][] packets)
        {
            var lacing = new List<byte>();
            foreach (var p in packets)
            {
                var left = p.Length;
                while (left >= 255) { lacing.Add(255); left -= 255; }
                lacing.Add((byte)left);
            }
            var head = new byte[27];
            Array.Copy(A("OggS"), head, 4);
            for (var i = 0; i < 8; i++) head[6 + i] = (byte)(granule >> (8 * i));
            head[26] = (byte)lacing.Count;
            return Cat(head, lacing.ToArray(), Cat(packets));
        }

        static byte[] Comment(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            return Cat(Le32(b.Length), b);
        }

        [Test]
        public void Ogg_ReadsVorbisComments_AndLengthFromTheLastPage()
        {
            var id = Cat(new byte[] { 1 }, A("vorbis"), Le32(0), new byte[] { 2 }, Le32(48000), new byte[13]);
            var comments = Cat(new byte[] { 3 }, A("vorbis"), Comment("test encoder"), Le32(6),
                Comment("TITLE=山の向こうの灯り"), Comment("Artist=星野カルテット"), Comment("ALBUM=山の向こう"),
                Comment("TRACKNUMBER=04"), Comment("METADATA_BLOCK_PICTURE=" + new string('A', 600)), Comment("title=二つ目は使わない"),
                new byte[] { 1 });
            var file = Cat(OggPage(0, id), OggPage(0, comments), OggPage(-1, new byte[40]), OggPage(48000L * 252, new byte[40]));
            var t = Read(file, "b.ogg");
            Assert.That(t.Title, Is.EqualTo("山の向こうの灯り"));
            Assert.That(t.Artist, Is.EqualTo("星野カルテット"));
            Assert.That(t.Album, Is.EqualTo("山の向こう"));
            Assert.That(t.Number, Is.EqualTo(4));
            Assert.That(t.DurationMs, Is.EqualTo(252000));
        }

        // ---- WAV

        [Test]
        public void Wav_ReadsLength_AndInfoTags()
        {
            var fmt = Cat(new byte[] { 1, 0, 2, 0 }, Le32(44100), Le32(176400), new byte[] { 4, 0, 16, 0 });
            var info = Cat(A("INFO"), A("INAM"), Le32(6), A("Chime\0"), A("IART"), Le32(4), A("Iori"));
            var data = new byte[176400 / 2];
            var body = Cat(A("WAVE"), A("fmt "), Le32(fmt.Length), fmt, A("LIST"), Le32(info.Length), info, A("data"), Le32(data.Length), data);
            var t = Read(Cat(A("RIFF"), Le32(body.Length), body), "c.wav");
            Assert.That(t.DurationMs, Is.EqualTo(500));
            Assert.That(t.Title, Is.EqualTo("Chime"));
            Assert.That(t.Artist, Is.EqualTo("Iori"));
        }

        // ---- フォルダ

        [Test]
        public void MusicFolder_FindsSongsInSubfolders_AndUsesTheFolderPicture()
        {
            var root = Path.Combine(Path.GetTempPath(), "iori-music-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "A"));
                Directory.CreateDirectory(Path.Combine(root, "B", "deep"));
                File.WriteAllBytes(Path.Combine(root, "A", "one.mp3"), Mp3Frames(3));
                File.WriteAllBytes(Path.Combine(root, "A", "Cover.JPG"), Jpeg);
                File.WriteAllBytes(Path.Combine(root, "A", "notes.txt"), A("not music"));
                File.WriteAllBytes(Path.Combine(root, "B", "deep", "two.mp3"), Mp3Frames(3));
                File.WriteAllBytes(Path.Combine(root, "B", "deep", "three.flac"), new byte[10]);

                var found = MusicFolder.Scan(root);
                Assert.That(found.Select(t => t.Title), Is.EqualTo(new[] { "one", "two" }));
                Assert.That(Path.GetFileName(found[0].ArtUri), Is.EqualTo("Cover.JPG"));
                Assert.That(found[0].Album, Is.EqualTo("A"));
                Assert.That(found[1].ArtUri, Is.EqualTo(""));

                Assert.That(MusicFolder.Scan(root, maxTracks: 1).Count, Is.EqualTo(1));
                Assert.That(MusicFolder.Scan(root, cancelled: () => true), Is.Empty);
                Assert.That(MusicFolder.Scan(Path.Combine(root, "missing")), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
