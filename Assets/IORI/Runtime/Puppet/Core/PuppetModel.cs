// The LivLiv model package's model.json (format 4, and the format before it), read into plain objects.
// The format is LivLiv's (https://github.com/livliv-dev/player, FORMAT.md, Apache-2.0); this reader is our own.
// What it reads beyond the format, from the extension field "x-iori" (other players ignore it):
//   head_tilt   degrees the drawn head is tilted (clockwise on the picture): the head turns and nods along its own
//               axes instead of the picture's
//   body_pivot  [x, y]: the point the upper body leans about (a seated figure: the hips), instead of a point below
//               the middle of the picture's bottom edge
//   still       ["layer id", ...]: layers that never move, whatever their place (a seated figure: the legs and the
//               chair as one layer, whose knees rise above the seat line in front of the body)
// With anchors.bottom_y on the seat line, body layers wholly below it stay still without being listed.
using System;
using System.Collections.Generic;
using Iori.Audio;

namespace Iori.Puppet
{
    public sealed class PuppetLayerDef
    {
        public string id, texture, mask, group, role, clip, expression;
        public string[] frames = new string[0], visemes = new string[0], bones = new string[0];
        public int x, y, w, h;
        public float depth;
        /// <summary>null when the layer has none</summary>
        public float[] pivot, range, rigid;
        /// <summary>NaN when missing</summary>
        public float fall = float.NaN, fallEnd = float.NaN;
        public Dictionary<string, float> blush, show, hide;
    }

    public sealed class PuppetChainDef
    {
        public string id;
        public bool onBody;
        public float depth;
        public float[] points;          // x0, y0, x1, y1 ... root first
        public float k0, k1, damp, drive, hang, wind, freq = 0.5f;
        public float limit = float.PositiveInfinity, sigma = 40f, reach = float.NaN;
        public Dictionary<string, float> bias;
    }

    public sealed class PuppetRigDef
    {
        public float yaw = 0.27f, pitch = 0.22f, roll = 0.5f, bulge = 0.6f;
        public float[] hairFront = { 140, 11, 0.022f, 70, 7 }, hairBack = { 48, 5.5f, 0.06f, 24, 3.5f }, hairSide;
        public float hairLimit = 0.25f;
        public float[] hairDrive;
        public float wind = 1, breath = 1, sway = 1, brow = 0.06f;
        public float bodyYaw = 0.35f, bodyRoll = 0.3f, bodyBreath = 1;
        public float pitchEase = 0.25f, bulgeShape = 1;
        public float driftX = 0.12f, driftY = 0.08f;
        public float[] bulgeExtent;
        public bool hasNeck;
        public float neckTwist, neckReach = 0.45f;
        /// <summary>NaN: the player's default</summary>
        public float jaw = float.NaN;
        public int lattice;
        public PuppetChainDef[] chains = new PuppetChainDef[0];
    }

    public sealed class PuppetTuning
    {
        public float turn = 1, nod = 1, hair = 1, blink = 1, expr = 1, mouth = 1, breath = 1, gaze = 1;
        /// <summary>NaN: `hair`</summary>
        public float hairFront = float.NaN, hairBack = float.NaN;
    }

    public sealed class PuppetAnchors
    {
        public float headCx, headCy, headRx, headRy;
        public float faceX0, faceY0, faceX1, faceY1;
        public float neckY, topY, bottomY, hairRootY;
        public float[][] eyes = new float[0][];
        public bool hasJaw;
        public float jawTop, jawChin, jawHalf, jawOpen, jawRound, jawWide;
        public bool hasChest;
        public float chestCx, chestCy, chestRx, chestRy;
    }

    public sealed class PuppetModel
    {
        public string name = "";
        public int canvasW, canvasH;
        public PuppetAnchors anchors = new PuppetAnchors();
        public PuppetRigDef rig = new PuppetRigDef();
        public PuppetTuning tuning = new PuppetTuning();
        public PuppetLayerDef[] layers = new PuppetLayerDef[0];
        public string[] expressions = new string[0];
        /// <summary>x-iori.head_tilt, degrees</summary>
        public float headTilt;
        /// <summary>x-iori.body_pivot, or null</summary>
        public float[] bodyPivot;
        /// <summary>x-iori.still: ids of the layers that never move (never null)</summary>
        public string[] still = new string[0];

        /// <summary>The mesh lattice's cell, model px.</summary>
        public int Lattice => rig.lattice > 0 ? rig.lattice : Math.Max(6, (int)Math.Round(Math.Max(canvasW, canvasH) / 128.0));

        static readonly string[] Groups = { "head", "body", "hair_back", "hair_side", "hair_front", "hair_inner" };

        public static PuppetModel Parse(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("model.json is not an object");
            string format = Str(root, "format");
            if (format != null && format != "livliv-model" && format != "livemaker-puppet") throw new FormatException("not a LivLiv model (format \"" + format + "\")");
            if (format == "livliv-model" && Num(root, "version", 4) > 4) throw new FormatException("this model's format is newer than this reader (4)");
            foreach (var need in List(root, "requires"))
                throw new FormatException("this model needs " + need + ", which this reader does not have");

            var m = new PuppetModel { name = Str(root, "name") ?? "" };
            var canvas = Floats(root, "canvas");
            if (canvas == null || canvas.Length != 2 || !(canvas[0] >= 16 && canvas[1] >= 16 && canvas[0] <= 8192 && canvas[1] <= 8192)) throw new FormatException("model.json has no usable canvas");
            m.canvasW = (int)canvas[0]; m.canvasH = (int)canvas[1];

            var a = Obj(root, "anchors");
            if (a == null) throw new FormatException("model.json has no anchors");
            var A = m.anchors;
            var head = Obj(a, "head");
            var face = Floats(a, "face");
            if (head == null || face == null || face.Length != 4) throw new FormatException("anchors.head / anchors.face are missing");
            A.headCx = Need(head, "cx"); A.headCy = Need(head, "cy"); A.headRx = Need(head, "rx"); A.headRy = Need(head, "ry");
            if (!(A.headRx > 1e-3f && A.headRy > 1e-3f)) throw new FormatException("anchors.head has no size");
            A.faceX0 = face[0]; A.faceY0 = face[1]; A.faceX1 = face[2]; A.faceY1 = face[3];
            A.neckY = Need(a, "neck_y"); A.topY = Num(a, "top_y", 0); A.bottomY = Num(a, "bottom_y", m.canvasH); A.hairRootY = Num(a, "hair_root_y", A.headCy - A.headRy * 0.6f);
            var eyes = new List<float[]>();
            foreach (var e in List(a, "eyes")) { var b = ToFloats(e); if (b != null && b.Length == 4) eyes.Add(b); }
            A.eyes = eyes.ToArray();
            var jaw = Obj(a, "jaw");
            if (jaw != null)
            {
                A.hasJaw = true;
                A.jawTop = Num(jaw, "top", A.faceY1); A.jawChin = Num(jaw, "chin", A.neckY);
                A.jawHalf = Num(jaw, "half", 0); A.jawOpen = Num(jaw, "open", 0); A.jawRound = Num(jaw, "round", 0); A.jawWide = Num(jaw, "wide", 0);
            }
            var chest = Obj(a, "chest");
            if (chest != null)
            {
                A.hasChest = true;
                A.chestCx = Num(chest, "cx", 0); A.chestCy = Num(chest, "cy", 0); A.chestRx = Math.Max(1, Num(chest, "rx", 1)); A.chestRy = Math.Max(1, Num(chest, "ry", 1));
            }

            var r = Obj(root, "rig");
            var R = m.rig;
            if (r != null)
            {
                R.yaw = Num(r, "yaw", R.yaw); R.pitch = Num(r, "pitch", R.pitch); R.roll = Num(r, "roll", R.roll); R.bulge = Num(r, "bulge", R.bulge);
                var hair = Obj(r, "hair");
                if (hair != null)
                {
                    R.hairFront = Five(hair, "front") ?? R.hairFront; R.hairBack = Five(hair, "back") ?? R.hairBack; R.hairSide = Five(hair, "side");
                }
                R.hairLimit = Num(r, "hair_limit", R.hairLimit);
                var drive = Floats(r, "hair_drive");
                if (drive != null && drive.Length >= 2) R.hairDrive = drive;
                R.wind = Num(r, "wind", R.wind); R.breath = Num(r, "breath", R.breath); R.sway = Num(r, "sway", R.sway); R.brow = Num(r, "brow", R.brow);
                var body = Obj(r, "body");
                if (body != null) { R.bodyYaw = Num(body, "yaw", R.bodyYaw); R.bodyRoll = Num(body, "roll", R.bodyRoll); R.bodyBreath = Num(body, "breath", R.bodyBreath); }
                R.pitchEase = Num(r, "pitch_ease", R.pitchEase); R.bulgeShape = Num(r, "bulge_shape", R.bulgeShape);
                var drift = Floats(r, "drift");
                if (drift != null && drift.Length == 2) { R.driftX = drift[0]; R.driftY = drift[1]; }
                var extent = Floats(r, "bulge_extent");
                if (extent != null && extent.Length >= 3) R.bulgeExtent = extent;
                var neck = Obj(r, "neck");
                if (neck != null) { R.hasNeck = true; R.neckTwist = Num(neck, "twist", 0); R.neckReach = Num(neck, "reach", R.neckReach); }
                R.jaw = Num(r, "jaw", float.NaN);
                int lattice = (int)Num(r, "lattice", 0);
                if (lattice != 0 && (lattice < 6 || lattice > 256)) throw new FormatException("rig.lattice is not a whole number from 6 to 256");
                R.lattice = lattice;
                var chains = new List<PuppetChainDef>();
                foreach (var c in List(r, "chains"))
                {
                    var d = c as Dictionary<string, object>;
                    if (d == null || Str(d, "id") == null) throw new FormatException("a chain has no id");
                    var def = new PuppetChainDef { id = Str(d, "id"), onBody = Str(d, "attach") == "body", depth = Num(d, "depth", 0) };
                    var pts = new List<float>();
                    foreach (var p in List(d, "points")) { var q = ToFloats(p); if (q == null || q.Length != 2) throw new FormatException("chain " + def.id + " has a bad point"); pts.Add(q[0]); pts.Add(q[1]); }
                    if (pts.Count < 4) throw new FormatException("chain " + def.id + " has fewer than two points");
                    def.points = pts.ToArray();
                    var k = Floats(d, "k");
                    if (k == null || k.Length != 2) throw new FormatException("chain " + def.id + " has no k");
                    def.k0 = k[0]; def.k1 = k[1];
                    def.damp = Need(d, "damp"); def.drive = Need(d, "drive"); def.hang = Need(d, "hang"); def.wind = Need(d, "wind");
                    def.freq = Num(d, "freq", def.freq); def.limit = Num(d, "limit", def.limit); def.sigma = Num(d, "sigma", def.sigma); def.reach = Num(d, "reach", float.NaN);
                    def.bias = Weights(d, "bias");
                    chains.Add(def);
                }
                R.chains = chains.ToArray();
            }

            var t = Obj(root, "tuning");
            if (t != null)
            {
                var T = m.tuning;
                T.turn = Num(t, "turn", 1); T.nod = Num(t, "nod", 1); T.hair = Num(t, "hair", 1); T.blink = Num(t, "blink", 1);
                T.expr = Num(t, "expr", 1); T.mouth = Num(t, "mouth", 1); T.breath = Num(t, "breath", 1); T.gaze = Num(t, "gaze", 1);
                T.hairFront = Num(t, "hair_front", Num(t, "hairFront", float.NaN)); T.hairBack = Num(t, "hair_back", Num(t, "hairBack", float.NaN));
            }

            var exprs = new List<string>();
            foreach (var e in List(root, "expressions")) { var d = e as Dictionary<string, object>; if (d != null && Str(d, "id") != null) exprs.Add(Str(d, "id")); }
            m.expressions = exprs.ToArray();

            var layers = new List<PuppetLayerDef>();
            var ids = new HashSet<string>();
            foreach (var l in List(root, "layers"))
            {
                var d = l as Dictionary<string, object>;
                if (d == null || Str(d, "id") == null) throw new FormatException("a layer has no id");
                var L = new PuppetLayerDef { id = Str(d, "id"), texture = Str(d, "texture"), mask = Str(d, "mask"), group = Str(d, "group") ?? "body", role = Str(d, "role") ?? "", clip = Str(d, "clip") };
                if (!ids.Add(L.id)) throw new FormatException("two layers are called " + L.id);
                if (L.texture == null) throw new FormatException("layer " + L.id + " has no texture");
                if (Array.IndexOf(Groups, L.group) < 0) throw new FormatException("layer " + L.id + " has a group this reader does not know: " + L.group);
                L.expression = Str(d, "expression") ?? Str(d, "param");
                L.frames = Strings(d, "frames"); L.visemes = Strings(d, "visemes"); L.bones = Strings(d, "bones");
                L.x = (int)Need(d, "x"); L.y = (int)Need(d, "y"); L.w = (int)Need(d, "w"); L.h = (int)Need(d, "h");
                if (L.w <= 0 || L.h <= 0) throw new FormatException("layer " + L.id + " has no size");
                L.depth = Num(d, "depth", 0);
                L.pivot = Pair(d, "pivot"); L.range = Pair(d, "range"); L.rigid = Pair(d, "rigid");
                L.fall = Num(d, "fall", float.NaN); L.fallEnd = Num(d, "fall_end", float.NaN);
                L.blush = Weights(d, "blush"); L.show = Weights(d, "show"); L.hide = Weights(d, "hide");
                layers.Add(L);
            }
            if (layers.Count == 0) throw new FormatException("model.json has no layers");
            m.layers = layers.ToArray();

            var x = Obj(root, "x-iori");
            if (x != null)
            {
                m.headTilt = Num(x, "head_tilt", 0);
                m.bodyPivot = Pair(x, "body_pivot");
                if (x.TryGetValue("still", out var st) && st is List<object> stillIds)
                {
                    var names = new List<string>();
                    foreach (var o in stillIds) if (o is string name) names.Add(name);
                    m.still = names.ToArray();
                    foreach (var id in m.still)
                        if (Array.FindIndex(m.layers, l => l.id == id) < 0) throw new FormatException("x-iori.still names a layer that is not there: " + id);
                }
            }
            return m;
        }

        // ---- small typed readers over Json.Parse's objects
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;
        static float Num(Dictionary<string, object> d, string key, float fallback)
        {
            if (d.TryGetValue(key, out var v) && v is double n && !double.IsNaN(n) && !double.IsInfinity(n)) return (float)n;
            return fallback;
        }
        static float Need(Dictionary<string, object> d, string key)
        {
            float v = Num(d, key, float.NaN);
            if (float.IsNaN(v)) throw new FormatException(key + " is missing or not a number");
            return v;
        }
        static List<object> List(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();
        static float[] ToFloats(object o)
        {
            var l = o as List<object>;
            if (l == null) return null;
            var f = new float[l.Count];
            for (int i = 0; i < f.Length; i++) { if (!(l[i] is double n)) return null; f[i] = (float)n; }
            return f;
        }
        static float[] Floats(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? ToFloats(v) : null;
        static float[] Pair(Dictionary<string, object> d, string key) { var f = Floats(d, key); return f != null && f.Length == 2 ? f : null; }
        static float[] Five(Dictionary<string, object> d, string key) { var f = Floats(d, key); return f != null && f.Length == 5 ? f : null; }
        static string[] Strings(Dictionary<string, object> d, string key)
        {
            var o = new List<string>();
            foreach (var s in List(d, key)) if (s is string t) o.Add(t);
            return o.ToArray();
        }
        static Dictionary<string, float> Weights(Dictionary<string, object> d, string key)
        {
            var o = Obj(d, key);
            if (o == null) return null;
            var w = new Dictionary<string, float>();
            foreach (var kv in o) if (kv.Value is double n) w[kv.Key] = (float)n;
            return w;
        }
    }
}
