// Runs the puppet without Unity and writes out, for each picture asked for, where every layer's mesh is.
// render.js then draws those the way PuppetRenderer does. Usage: PuppetPreview <model.json> <jobs.json> <out prefix>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Iori.Puppet;

static class Program
{
    static float F(JsonElement e, string key, float otherwise)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : otherwise;

    static void SetPose(PuppetParams P, JsonElement pose)
    {
        P.angleX = F(pose, "angleX", P.angleX); P.angleY = F(pose, "angleY", P.angleY); P.angleZ = F(pose, "angleZ", P.angleZ);
        P.eyeLOpen = F(pose, "eyeLOpen", P.eyeLOpen); P.eyeROpen = F(pose, "eyeROpen", P.eyeROpen);
        P.eyeBallX = F(pose, "eyeBallX", P.eyeBallX); P.eyeBallY = F(pose, "eyeBallY", P.eyeBallY);
        P.mouthOpen = F(pose, "mouthOpen", P.mouthOpen); P.vowel = F(pose, "vowel", P.vowel); P.smile = F(pose, "smile", P.smile);
        P.bodyX = F(pose, "bodyX", P.bodyX); P.bodyZ = F(pose, "bodyZ", P.bodyZ); P.breath = F(pose, "breath", P.breath);
    }

    static void Rest(PuppetRig rig)
    {
        var P = rig.param;
        P.angleX = P.angleY = P.angleZ = P.eyeBallX = P.eyeBallY = P.bodyX = P.bodyZ = P.breath = 0f;
        P.eyeLOpen = P.eyeROpen = 1f;
        P.mouthOpen = P.vowel = P.smile = 0f;
        rig.time = 0f;
        rig.ResetPhysics(); rig.ResetMouth();
    }

    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("PuppetPreview <model.json> <jobs.json> <out prefix>"); return 2; }
        var model = PuppetModel.Parse(File.ReadAllText(args[0]));
        var rig = new PuppetRig(model);
        using var jobs = JsonDocument.Parse(File.ReadAllText(args[1]));
        using var bin = new BinaryWriter(File.Create(args[2] + ".bin"));
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        long offset = 0;
        sb.Append("{\"canvas\":[").Append(model.canvasW).Append(',').Append(model.canvasH).Append("],\"layers\":[");
        for (int i = 0; i < rig.layers.Length; i++)
        {
            var L = rig.layers[i]; var d = L.def;
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(JsonSerializer.Serialize(d.id)).Append(",\"texture\":").Append(JsonSerializer.Serialize(d.texture))
              .Append(",\"mask\":").Append(JsonSerializer.Serialize(d.mask)).Append(",\"frames\":").Append(JsonSerializer.Serialize(d.frames))
              .Append(",\"still\":").Append(L.still ? "true" : "false")
              .Append(",\"uv\":[");
            for (int k = 0; k < L.uv.Length; k++) { if (k > 0) sb.Append(','); sb.Append(L.uv[k].ToString("R", inv)); }
            sb.Append("],\"tris\":[");
            for (int k = 0; k < L.tris.Length; k++) { if (k > 0) sb.Append(','); sb.Append(L.tris[k]); }
            sb.Append("]}");
        }
        sb.Append("],\"frames\":[");
        int count = 0;
        bool dry = false;   // "dry": the pose's numbers only, no meshes (for looking at how far a long motion goes)
        HashSet<string> only = null;    // "only": these layers' meshes only (for measuring, not for drawing)
        void Frame(string name)
        {
            if (count++ > 0) sb.Append(',');
            var P = rig.param;
            sb.Append("{\"name\":").Append(JsonSerializer.Serialize(name))
              .Append(",\"param\":[").Append(string.Join(",", new[] { P.angleX, P.angleY, P.angleZ, P.bodyX, P.bodyZ, P.breath, P.eyeLOpen, P.eyeBallX, P.eyeBallY }.Select(v => v.ToString("0.###", inv))))
              .Append("],\"draws\":[");
            bool first = true;
            foreach (var d in rig.draws)
            {
                if (d.alpha <= 0f || dry || (only != null && !only.Contains(rig.layers[d.layer].def.id))) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append("[").Append(d.layer).Append(',').Append((int)d.texture).Append(',').Append(d.frame).Append(',')
                  .Append(d.alpha.ToString("0.####", inv)).Append(',').Append((int)d.stencil).Append(',').Append(d.stencilRef).Append(',')
                  .Append(offset).Append(',').Append(d.positions.Length).Append(']');
                foreach (float v in d.positions) bin.Write(v);
                offset += d.positions.Length;
            }
            sb.Append("]}");
        }
        const float dt = 1f / 30f;
        foreach (var job in jobs.RootElement.EnumerateArray())
        {
            string name = job.GetProperty("name").GetString();
            float swing = F(job, "swing", 1f);
            dry = job.TryGetProperty("dry", out var dr) && dr.ValueKind == JsonValueKind.True;
            only = null;
            if (job.TryGetProperty("only", out var on) && on.ValueKind == JsonValueKind.Array)
            {
                only = new HashSet<string>();
                foreach (var e in on.EnumerateArray()) only.Add(e.GetString());
            }
            Rest(rig);
            rig.physicsScale = swing;
            if (job.TryGetProperty("idle", out var idleDef))
            {
                // as PuppetRenderer.RenderAt plays it: from rest, in steps of a thirtieth
                var idle = new PuppetIdle((int)F(idleDef, "seed", 1));
                idle.amount = F(idleDef, "amount", 1f); idle.turn = F(idleDef, "turn", 1f); idle.tilt = F(idleDef, "tilt", 1f); idle.lean = F(idleDef, "lean", 1f);
                float from = F(job, "from", 1f), seconds = F(job, "seconds", 12f);
                int every = Math.Max(1, (int)F(job, "every", 1f));
                int start = (int)Math.Round(from / dt), n = (int)Math.Round(seconds / dt);
                for (int i = 0; i < start + n; i++)
                {
                    if (i > 0) { idle.Apply(rig.param, dt); rig.Update(dt); }
                    else { idle.Apply(rig.param, 0f); rig.Update(0f); }
                    if (i >= start && (i - start) % every == 0) Frame(name + "_" + (i - start).ToString("0000"));
                }
            }
            else
            {
                // as PuppetRenderer.RenderPose holds it; "frames": every step's picture, not only the last
                job.TryGetProperty("pose", out var pose);
                float seconds = F(job, "seconds", 2f);
                bool all = job.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.True;
                int steps = Math.Max(1, (int)Math.Ceiling(seconds / dt));
                int each = Math.Max(1, (int)F(job, "every", 1f));
                for (int i = 0; i < steps; i++)
                {
                    SetPose(rig.param, pose); rig.Update(dt);
                    if (all && i % each == 0) Frame(name + "_" + i.ToString("0000"));
                }
                if (!all) Frame(name);
            }
        }
        sb.Append("]}");
        File.WriteAllText(args[2] + ".json", sb.ToString());
        Console.WriteLine(count + " pictures, " + offset + " numbers");
        return 0;
    }
}

static class Ext
{
    public static IEnumerable<TR> Select<T, TR>(this T[] a, Func<T, TR> f) { foreach (var x in a) yield return f(x); }
}
