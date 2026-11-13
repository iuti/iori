using System;
using System.IO;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class AudioIndexTests
    {
        [Test]
        public void ReadsWavDuration()
        {
            var wav = PlaceholderAudio.EncodeWav(new float[22050 * 3], 22050);
            Assert.That(AudioFileInfo.TryReadDurationMs("x.wav", wav, out var ms), Is.True);
            Assert.That(ms, Is.EqualTo(3000));
        }

        [Test]
        public void ReadsOggVorbisDuration_FromHeaders()
        {
            // 最小限の Ogg ページを 2 枚手で組む(識別ヘッダ、最後のページ)。
            var ms = new MemoryStream();
            WritePage(ms, granule: 0, packet: VorbisIdHeader(44100));
            WritePage(ms, granule: 44100L * 75 / 10, packet: new byte[] { 0 }); // 7.5 秒
            Assert.That(AudioFileInfo.TryReadDurationMs("x.ogg", ms.ToArray(), out var dur), Is.True);
            Assert.That(dur, Is.EqualTo(7500));
        }

        [Test]
        public void UnknownFormat_ReturnsFalse()
        {
            Assert.That(AudioFileInfo.TryReadDurationMs("x.mp3", new byte[100], out _), Is.False);
            Assert.That(AudioFileInfo.TryReadDurationMs("x.wav", new byte[100], out _), Is.False);
        }

        [Test]
        public void Index_RoundTrips()
        {
            var index = new AudioIndex();
            index.DurationsMs["radio/a.ogg"] = 123456;
            var back = AudioIndex.Parse(index.ToJson());
            Assert.That(back.DurationOf("radio/a.ogg"), Is.EqualTo(123456));
            Assert.That(back.DurationOf("missing"), Is.EqualTo(0));
        }

        static byte[] VorbisIdHeader(int sampleRate)
        {
            var b = new byte[30];
            b[0] = 1;
            for (var i = 0; i < 6; i++) b[1 + i] = (byte)"vorbis"[i];
            b[11] = 2; // channels
            BitConverter.GetBytes(sampleRate).CopyTo(b, 12);
            return b;
        }

        static void WritePage(Stream s, long granule, byte[] packet)
        {
            var header = new byte[27];
            header[0] = (byte)'O'; header[1] = (byte)'g'; header[2] = (byte)'g'; header[3] = (byte)'S';
            BitConverter.GetBytes(granule).CopyTo(header, 6);
            header[26] = 1; // segments
            s.Write(header, 0, header.Length);
            s.WriteByte((byte)packet.Length);
            s.Write(packet, 0, packet.Length);
        }
    }
}
