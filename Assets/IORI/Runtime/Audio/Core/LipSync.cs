using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// 声 1 本ぶんの口の動き。一定の間隔(<see cref="FrameSeconds"/>)ごとに、口の開き(0〜1)と口の形(-1: お・う 〜 1: い・え)を持つ。
    /// 人形の口は、これを声の再生位置で引いて動かす(人形の側で、ちらつかないように段を切り替える)。
    /// </summary>
    public sealed class MouthTrack
    {
        public const double FrameSeconds = 0.02;

        public readonly float[] Open;
        public readonly float[] Vowel;

        public MouthTrack(float[] open, float[] vowel)
        {
            Open = open ?? throw new ArgumentNullException(nameof(open));
            Vowel = vowel ?? throw new ArgumentNullException(nameof(vowel));
            if (open.Length != vowel.Length) throw new ArgumentException("Open と Vowel の長さが違います");
        }

        public double DurationSeconds => Open.Length * FrameSeconds;

        /// <summary>声の頭から seconds の口の開き(コマの間はなめらかにつなぐ。範囲の外は閉じる)。</summary>
        public float OpenAt(double seconds) => At(Open, seconds);

        public float VowelAt(double seconds) => At(Vowel, seconds);

        static float At(float[] a, double seconds)
        {
            if (a.Length == 0 || seconds < 0 || seconds >= a.Length * FrameSeconds) return 0f;
            var x = seconds / FrameSeconds - 0.5;
            if (x <= 0) return a[0];
            var i = (int)x;
            if (i >= a.Length - 1) return a[a.Length - 1];
            var f = (float)(x - i);
            return a[i] + (a[i + 1] - a[i]) * f;
        }
    }

    /// <summary>
    /// 声の波形から口の動きを作る。UnityEngine を使わないので、どの機種でも同じ結果になる。
    /// - 開き: 20ms ごとの音の大きさ(dB)を、その声のいちばん大きいところから OpenRangeDb 下までを 0〜1 に割り当てる。
    ///   開くのは速く、閉じるのは少しゆっくり(口がパタパタしすぎないように)。
    /// - 形: 高い音の多さ(波形の差分の強さと元の強さの比)。い・え は高い響きが強く、お・う は弱い。
    ///   その声の中の真ん中を 0 にして、上下に振る(声ごとの癖を打ち消すため)。
    /// </summary>
    public static class LipSync
    {
        /// <summary>この dB 幅を開き 0〜1 に割り当てる(いちばん大きいところが 1)。</summary>
        public const double OpenRangeDb = 26;
        /// <summary>いちばん大きいところからこれ以上小さい所は口を閉じたまま(息や残響)。</summary>
        public const double SilenceDb = 30;
        const double AttackSeconds = 0.025;
        const double ReleaseSeconds = 0.07;
        /// <summary>形の振れ幅: 高い音の比(対数)がこれだけ真ん中から離れると ±1。</summary>
        const double VowelSpan = 1.5;

        /// <summary>
        /// samples はチャンネルが交互に並んだ波形(Unity の AudioClip.GetData と同じ並び)。
        /// </summary>
        public static MouthTrack Analyze(float[] samples, int channels, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels < 1) throw new ArgumentException("channels は 1 以上です");
            if (sampleRate < 1000) throw new ArgumentException("sampleRate が小さすぎます");
            var frames = samples.Length / channels;
            var hop = (int)Math.Round(sampleRate * MouthTrack.FrameSeconds);
            var count = (frames + hop - 1) / hop;
            var db = new double[count];
            var tilt = new double[count];

            // 1. 20ms ごとの大きさと、高い音の比(窓は前後に少し広げて 40ms)
            for (var i = 0; i < count; i++)
            {
                var from = Math.Max(1, i * hop - hop / 2);
                var to = Math.Min(frames, i * hop + hop + hop / 2);
                double e = 0, d = 0;
                var prev = Mono(samples, channels, from - 1);
                for (var k = from; k < to; k++)
                {
                    var x = Mono(samples, channels, k);
                    e += x * x;
                    d += (x - prev) * (x - prev);
                    prev = x;
                }
                var n = Math.Max(1, to - from);
                db[i] = 10 * Math.Log10(e / n + 1e-12);
                tilt[i] = e > 1e-12 ? Math.Log((d + 1e-12) / e) : 0;
            }

            var peak = double.MinValue;
            foreach (var v in db) peak = Math.Max(peak, v);

            // 2. 開き(声のある所だけ)
            var open = new float[count];
            var level = 0.0;
            var attack = 1 - Math.Exp(-MouthTrack.FrameSeconds / AttackSeconds);
            var release = 1 - Math.Exp(-MouthTrack.FrameSeconds / ReleaseSeconds);
            for (var i = 0; i < count; i++)
            {
                var target = db[i] < peak - SilenceDb ? 0 : Clamp01((db[i] - (peak - OpenRangeDb)) / OpenRangeDb);
                level += (target - level) * (target > level ? attack : release);
                open[i] = (float)level;
            }

            // 3. 形(声のある所の、高い音の比の真ん中を 0 に)
            var voiced = new List<double>();
            for (var i = 0; i < count; i++)
                if (db[i] >= peak - OpenRangeDb * 0.6) voiced.Add(tilt[i]);
            var center = Median(voiced);
            var vowel = new float[count];
            for (var i = 0; i < count; i++)
            {
                if (open[i] < 0.05f) continue;
                var v = Math.Max(-1, Math.Min(1, (tilt[i] - center) / VowelSpan));
                vowel[i] = (float)v;
            }
            // 形は細かく揺れると落ち着かないので、前後 3 コマでならす
            var smooth = new float[count];
            for (var i = 0; i < count; i++)
            {
                float s = 0; var n = 0;
                for (var k = Math.Max(0, i - 3); k <= Math.Min(count - 1, i + 3); k++) { s += vowel[k]; n++; }
                smooth[i] = s / n;
            }
            return new MouthTrack(open, smooth);
        }

        static double Mono(float[] samples, int channels, int frame)
        {
            if (channels == 1) return samples[frame];
            double s = 0;
            for (var c = 0; c < channels; c++) s += samples[frame * channels + c];
            return s / channels;
        }

        static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            return values[values.Count / 2];
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
