using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iori.Puppet.Tests
{
    public class PuppetTests
    {
        // A small seated figure: the seat line (bottom_y) at 560, legs and chair ("base") below it.
        const string Model = @"{
 ""format"": ""livliv-model"", ""version"": 4, ""name"": ""test"", ""canvas"": [512, 768],
 ""anchors"": { ""head"": { ""cx"": 256, ""cy"": 200, ""rx"": 90, ""ry"": 110 }, ""face"": [176, 120, 336, 300], ""neck_y"": 310, ""top_y"": 60,
   ""bottom_y"": 560, ""hair_root_y"": 110, ""eyes"": [[200, 180, 240, 210], [272, 180, 312, 210]], ""jaw"": { ""top"": 250, ""chin"": 300, ""half"": 4, ""open"": 9 } },
 ""rig"": { ""wind"": 0, ""chains"": [ { ""id"": ""ribbon"", ""attach"": ""head"", ""points"": [[340, 220], [344, 260], [346, 300]], ""k"": [60, 30], ""damp"": 4, ""drive"": 1, ""hang"": 0.8, ""wind"": 0 } ] },
 ""layers"": [
  { ""id"": ""hair_back"", ""texture"": ""t/hair_back.png"", ""group"": ""hair_back"", ""x"": 150, ""y"": 80, ""w"": 212, ""h"": 400, ""depth"": -0.3 },
  { ""id"": ""base"", ""texture"": ""t/base.png"", ""group"": ""body"", ""x"": 60, ""y"": 560, ""w"": 400, ""h"": 208 },
  { ""id"": ""body"", ""texture"": ""t/body.png"", ""group"": ""body"", ""x"": 120, ""y"": 290, ""w"": 272, ""h"": 280 },
  { ""id"": ""face"", ""texture"": ""t/face.png"", ""group"": ""head"", ""x"": 170, ""y"": 100, ""w"": 172, ""h"": 215 },
  { ""id"": ""lid_L"", ""texture"": ""t/lid_L.png"", ""group"": ""head"", ""role"": ""lid_L"", ""x"": 198, ""y"": 178, ""w"": 44, ""h"": 34, ""depth"": 0.02 },
  { ""id"": ""eye_L"", ""texture"": ""t/eye_L.png"", ""mask"": ""t/eye_L_mask.png"", ""group"": ""head"", ""role"": ""eye_L"", ""x"": 198, ""y"": 178, ""w"": 44, ""h"": 34, ""depth"": 0.02, ""pivot"": [220, 206] },
  { ""id"": ""iris_L"", ""texture"": ""t/iris_L.png"", ""group"": ""head"", ""role"": ""iris_L"", ""clip"": ""eye_L"", ""x"": 208, ""y"": 180, ""w"": 24, ""h"": 28, ""depth"": 0.02, ""pivot"": [220, 206], ""range"": [6, 3] },
  { ""id"": ""mouth"", ""texture"": ""t/mouth0.png"", ""frames"": [""t/mouth0.png"", ""t/mouth1.png"", ""t/mouth2.png""], ""group"": ""head"", ""role"": ""mouth"", ""x"": 236, ""y"": 258, ""w"": 40, ""h"": 30, ""depth"": 0.03, ""pivot"": [256, 264] },
  { ""id"": ""ribbon"", ""texture"": ""t/ribbon.png"", ""group"": ""hair_side"", ""bones"": [""ribbon""], ""x"": 326, ""y"": 214, ""w"": 36, ""h"": 96, ""depth"": 0.1 },
  { ""id"": ""hair_front"", ""texture"": ""t/hair_front.png"", ""group"": ""hair_front"", ""x"": 160, ""y"": 70, ""w"": 192, ""h"": 150, ""depth"": 0.12 }
 ],
 ""x-iori"": { ""head_tilt"": 0, ""body_pivot"": [256, 600] } }";

        static PuppetRig Rig(string json = null) => new PuppetRig(PuppetModel.Parse(json ?? Model));

        static PuppetLayer Layer(PuppetRig r, string id)
        {
            foreach (var L in r.layers) if (L.def.id == id) return L;
            throw new ArgumentException(id);
        }

        static int Nearest(PuppetLayer L, float x, float y)
        {
            int best = 0; float bd = float.PositiveInfinity;
            for (int i = 0; i < L.rest.Length / 2; i++)
            {
                float dx = L.rest[2 * i] - x, dy = L.rest[2 * i + 1] - y, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static float MaxMove(PuppetLayer L)
        {
            float m = 0;
            for (int i = 0; i < L.rest.Length; i++) m = Math.Max(m, Math.Abs(L.pos[i] - L.rest[i]));
            return m;
        }

        static void Run(PuppetRig r, float seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds * 60); i++) r.Update(1f / 60f);
        }

        static void AssertFinite(PuppetRig r)
        {
            foreach (var d in r.draws)
                foreach (float v in d.positions) Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False, "layer " + r.layers[d.layer].def.id);
        }

        static PuppetDraw Draw(PuppetRig r, string id, PuppetTexture tex = PuppetTexture.Main)
        {
            foreach (var d in r.draws) if (r.layers[d.layer].def.id == id && d.texture == tex) return d;
            throw new ArgumentException(id + " is not drawn");
        }

        static bool Drawn(PuppetRig r, string id)
        {
            foreach (var d in r.draws) if (r.layers[d.layer].def.id == id) return true;
            return false;
        }

        [Test]
        public void Parse_ReadsTheModel()
        {
            var m = PuppetModel.Parse(Model);
            Assert.That(m.canvasW, Is.EqualTo(512));
            Assert.That(m.canvasH, Is.EqualTo(768));
            Assert.That(m.Lattice, Is.EqualTo(6));
            Assert.That(m.layers.Length, Is.EqualTo(10));
            Assert.That(m.anchors.bottomY, Is.EqualTo(560f));
            Assert.That(m.anchors.hasJaw, Is.True);
            Assert.That(m.rig.wind, Is.EqualTo(0f));
            Assert.That(m.rig.yaw, Is.EqualTo(0.27f));
            Assert.That(m.rig.chains.Length, Is.EqualTo(1));
            Assert.That(m.rig.chains[0].points, Is.EqualTo(new float[] { 340, 220, 344, 260, 346, 300 }));
            Assert.That(m.layers[7].frames.Length, Is.EqualTo(3));
            Assert.That(m.layers[6].clip, Is.EqualTo("eye_L"));
            Assert.That(m.bodyPivot, Is.EqualTo(new float[] { 256, 600 }));
        }

        [Test]
        public void Parse_RefusesWhatItCannotPlay()
        {
            Assert.Throws<FormatException>(() => PuppetModel.Parse(Model.Replace(@"""name"": ""test""", @"""requires"": [""full_body""]")));
            Assert.Throws<FormatException>(() => PuppetModel.Parse(Model.Replace(@"""group"": ""hair_back""", @"""group"": ""tail""")));
            Assert.Throws<FormatException>(() => PuppetModel.Parse(Model.Replace(@"""canvas"": [512, 768],", "")));
            Assert.Throws<FormatException>(() => PuppetModel.Parse(Model.Replace(@"""version"": 4", @"""version"": 5")));
        }

        [Test]
        public void AtRest_EveryLayerIsWhereItWasDrawn()
        {
            var r = Rig();
            var order = new List<string>();
            foreach (var d in r.draws)
            {
                var L = r.layers[d.layer];
                order.Add(L.def.id + ":" + d.texture + ":" + d.stencil);
                for (int i = 0; i < L.rest.Length; i++) Assert.That(d.positions[i], Is.EqualTo(L.rest[i]).Within(1e-3f), L.def.id);
            }
            // the closed lashes are not drawn on an open eye; the whites go into the stencil, the iris is cut by it
            Assert.That(order, Is.EqualTo(new[]
            {
                "hair_back:Main:None", "base:Main:None", "body:Main:None", "face:Main:None", "eye_L:Main:None", "eye_L:Mask:Write",
                "iris_L:Main:Test", "mouth:Frame:None", "ribbon:Main:None", "hair_front:Main:None",
            }));
            Assert.That(Draw(r, "iris_L").stencilRef, Is.EqualTo(Draw(r, "eye_L", PuppetTexture.Mask).stencilRef));
            Assert.That(Draw(r, "iris_L").stencilRef, Is.GreaterThan(0));
        }

        [Test]
        public void BelowTheSeatLine_NothingMoves()
        {
            var r = Rig();
            var baseL = Layer(r, "base"); var body = Layer(r, "body");
            Assert.That(baseL.still, Is.True);
            Assert.That(body.still, Is.False);
            r.param.angleX = 30; r.param.angleZ = -30; r.param.bodyX = 10; r.param.bodyZ = 10; r.param.breath = 1;
            Run(r, 2f);
            AssertFinite(r);
            Assert.That(MaxMove(baseL), Is.EqualTo(0f));
            // the torso leans from the hips: a lot at the shoulders, nothing at the seat
            int shoulder = Nearest(body, 200, 330), seat = Nearest(body, 200, 566);
            float sm = Math.Abs(body.pos[2 * shoulder] - body.rest[2 * shoulder]) + Math.Abs(body.pos[2 * shoulder + 1] - body.rest[2 * shoulder + 1]);
            float bm = Math.Abs(body.pos[2 * seat] - body.rest[2 * seat]) + Math.Abs(body.pos[2 * seat + 1] - body.rest[2 * seat + 1]);
            Assert.That(sm, Is.GreaterThan(5f));
            Assert.That(bm, Is.LessThan(0.01f));
        }

        [Test]
        public void Mouth_WithASmallFrame_ClimbsFourSteps_AndTheRoundVowelStillWins()
        {
            string json = Model.Replace(@"""frames"": [""t/mouth0.png"", ""t/mouth1.png"", ""t/mouth2.png""]",
                @"""frames"": [""t/m0.png"", ""t/m1.png"", ""t/m2.png"", ""t/m3.png"", ""t/m4.png""], ""visemes"": [""closed"", ""small"", ""half"", ""open"", ""round""]");
            var r = Rig(json);
            int Shown(float open, float vowel = 0)
            {
                r.param.mouthOpen = open; r.param.vowel = vowel;
                Run(r, 0.3f);
                return Draw(r, "mouth", PuppetTexture.Frame).frame;
            }
            Assert.That(Shown(0), Is.EqualTo(0));
            Assert.That(Shown(0.2f), Is.EqualTo(1));
            Assert.That(Shown(0.45f), Is.EqualTo(2));
            Assert.That(Shown(1), Is.EqualTo(3));
            Assert.That(Shown(0.45f), Is.EqualTo(2));
            Assert.That(Shown(0.2f), Is.EqualTo(1));
            Assert.That(Shown(0.5f, -1), Is.EqualTo(4));
            Assert.That(Shown(0), Is.EqualTo(0));
            AssertFinite(r);
        }

        [Test]
        public void TiltedHead_ClosesItsEyesAndLooksAlongItsOwnAxes()
        {
            // the head drawn lying on its side: the eye shuts across the picture, and "look right" goes down it
            var r = Rig(Model.Replace(@"""head_tilt"": 0", @"""head_tilt"": 90"));
            var eye = Layer(r, "eye_L"); var iris = Layer(r, "iris_L");
            float Span(PuppetLayer L, int axis, bool rest)
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                var a = rest ? L.rest : L.pos;
                for (int i = axis; i < a.Length; i += 2) { lo = Math.Min(lo, a[i]); hi = Math.Max(hi, a[i]); }
                return hi - lo;
            }
            int mid = Nearest(iris, 220, 194);
            r.param.eyeBallX = 1;
            r.Update(1f / 60f);
            float ix = iris.pos[2 * mid], iy = iris.pos[2 * mid + 1];
            r.param.eyeBallX = -1;
            r.Update(1f / 60f);
            Assert.That(Math.Abs(iris.pos[2 * mid] - ix), Is.LessThan(0.01f));
            Assert.That(ix > 0 && iy - iris.pos[2 * mid + 1] > 5f, Is.True);
            r.param.eyeBallX = 0; r.param.eyeLOpen = 0;
            r.Update(1f / 60f);
            Assert.That(Span(eye, 0, false), Is.LessThan(0.2f * Span(eye, 0, true)));
            Assert.That(Span(eye, 1, false), Is.GreaterThan(0.9f * Span(eye, 1, true)));
            AssertFinite(r);
        }

        [Test]
        public void LayersNamedStill_NeverMove_EvenAboveTheSeatLine()
        {
            // the legs and the chair as one layer whose knees rise above the seat line, in front of the body
            string json = Model.Replace(@"""x"": 60, ""y"": 560, ""w"": 400, ""h"": 208", @"""x"": 60, ""y"": 440, ""w"": 400, ""h"": 328")
                               .Replace(@"""head_tilt"": 0,", @"""head_tilt"": 0, ""still"": [""base""],");
            var moving = Layer(Rig(Model.Replace(@"""x"": 60, ""y"": 560, ""w"": 400, ""h"": 208", @"""x"": 60, ""y"": 440, ""w"": 400, ""h"": 328")), "base");
            Assert.That(moving.still, Is.False);
            var r = Rig(json);
            var baseL = Layer(r, "base");
            Assert.That(baseL.still, Is.True);
            r.param.angleX = 30; r.param.bodyX = 10; r.param.bodyZ = 10; r.param.breath = 1;
            Run(r, 2f);
            AssertFinite(r);
            Assert.That(MaxMove(baseL), Is.EqualTo(0f));
            Assert.That(MaxMove(Layer(r, "body")), Is.GreaterThan(5f));
            Assert.Throws<FormatException>(() => PuppetModel.Parse(Model.Replace(@"""head_tilt"": 0,", @"""head_tilt"": 0, ""still"": [""nobody""],")));
        }

        [Test]
        public void TurningTheHead_MovesTheFaceTowardTheTurn()
        {
            var r = Rig();
            var face = Layer(r, "face");
            int nose = Nearest(face, 256, 200);
            r.param.angleX = 30;
            Run(r, 2f);
            AssertFinite(r);
            Assert.That(face.pos[2 * nose] - face.rest[2 * nose], Is.GreaterThan(5f));
            r.param.angleX = -30;
            Run(r, 2f);
            Assert.That(face.pos[2 * nose] - face.rest[2 * nose], Is.LessThan(-5f));
        }

        [Test]
        public void Blink_SquashesTheEyeAndShowsTheLashes()
        {
            var r = Rig();
            var eye = Layer(r, "eye_L");
            Assert.That(Drawn(r, "lid_L"), Is.False);
            r.param.eyeLOpen = 0;
            r.Update(1f / 60f);
            Assert.That(Draw(r, "lid_L").alpha, Is.GreaterThan(0.99f));
            Assert.That(Draw(r, "eye_L").alpha, Is.LessThan(0.01f));
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity, rlo = float.PositiveInfinity, rhi = float.NegativeInfinity;
            for (int i = 1; i < eye.pos.Length; i += 2)
            {
                lo = Math.Min(lo, eye.pos[i]); hi = Math.Max(hi, eye.pos[i]);
                rlo = Math.Min(rlo, eye.rest[i]); rhi = Math.Max(rhi, eye.rest[i]);
            }
            Assert.That(hi - lo, Is.LessThan(0.2f * (rhi - rlo)));
        }

        [Test]
        public void Gaze_MovesTheIrisByItsRange()
        {
            var r = Rig();
            var iris = Layer(r, "iris_L");
            r.param.eyeBallX = 1; r.param.eyeBallY = 1;
            r.Update(1f / 60f);
            int i = Nearest(iris, 220, 194);
            Assert.That(iris.pos[2 * i] - iris.rest[2 * i], Is.EqualTo(6f).Within(0.3f));
            Assert.That(iris.pos[2 * i + 1] - iris.rest[2 * i + 1], Is.EqualTo(-3f).Within(0.3f));
            // the whites it is cut by stay where they are
            Assert.That(MaxMove(Layer(r, "eye_L")), Is.LessThan(1e-3f));
        }

        [Test]
        public void Mouth_ShowsTheFrameForHowOpenItIs_AndDropsTheJaw()
        {
            var r = Rig();
            var face = Layer(r, "face");
            int chin = Nearest(face, 256, 298);
            r.param.mouthOpen = 1;
            Run(r, 0.3f);
            Assert.That(Draw(r, "mouth", PuppetTexture.Frame).frame, Is.EqualTo(2));
            Assert.That(face.pos[2 * chin + 1] - face.rest[2 * chin + 1], Is.GreaterThan(5f));
            r.param.mouthOpen = 0.3f;
            Run(r, 0.3f);
            Assert.That(Draw(r, "mouth", PuppetTexture.Frame).frame, Is.EqualTo(1));
            r.param.mouthOpen = 0;
            Run(r, 0.5f);
            Assert.That(Draw(r, "mouth", PuppetTexture.Frame).frame, Is.EqualTo(0));
            Assert.That(Math.Abs(face.pos[2 * chin + 1] - face.rest[2 * chin + 1]), Is.LessThan(0.05f));
        }

        [Test]
        public void HairAndRibbon_LagBehindTheHead_ThenSettle()
        {
            var r = Rig();
            var back = Layer(r, "hair_back"); var ribbon = Layer(r, "ribbon"); var face = Layer(r, "face");
            int tip = Nearest(back, 256, 470), end = Nearest(ribbon, 346, 300), cheek = Nearest(face, 330, 220);
            float swing = 0, ribbonOff = 0;
            for (int i = 0; i < 120; i++)
            {
                r.param.angleX = (i / 15) % 2 == 0 ? 30 : -30;
                r.Update(1f / 60f);
                swing = Math.Max(swing, Math.Abs(back.pos[2 * tip] - back.rest[2 * tip]));
                // the ribbon's end against a point of the face next to its root
                float rel = (ribbon.pos[2 * end] - ribbon.rest[2 * end]) - (face.pos[2 * cheek] - face.rest[2 * cheek]);
                ribbonOff = Math.Max(ribbonOff, Math.Abs(rel));
            }
            AssertFinite(r);
            Assert.That(swing, Is.GreaterThan(1f));
            Assert.That(ribbonOff, Is.GreaterThan(1f));
            r.param.angleX = 0;
            Run(r, 8f);
            foreach (var L in r.layers) if (Drawn(r, L.def.id)) Assert.That(MaxMove(L), Is.LessThan(0.3f), L.def.id);
        }

        [Test]
        public void TiltedHead_NodsAlongItsOwnAxis()
        {
            var straight = Rig();
            var tilted = Rig(Model.Replace(@"""head_tilt"": 0", @"""head_tilt"": 90"));
            foreach (var r in new[] { straight, tilted })
            {
                for (int i = 0; i < r.draws.Count; i++)
                {
                    var L = r.layers[r.draws[i].layer];
                    for (int k = 0; k < L.rest.Length; k++) Assert.That(L.pos[k], Is.EqualTo(L.rest[k]).Within(1e-2f));
                }
                r.param.angleY = 30;
                r.Update(1f / 60f);
            }
            var a = Layer(straight, "face"); var b = Layer(tilted, "face");
            int n = Nearest(a, 256, 200);
            float ax = a.pos[2 * n] - a.rest[2 * n], ay = a.pos[2 * n + 1] - a.rest[2 * n + 1];
            float bx = b.pos[2 * n] - b.rest[2 * n], by = b.pos[2 * n + 1] - b.rest[2 * n + 1];
            Assert.That(Math.Abs(ay), Is.GreaterThan(5 * Math.Abs(ax)));
            Assert.That(Math.Abs(bx), Is.GreaterThan(5 * Math.Abs(by)));
        }

        [Test]
        public void Occupancy_LeavesOutEmptyCells()
        {
            var m = PuppetModel.Parse(Model);
            var def = m.layers[2];   // body, 272 x 280
            int w = 68, h = 70;
            var alpha = new byte[w * h];
            for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++) alpha[y * w + x] = 255;    // only its top left corner
            var occ = new byte[m.layers.Length][];
            occ[2] = PuppetRig.Occupancy(m, def, new[] { alpha }, w, h);
            PuppetRig.GridOf(m, def, out _, out _, out int cols, out int rows);
            Assert.That(occ[2].Length, Is.EqualTo(cols * rows));
            var cut = new PuppetRig(m, occ); var whole = new PuppetRig(m);
            Assert.That(cut.layers[2].tris.Length, Is.GreaterThan(0));
            Assert.That(cut.layers[2].tris.Length, Is.LessThan(whole.layers[2].tris.Length / 10));
            Assert.That(whole.layers[2].tris.Length, Is.EqualTo(cols * rows * 6));
            occ[2] = new byte[3];
            Assert.Throws<ArgumentException>(() => new PuppetRig(m, occ));
        }

        [Test]
        public void Idle_BlinksBreathesAndStaysInRange()
        {
            var idle = new PuppetIdle(7); var again = new PuppetIdle(7);
            var P = new PuppetParams(); var Q = new PuppetParams();
            int blinks = 0; bool shut = false;
            float lo = 1, hi = 0, tiltMost = 0, gazeMost = 0;
            for (int i = 0; i < 120 * 60; i++)
            {
                idle.Apply(P, 1f / 60f); again.Apply(Q, 1f / 60f);
                Assert.That(P.angleX, Is.EqualTo(Q.angleX));
                Assert.That(P.eyeLOpen, Is.EqualTo(Q.eyeLOpen));
                foreach (float v in new[] { P.angleX, P.angleY, P.angleZ, P.bodyX, P.bodyZ, P.breath, P.eyeLOpen, P.eyeBallX, P.eyeBallY })
                    Assert.That(float.IsNaN(v), Is.False);
                Assert.That(Math.Abs(P.angleX), Is.LessThanOrEqualTo(30f));
                Assert.That(Math.Abs(P.angleY), Is.LessThanOrEqualTo(30f));
                Assert.That(Math.Abs(P.angleZ), Is.LessThanOrEqualTo(30f));
                Assert.That(Math.Abs(P.bodyX), Is.LessThanOrEqualTo(10f));
                Assert.That(Math.Abs(P.bodyZ), Is.LessThanOrEqualTo(10f));
                Assert.That(P.breath, Is.InRange(0f, 1f));
                Assert.That(P.eyeLOpen, Is.InRange(0f, 1f));
                Assert.That(P.eyeBallX, Is.InRange(-1f, 1f));
                Assert.That(P.eyeBallY, Is.InRange(-1f, 1f));
                if (P.eyeLOpen < 0.05f && !shut) { shut = true; blinks++; }
                if (P.eyeLOpen > 0.9f) shut = false;
                lo = Math.Min(lo, P.breath); hi = Math.Max(hi, P.breath);
                tiltMost = Math.Max(tiltMost, Math.Abs(P.angleZ));
                gazeMost = Math.Max(gazeMost, Math.Abs(P.eyeBallX));
            }
            // two minutes: a blink every few seconds, whole breaths, at least one tilt of the head and one glance away
            Assert.That(blinks, Is.InRange(15, 70));
            Assert.That(lo, Is.LessThan(0.02f));
            Assert.That(hi, Is.GreaterThan(0.9f));
            Assert.That(tiltMost, Is.GreaterThan(3f));
            Assert.That(gazeMost, Is.GreaterThan(0.25f));
        }

        // pictures of her motion are each played from rest (PuppetRenderer.RenderAt): played again, the motion must be the
        // same one, or pictures a thirtieth of a second apart show poses from different motions and she jumps about
        [Test]
        public void Idle_PlayedAgainFromReset_IsTheSameMotion_SoPicturesTakenOneByOneJoinUp()
        {
            var idle = new PuppetIdle(1);
            var P = new PuppetParams();
            const float dt = 1f / 30f;
            float[] Play(int steps)
            {
                idle.Reset();
                for (int i = 0; i < steps; i++) idle.Apply(P, dt);
                return new[] { P.angleX, P.angleY, P.angleZ, P.bodyX, P.bodyZ, P.eyeBallX, P.eyeBallY, P.eyeLOpen, P.breath };
            }
            var first = Play(600);
            var again = Play(600);
            for (int k = 0; k < first.Length; k++) Assert.AreEqual(first[k], again[k], 1e-6f, "value " + k);

            // and one step on is a small step away, all through twenty seconds (head and body, in degrees)
            float worst = 0;
            var prev = Play(30);
            for (int n = 31; n <= 600; n++)
            {
                var now = Play(n);
                for (int k = 0; k < 5; k++) worst = Math.Max(worst, Math.Abs(now[k] - prev[k]));
                prev = now;
            }
            Assert.Less(worst, 0.8f, "degrees between two pictures a thirtieth of a second apart");
        }

        [Test]
        public void Idle_Asleep_KeepsTheEyesShut()
        {
            var idle = new PuppetIdle(3) { asleep = 1 };
            var P = new PuppetParams();
            for (int i = 0; i < 600; i++)
            {
                idle.Apply(P, 1f / 60f);
                Assert.That(P.eyeLOpen, Is.EqualTo(0f));
            }
        }

        [Test]
        public void Idle_DrivesTheRig_WithoutBreakingIt()
        {
            var r = Rig();
            var idle = new PuppetIdle(11);
            for (int i = 0; i < 30 * 30; i++)
            {
                idle.Apply(r.param, 1f / 30f);
                r.Update(1f / 30f);
            }
            AssertFinite(r);
            foreach (var L in r.layers) if (Drawn(r, L.def.id)) Assert.That(MaxMove(L), Is.LessThan(40f), L.def.id);
            Assert.That(MaxMove(Layer(r, "base")), Is.EqualTo(0f));
        }
    }
}
