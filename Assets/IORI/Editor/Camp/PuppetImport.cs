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
