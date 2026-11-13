using UnityEngine;

namespace Iori.Camp
{
    /// <summary>Deterministic noise and random numbers, ported from the three.js mock so the layout matches.</summary>
    public sealed class CampRandom
    {
        uint _a;
        public CampRandom(uint seed) { _a = seed; }

        /// <summary>mulberry32, same sequence as the mock.</summary>
        public float Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (1u | t);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }

        public float Range(float a, float b) => a + (b - a) * Next();
    }

    public static class CampNoise
    {
        public static float Hash2(float x, float y)
        {
            double s = System.Math.Sin(x * 127.1 + y * 311.7) * 43758.5453;
            return (float)(s - System.Math.Floor(s));
        }

        public static float VNoise(float x, float y)
        {
            float xi = Mathf.Floor(x), yi = Mathf.Floor(y), xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash2(xi, yi), b = Hash2(xi + 1, yi), c = Hash2(xi, yi + 1), d = Hash2(xi + 1, yi + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        public static float Fbm(float x, float y)
        {
            float s = 0, a = 0.5f;
            for (int i = 0; i < 4; i++) { s += a * VNoise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; }
            return s;
        }

        public static float SStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
