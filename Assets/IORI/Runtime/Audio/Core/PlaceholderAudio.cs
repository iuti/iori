using System;
using System.Collections.Generic;
using System.IO;

namespace Iori.Audio
{
    /// <summary>
    /// 本素材(Suno / ElevenLabs など)が揃うまでの仮の音を合成する。
    /// エディタのメニュー「IORI/Audio/仮素材を生成」から StreamingAssets に WAV として書き出す。
    /// 音楽的な出来は求めず、時間帯ごとに聞き分けられることと、ループがつながることだけを目的にする。
    /// </summary>
    public static class PlaceholderAudio
    {
        public const int SampleRate = 22050;

        public sealed class Asset
        {
            public string File;
            public float[] Samples;
        }

        /// <summary>生成する仮素材の一覧(ファイル名は StreamingAssets/IORI/Audio/ からの相対パス)。</summary>
        public static IEnumerable<Asset> All()
        {
            // 時間帯ごとに根音を変える。雨の日は 1 オクターブ下で暗めにする。
            var phases = new (string id, double root, int[] chord)[]
            {
                ("early_morning", 293.66, new[] { 0, 4, 7, 11 }),
                ("morning", 329.63, new[] { 0, 4, 7, 9 }),
                ("day", 349.23, new[] { 0, 4, 7, 14 }),
                ("evening", 261.63, new[] { 0, 3, 7, 10 }),
                ("night", 220.00, new[] { 0, 3, 7, 14 }),
                ("late_night", 196.00, new[] { 0, 5, 7, 10 }),
            };
            foreach (var (id, root, chord) in phases)
            {
                yield return new Asset { File = $"placeholder/music_{id}_clear.wav", Samples = Music(root, chord, 40, false, Seed(id)) };
                yield return new Asset { File = $"placeholder/music_{id}_rain.wav", Samples = Music(root / 2, chord, 40, true, Seed(id) + 1) };
            }
            yield return new Asset { File = "placeholder/chime.wav", Samples = Chime() };
            for (var i = 0; i < 3; i++)
                yield return new Asset { File = $"placeholder/piano_{i + 1:00}.wav", Samples = PianoPhrase(i) };
            yield return new Asset { File = "placeholder/amb_fire.wav", Samples = Fire(20) };
            yield return new Asset { File = "placeholder/amb_rain.wav", Samples = Rain(20) };
            yield return new Asset { File = "placeholder/amb_wind.wav", Samples = Wind(20) };
            yield return new Asset { File = "placeholder/amb_insects.wav", Samples = Insects(20) };
        }

        static ulong Seed(string s)
        {
            ulong h = 1469598103934665603UL;
            foreach (var c in s) h = (h ^ c) * 1099511628211UL;
            return h;
        }

        static double Note(double root, int semitones) => root * Math.Pow(2, semitones / 12.0);

        /// <summary>和音のパッドに、ペンタトニックのつま弾きを重ねた曲。頭と終わりは曲としてフェードする。</summary>
        static float[] Music(double root, int[] chord, double seconds, bool rain, ulong seed)
        {
            var n = (int)(seconds * SampleRate);
            var buf = new float[n];
            var rng = new SplitMix64(seed);
            // パッド
            foreach (var st in chord)
            {
                var f = Note(root / 2, st);
                for (var i = 0; i < n; i++)
                {
                    var t = i / (double)SampleRate;
                    var trem = 0.8 + 0.2 * Math.Sin(2 * Math.PI * 0.15 * t + st);
                    buf[i] += (float)(0.06 * trem * (Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(4 * Math.PI * f * t)));
                }
            }
            // つま弾き
            var penta = new[] { 0, 2, 4, 7, 9, 12, 14 };
            var beat = rain ? 1.2 : 0.75;
            for (var t0 = 1.0; t0 < seconds - 2; t0 += beat)
            {
                if (rng.NextDouble() < 0.3) continue;
                var f = Note(root, penta[rng.NextInt(penta.Length)]);
                Pluck(buf, t0, f, rain ? 0.08 : 0.12, rain ? 1.8 : 1.2);
            }
            ApplyFades(buf, 1.5, 3.0);
            if (rain) LowPass(buf, 0.25f);
            return buf;
        }

        static void Pluck(float[] buf, double startSec, double freq, double amp, double decaySec)
        {
            var start = (int)(startSec * SampleRate);
            var len = (int)(decaySec * 4 * SampleRate);
            for (var i = 0; i < len && start + i < buf.Length; i++)
            {
                var t = i / (double)SampleRate;
                var env = Math.Exp(-t / decaySec) * Math.Min(1.0, t * 200);
                buf[start + i] += (float)(amp * env * (Math.Sin(2 * Math.PI * freq * t)
                                                       + 0.4 * Math.Sin(4 * Math.PI * freq * t)
                                                       + 0.15 * Math.Sin(6 * Math.PI * freq * t)));
            }
        }

        static float[] Chime()
        {
            var buf = new float[(int)(4.0 * SampleRate)];
            Bell(buf, 0.0, 880.0);
            Bell(buf, 0.6, 659.25);
            return buf;
        }

        static void Bell(float[] buf, double startSec, double f)
        {
            var partials = new[] { (1.0, 1.0), (2.76, 0.5), (5.40, 0.25), (8.93, 0.12) };
            var start = (int)(startSec * SampleRate);
            for (var i = 0; start + i < buf.Length; i++)
            {
                var t = i / (double)SampleRate;
                double v = 0;
                foreach (var (ratio, amp) in partials)
                    v += amp * Math.Exp(-t * (1.2 + ratio * 0.4)) * Math.Sin(2 * Math.PI * f * ratio * t);
                buf[start + i] += (float)(0.18 * v * Math.Min(1.0, t * 300));
            }
        }

        static float[] PianoPhrase(int variant)
        {
            var phrases = new[]
            {
                new[] { 12, 7, 4, 7 },
                new[] { 9, 7, 2, 0 },
                new[] { 4, 9, 7, 14 },
            };
            var buf = new float[(int)(7.0 * SampleRate)];
            var notes = phrases[variant % phrases.Length];
            for (var i = 0; i < notes.Length; i++)
                Pluck(buf, 0.2 + i * 0.7, Note(261.63, notes[i]), 0.16, 1.4);
            return buf;
        }

        static float[] Fire(double seconds)
        {
            var n = (int)(seconds * SampleRate);
            var extra = SampleRate; // ループの継ぎ目を馴染ませるぶん
            var buf = new float[n + extra];
            var rng = new SplitMix64(11);
            double brown = 0;
            for (var i = 0; i < buf.Length; i++)
            {
                brown = 0.995 * brown + 0.02 * (rng.NextDouble() * 2 - 1);
                buf[i] = (float)(brown * 0.9);
            }
            // ぱちぱち
            for (var k = 0; k < seconds * 9; k++)
            {
                var at = rng.NextInt(buf.Length - 2000);
                var amp = 0.15 + 0.35 * rng.NextDouble();
                var len = 200 + rng.NextInt(900);
                for (var i = 0; i < len; i++)
                    buf[at + i] += (float)(amp * Math.Exp(-i / (len / 5.0)) * (rng.NextDouble() * 2 - 1));
            }
            return MakeLoop(buf, n, extra);
        }

        static float[] Rain(double seconds)
        {
            var n = (int)(seconds * SampleRate);
            var extra = SampleRate;
            var buf = new float[n + extra];
            var rng = new SplitMix64(23);
            double lp = 0, prev = 0;
            for (var i = 0; i < buf.Length; i++)
            {
                var white = rng.NextDouble() * 2 - 1;
                lp += 0.35 * (white - lp);
                var hp = lp - prev; // 低い成分を少し削る
                prev = lp * 0.97;
                buf[i] = (float)(0.25 * lp + 0.12 * hp);
            }
            for (var k = 0; k < seconds * 25; k++)
            {
                var at = rng.NextInt(buf.Length - 400);
                var amp = 0.05 + 0.1 * rng.NextDouble();
                for (var i = 0; i < 300; i++)
                    buf[at + i] += (float)(amp * Math.Exp(-i / 40.0) * (rng.NextDouble() * 2 - 1));
            }
            return MakeLoop(buf, n, extra);
        }

        static float[] Wind(double seconds)
        {
            var n = (int)(seconds * SampleRate);
            var extra = SampleRate;
            var buf = new float[n + extra];
            var rng = new SplitMix64(37);
            double brown = 0;
            for (var i = 0; i < buf.Length; i++)
            {
                var t = i / (double)SampleRate;
                brown = 0.998 * brown + 0.015 * (rng.NextDouble() * 2 - 1);
                // ループ長で 1 周する揺らぎにして継ぎ目を目立たせない
                var swell = 0.55 + 0.45 * Math.Sin(2 * Math.PI * t / seconds) * Math.Sin(2 * Math.PI * 3 * t / seconds);
                buf[i] = (float)(brown * 1.6 * swell);
            }
            return MakeLoop(buf, n, extra);
        }

        static float[] Insects(double seconds)
        {
            var n = (int)(seconds * SampleRate);
            var extra = SampleRate;
            var buf = new float[n + extra];
            var rng = new SplitMix64(41);
            for (var t0 = 0.3; t0 < seconds; t0 += 0.9 + rng.NextDouble() * 1.4)
            {
                var f = 3800 + rng.NextDouble() * 900;
                var chirps = 2 + rng.NextInt(4);
                for (var c = 0; c < chirps; c++)
                {
                    var start = (int)((t0 + c * 0.11) * SampleRate);
                    var len = (int)(0.06 * SampleRate);
                    for (var i = 0; i < len && start + i < buf.Length; i++)
                    {
                        var env = Math.Sin(Math.PI * i / len);
                        buf[start + i] += (float)(0.05 * env * Math.Sin(2 * Math.PI * f * i / SampleRate));
                    }
                }
            }
            return MakeLoop(buf, n, extra);
        }

        /// <summary>buf の末尾 extra サンプルを頭にクロスフェードで重ね、長さ n の継ぎ目のないループにする。</summary>
        static float[] MakeLoop(float[] buf, int n, int extra)
        {
            var loop = new float[n];
            Array.Copy(buf, loop, n);
            for (var i = 0; i < extra && i < n; i++)
            {
                var a = i / (float)extra;
                loop[i] = loop[i] * a + buf[n + i] * (1 - a);
            }
            return loop;
        }

        static void ApplyFades(float[] buf, double inSec, double outSec)
        {
            var fin = (int)(inSec * SampleRate);
            var fout = (int)(outSec * SampleRate);
            for (var i = 0; i < fin && i < buf.Length; i++) buf[i] *= i / (float)fin;
            for (var i = 0; i < fout && i < buf.Length; i++) buf[buf.Length - 1 - i] *= i / (float)fout;
        }

        static void LowPass(float[] buf, float a)
        {
            float y = 0;
            for (var i = 0; i < buf.Length; i++) { y += a * (buf[i] - y); buf[i] = y * 1.6f; }
        }

        /// <summary>モノラル 16bit PCM の WAV にする。</summary>
        public static byte[] EncodeWav(float[] samples, int sampleRate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                var dataBytes = samples.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1); // PCM
                w.Write((short)1); // mono
                w.Write(sampleRate);
                w.Write(sampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                foreach (var s in samples)
                {
                    var v = s < -1f ? -1f : s > 1f ? 1f : s;
                    w.Write((short)(v * 32767));
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
