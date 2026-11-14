// A layered 2D character moved the way LivLiv Player moves it: every layer is a lattice mesh, and each vertex takes
// the head's turn (the head as a rounded volume), the torso's breathing and lean, the hair's springs, the swinging
// chains, the blink's squash and the jaw's drop by weights that are functions of its place on the drawing.
//
// This file is a C# port of parts of LivLiv Player's src/runtime/puppet.ts
// (https://github.com/livliv-dev/player, Copyright 2026 LivLiv, Apache License 2.0; see ../NOTICE.md).
// Changed from the original: no drawing here (the caller draws `draws` with its own renderer), no brows' lift,
// keyforms, full-body figures, chest spring, soft iris masks or lid curves; added the head's own tilt and the
// body's pivot (PuppetModel's x-iori).
using System;
using System.Collections.Generic;

namespace Iori.Puppet
{
    public enum PuppetTexture { Main, Frame, Mask }
    public enum PuppetStencil { None, Write, Test }

    /// <summary>One thing to draw this frame, in order.</summary>
    public struct PuppetDraw
    {
        public int layer;
        public PuppetTexture texture;
        /// <summary>which of the layer's frames, for PuppetTexture.Frame</summary>
        public int frame;
        public float alpha;
        /// <summary>Write: only into the stencil, where the texture's alpha is at least a half. Test: only where the stencil holds stencilRef.</summary>
        public PuppetStencil stencil;
        public int stencilRef;
        /// <summary>the vertices to draw it with (x, y pairs, model px, y down)</summary>
        public float[] positions;
    }

    public sealed class PuppetLayer
    {
        public PuppetLayerDef def;
        /// <summary>x, y pairs in model px (y down): at rest, and where they are now</summary>
        public float[] rest, pos;
        /// <summary>u, v pairs, v down like the picture (outside 0..1 where the lattice reaches past the layer's box)</summary>
        public float[] uv;
        public int[] tris;
        /// <summary>true for a layer nothing ever moves (a seated figure's legs and chair, below the seat line):
        /// pos stays at rest, so its mesh needs sending only once</summary>
        public bool still;
        internal float[] mouthPos;
        internal float[] wHead, wPhys1, wPhys2, dep, wFront, wJaw, wNeck, wTwist;
        internal PuppetRig.Skin skin;
        internal int stencilRef;
        internal int side;      // 0: none, 1: the picture's left eye, 2: its right
        internal bool isEye, isIris, isShine, isLid;
    }

    public sealed class PuppetRig
    {
        public readonly PuppetModel model;
        public readonly PuppetLayer[] layers;
        public readonly PuppetParams param = new PuppetParams();
        /// <summary>What to draw this frame, in order (filled by Update).</summary>
        public readonly List<PuppetDraw> draws = new List<PuppetDraw>();
        /// <summary>Expression weights by id (smile lives in param).</summary>
        public readonly Dictionary<string, float> expr = new Dictionary<string, float>();
        public float physicsScale = 1;
        public float time;

        const float ChinNeck = 0.08f, ChinNeckX0 = 0.2f, ChinNeckX1 = 0.34f;
        const float NeckFollow = 0.2f, NeckFollowY = 0.6f, NeckFollowX0 = 0.6f, NeckFollowX1 = 1.3f;
        const float NeckShadow = 0.3f, ProcJaw = 0.015f, IrisDrop = 0.3f;
        const float MouthAt1 = 0.12f, MouthAt2 = 0.5f, MouthHyst = 0.04f, MouthHold = 0.055f;
        const float VowelIn = 0.65f, VowelOut = 0.35f, TalkLatch = 0.4f;
        const float SideAt0 = 0.5f, SideAt1 = 0.85f;

        sealed class Spring { public float x, y, vx, vy; public void Reset() { x = y = vx = vy = 0; } }

        internal sealed class Skin { public int B; public int[] c, seg; public float[] t, ux, uy, w, sum; }

        sealed class ChainRun
        {
            public PuppetChainDef def;
            public float[] R, p, q, len, s, disp, beta;
            public float wx, wy, vx, vy, accX, accY, rho, phase;
            public bool started, moving;
        }

        readonly Dictionary<string, int> _byId = new Dictionary<string, int>();
        readonly Spring[] _front = { new Spring(), new Spring() }, _back = { new Spring(), new Spring() }, _side = { new Spring(), new Spring() };
        readonly ChainRun[] _chains;
        readonly float _tiltCos, _tiltSin;
        float _lastHeadX, _lastHeadY;
        bool _headStarted;
        float _dvX, _dvY, _dvVx, _dvVy, _dvAx, _dvAy;
        bool _dvStarted;
        float _bodyYaw, _bodyRoll;
        float _smileShown;
        // the head's turn, nod and roll as cosines and sines
        float _cy = 1, _sy, _cp = 1, _sp, _cr = 1, _sr, _roll;
        // the mouth
        int _mouthFrame, _mouthAmp, _mouthShape;      // shape: 0 none, 1 round, 2 wide
        float _mouthHeld = 1, _talkLeft;
        float[] _mouthW;
        readonly string[] _frameKind;
        readonly float[] _frameJaw;
        readonly int _visRound = -1, _visWide = -1, _visShut = -1;
        readonly bool _anyJaw;

        /// <summary>The layer's part of the model's one lattice: its first cell and how many cells it spans.</summary>
        public static void GridOf(PuppetModel m, PuppetLayerDef L, out int gx0, out int gy0, out int cols, out int rows)
        {
            int cell = m.Lattice;
            gx0 = FloorDiv(L.x, cell); gy0 = FloorDiv(L.y, cell);
            cols = CeilDiv(L.x + L.w, cell) - gx0; rows = CeilDiv(L.y + L.h, cell) - gy0;
        }
        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
        static int CeilDiv(int a, int b) => (int)Math.Ceiling(a / (double)b);

        /// <summary>Which of the layer's lattice cells hold anything of its images (any of them: the mouth's frames
        /// share one mesh), and the cells next to those. alphas: each image's alpha, w x h, top row first.</summary>
        public static byte[] Occupancy(PuppetModel m, PuppetLayerDef L, byte[][] alphas, int w, int h)
        {
            GridOf(m, L, out int gx0, out int gy0, out int cols, out int rows);
            int cell = m.Lattice;
            var occ = new byte[cols * rows];
            float sx = L.w / (float)w, sy = L.h / (float)h;
            foreach (var a in alphas)
            {
                if (a == null) continue;
                for (int py = 0; py < h; py++)
                {
                    int r = (int)Math.Floor((L.y + (py + 0.5f) * sy) / cell) - gy0;
                    if (r < 0 || r >= rows) continue;
                    int row = py * w;
                    for (int px = 0; px < w; px++)
                    {
                        if (a[row + px] == 0) continue;
                        int c = (int)Math.Floor((L.x + (px + 0.5f) * sx) / cell) - gx0;
                        if (c >= 0 && c < cols) occ[r * cols + c] = 1;
                    }
                }
            }
            var o = (byte[])occ.Clone();
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
            {
                if (occ[r * cols + c] != 0) continue;
                for (int dr = -1; dr <= 1 && o[r * cols + c] == 0; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if (rr >= 0 && rr < rows && cc >= 0 && cc < cols && occ[rr * cols + cc] != 0) { o[r * cols + c] = 1; break; }
                }
            }
            return o;
        }

        /// <summary>occupancy: per layer, Occupancy()'s result (cols x rows of GridOf), or null for every cell.</summary>
        public PuppetRig(PuppetModel model, byte[][] occupancy = null)
        {
            this.model = model;
            float tilt = model.headTilt * MathF.PI / 180f;
            _tiltCos = MathF.Cos(tilt); _tiltSin = MathF.Sin(tilt);

            _chains = new ChainRun[model.rig.chains.Length];
            for (int i = 0; i < _chains.Length; i++) _chains[i] = MakeChain(model.rig.chains[i]);

            // the mouth's frames: what each one is, and the jaw drop it was drawn with
            PuppetLayerDef mouth = null;
            foreach (var L in model.layers) if (L.role == "mouth") { mouth = L; break; }
            int frames = mouth != null && mouth.frames.Length > 0 ? mouth.frames.Length : 3;
            string[] usual = { "closed", "half", "open" };
            _frameKind = new string[frames];
            for (int i = 0; i < frames; i++) _frameKind[i] = mouth != null && i < mouth.visemes.Length ? mouth.visemes[i] : i < 3 ? usual[i] : "";
            _frameJaw = new float[frames];
            var A = model.anchors;
            bool some = false;
            if (A.hasJaw)
            {
                for (int i = 0; i < frames; i++)
                {
                    string k = _frameKind[i];
                    float j = k == "half" ? (A.jawOpen > 0 ? A.jawHalf : 0) : k == "open" ? A.jawOpen : k == "round" ? A.jawRound : k == "wide" ? A.jawWide : 0;
                    _frameJaw[i] = j; some |= j > 0;
                }
                float proc = float.IsNaN(model.rig.jaw) ? ProcJaw : model.rig.jaw;
                if (!some && proc > 0)
                {
                    float fh = A.faceY1 - A.faceY0;
                    for (int i = 0; i < frames; i++)
                    {
                        string k = _frameKind[i];
                        float amt = k == "half" ? 0.45f : k == "open" ? 1f : k == "round" ? 0.7f : k == "wide" ? 0.35f : 0f;
                        _frameJaw[i] = proc * fh * amt; some |= _frameJaw[i] > 0;
                    }
                }
            }
            _anyJaw = some;
            for (int i = 0; i < frames; i++)
            {
                if (_frameKind[i] == "round") _visRound = i;
                else if (_frameKind[i] == "wide") _visWide = i;
                else if (_frameKind[i] == "shut") _visShut = i;
            }
            _mouthW = new float[frames];

            layers = new PuppetLayer[model.layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = BuildLayer(model.layers[i], occupancy != null && i < occupancy.Length ? occupancy[i] : null);
                _byId[model.layers[i].id] = i;
            }
            ResetMouth();
            ResetPhysics();
            Deform();
            BuildDraws();
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Math.Min(1f, Math.Max(0f, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        bool Chained => _chains.Length > 0;

        // ---------------------------------------------------------------- building

        static ChainRun MakeChain(PuppetChainDef def)
        {
            int m = def.points.Length / 2;
            var C = new ChainRun { def = def, R = (float[])def.points.Clone(), p = (float[])def.points.Clone(), q = (float[])def.points.Clone(),
                len = new float[m], s = new float[m], disp = new float[2 * m], beta = new float[m] };
            for (int j = 1; j < m; j++)
            {
                C.len[j] = Hypot(C.R[2 * j] - C.R[2 * j - 2], C.R[2 * j + 1] - C.R[2 * j - 1]);
                C.s[j] = C.s[j - 1] + C.len[j];
            }
            float total = C.s[m - 1] > 0 ? C.s[m - 1] : 1;
            for (int j = 1; j < m; j++) C.s[j] /= total;
            int h = 0;
            foreach (char ch in def.id) h = (h * 31 + ch) % 997;
            C.phase = h / 997f * 2 * MathF.PI;
            return C;
        }

        static float Hypot(float x, float y) => MathF.Sqrt(x * x + y * y);

        /// <summary>A point of the drawing in the head's own frame: across and down the head, in head radii.</summary>
        void HeadUv(float x, float y, out float u, out float v)
        {
            var A = model.anchors;
            float dx = x - A.headCx, dy = y - A.headCy;
            u = (dx * _tiltCos + dy * _tiltSin) / A.headRx;
            v = (-dx * _tiltSin + dy * _tiltCos) / A.headRy;
        }

        PuppetLayer BuildLayer(PuppetLayerDef L, byte[] occ)
        {
            var A = model.anchors;
            int cell = model.Lattice;
            GridOf(model, L, out int gx0, out int gy0, out int cols, out int rows);
            if (occ != null && occ.Length != cols * rows) throw new ArgumentException("layer " + L.id + ": occupancy is " + occ.Length + " cells, the layer has " + cols * rows);
            var vid = new int[(cols + 1) * (rows + 1)];
            for (int i = 0; i < vid.Length; i++) vid[i] = -1;
            var pos = new List<float>(); var uv = new List<float>(); var idx = new List<int>();
            int Vert(int c, int r)
            {
                int k = r * (cols + 1) + c;
                if (vid[k] < 0)
                {
                    vid[k] = pos.Count / 2;
                    float X = (gx0 + c) * cell, Y = (gy0 + r) * cell;
                    pos.Add(X); pos.Add(Y); uv.Add((X - L.x) / L.w); uv.Add((Y - L.y) / L.h);
                }
                return vid[k];
            }
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
            {
                if (occ != null && occ[r * cols + c] == 0) continue;
                int a = Vert(c, r), b = Vert(c + 1, r), d = Vert(c, r + 1), e = Vert(c + 1, r + 1);
                idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(e); idx.Add(d);
            }
            int n = pos.Count / 2;
            var P = new PuppetLayer { def = L, rest = pos.ToArray(), uv = uv.ToArray(), tris = idx.ToArray(), pos = new float[2 * n],
                wHead = new float[n], wPhys1 = new float[n], wPhys2 = new float[n], dep = new float[n], wFront = new float[n], wJaw = new float[n] };
            string role = L.role ?? "";
            P.side = role.EndsWith("_L") ? 1 : role.EndsWith("_R") ? 2 : 0;
            P.isIris = role.StartsWith("iris_"); P.isShine = role.StartsWith("shine_"); P.isLid = role.StartsWith("lid_");
            P.isEye = role.StartsWith("eye_") || P.isIris || P.isShine;
            bool body = L.group == "body";
            if (body) P.wNeck = new float[n];
            var R = model.rig;
            if (body && R.hasNeck && R.neckTwist > 0) P.wTwist = new float[n];

            float fw = A.faceX1 - A.faceX0, fh = A.faceY1 - A.faceY0, ry = A.headRy, cx = A.headCx;
            // long hair only starts to swing below the jaw: anything at head height (hats, side locks) stays rigid
            float hairTop = A.neckY - 0.1f * ry;
            float hairLen = Math.Max(1f, A.bottomY - hairTop);
            bool hairFront = L.group == "hair_side" || L.group == "hair_front";
            bool mouthCopy = role == "mouth";
            for (int i = 0; i < n; i++)
            {
                float x = P.rest[2 * i], y = P.rest[2 * i + 1];
                HeadUv(x, y, out float hu, out float hv);
                float rr = Hypot(hu, hv);
                float fallEnd = float.IsNaN(L.fallEnd) ? 1f : L.fallEnd;
                P.dep[i] = L.group == "head" ? L.depth
                    : L.group == "hair_front" ? L.depth * Smooth(fallEnd, float.IsNaN(L.fall) ? 0.55f : L.fall, rr)
                    : L.depth * Smooth(fallEnd, float.IsNaN(L.fallEnd) || float.IsNaN(L.fall) ? 0.7f : L.fall, rr);
                // hair beside the jaw moves with the head
                float longHead = 1 - Smooth(A.neckY + 0.05f * ry, A.neckY + 1.1f * ry, y);
                float tl = Math.Min(1f, Math.Max(0f, (y - hairTop) / hairLen));
                float lp1 = Smooth(0, 0.55f, tl), lp2 = MathF.Pow(Smooth(0.25f, 1, tl), 1.3f);
                // ONE weight field for every hair layer, a function of position only: two layers that overlap at a
                // seam always get the same weights, so they can never tear apart on a tilt or turn.
                // f = 1 inside the skull and above the neck (bangs-like), 0 for long hair
                float f = Smooth(0.95f, 0.7f, rr) * (1 - Smooth(A.neckY - 0.2f * ry, A.neckY + 0.3f * ry, y));
                float tb = Smooth(A.hairRootY + 0.25f * ry, A.headCy + 0.7f * ry, y);
                if (L.group == "head") P.wHead[i] = 1;
                else if (body) P.wHead[i] = 0;
                else
                {
                    P.wHead[i] = f + (1 - f) * longHead;
                    P.wPhys1[i] = f * tb + (1 - f) * lp1; P.wPhys2[i] = f * tb * tb + (1 - f) * lp2;
                    P.wFront[i] = f;
                    if (L.group == "hair_inner") { P.wHead[i] *= 0.5f; P.wPhys1[i] *= 0.35f; P.wPhys2[i] *= 0.35f; }
                }
                // the lower face follows the jaw when the mouth opens. The same field, a function of position only, for
                // every layer that holds a copy of the lower face
                if ((L.id == "face" || role == "expr" || hairFront || mouthCopy || role == "neck_shadow") && A.hasJaw && _anyJaw)
                {
                    float vy = Smooth(A.jawTop - 0.02f * ry, A.jawChin, y) * (hairFront ? 1 - Smooth(A.jawChin, A.jawChin + 0.15f * ry, y) : 1);
                    float vx = 1 - Smooth(0.22f * fw, 0.5f * fw, Math.Abs(x - cx));
                    P.wJaw[i] = vy * vx;
                }
                if (role == "neck_shadow" && A.hasJaw)
                {
                    float k = 1 - Smooth(A.jawChin, A.jawChin + NeckShadow * fh, y);
                    P.wHead[i] = k; P.wJaw[i] *= k;
                }
                if (body && A.hasJaw)
                {
                    // the neck right under the chin goes with the head and the jaw: the chin's shadow is drawn on the body
                    float wy = 1 - Smooth(A.jawChin, A.jawChin + ChinNeck * fh, y);
                    float wx = 1 - Smooth(ChinNeckX0 * fw, ChinNeckX1 * fw, Math.Abs(x - cx));
                    float w = wy * wx;
                    if (w > 0)
                    {
                        P.wHead[i] = w;
                        if (_anyJaw) P.wJaw[i] = Smooth(A.jawTop - 0.02f * ry, A.jawChin, y) * (1 - Smooth(0.22f * fw, 0.5f * fw, Math.Abs(x - cx))) * w;
                    }
                }
                if (P.wNeck != null)
                {
                    // the shoulders and collar by the neck tilt a little with the head's roll
                    P.wNeck[i] = Math.Max(0f, NeckFollow * (1 - Smooth(A.neckY, A.neckY + NeckFollowY * fh, y))
                        * (1 - Smooth(NeckFollowX0 * fw, NeckFollowX1 * fw, Math.Abs(x - cx))) - P.wHead[i]);
                }
                if (P.wTwist != null)
                {
                    // the neck's column below the chin goes a share of the way the chin goes sideways
                    float cy = A.hasJaw ? A.jawChin : A.neckY;
                    P.wTwist[i] = Math.Max(0f, R.neckTwist * (1 - Smooth(cy, cy + R.neckReach * fh, y))
                        * (1 - Smooth(ChinNeckX0 * fw, ChinNeckX1 * fw, Math.Abs(x - cx))) - P.wHead[i]);
                }
            }
            // chained layers: the chains replace the hair springs, except where reaching chains leave a layer partly to them
            if (L.bones.Length > 0)
            {
                P.skin = SkinOf(L.bones, P.rest);
                bool allReach = true;
                foreach (var id in L.bones)
                {
                    PuppetChainDef def = null;
                    foreach (var C in _chains) if (C.def.id == id) def = C.def;
                    if (def == null || float.IsNaN(def.reach)) allReach = false;
                }
                if (P.skin != null && !allReach) { Array.Clear(P.wPhys1, 0, n); Array.Clear(P.wPhys2, 0, n); }
            }
            if (L.rigid != null) { Array.Clear(P.wPhys1, 0, n); Array.Clear(P.wPhys2, 0, n); }
            if (mouthCopy) P.mouthPos = new float[2 * n];
            // part of the body, wholly below where the torso starts to move, and not hung on anything. (Its lattice
            // reaches up to a cell above its box: there the torso would move it by a hundredth of a pixel)
            bool still = body && P.skin == null && L.rigid == null && role == "" && L.y >= A.bottomY;
            for (int i = 0; i < n && still; i++)
            {
                if (P.rest[2 * i + 1] <= A.bottomY - cell || P.wHead[i] != 0 || P.wJaw[i] != 0) still = false;
                else if (A.hasChest)
                {
                    float ux = (P.rest[2 * i] - A.chestCx) / A.chestRx, uy = (P.rest[2 * i + 1] - A.chestCy) / A.chestRy;
                    if (MathF.Sqrt(ux * ux + uy * uy) < 1.15f) still = false;
                }
            }
            if (still) { P.still = true; Array.Copy(P.rest, P.pos, P.rest.Length); }
            return P;
        }

        /// <summary>Each vertex's place on the chains `ids`: its nearest point on each chain's line, its offset from there,
        /// and a weight by distance (a Gaussian of sigma, normalised over the chains).</summary>
        Skin SkinOf(string[] ids, float[] rest)
        {
            var cs = new List<int>();
            foreach (var id in ids) for (int c = 0; c < _chains.Length; c++) if (_chains[c].def.id == id) { cs.Add(c); break; }
            if (cs.Count == 0) return null;
            int n = rest.Length / 2, B = cs.Count;
            var S = new Skin { B = B, c = new int[n * B], seg = new int[n * B], t = new float[n * B], ux = new float[n * B], uy = new float[n * B], w = new float[n * B], sum = new float[n] };
            for (int i = 0; i < n; i++)
            {
                float x = rest[2 * i], y = rest[2 * i + 1], sum = 0;
                for (int b = 0; b < B; b++)
                {
                    var C = _chains[cs[b]]; var R = C.R; int m = R.Length / 2;
                    float best = float.PositiveInfinity, bt = 0; int bk = 0;
                    for (int k = 0; k < m - 1; k++)
                    {
                        float ax = R[2 * k], ay = R[2 * k + 1], dx = R[2 * k + 2] - ax, dy = R[2 * k + 3] - ay;
                        float L2 = dx * dx + dy * dy; if (L2 == 0) L2 = 1;
                        float t = ((x - ax) * dx + (y - ay) * dy) / L2;
                        // before the root and past the tip the line goes on straight
                        if (k > 0) t = Math.Max(0f, t);
                        if (k < m - 2) t = Math.Min(1f, t);
                        float d = Hypot(x - (ax + dx * t), y - (ay + dy * t));
                        if (d < best) { best = d; bk = k; bt = t; }
                    }
                    int j = i * B + b; float tt = Math.Min(1f, bt);
                    float px = R[2 * bk] + (R[2 * bk + 2] - R[2 * bk]) * tt, py = R[2 * bk + 1] + (R[2 * bk + 3] - R[2 * bk + 1]) * tt;
                    S.c[j] = cs[b]; S.seg[j] = bk; S.t[j] = bt; S.ux[j] = x - px; S.uy[j] = y - py;
                    float q = best / C.def.sigma;
                    S.w[j] = MathF.Exp(-q * q) + 1e-6f;
                    sum += S.w[j];
                }
                float near = float.PositiveInfinity, reach = float.PositiveInfinity;
                for (int b = 0; b < B; b++)
                {
                    int j = i * B + b; var def = _chains[S.c[j]].def;
                    S.w[j] /= sum;
                    if (!float.IsNaN(def.reach)) reach = Math.Min(reach, def.reach);
                    near = Math.Min(near, Hypot(S.ux[j], S.uy[j]));
                }
                if (!float.IsPositiveInfinity(reach)) { float k = 1 - Smooth(reach, 1.6f * reach, near); for (int b = 0; b < B; b++) S.w[i * B + b] *= k; }
                for (int b = 0; b < B; b++) S.sum[i] += S.w[i * B + b];
            }
            return S;
        }

        void SkinOffset(Skin S, int i, out float ox, out float oy)
        {
            ox = 0; oy = 0;
            if (S.sum[i] < 1e-4f) return;
            for (int b = 0; b < S.B; b++)
            {
                int j = i * S.B + b; var C = _chains[S.c[j]]; int k = S.seg[j];
                if (!C.moving) continue;
                // before the root the rope has not moved; past the tip it goes on as the last segment does
                float t = Math.Min(1f, Math.Max(0f, S.t[j])), e = S.t[j] < 0 ? 0 : 1;
                float dx = (C.disp[2 * k] + (C.disp[2 * k + 2] - C.disp[2 * k]) * t) * e;
                float dy = (C.disp[2 * k + 1] + (C.disp[2 * k + 3] - C.disp[2 * k + 1]) * t) * e;
                float be = (C.beta[k] + (C.beta[k + 1] - C.beta[k]) * t) * e;
                float c = MathF.Cos(be), s = MathF.Sin(be), ux = S.ux[j], uy = S.uy[j];
                float lx = dx + ux * c - uy * s - ux, ly = dy + ux * s + uy * c - uy;
                float cr = MathF.Cos(C.rho), sr = MathF.Sin(C.rho);     // the root's frame is turned with the head / torso
                ox += S.w[j] * (lx * cr - ly * sr); oy += S.w[j] * (lx * sr + ly * cr);
            }
        }

        // ---------------------------------------------------------------- the head and the torso

        void HeadTrig()
        {
            var R = model.rig; var T = model.tuning; var P = param;
            float py = P.angleY / 30f;
            float yaw = P.angleX / 30f * R.yaw * T.turn, pitch = py * (1 - R.pitchEase * py * py) * R.pitch * T.turn * T.nod;
            _roll = P.angleZ * MathF.PI / 180f * R.roll * T.turn;
            _cy = MathF.Cos(yaw); _sy = MathF.Sin(yaw); _cp = MathF.Cos(pitch); _sp = MathF.Sin(pitch); _cr = MathF.Cos(_roll); _sr = MathF.Sin(_roll);
        }

        /// <summary>Where the head puts the point (x, y) of a layer at `depth`: turned and nodded as a point on a rounded
        /// volume (along the head's own axes), drifted a little toward the turn, then rolled about the neck.</summary>
        void HeadXform(float x, float y, float depth, out float X, out float Y)
        {
            var A = model.anchors; var R = model.rig;
            HeadUv(x, y, out float u, out float v);
            var E = R.bulgeExtent;
            float ex = E != null ? E[0] : 1;
            if (E != null && E.Length > 3 && v > 0)
            {
                float t = Math.Min(1f, v / (E[2] * (E.Length > 4 ? E[4] : 1)));
                ex += (E[3] - E[0]) * t * t * (3 - 2 * t);
            }
            float bu = E != null ? u / ex : u, bv = E != null ? v / (v < 0 ? E[1] : E[2]) : v;
            float q = R.bulgeShape;
            float bulge = R.bulge * Math.Max(0f, q == 1 ? 1 - bu * bu - bv * bv : 1 - MathF.Pow(bu * bu + bv * bv, q));
            float z = bulge + depth;
            float u2 = u * _cy + z * _sy;
            float z2 = -u * _sy + z * _cy;
            float v2 = v * _cp - z2 * _sp;
            u2 += R.driftX * _sy; v2 -= R.driftY * _sp;     // the whole head drifts a little toward the turn
            float hx = u2 * A.headRx, hy = v2 * A.headRy;   // back from the head's own axes to the picture's
            X = A.headCx + hx * _tiltCos - hy * _tiltSin; Y = A.headCy + hx * _tiltSin + hy * _tiltCos;
            float dx = X - A.headCx, dy = Y - A.neckY;
            X = A.headCx + dx * _cr - dy * _sr; Y = A.neckY + dx * _sr + dy * _cr;
        }

        /// <summary>The torso's motion at (x, y): breathing, following the head's turn, the lean.</summary>
        void BodyOffset(float x, float y, out float dx, out float dy)
        {
            var A = model.anchors; var R = model.rig;
            float t = Smooth(A.bottomY, A.neckY, y);    // 0 at the bottom edge (a seated figure: the seat), 1 at the neck
            float fw = A.faceX1 - A.faceX0, ry = A.headRy;
            float br = param.breath * model.tuning.breath * R.breath * R.bodyBreath;
            dx = (x - A.headCx) * 0.01f * br * t;
            dy = -br * 0.024f * ry * MathF.Pow(t, 1.3f);
            float yaw = _bodyYaw / 30f;
            float bw = 1.4f * fw;
            float u = Math.Max(-1.6f, Math.Min(1.6f, (x - A.headCx) / bw));
            dx += yaw * bw * 0.16f * MathF.Pow(t, 1.2f) * (1 - 0.18f * u * u) + yaw * (x - A.headCx) * -0.04f * Math.Sign(u) * t;
            float a = _bodyRoll * MathF.PI / 180f * t;
            if (a != 0)
            {
                float px = model.bodyPivot != null ? model.bodyPivot[0] : A.headCx;
                float py = model.bodyPivot != null ? model.bodyPivot[1] : A.bottomY + 0.4f * (A.bottomY - A.neckY);
                float rx = x - px, ryy = y - py, c = MathF.Cos(a), s = MathF.Sin(a);
                dx += rx * (c - 1) - ryy * s; dy += rx * s + ryy * (c - 1);
            }
            if (A.hasChest)
            {
                float ux = (x - A.chestCx) / A.chestRx, uy = (y - A.chestCy) / A.chestRy;
                float w = 1 - Smooth(0.2f, 1.15f, MathF.Sqrt(ux * ux + uy * uy));
                if (w > 0) dy -= br * 0.012f * ry * w * 0.4f;   // the chest lifts more than the shoulders when breathing in
            }
        }

        // ---------------------------------------------------------------- stepping

        /// <summary>One frame: time, the mouth, the springs, the mesh, and what to draw.</summary>
        public void Update(float dt)
        {
            if (dt < 0) dt = 0;
            if (dt > 0.1f) dt = 0.1f;
            time += dt;
            var P = param;
            P.angleX = Math.Max(-30f, Math.Min(30f, P.angleX)); P.angleY = Math.Max(-30f, Math.Min(30f, P.angleY)); P.angleZ = Math.Max(-30f, Math.Min(30f, P.angleZ));
            _smileShown += (P.smile - _smileShown) * Math.Min(1f, dt * 7);
            StepMouth(dt);
            HeadTrig();
            if (dt > 0)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(dt / (1f / 120f)));   // sub-stepped for stability
                for (int i = 0; i < steps; i++) { StepBody(dt / steps); StepPhysics(dt / steps); StepChains(dt / steps); }
            }
            Deform();
            BuildDraws();
        }

        /// <summary>Everything that carries over from frame to frame starts again, at rest.</summary>
        public void ResetPhysics()
        {
            foreach (var s in _front) s.Reset(); foreach (var s in _back) s.Reset(); foreach (var s in _side) s.Reset();
            _headStarted = false; _dvStarted = false; _dvX = _dvY = _dvVx = _dvVy = _dvAx = _dvAy = 0;
            _bodyYaw = param.bodyX + param.angleX * model.rig.bodyYaw * model.tuning.turn;
            _bodyRoll = param.bodyZ + param.angleZ * model.rig.bodyRoll * model.tuning.turn;
            foreach (var C in _chains)
            {
                Array.Copy(C.R, C.p, C.R.Length); Array.Copy(C.R, C.q, C.R.Length);
                Array.Clear(C.disp, 0, C.disp.Length); Array.Clear(C.beta, 0, C.beta.Length);
                C.started = false; C.moving = false; C.accX = C.accY = C.vx = C.vy = 0;
            }
        }

        public void ResetMouth()
        {
            _mouthFrame = 0; _mouthAmp = 0; _mouthShape = 0; _mouthHeld = 1; _talkLeft = 0;
            for (int i = 0; i < _mouthW.Length; i++) _mouthW[i] = i == 0 ? 1 : 0;
        }

        /// <summary>The torso lags the head.</summary>
        void StepBody(float dt)
        {
            var P = param; var R = model.rig; float tt = model.tuning.turn;
            float ty = P.bodyX + P.angleX * R.bodyYaw * tt, tr = P.bodyZ + P.angleZ * R.bodyRoll * tt;
            float k = Math.Min(1f, dt * 5);
            _bodyYaw += (ty - _bodyYaw) * k;
            _bodyRoll += (tr - _bodyRoll) * k;
        }

        /// <summary>The mouth frame shown follows mouthOpen and the vowel with a dead band and a shortest hold: a voice's
        /// loudness crosses the thresholds many times a second, which as mouth shapes is flicker, not speech.</summary>
        void StepMouth(float dt)
        {
            float m = Math.Min(1f, Math.Max(0f, param.mouthOpen)), v = Math.Min(1f, Math.Max(-1f, param.vowel));
            const float t1 = MouthAt1, t2 = MouthAt2, h = MouthHyst;
            _mouthHeld += dt;
            int a = _mouthAmp;
            if (a == 0 && m > t1 + h) a = m > t2 + h ? 2 : 1;
            else if (a == 1 && (m < t1 - h || m > t2 + h)) a = m < t1 - h ? 0 : 2;
            else if (a == 2 && m < t2 - h) a = m < t1 - h ? 0 : 1;
            int s = _mouthShape;
            if (s == 0 || (s == 1 ? v > -VowelOut : v < VowelOut)) s = v < -VowelIn ? 1 : v > VowelIn ? 2 : 0;
            int shaped = s == 1 ? _visRound : s == 2 ? _visWide : -1;
            int want = a > 0 && shaped >= 0 ? shaped : Math.Min(a, _mouthW.Length - 1);
            if (want == 0 && _talkLeft > 0 && _visShut >= 0) want = _visShut;
            if (want == _mouthFrame) { _mouthAmp = a; _mouthShape = s; }
            else if (_mouthHeld >= MouthHold) { _mouthFrame = want; _mouthAmp = a; _mouthShape = s; _mouthHeld = 0; }
            int f = _mouthFrame;
            _talkLeft = f > 0 && f != _visShut ? TalkLatch : Math.Max(0f, _talkLeft - dt);
            float k = Math.Min(1f, dt * 28);
            for (int i = 0; i < _mouthW.Length; i++) _mouthW[i] += ((i == f ? 1 : 0) - _mouthW[i]) * k;
        }

        /// <summary>How far frame i is along with mouthOpen m: within a frame the opening keeps moving a little.</summary>
        float FrameEase(int i, float m)
        {
            string kind = _frameKind[i];
            return kind == "half" ? Smooth(MouthAt1, MouthAt2, m) : kind == "open" ? Smooth(MouthAt2, 1, m) : Smooth(MouthAt1, 1, m);
        }

        /// <summary>The hair's springs: per group a chain of two, thrown by the hair root's speed, the head's own
        /// acceleration (rig.hair_drive) and a slow wind.</summary>
        void StepPhysics(float dt)
        {
            var P = param; var A = model.anchors; var R = model.rig; var T = model.tuning;
            HeadXform(A.headCx, A.hairRootY, 0, out float hx, out float hy);
            float inv = 1 / Math.Max(dt, 1e-4f);
            if (!_headStarted) { _lastHeadX = hx; _lastHeadY = hy; _headStarted = true; }
            float vx = (hx - _lastHeadX) * inv, vy = (hy - _lastHeadY) * inv;
            _lastHeadX = hx; _lastHeadY = hy;
            var D = R.hairDrive;
            if (D != null)
            {
                // the pivot's acceleration, low-passed (~40 ms)
                HeadXform(A.headCx, A.headCy, 0, out float fx, out float fy);
                if (!_dvStarted) { _dvX = fx; _dvY = fy; _dvVx = _dvVy = _dvAx = _dvAy = 0; _dvStarted = true; }
                float ks = Math.Min(1f, dt * 25);
                float svx = _dvVx + ((fx - _dvX) * inv - _dvVx) * ks, svy = _dvVy + ((fy - _dvY) * inv - _dvVy) * ks;
                _dvAx += ((svx - _dvVx) * inv - _dvAx) * ks; _dvAy += ((svy - _dvVy) * inv - _dvAy) * ks;
                _dvX = fx; _dvY = fy; _dvVx = svx; _dvVy = svy;
            }
            float lim = A.headRy * R.hairLimit;
            int groups = R.hairSide != null ? 3 : 2;
            for (int g = 0; g < groups; g++)
            {
                float[] cfg = g == 0 ? R.hairFront : g == 1 ? R.hairBack : R.hairSide;
                var pair = g == 0 ? _front : g == 1 ? _back : _side;
                Spring a = pair[0], b = pair[1];
                float k1 = cfg[0], c1 = cfg[1], gain = cfg[2], k2 = cfg[3], c2 = cfg[4];
                float own = g == 1 ? T.hairBack : T.hairFront;
                float s = physicsScale * (float.IsNaN(own) ? T.hair : own);
                float wind = g == 2 ? MathF.Sin(time * 1.5f + 0.7f) * 1.2f * s * R.wind
                    : MathF.Sin(time * (g == 0 ? 1.9f : 1.15f)) * (g == 0 ? 0.6f : 1.8f) * s * R.wind;
                float gravity = -P.angleZ * 0.2f * s;
                float ax = -k1 * (a.x - wind - gravity) - c1 * a.vx - vx * gain * k1 * 0.06f * s;
                float ay = -k1 * a.y - c1 * a.vy - vy * gain * k1 * 0.04f * s;
                if (D != null)
                {
                    float m = (g == 0 ? D[0] : g == 1 ? D[1] : D.Length > 2 ? D[2] : D[0]) * s;
                    ax -= _dvAx * m; ay -= _dvAy * m;
                }
                a.vx += ax * dt; a.vy += ay * dt; a.x += a.vx * dt; a.y += a.vy * dt;
                float bx = -k2 * (b.x - a.x * 1.6f) - c2 * b.vx;
                float by = -k2 * (b.y - a.y * 1.4f) - c2 * b.vy;
                b.vx += bx * dt; b.vy += by * dt; b.x += b.vx * dt; b.y += b.vy * dt;
                a.x = Math.Max(-lim, Math.Min(lim, a.x)); a.y = Math.Max(-lim * 0.6f, Math.Min(lim * 0.6f, a.y));
                b.x = Math.Max(-lim, Math.Min(lim, b.x)); b.y = Math.Max(-lim * 0.6f, Math.Min(lim * 0.6f, b.y));
            }
        }

        /// <summary>Where a chain's root is and how far its frame is turned (rad).</summary>
        void ChainRoot(PuppetChainDef def, float nbx, float nby, out float x, out float y, out float rho)
        {
            float rx = def.points[0], ry = def.points[1]; var A = model.anchors;
            if (def.onBody)
            {
                BodyOffset(rx, ry, out float bx, out float by);
                x = rx + bx; y = ry + by;
                rho = _bodyRoll * MathF.PI / 180f * Smooth(A.bottomY, A.neckY, ry);
            }
            else
            {
                HeadXform(rx, ry, def.depth, out float hx, out float hy);
                x = hx + nbx; y = hy + nby;
                rho = _roll;
            }
        }

        /// <summary>Every chain one step: a verlet rope pulled toward its drawn shape (counter-turned by the tilt as far as
        /// it hangs, bent by expressions, swayed by wind), thrown by its root's acceleration, its segments kept at length.</summary>
        void StepChains(float dt)
        {
            if (_chains.Length == 0 || dt <= 0) return;
            var A = model.anchors; var T = model.tuning;
            BodyOffset(A.headCx, A.neckY, out float nbx, out float nby);
            float g = physicsScale * (float.IsNaN(T.hairBack) ? T.hair : T.hairBack);
            foreach (var C in _chains)
            {
                var d = C.def; var R = C.R; var p = C.p; var q = C.q; int m = R.Length / 2;
                ChainRoot(d, nbx, nby, out float rx, out float ry, out float rho);
                if (!C.started) { C.wx = rx; C.wy = ry; C.vx = C.vy = C.accX = C.accY = 0; C.started = true; }
                float vx = (rx - C.wx) / dt, vy = (ry - C.wy) / dt;
                float ax = (vx - C.vx) / dt, ay = (vy - C.vy) / dt;
                C.wx = rx; C.wy = ry; C.vx = vx; C.vy = vy; C.rho = rho;
                float ks = Math.Min(1f, dt * 30);   // one frame's jitter in the root is not a throw
                C.accX += (ax - C.accX) * ks; C.accY += (ay - C.accY) * ks;
                float cr = MathF.Cos(-rho), sr = MathF.Sin(-rho);
                float lax = C.accX * cr - C.accY * sr, lay = C.accX * sr + C.accY * cr;
                float bias = 0;
                if (d.bias != null) foreach (var kv in d.bias) bias += kv.Value * ExprValue(kv.Key);
                float turn = -rho * d.hang + bias * MathF.PI / 180f, ct = MathF.Cos(turn), st = MathF.Sin(turn);
                float wt = 2 * MathF.PI * d.freq * time + C.phase;
                float wind = d.wind * g * model.rig.wind * (MathF.Sin(wt) + 0.4f * MathF.Sin(1.7f * wt + 2 * C.phase));
                float damp = Math.Max(0f, 1 - d.damp * dt);
                for (int j = 1; j < m; j++)
                {
                    float s = C.s[j], k = d.k0 + (d.k1 - d.k0) * s;
                    float tx = R[2 * j] - R[0], ty = R[2 * j + 1] - R[1];
                    float gx = R[0] + tx * ct - ty * st + wind * s, gy = R[1] + tx * st + ty * ct;
                    float fx = k * (gx - p[2 * j]) - d.drive * g * lax, fy = k * (gy - p[2 * j + 1]) - d.drive * g * lay;
                    float nx = p[2 * j] + (p[2 * j] - q[2 * j]) * damp + fx * dt * dt;
                    float ny = p[2 * j + 1] + (p[2 * j + 1] - q[2 * j + 1]) * damp + fy * dt * dt;
                    q[2 * j] = p[2 * j]; q[2 * j + 1] = p[2 * j + 1]; p[2 * j] = nx; p[2 * j + 1] = ny;
                }
                for (int j = 1; j < m; j++)
                {
                    float dx = p[2 * j] - p[2 * j - 2], dy = p[2 * j + 1] - p[2 * j - 1], l = Hypot(dx, dy);
                    if (l == 0) l = 1;
                    p[2 * j] = p[2 * j - 2] + dx / l * C.len[j]; p[2 * j + 1] = p[2 * j - 1] + dy / l * C.len[j];
                    float ex = p[2 * j] - R[2 * j], ey = p[2 * j + 1] - R[2 * j + 1], e = Hypot(ex, ey);
                    if (e > d.limit) { p[2 * j] = R[2 * j] + ex * d.limit / e; p[2 * j + 1] = R[2 * j + 1] + ey * d.limit / e; }
                }
                float mv = 0;
                for (int j = 0; j < m; j++)
                {
                    C.disp[2 * j] = p[2 * j] - R[2 * j]; C.disp[2 * j + 1] = p[2 * j + 1] - R[2 * j + 1];
                    mv = Math.Max(mv, Math.Max(Math.Abs(C.disp[2 * j]), Math.Abs(C.disp[2 * j + 1])));
                }
                C.moving = mv > 1e-3f;
                // how far each joint's surroundings are turned: the mean of the segments meeting there
                float prev = 0;
                C.beta[0] = 0;
                for (int k = 0; k < m - 1; k++)
                {
                    float sx = R[2 * k + 2] - R[2 * k], sy = R[2 * k + 3] - R[2 * k + 1], cx = p[2 * k + 2] - p[2 * k], cy = p[2 * k + 3] - p[2 * k + 1];
                    float al = MathF.Atan2(sx * cy - sy * cx, sx * cx + sy * cy);
                    if (k > 0) C.beta[k] = 0.5f * (prev + al);
                    prev = al;
                }
                C.beta[m - 1] = prev;
            }
        }

        // ---------------------------------------------------------------- expressions

        float ExprValue(string id) => id == "smile" ? _smileShown : expr.TryGetValue(id, out float v) ? v : 0f;

        float ExprAlpha(PuppetLayerDef d) => Smooth(0.3f, 0.7f, string.IsNullOrEmpty(d.expression) ? _smileShown : ExprValue(d.expression));

        float Sum(Dictionary<string, float> m)
        {
            float a = 0;
            foreach (var kv in m) a += kv.Value * ExprValue(kv.Key);
            return Math.Min(1f, a);
        }

        float MoodAlpha(PuppetLayerDef d) => (d.show != null ? Sum(d.show) : 1f) * (d.hide != null ? 1 - Sum(d.hide) : 1f);

        float EyeOpen(PuppetLayer L) => L.side == 1 ? param.eyeLOpen : L.side == 2 ? param.eyeROpen : 1f;

        /// <summary>Whether the layer is drawn this frame (the rest is not moved either).</summary>
        bool Shown(PuppetLayer L)
        {
            var d = L.def; string role = d.role;
            if ((d.show != null || d.hide != null) && MoodAlpha(d) <= 0.01f) return false;
            if (role == "expr" || role == "expr_brows") return ExprAlpha(d) > 0.01f;
            if (L.isLid) return 1 - Smooth(0.05f, 0.35f, EyeOpen(L)) > 0.01f;
            if (role == "blush") return d.blush != null && Sum(d.blush) > 0.01f;
            if (role == "hole" || role == "edge" || role == "edge_wide") return false;
            return true;
        }

        // ---------------------------------------------------------------- the mesh

        /// <summary>Where the head, the body and the hair springs take a point with these weights.</summary>
        void Place(float x, float y, float wh, float dep, float w1, float w2, float f, float nbx, float nby, out float X, out float Y)
        {
            // a point that moves with the head alone takes nothing of the torso
            float bx = 0, by = 0;
            if (wh < 1) BodyOffset(x, y, out bx, out by);
            X = x + bx; Y = y + by;
            if (wh > 0)
            {
                HeadXform(x, y, dep, out float hx, out float hy);
                X = X * (1 - wh) + (hx + nbx) * wh;
                Y = Y * (1 - wh) + (hy + nby) * wh;
            }
            if (w1 > 0 || w2 > 0)
            {
                Spring b0 = _back[0], b1 = _back[1], f0 = _front[0], f1 = _front[1];
                float c0x, c0y, c1x, c1y;
                if (model.rig.hairSide != null)
                {
                    // the side chain takes the head-height share (f) by how far out the point lies
                    float sd = Smooth(SideAt0, SideAt1, Math.Abs(x - model.anchors.headCx) / model.anchors.headRx), ff = f * (1 - sd), fs = f * sd;
                    c0x = f0.x * ff + _side[0].x * fs + b0.x * (1 - f); c0y = f0.y * ff + _side[0].y * fs + b0.y * (1 - f);
                    c1x = f1.x * ff + _side[1].x * fs + b1.x * (1 - f); c1y = f1.y * ff + _side[1].y * fs + b1.y * (1 - f);
                }
                else
                {
                    // the bangs' chain blends into the long hair's at the seam
                    c0x = f0.x * f + b0.x * (1 - f); c0y = f0.y * f + b0.y * (1 - f);
                    c1x = f1.x * f + b1.x * (1 - f); c1y = f1.y * f + b1.y * (1 - f);
                }
                X += c0x * w1 + c1x * w2; Y += c0y * w1 + c1y * w2;
            }
        }

        /// <summary>Every shown layer's vertices at the current params, springs and torso.</summary>
        void Deform()
        {
            var P = param; var A = model.anchors; var R = model.rig;
            HeadTrig();
            BodyOffset(A.headCx, A.neckY, out float nbx, out float nby);
            // the jaw follows the frame shown, opening a little further with mouthOpen
            float mo = Math.Min(1f, Math.Max(0f, P.mouthOpen));
            float jawDrop = 0;
            for (int i = 0; i < _mouthW.Length; i++)
                if (_mouthW[i] > 0 && _frameJaw[i] > 0) jawDrop += _mouthW[i] * _frameJaw[i] * (_frameKind[i] == "half" ? 0.6f + 0.4f * FrameEase(i, mo) : 0.75f + 0.25f * FrameEase(i, mo));
            float g = model.tuning.gaze, gz = g >= 0 && g < 1 ? g : 1;
            // rig.neck: how far the chin goes sideways from where the torso alone puts it
            float chinDx = 0;
            if (R.hasNeck)
            {
                float cy = A.hasJaw ? A.jawChin : A.neckY;
                HeadXform(A.headCx, cy, 0, out float hx, out float _);
                BodyOffset(A.headCx, cy, out float bx, out float _);
                chinDx = hx + nbx - (A.headCx + bx);
            }
            foreach (var L in layers)
            {
                if (L.still || !Shown(L)) continue;
                var d = L.def; var r = L.rest; var o = L.pos;
                int n = r.Length / 2;
                float open = EyeOpen(L);
                float squash = 0.12f + 0.88f * open;
                float ix = 0, iy = 0;
                bool gazes = d.range != null && (L.isIris || (Chained && L.isShine));
                if (gazes) { ix = P.eyeBallX * d.range[0] * gz; iy = -P.eyeBallY * d.range[1] * gz; }
                // an iris with gaze travel drops a little as the eye closes and squashes only below the line its patch
                // squashes toward; above it the closing lid covers it
                bool lidIris = L.isIris && d.pivot != null && d.range != null && (d.range[0] > 0 || d.range[1] > 0);
                if (lidIris) iy += IrisDrop * (1 - squash) * (d.pivot[1] - (d.y + d.h / 2f));
                // highlights breathe a little and now and then sparkle, each eye on its own phase (models with chains)
                float tw = Chained && L.isShine ? Twinkle(time + (L.side == 2 ? 1.3f : 0)) : 1;
                float tcx = d.x + d.w / 2f, tcy = d.y + d.h / 2f;
                float rgx = 0, rgy = 0;
                if (d.rigid != null) { HeadXform(d.rigid[0], d.rigid[1], d.depth, out float hx, out float hy); rgx = hx + nbx; rgy = hy + nby; }
                bool pivoted = L.isEye && d.pivot != null;
                for (int i = 0; i < n; i++)
                {
                    float x = r[2 * i], y = r[2 * i + 1];
                    if (tw != 1) { x = tcx + (x - tcx) * tw; y = tcy + (y - tcy) * tw; }
                    if (pivoted)
                    {
                        float py = d.pivot[1];
                        x += ix; y += iy;
                        if (!lidIris || y > py) y = py + (y - py) * squash;
                    }
                    if (jawDrop > 0 && L.wJaw[i] > 0) y += jawDrop * L.wJaw[i];
                    float X, Y;
                    if (d.rigid != null)
                    {
                        float vx = x - d.rigid[0], vy = y - d.rigid[1];
                        X = rgx + vx * _cr - vy * _sr; Y = rgy + vx * _sr + vy * _cr;
                    }
                    else Place(x, y, L.wHead[i], L.dep[i], L.wPhys1[i], L.wPhys2[i], L.wFront[i], nbx, nby, out X, out Y);
                    if (L.skin != null) { SkinOffset(L.skin, i, out float sx, out float sy); X += sx; Y += sy; }
                    if (L.wTwist != null && L.wTwist[i] > 0) X += chinDx * L.wTwist[i];
                    if (L.wNeck != null && L.wNeck[i] > 0)
                    {
                        // the head's roll about the neck, as HeadXform turns it
                        float a = _roll * L.wNeck[i], c = MathF.Cos(a), s = MathF.Sin(a);
                        float nx = A.headCx + nbx, ny = A.neckY + nby, dx = X - nx, dy = Y - ny;
                        X = nx + dx * c - dy * s; Y = ny + dx * s + dy * c;
                    }
                    o[2 * i] = X; o[2 * i + 1] = Y;
                }
            }
        }

        /// <summary>An eye highlight's scale at time t: a slow 3 % breath and, about every 5 s, a short sparkle.</summary>
        static float Twinkle(float t)
        {
            float k = MathF.Floor(t / 5.3f), u = t - k * 5.3f - (k * 7919 % 13) / 13f * 3.5f;
            float spark = u > 0 && u < 0.35f ? MathF.Sin(u / 0.35f * MathF.PI) : 0;
            return 1 + 0.03f * MathF.Sin(t * 2.1f) + 0.12f * spark;
        }

        // ---------------------------------------------------------------- what to draw

        void Add(int layer, PuppetTexture tex, int frame, float alpha, PuppetStencil stencil, int stencilRef, float[] positions)
        {
            draws.Add(new PuppetDraw { layer = layer, texture = tex, frame = frame, alpha = alpha, stencil = stencil, stencilRef = stencilRef, positions = positions });
        }

        void DrawMouth(int li)
        {
            var L = layers[li]; var d = L.def;
            // one frame, opaque; within a frame the opening keeps moving a little: every open frame stretches down from the
            // mouth's top edge as mouthOpen grows
            float m = Math.Min(1f, Math.Max(0f, param.mouthOpen)); int f = _mouthFrame;
            float py = d.pivot != null ? d.pivot[1] : d.y;
            float k = _frameKind[f] == "half" ? 0.88f + 0.12f * FrameEase(f, m) : 0.9f + 0.1f * FrameEase(f, m);
            float[] verts = L.pos;
            if (f > 0 && f != _visShut)
            {
                // the squeeze is the drawn mouth's: toward the chin it fades as the jaw takes over
                verts = L.mouthPos;
                Array.Copy(L.pos, verts, verts.Length);
                for (int i = 1; i < verts.Length; i += 2) verts[i] += (L.rest[i] - py) * (k - 1) * (1 - L.wJaw[i >> 1]);
            }
            Add(li, PuppetTexture.Frame, f, 1, PuppetStencil.None, 0, verts);
        }

        void BuildDraws()
        {
            draws.Clear();
            var P = param;
            // while talking the mouth is drawn above the expression patches: each holds its own still mouth
            int lastExpr = -1;
            if (_talkLeft > 0) for (int i = layers.Length - 1; i >= 0; i--) if (layers[i].def.role == "expr") { lastExpr = i; break; }
            int deferred = -1, stencil = 0;
            for (int li = 0; li < layers.Length; li++)
            {
                var L = layers[li]; var d = L.def; string role = d.role;
                if (deferred >= 0 && li > lastExpr) { DrawMouth(deferred); deferred = -1; }
                if (role == "hole" || role == "edge" || role == "edge_wide") continue;
                if (role == "mouth" && d.frames.Length >= 3)
                {
                    if (li < lastExpr) deferred = li; else DrawMouth(li);
                    continue;
                }
                if (role == "expr" || role == "expr_brows")
                {
                    float a = ExprAlpha(d);
                    if (a > 0.01f) Add(li, PuppetTexture.Main, 0, a, PuppetStencil.None, 0, L.pos);
                    continue;
                }
                if (L.isLid)
                {
                    // closed lashes only appear in the last part of the blink
                    float a = 1 - Smooth(0.05f, 0.35f, EyeOpen(L));
                    if (a > 0.01f) Add(li, PuppetTexture.Main, 0, a, PuppetStencil.None, 0, L.pos);
                    continue;
                }
                if (L.isEye && !L.isIris && !L.isShine)
                {
                    Add(li, PuppetTexture.Main, 0, Smooth(0.02f, 0.2f, EyeOpen(L)), PuppetStencil.None, 0, L.pos);     // squash, don't fade
                    L.stencilRef = 0;
                    if (d.mask != null)
                    {
                        // the (squashed) whites into the stencil, so the iris stays inside them
                        stencil = stencil % 250 + 1;
                        L.stencilRef = stencil;
                        Add(li, PuppetTexture.Mask, 0, 1, PuppetStencil.Write, stencil, L.pos);
                    }
                    continue;
                }
                if (role == "blush")
                {
                    float a = d.blush != null ? Sum(d.blush) : 0;
                    if (a > 0.01f) Add(li, PuppetTexture.Main, 0, a, PuppetStencil.None, 0, L.pos);
                    continue;
                }
                if (L.isIris || L.isShine)
                {
                    float a = Smooth(0.02f, 0.2f, EyeOpen(L));
                    if (d.clip != null && _byId.TryGetValue(d.clip, out int pi) && layers[pi].stencilRef != 0)
                        Add(li, PuppetTexture.Main, 0, a, PuppetStencil.Test, layers[pi].stencilRef, L.pos);
                    else if (d.clip == null) Add(li, PuppetTexture.Main, 0, a, PuppetStencil.None, 0, L.pos);
                    continue;
                }
                float ma = d.show != null || d.hide != null ? MoodAlpha(d) : 1;
                if (ma > 0.01f) Add(li, PuppetTexture.Main, 0, ma, PuppetStencil.None, 0, L.pos);
            }
            if (deferred >= 0) DrawMouth(deferred);
        }
    }
}
