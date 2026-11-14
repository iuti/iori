using System;
using System.Collections.Generic;
using System.IO;
using Iori.Puppet;
using UnityEditor;
using UnityEngine;

namespace Iori.Camp.EditorTools
{
    /// <summary>
    /// Takes a puppet model in from a folder (model.json in LivLiv's format, its layer pictures as PNG, and for a
    /// layer picture "x.png" its normals as "x_n.png" where there are any): sets how the pictures are imported and
    /// writes puppet.asset next to model.json, which PuppetRenderer plays.
    /// The folder is Assets/IORI/Art/Character/Puppet/(name). "iori" is used if it is there, else the first one
    /// found. A folder called _local is not kept in git (for trying someone else's model).
    /// </summary>
    public static class PuppetImport
    {
        public const string Dir = "Assets/IORI/Art/Character/Puppet";

        /// <summary>The folder of the model to use, or null when there is none.</summary>
        public static string FindFolder()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return null;
            if (File.Exists(Dir + "/iori/model.json")) return Dir + "/iori";
            foreach (var sub in AssetDatabase.GetSubFolders(Dir))
                if (File.Exists(sub + "/model.json")) return sub;
            return null;
        }

        [MenuItem("IORI/Puppet/モデルを取り込む")]
        public static void ImportMenu()
        {
            string folder = FindFolder();
            if (folder == null) { EditorUtility.DisplayDialog("人形のモデル", Dir + "/(名前)/model.json が見つかりません。", "OK"); return; }
            var asset = Import(folder);
            Selection.activeObject = asset;
            Debug.Log("[IORI] puppet imported: " + folder + " (" + asset.layers.Length + " layers)");
        }

        public static void ImportBatch()
        {
            try
            {
                string folder = FindFolder();
                if (folder == null) throw new Exception("no model.json under " + Dir);
                var asset = Import(folder);
                Debug.Log("[IORI] puppet imported: " + folder + " (" + asset.layers.Length + " layers)");
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>Writes what the puppet draws, as drawn at rest: puppet_color.png (her colors as the layers' files
        /// have them, not multiplied by alpha: it should look like the drawing the model was made from),
        /// puppet_normals.png and puppet_draws.txt (what was drawn in which order).</summary>
        public static void Dump(PuppetRenderer puppet, string dir)
        {
            puppet.RenderAt(0f);
            if (puppet.Rig == null || !puppet.ColorTexture) { Debug.LogWarning("[IORI] puppet: nothing to write out"); return; }
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "puppet_color.png"), Straight(puppet.ColorTexture));
            File.WriteAllBytes(Path.Combine(dir, "puppet_normals.png"), Straight(puppet.NormalTexture));
            var text = new System.Text.StringBuilder();
            var rig = puppet.Rig;
            text.AppendLine($"model {rig.model.name} canvas {rig.model.canvasW}x{rig.model.canvasH} lattice {rig.model.Lattice} layers {rig.layers.Length} chains {rig.model.rig.chains.Length} head_tilt {rig.model.headTilt}");
            text.AppendLine($"texture {puppet.ColorTexture.width}x{puppet.ColorTexture.height}");
            foreach (var d in rig.draws)
            {
                var L = rig.layers[d.layer];
                text.AppendLine($"{d.layer,3} {L.def.id,-24} group {L.def.group,-10} role {L.def.role,-10} {d.texture}{(d.texture == PuppetTexture.Frame ? " " + d.frame : "")} alpha {d.alpha:0.###} stencil {d.stencil} {d.stencilRef} verts {L.rest.Length / 2}{(L.still ? " still" : "")}");
            }
            File.WriteAllText(Path.Combine(dir, "puppet_draws.txt"), text.ToString());
            Debug.Log("[IORI] puppet written out to " + dir);
        }

        /// <summary>The poses DumpPoses writes: each asks one thing of the model, far enough to show where its
        /// layers part (a head turned further than her idle motion ever turns it).</summary>
        static readonly (string name, float seconds, Action<PuppetParams> pose)[] Poses =
        {
            ("rest", 0.1f, P => { }),
            ("eyes_closed", 0.5f, P => { P.eyeLOpen = 0; P.eyeROpen = 0; }),
            ("eyes_half", 0.5f, P => { P.eyeLOpen = 0.5f; P.eyeROpen = 0.5f; }),
            ("eyes_left", 0.5f, P => P.eyeBallX = -1),
            ("eyes_right", 0.5f, P => P.eyeBallX = 1),
            ("eyes_up", 0.5f, P => P.eyeBallY = 1),
            ("eyes_down", 0.5f, P => P.eyeBallY = -1),
            ("mouth_small", 0.5f, P => P.mouthOpen = 0.2f),
            ("mouth_half", 0.5f, P => P.mouthOpen = 0.45f),
            ("mouth_open", 0.5f, P => P.mouthOpen = 1f),
            ("mouth_round", 0.5f, P => { P.mouthOpen = 0.5f; P.vowel = -1f; }),
            ("head_left", 2f, P => P.angleX = -15),
            ("head_right", 2f, P => P.angleX = 15),
            ("head_up", 2f, P => P.angleY = 12),
            ("head_down", 2f, P => P.angleY = -12),
            ("tilt_left", 2f, P => P.angleZ = -10),
            ("tilt_right", 2f, P => P.angleZ = 10),
            ("head_right_moving", 0.2f, P => P.angleX = 15),
            ("tilt_left_moving", 0.2f, P => P.angleZ = -10),
            ("lean_left", 2f, P => P.bodyZ = -5),
            ("lean_right", 2f, P => P.bodyZ = 5),
            ("turn_body", 2f, P => P.bodyX = 8),
            ("breath_in", 0.5f, P => P.breath = 1),
            ("all_at_once", 2f, P => { P.angleX = 12; P.angleY = -8; P.angleZ = 8; P.bodyZ = 4; P.breath = 1; P.eyeBallX = 0.6f; P.mouthOpen = 0.45f; }),
        };

        /// <summary>Writes the puppet in each of Poses, drawn at the model's own size (not the smaller size the board
        /// keeps): pose_NN_name.png under dir/poses, her colors only, not lit. To look closely for gaps, doubled
        /// lines and things showing that should be hidden.</summary>
        public static void DumpPoses(PuppetRenderer puppet, string dir)
        {
            if (!puppet || puppet.Rig == null) { Debug.LogWarning("[IORI] puppet: no poses to write out"); return; }
            int oldSize = puppet.maxSize;
            string outDir = Path.Combine(dir, "poses");
            Directory.CreateDirectory(outDir);
            try
            {
                puppet.maxSize = Mathf.Max(puppet.Rig.model.canvasW, puppet.Rig.model.canvasH);
                for (int i = 0; i < Poses.Length; i++)
                {
                    puppet.RenderPose(Poses[i].pose, Poses[i].seconds);
                    if (!puppet.ColorTexture) break;
                    File.WriteAllBytes(Path.Combine(outDir, $"pose_{i:00}_{Poses[i].name}.png"), Straight(puppet.ColorTexture));
                }
                Debug.Log("[IORI] puppet poses written out to " + outDir);
            }
            finally
            {
                puppet.maxSize = oldSize;
                puppet.RenderAt(0f);
            }
        }

        /// <summary>Writes her idle motion as pictures of the puppet alone, at the model's own size, not lit:
        /// motion_NNN.png under dir/motion, `fps` pictures a second for `seconds` (from one second in, as the shots
        /// on the board are). For a film that shows the motion itself, apart from the camp, its light and the
        /// size of a phone's screen.</summary>
        public static void DumpMotion(PuppetRenderer puppet, string dir, float fps, float seconds)
        {
            if (!puppet || puppet.Rig == null) { Debug.LogWarning("[IORI] puppet: no motion to write out"); return; }
            int oldSize = puppet.maxSize;
            string outDir = Path.Combine(dir, "motion");
            Directory.CreateDirectory(outDir);
            try
            {
                puppet.maxSize = Mathf.Max(puppet.Rig.model.canvasW, puppet.Rig.model.canvasH);
                int count = Mathf.Clamp(Mathf.RoundToInt(fps * seconds), 1, 999);
                for (int i = 0; i < count; i++)
                {
                    puppet.RenderAt(1f + i / fps);
                    if (!puppet.ColorTexture) break;
                    File.WriteAllBytes(Path.Combine(outDir, $"motion_{i:000}.png"), Straight(puppet.ColorTexture));
                }
                Debug.Log("[IORI] puppet motion written out to " + outDir);
            }
            finally
            {
                puppet.maxSize = oldSize;
                puppet.RenderAt(0f);
            }
        }

        /// <summary>Each motion alone, made bigger step by step, to find how far it can go before the layers part
        /// (dir/sweep/axis_value.png, the model's own size). Head turn, nod and tilt, the body's turn and lean are
        /// held 2 s (hair settled); the hair's swing is a quick turn of the head from -15 to +15, taken 0.25 s after
        /// the turn, with the swing made 1 to 4 times as strong.</summary>
        public static void DumpSweep(PuppetRenderer puppet, string dir)
        {
            if (!puppet || puppet.Rig == null) { Debug.LogWarning("[IORI] puppet: no sweep to write out"); return; }
            int oldSize = puppet.maxSize; float oldSwing = puppet.swing;
            string outDir = Path.Combine(dir, "sweep");
            Directory.CreateDirectory(outDir);
            void Save(string name) => File.WriteAllBytes(Path.Combine(outDir, name + ".png"), Straight(puppet.ColorTexture));
            try
            {
                puppet.maxSize = Mathf.Max(puppet.Rig.model.canvasW, puppet.Rig.model.canvasH);
                float[] head = { 5, 10, 15, 20, 25, 30, 40 }, body = { 2, 4, 6, 8, 10, 14 };
                var axes = new (string name, float[] values, Action<PuppetParams, float> set)[]
                {
                    ("turn", head, (P, v) => P.angleX = v), ("nod", head, (P, v) => P.angleY = v), ("tilt", head, (P, v) => P.angleZ = v),
                    ("bodyturn", body, (P, v) => P.bodyX = v), ("lean", body, (P, v) => P.bodyZ = v),
                };
                foreach (var (name, values, set) in axes)
                    foreach (float v in values)
                        foreach (float s in new[] { -1f, 1f })
                        {
                            float val = v * s;
                            puppet.RenderPose(P => set(P, val), 2f);
                            Save($"{name}_{(val < 0 ? "m" : "p")}{v:00}");
                        }
                foreach (float sw in new[] { 1f, 1.5f, 2f, 3f, 4f })
                {
                    puppet.swing = sw;
                    int step = 0;
                    puppet.RenderPose(P => { P.angleX = step++ < 45 ? -15f : 15f; }, 1.5f + 0.25f);
                    Save($"swing_x{sw:0.0}");
                }
                Debug.Log("[IORI] puppet sweep written out to " + outDir);
            }
            finally
            {
                puppet.maxSize = oldSize; puppet.swing = oldSwing;
                puppet.RenderAt(0f);
            }
        }

        /// <summary>A texture the puppet drew into, as a PNG with its colors divided by alpha again.</summary>
        static byte[] Straight(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            try
            {
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = prev;
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    if (p.a == 0) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    px[i] = new Color32((byte)Mathf.Min(255, p.r * 255 / p.a), (byte)Mathf.Min(255, p.g * 255 / p.a), (byte)Mathf.Min(255, p.b * 255 / p.a), p.a);
                }
                t.SetPixels32(px);
                t.Apply();
                return t.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(t);
            }
        }

        /// <summary>The model's picture as it lies in the project: a model made for the web names WebP files,
        /// which Unity does not read, so the PNG of the same name is taken.</summary>
        static string PicturePath(string folder, string name)
        {
            string p = folder + "/" + name.Replace('\\', '/');
            if (p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 5) + ".png";
            return p;
        }

        static string NormalPath(string picture) => picture.Substring(0, picture.Length - 4) + "_n.png";

        static void SetImport(string path, bool normals)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter t)) throw new Exception("見つからない: " + path);
            // a layer's colors are read as the numbers in the file (sRGB off): the layers are laid over each other as
            // the tool that drew them does, and the character board turns the result into light (IoriCharacter.shader).
            // Not compressed: flat cel colors and thin lines show every block of a compressed texture
            bool alpha = !normals;
            bool dirty = t.textureType != TextureImporterType.Default || t.sRGBTexture || t.alphaIsTransparency != alpha || !t.mipmapEnabled
                         || t.wrapMode != TextureWrapMode.Clamp || t.filterMode != FilterMode.Trilinear || t.textureCompression != TextureImporterCompression.Uncompressed
                         || t.npotScale != TextureImporterNPOTScale.None || t.maxTextureSize != 4096
                         || t.alphaSource != (alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None);
            if (!dirty) return;
            t.textureType = TextureImporterType.Default;
            t.sRGBTexture = false;
            t.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            t.alphaIsTransparency = alpha;
            t.mipmapEnabled = true; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Trilinear;
            t.textureCompression = TextureImporterCompression.Uncompressed; t.npotScale = TextureImporterNPOTScale.None; t.maxTextureSize = 4096;
            t.SaveAndReimport();
        }

        static Texture2D Picture(string path, bool normals)
        {
            if (!File.Exists(path)) return null;
            SetImport(path, normals);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The file's alpha, top row first, read from the file itself (the imported texture is not readable).</summary>
        static byte[] Alpha(string path, out int w, out int h)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(path))) throw new Exception("読めない: " + path);
                w = t.width; h = t.height;
                var px = t.GetPixels32();
                var a = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    int from = (h - 1 - y) * w, to = y * w;     // Unity counts rows from the bottom
                    for (int x = 0; x < w; x++) a[to + x] = px[from + x].a;
                }
                return a;
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        public static PuppetAsset Import(string folder)
        {
            string jsonPath = folder + "/model.json";
            var model = PuppetModel.Parse(File.ReadAllText(jsonPath));
            AssetDatabase.ImportAsset(jsonPath);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (!json) throw new Exception("見つからない: " + jsonPath);

            var layers = new PuppetAsset.Layer[model.layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                var d = model.layers[i];
                var L = new PuppetAsset.Layer { id = d.id };
                string main = PicturePath(folder, d.texture);
                L.main = Picture(main, false);
                if (!L.main) throw new Exception("レイヤー " + d.id + " の絵がない: " + main + "(WebP は読めないので PNG で置く)");
                L.normal = Picture(NormalPath(main), true);
                var files = new List<string> { main };
                L.frames = new Texture2D[d.frames.Length]; L.frameNormals = new Texture2D[d.frames.Length];
                for (int f = 0; f < d.frames.Length; f++)
                {
                    string p = PicturePath(folder, d.frames[f]);
                    L.frames[f] = Picture(p, false);
                    if (!L.frames[f]) throw new Exception("レイヤー " + d.id + " のコマがない: " + p);
                    L.frameNormals[f] = Picture(NormalPath(p), true);
                    files.Add(p);
                }
                if (d.mask != null)
                {
                    string p = PicturePath(folder, d.mask);
                    L.mask = Picture(p, false);
                    if (!L.mask) throw new Exception("レイヤー " + d.id + " のマスクがない: " + p);
                }
                // which cells of the mesh hold anything of the layer's pictures (any of them: the frames share the mesh)
                byte[] occ = null;
                foreach (var p in files)
                {
                    var a = Alpha(p, out int w, out int h);
                    var o = PuppetRig.Occupancy(model, d, new[] { a }, w, h);
                    if (occ == null) occ = o;
                    else for (int k = 0; k < occ.Length; k++) occ[k] |= o[k];
                }
                L.occupancy = occ;
                layers[i] = L;
            }

            string assetPath = folder + "/puppet.asset";
            var asset = AssetDatabase.LoadAssetAtPath<PuppetAsset>(assetPath);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<PuppetAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.model = json;
            asset.layers = layers;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
