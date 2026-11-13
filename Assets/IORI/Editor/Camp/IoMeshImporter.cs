using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Iori.Camp.EditorTools
{
    /// <summary>
    /// Reads .iomesh: the camp's models as written by tools/camp/blender (campkit.Scene.export).
    /// A gzip stream of: "IOM1", mesh count, then for each mesh its name, vertex count, index count,
    /// positions (3 floats), normals (3 floats), colors (RGBA bytes), uv0 (2 floats), uv1 (4 floats)
    /// and 32-bit indices. Every mesh becomes a sub-asset with that name.
    /// Vertex color = surface color (sRGB) with the baked shade in alpha; uv1 = glow channel,
    /// glow strength, gloss, free (for plants: uv0 and uv1 hold each leaf's center, see IoriFoliage.shader).
    /// </summary>
    [ScriptedImporter(3, "iomesh")]
    public sealed class IoMeshImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] raw;
            using (var file = File.OpenRead(ctx.assetPath))
            using (var gz = new GZipStream(file, CompressionMode.Decompress))
            using (var mem = new MemoryStream())
            {
                gz.CopyTo(mem);
                raw = mem.ToArray();
            }
            using var br = new BinaryReader(new MemoryStream(raw));
            if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "IOM1") throw new Exception("not an iomesh file: " + ctx.assetPath);
            int count = (int)br.ReadUInt32();
            var summary = new StringBuilder();
            long totalTris = 0;
            for (int m = 0; m < count; m++)
            {
                string name = Encoding.UTF8.GetString(br.ReadBytes(br.ReadUInt16()));
                int vc = (int)br.ReadUInt32();
                int ic = (int)br.ReadUInt32();
                var pos = ReadFloats(br, vc * 3);
                var nor = ReadFloats(br, vc * 3);
                var col = br.ReadBytes(vc * 4);
                var uv = ReadFloats(br, vc * 2);
                var dat = ReadFloats(br, vc * 4);
                var idx = new int[ic];
                Buffer.BlockCopy(br.ReadBytes(ic * 4), 0, idx, 0, ic * 4);

                var vertices = new Vector3[vc];
                var normals = new Vector3[vc];
                var colors = new Color32[vc];
                var uv0 = new Vector2[vc];
                var uv1 = new Vector4[vc];
                for (int i = 0; i < vc; i++)
                {
                    vertices[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                    normals[i] = new Vector3(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]);
                    colors[i] = new Color32(col[i * 4], col[i * 4 + 1], col[i * 4 + 2], col[i * 4 + 3]);
                    uv0[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                    uv1[i] = new Vector4(dat[i * 4], dat[i * 4 + 1], dat[i * 4 + 2], dat[i * 4 + 3]);
                }
                var mesh = new Mesh { name = name, indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, new List<Vector4>(uv1));
                mesh.SetIndices(idx, MeshTopology.Triangles, 0, true);
                // plants sway a little: leave room so they are not culled at the edge of the view
                var b = mesh.bounds;
                b.Expand(0.3f);
                mesh.bounds = b;
                mesh.UploadMeshData(false);
                ctx.AddObjectToAsset(name, mesh);
                summary.AppendLine($"{name}: {vc} vertices, {ic / 3} triangles");
                totalTris += ic / 3;
            }
            summary.Insert(0, $"{count} meshes, {totalTris} triangles\n");
            var info = new TextAsset(summary.ToString()) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("info", info);
            ctx.SetMainObject(info);
        }

        static float[] ReadFloats(BinaryReader br, int n)
        {
            var f = new float[n];
            Buffer.BlockCopy(br.ReadBytes(n * 4), 0, f, 0, n * 4);
            return f;
        }
    }
}
