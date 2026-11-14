// The small motions of someone sitting quietly by a fire: breathing that is not a metronome, blinks (now and then
// two in a row), eyes that rest on one thing and then another, a head that follows them late and sometimes tilts,
// a body that shifts its weight. Written for Iori ("quiet, watches closely"): everything is small and slow.
// Our own (LivLiv Player's idle motion was read for what makes an idle look alive; none of its code is here).
using System;

namespace Iori.Puppet
{
    public sealed class PuppetIdle
    {
        /// <summary>How much the head and the body move (0: only breath, blinks and eyes).</summary>
        public float amount = 1f;
        /// <summary>On top of amount, each on its own: how far the head turns and nods, how far it tilts, how far the
        /// body sways and leans (1: as written here; the model's rig decides what a degree of these comes to).</summary>
        public float turn = 1f, tilt = 1f, lean = 1f;
        /// <summary>Seconds between blinks, on average.</summary>
        public float blinkEvery = 4.2f;
        /// <summary>Seconds a breath takes, on average.</summary>
        public float breathSeconds = 4.4f;
        /// <summary>A place her eyes go to now and then (the fire), as eyeBallX / eyeBallY.</summary>
        public float glanceX = 0.35f, glanceY = -0.45f;
        /// <summary>Where she is asked to look (eyeBallX / eyeBallY) and how much that counts (0: her own mind, 1: only there).</summary>
        public float lookX, lookY, lookWeight;
        /// <summary>0: awake. 1: eyes closed, breathing slow and deep, head sunk a little.</summary>
        public float asleep;

        Random _rng;
        readonly int _seed;
        float _t;
        // breath
        float _breathPhase, _breathPeriod, _breathDepth, _breathsToDeep;
        // blinks
        float _blinkIn, _blinkAt = -1f, _sinceBlink;
        bool _blinkAgain;
        // eyes
        float _gazeX, _gazeY, _toX, _toY, _gazeIn;
        // the head following the eyes, the tilt, the weight on the seat
        float _hx, _hy, _hvx, _hvy;
        float _tilt, _tiltV, _tiltTo, _tiltIn, _tiltLeft;
        float _lean, _leanV, _leanTo, _leanIn;

        public PuppetIdle(int seed = 1)
        {
            _seed = seed;
            Reset();
        }

        float Rand(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        /// <summary>Back to the start: the same seed plays the same motion again (a picture of "second 3" is the same
        /// picture every time, and pictures taken one after another join into one motion).</summary>
        public void Reset()
        {
            _rng = new Random(_seed);
            _t = 0;
            _breathPhase = 0; _breathsToDeep = Rand(7, 13); NextBreath();
            _blinkIn = Rand(1.2f, 3.5f); _blinkAt = -1; _blinkAgain = false; _sinceBlink = 10;
            _gazeX = _gazeY = _toX = _toY = 0; _gazeIn = Rand(1.5f, 3.5f);
            _hx = _hy = _hvx = _hvy = 0;
            _tilt = _tiltV = _tiltTo = 0; _tiltIn = Rand(5, 11); _tiltLeft = 0;
            _lean = _leanV = _leanTo = 0; _leanIn = Rand(8, 16);
        }

        void NextBreath()
        {
            bool deep = _breathsToDeep <= 0;
            if (deep) _breathsToDeep = Rand(7, 13);
            _breathPeriod = breathSeconds * (deep ? Rand(1.25f, 1.45f) : Rand(0.9f, 1.15f));
            _breathDepth = deep ? 1f : Rand(0.6f, 0.8f);
        }

        static float Smooth(float x)
        {
            x = Math.Min(1f, Math.Max(0f, x));
            return x * x * (3 - 2 * x);
        }

        /// <summary>A slow wander in -1..1: smooth, never repeating in step with anything else.</summary>
        float Wander(float x, int lane)
        {
            return 0.7f * Value(x, lane) + 0.3f * Value(x * 2.3f + 11.7f, lane + 17);
        }

        float Value(float x, int lane)
        {
            float f = MathF.Floor(x);
            int i = (int)f;
            float t = x - f;
            float a = Hash(i, lane), b = Hash(i + 1, lane);
            return a + (b - a) * (t * t * (3 - 2 * t));
        }

        float Hash(int i, int lane)
        {
            unchecked
            {
                uint h = (uint)i * 374761393u + (uint)lane * 668265263u + (uint)_seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0x7fffff - 1f;
            }
        }

        /// <summary>One step toward `to` the way something with weight goes there: no overshoot, no sudden start.</summary>
        static void Ease(ref float x, ref float v, float to, float rate, float dt)
        {
            v += (rate * rate * (to - x) - 2 * rate * v) * dt;
            x += v * dt;
        }

        /// <summary>x as it is up to four fifths of the limit, then bending over so that it never passes the limit.</summary>
        static float Within(float x, float limit)
        {
            float knee = 0.8f * limit, m = Math.Abs(x);
            if (m <= knee) return x;
            return Math.Sign(x) * (knee + (limit - knee) * MathF.Tanh((m - knee) / (limit - knee)));
        }

        /// <summary>Writes this moment's head, eyes, body and breath into P (the mouth and the smile are left alone).</summary>
        public void Apply(PuppetParams P, float dt)
        {
            if (dt < 0) dt = 0;
            if (dt > 0.1f) dt = 0.1f;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / (1f / 60f)));
            for (int i = 0; i < steps; i++) Step(dt / steps);

            float sleep = Smooth(asleep), awake = 1 - sleep;
            // breath: in for two fifths, out for three
            float ph = _breathPhase;
            float breath = ph < 0.4f ? Smooth(ph / 0.4f) : 1 - Smooth((ph - 0.4f) / 0.6f);
            P.breath = breath * (_breathDepth + (1 - _breathDepth) * sleep);

            float open = 1f;
            if (_blinkAt >= 0)
            {
                float b = _blinkAt;     // shut in 0.08 s, shut for 0.04, open in 0.14
                open = b < 0.08f ? 1 - Smooth(b / 0.08f) : b < 0.12f ? 0 : Smooth((b - 0.12f) / 0.14f);
            }
            open *= awake;
            P.eyeLOpen = open; P.eyeROpen = open;

            float lw = Math.Min(1f, Math.Max(0f, lookWeight)) * awake;
            float gx = _gazeX * (1 - lw) * awake + lookX * lw, gy = _gazeY * (1 - lw) * awake + lookY * lw;
            P.eyeBallX = Math.Max(-1f, Math.Min(1f, gx)); P.eyeBallY = Math.Max(-1f, Math.Min(1f, gy));

            float a = amount, w = a * (1 - 0.6f * sleep);
            // (asked for more than the model's range, they come to rest against it softly instead of stopping dead)
            P.angleX = Within((_hx * a + 2.2f * w * Wander(_t * 0.11f, 1)) * turn, 30f);
            P.angleY = Within((_hy * a + 1.6f * w * Wander(_t * 0.13f, 2)) * turn - 9f * sleep * a, 30f);
            P.angleZ = Within((_tilt * a + 1.8f * w * Wander(_t * 0.09f, 3)) * tilt, 30f);
            P.bodyX = Within(2.2f * w * Wander(_t * 0.07f, 4) * lean, 10f);
            P.bodyZ = Within((_lean * a + 1.4f * w * Wander(_t * 0.06f, 5)) * lean, 10f);
        }

        void Step(float dt)
        {
            _t += dt;
            float sleep = Smooth(asleep);

            _breathPhase += dt / (_breathPeriod * (1 + 0.45f * sleep));
            if (_breathPhase >= 1) { _breathPhase -= 1; _breathsToDeep -= 1; NextBreath(); }

            // blinks
            _sinceBlink += dt;
            if (_blinkAt >= 0)
            {
                _blinkAt += dt;
                if (_blinkAt >= 0.26f)
                {
                    _blinkAt = -1;
                    if (_blinkAgain) { _blinkAgain = false; _blinkIn = Rand(0.08f, 0.16f); }
                    else _blinkIn = blinkEvery * Rand(0.45f, 1.55f);
                }
            }
            else
            {
                _blinkIn -= dt;
                if (_blinkIn <= 0) StartBlink(_rng.NextDouble() < 0.18);
            }

            // eyes: rest on one thing, then jump to another
            _gazeIn -= dt;
            if (_gazeIn <= 0)
            {
                double r = _rng.NextDouble();
                float ox = _toX, oy = _toY;
                if (r < 0.55) { _toX = Rand(-0.08f, 0.08f); _toY = Rand(-0.06f, 0.06f); _gazeIn = Rand(2.5f, 6.5f); }
                else if (r < 0.82) { _toX = glanceX + Rand(-0.08f, 0.08f); _toY = glanceY + Rand(-0.06f, 0.06f); _gazeIn = Rand(2f, 5f); }
                else { _toX = Rand(-0.6f, 0.6f); _toY = Rand(-0.3f, 0.25f); _gazeIn = Rand(1.2f, 3f); }
                // a far jump of the eyes comes with a blink, as it does in people
                if (MathF.Abs(_toX - ox) + MathF.Abs(_toY - oy) > 0.5f && _blinkAt < 0 && _sinceBlink > 1f) StartBlink(false);
            }
            float k = Math.Min(1f, dt * 18);
            _gazeX += (_toX - _gazeX) * k; _gazeY += (_toY - _gazeY) * k;

            // the head goes part of the way the eyes went, late
            float lw = Math.Min(1f, Math.Max(0f, lookWeight));
            float tx = _toX * (1 - lw) + lookX * lw, ty = _toY * (1 - lw) + lookY * lw;
            Ease(ref _hx, ref _hvx, 9f * tx, 3.2f, dt);
            Ease(ref _hy, ref _hvy, 7f * ty, 3.2f, dt);

            // now and then the head tilts a little, stays, and comes back
            if (_tiltLeft > 0)
            {
                _tiltLeft -= dt;
                if (_tiltLeft <= 0) { _tiltTo = 0; _tiltIn = Rand(9, 20); }
            }
            else
            {
                _tiltIn -= dt;
                if (_tiltIn <= 0) { _tiltTo = Rand(4f, 8f) * (_rng.NextDouble() < 0.5 ? -1 : 1); _tiltLeft = Rand(1.8f, 4f); }
            }
            Ease(ref _tilt, ref _tiltV, _tiltTo, 3.6f, dt);

            // and she shifts her weight on the seat
            _leanIn -= dt;
            if (_leanIn <= 0) { _leanTo = Rand(-2.6f, 2.6f); _leanIn = Rand(14, 30); }
            Ease(ref _lean, ref _leanV, _leanTo, 1.6f, dt);
        }

        void StartBlink(bool twice)
        {
            _blinkAt = 0; _blinkAgain = twice; _sinceBlink = 0;
        }
    }
}
