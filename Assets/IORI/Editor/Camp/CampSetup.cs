using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Iori.Camp.EditorTools
{
    /// <summary>
    /// Menu "IORI/Camp": puts the camp scene together from the models that tools/camp/blender wrote
    /// (camp.iomesh and camp_layout.json), makes the materials, lights, water, fire and camera, and
    /// takes screenshots of the looks. Both also run in batch mode:
    ///   Unity.exe -batchmode -projectPath . -executeMethod Iori.Camp.EditorTools.CampSetup.BuildSceneBatch -quit
    ///   Unity.exe -batchmode -projectPath . -executeMethod Iori.Camp.EditorTools.CampSetup.CaptureBatch -shotDir C:\path -quit
    /// -resetLook puts CampLook.asset and the materials back to the values in the code.
    /// </summary>
    public static class CampSetup
    {
        const string ScenePath = "Assets/IORI/Scenes/Camp.unity";
        const string ArtDir = "Assets/IORI/Art/Camp";
        const string ShaderDir = ArtDir + "/Shaders";
        const string MaterialDir = ArtDir + "/Materials";
        const string LookPath = ArtDir + "/CampLook.asset";
        const string VolumePath = ArtDir + "/CampVolume.asset";
        const string MeshPath = ArtDir + "/Models/camp.iomesh";
        const string LayoutPath = ArtDir + "/Models/camp_layout.json";
        const string WaterMeshPath = ArtDir + "/Models/camp_water.asset";
        const string StrokePath = ArtDir + "/Textures/iori_strokes.png";
        const string GroundMapPath = ArtDir + "/Textures/iori_ground.png";
        const string RidgePath = ArtDir + "/Textures/iori_ridges.png";
        const string NoisePath = ArtDir + "/Textures/iori_noise3d.asset";
        const string IoriTexPath = "Assets/IORI/Art/Character/iori_placeholder.png";
        // Iori sitting (drawn with her own chair) and the normals made from that drawing by
        // tools/character/make_sprite.py. When both are there she is used instead of the standing placeholder.
        const string IoriSitPath = "Assets/IORI/Art/Character/iori_sit.png";
        const string IoriSitNormalPath = "Assets/IORI/Art/Character/iori_sit_n.png";
        /// <summary>Height of the sitting picture's board in metres (the figure, goggles to soles, fills 0.985 of it).</summary>
        const float IoriSitHeight = 1.28f;
        /// <summary>The model that the sitting picture's own drawn chair stands in for.</summary>
        const string ChairMesh = "Chair";

        static bool HasSittingIori => File.Exists(IoriSitPath) && File.Exists(IoriSitNormalPath);
        const string UrpAssetPath = "Assets/IORI/Settings/IORI_URP.asset";
        const string UrpRendererPath = "Assets/IORI/Settings/IORI_URP_Renderer.asset";

        static readonly string[] ShaderFiles = { "IoriLit", "IoriGround", "IoriFoliage", "IoriSky", "IoriWater", "IoriWaterClassic", "IoriFire", "IoriSparks", "IoriRain", "IoriCharacter", "IoriContact" };

        // ------------------------------------------------------------------ layout file

        [Serializable] public sealed class LayoutObject { public string name, mesh, material; public float[] position; public float yaw; public float scale = 1f; public bool shadows; public string layer, anchor; public int vertices, triangles; }
        [Serializable] public sealed class LayoutCamera { public float[] position, target; public float fov; }
        [Serializable] public sealed class LayoutAnchors { public float[] fire, iori, table, radio, player, tent, lantern; }
        [Serializable] public sealed class LayoutMap { public float x0, z0, size; }
        [Serializable] public sealed class LayoutArea { public float halfWidth, z0, z1; }
        [Serializable] public sealed class Layout
        {
            public LayoutObject[] objects;
            public LayoutCamera camera;
            public LayoutAnchors anchors;
            public LayoutMap groundMap;
            public float waterY, puddleY;
            public LayoutArea puddleArea;
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        // ------------------------------------------------------------------ menu and batch entry points

        [MenuItem("IORI/Camp/シーンを組み立てる")]
        public static void BuildSceneMenu()
        {
            if (!EditorUtility.DisplayDialog("キャンプのシーン", "Camp.unity の中身を作り直します(シーンに手で足したものは消えます。色の設定とマテリアルは残ります)。", "作り直す", "やめる")) return;
            BuildScene(false);
        }

        [MenuItem("IORI/Camp/色の設定とマテリアルを既定に戻して組み立てる")]
        public static void BuildSceneResetMenu()
        {
            if (!EditorUtility.DisplayDialog("キャンプのシーン", "CampLook.asset とマテリアルを既定値に戻し、Camp.unity の中身を作り直します。", "戻して作り直す", "やめる")) return;
            BuildScene(true);
        }

        public static void BuildSceneBatch()
        {
            try { BuildScene(HasArg("-resetLook")); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static bool HasArg(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;

        static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        // ------------------------------------------------------------------ small helpers

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!a) throw new Exception("見つからない: " + path);
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void EnsureLayer(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var p = tm.FindProperty("layers").GetArrayElementAtIndex(index);
            if (p.stringValue != name) { p.stringValue = name; tm.ApplyModifiedProperties(); }
        }

        static int LayerOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (name == CampLayers.GroundName) return CampLayers.Ground;
            if (name == CampLayers.NoMirrorName) return CampLayers.NoMirror;
            if (name == CampLayers.MirrorOnlyName) return CampLayers.MirrorOnly;
            int l = LayerMask.NameToLayer(name);
            return l < 0 ? 0 : l;
        }

        // ------------------------------------------------------------------ textures

        static void ConfigureTextures()
        {
            // which stroke a point belongs to: numbers, not colors. Never compressed, never treated as a picture.
            if (AssetImporter.GetAtPath(StrokePath) is TextureImporter s)
            {
                bool dirty = s.sRGBTexture || s.alphaIsTransparency || !s.mipmapEnabled || s.wrapMode != TextureWrapMode.Repeat || s.filterMode != FilterMode.Trilinear
                             || s.anisoLevel != 8 || s.textureCompression != TextureImporterCompression.Uncompressed || s.npotScale != TextureImporterNPOTScale.None || s.maxTextureSize != 1024;
                s.textureType = TextureImporterType.Default;
                s.sRGBTexture = false; s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = false;
                s.mipmapEnabled = true; s.wrapMode = TextureWrapMode.Repeat; s.filterMode = FilterMode.Trilinear; s.anisoLevel = 8;
                s.textureCompression = TextureImporterCompression.Uncompressed; s.npotScale = TextureImporterNPOTScale.None; s.maxTextureSize = 1024;
                if (dirty) s.SaveAndReimport();
            }
            else throw new Exception("見つからない: " + StrokePath);
            // what covers the ground and the shade baked under things
            if (AssetImporter.GetAtPath(GroundMapPath) is TextureImporter g)
            {
                bool dirty = g.sRGBTexture || g.alphaIsTransparency || g.mipmapEnabled || g.wrapMode != TextureWrapMode.Clamp || g.textureCompression != TextureImporterCompression.Uncompressed || g.maxTextureSize != 1024;
                g.textureType = TextureImporterType.Default;
                g.sRGBTexture = false; g.alphaSource = TextureImporterAlphaSource.FromInput; g.alphaIsTransparency = false;
                g.mipmapEnabled = false; g.wrapMode = TextureWrapMode.Clamp; g.filterMode = FilterMode.Bilinear;
                g.textureCompression = TextureImporterCompression.Uncompressed; g.npotScale = TextureImporterNPOTScale.None; g.maxTextureSize = 1024;
                if (dirty) g.SaveAndReimport();
            }
            else throw new Exception("見つからない: " + GroundMapPath);
            // the mountains' outlines: numbers again (16 bit split over two channels), read texel by texel
            if (AssetImporter.GetAtPath(RidgePath) is TextureImporter r)
            {
                bool dirty = r.sRGBTexture || r.alphaIsTransparency || r.mipmapEnabled || r.filterMode != FilterMode.Point || r.textureCompression != TextureImporterCompression.Uncompressed
                             || r.npotScale != TextureImporterNPOTScale.None || r.maxTextureSize != 2048;
                r.textureType = TextureImporterType.Default;
                r.sRGBTexture = false; r.alphaSource = TextureImporterAlphaSource.FromInput; r.alphaIsTransparency = false;
                r.mipmapEnabled = false; r.wrapMode = TextureWrapMode.Repeat; r.filterMode = FilterMode.Point;
                r.textureCompression = TextureImporterCompression.Uncompressed; r.npotScale = TextureImporterNPOTScale.None; r.maxTextureSize = 2048;
                if (dirty) r.SaveAndReimport();
            }
            else throw new Exception("見つからない: " + RidgePath);
            if (AssetImporter.GetAtPath(IoriTexPath) is TextureImporter t)
            {
                bool dirty = !t.alphaIsTransparency || t.wrapMode != TextureWrapMode.Clamp;
                t.alphaIsTransparency = true; t.wrapMode = TextureWrapMode.Clamp; t.mipmapEnabled = true; t.maxTextureSize = 2048;
                if (dirty) t.SaveAndReimport();
            }
            // the sitting picture keeps its own size (the board takes its shape from it) and is not compressed:
            // flat cel colors and thin lines show every block of a compressed texture
            if (AssetImporter.GetAtPath(IoriSitPath) is TextureImporter sit)
            {
                bool dirty = !sit.sRGBTexture || !sit.alphaIsTransparency || !sit.mipmapEnabled || sit.wrapMode != TextureWrapMode.Clamp || sit.filterMode != FilterMode.Trilinear
                             || sit.textureCompression != TextureImporterCompression.Uncompressed || sit.npotScale != TextureImporterNPOTScale.None || sit.maxTextureSize != 2048;
                sit.textureType = TextureImporterType.Default;
                sit.sRGBTexture = true; sit.alphaSource = TextureImporterAlphaSource.FromInput; sit.alphaIsTransparency = true;
                sit.mipmapEnabled = true; sit.wrapMode = TextureWrapMode.Clamp; sit.filterMode = FilterMode.Trilinear;
                sit.textureCompression = TextureImporterCompression.Uncompressed; sit.npotScale = TextureImporterNPOTScale.None; sit.maxTextureSize = 2048;
                if (dirty) sit.SaveAndReimport();
            }
            // her normals are numbers (r right, g up, b toward the viewer), read as they are: not a color, and not
            // Unity's "normal map" type, which repacks the channels differently per platform
            if (AssetImporter.GetAtPath(IoriSitNormalPath) is TextureImporter sn)
            {
                bool dirty = sn.sRGBTexture || sn.alphaIsTransparency || !sn.mipmapEnabled || sn.wrapMode != TextureWrapMode.Clamp || sn.filterMode != FilterMode.Trilinear
                             || sn.textureCompression != TextureImporterCompression.Uncompressed || sn.npotScale != TextureImporterNPOTScale.None || sn.maxTextureSize != 2048;
                sn.textureType = TextureImporterType.Default;
                sn.sRGBTexture = false; sn.alphaSource = TextureImporterAlphaSource.None; sn.alphaIsTransparency = false;
                sn.mipmapEnabled = true; sn.wrapMode = TextureWrapMode.Clamp; sn.filterMode = FilterMode.Trilinear;
                sn.textureCompression = TextureImporterCompression.Uncompressed; sn.npotScale = TextureImporterNPOTScale.None; sn.maxTextureSize = 2048;
                if (dirty) sn.SaveAndReimport();
            }
        }

        /// <summary>Two channels of smooth noise that tile in every direction, for the flame (same recipe as the prototype it was tuned with).</summary>
        static Texture3D EnsureNoise()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
            if (existing) return existing;
            const int n = 32;
            float[] r = TileNoise(n, 1), g = TileNoise(n, 2);
            var data = new byte[n * n * n * 2];
            for (int i = 0; i < n * n * n; i++)
            {
                data[i * 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(r[i] * 255f), 0, 255);
                data[i * 2 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(g[i] * 255f), 0, 255);
            }
            var tex = new Texture3D(n, n, n, TextureFormat.RG16, false) { name = "iori_noise3d", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixelData(data, 0);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, NoisePath);
            return tex;
        }

        static float[] TileNoise(int n, int seed)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var o = new float[n * n * n];
            float amp = 0f;
            foreach (var (cells, a) in new[] { (4, 0.55f), (8, 0.30f), (16, 0.15f) })
            {
                var grid = new float[cells * cells * cells];
                for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
                var i0 = new int[n]; var i1 = new int[n]; var f = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float p = (i + 0.5f) / n * cells - 0.5f;
                    int fl = Mathf.FloorToInt(p);
                    float fr = p - fl;
                    i0[i] = ((fl % cells) + cells) % cells; i1[i] = (i0[i] + 1) % cells; f[i] = fr * fr * (3f - 2f * fr);
                }
                for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float c00 = Mathf.Lerp(grid[(i0[z] * cells + i0[y]) * cells + i0[x]], grid[(i0[z] * cells + i0[y]) * cells + i1[x]], f[x]);
                    float c10 = Mathf.Lerp(grid[(i0[z] * cells + i1[y]) * cells + i0[x]], grid[(i0[z] * cells + i1[y]) * cells + i1[x]], f[x]);
                    float c01 = Mathf.Lerp(grid[(i1[z] * cells + i0[y]) * cells + i0[x]], grid[(i1[z] * cells + i0[y]) * cells + i1[x]], f[x]);
                    float c11 = Mathf.Lerp(grid[(i1[z] * cells + i1[y]) * cells + i0[x]], grid[(i1[z] * cells + i1[y]) * cells + i1[x]], f[x]);
                    o[(z * n + y) * n + x] += Mathf.Lerp(Mathf.Lerp(c00, c10, f[y]), Mathf.Lerp(c01, c11, f[y]), f[z]) * a;
                }
                amp += a;
            }
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < o.Length; i++) { o[i] /= amp; min = Mathf.Min(min, o[i]); max = Mathf.Max(max, o[i]); }
            for (int i = 0; i < o.Length; i++) o[i] = (o[i] - min) / Mathf.Max(1e-6f, max - min);
            return o;
        }

        // ------------------------------------------------------------------ render pipeline

        /// <summary>What the camp needs from the pipeline: shadows from the fire, soft shadows, and the depth texture before the water is drawn.</summary>
        static void ConfigurePipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp)
            {
                var so = new SerializedObject(urp);
                void Bool(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; else Debug.LogWarning("[IORI] URP asset has no " + n); }
                void Int(string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; else Debug.LogWarning("[IORI] URP asset has no " + n); }
                void Float(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; else Debug.LogWarning("[IORI] URP asset has no " + n); }
                Bool("m_RequireDepthTexture", true);
                Bool("m_SupportsHDR", true);
                Int("m_MSAA", 4);
                Bool("m_MainLightShadowsSupported", true);
                Int("m_MainLightShadowmapResolution", 2048);
                Int("m_AdditionalLightsRenderingMode", 1);
                Int("m_AdditionalLightsPerObjectLimit", 4);
                Bool("m_AdditionalLightShadowsSupported", true);
                Int("m_AdditionalLightsShadowmapResolution", 4096);
                Float("m_ShadowDistance", 28f);
                Int("m_ShadowCascadeCount", 2);
                Float("m_Cascade2Split", 0.3f);
                Bool("m_SoftShadowsSupported", true);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
            }
            else Debug.LogWarning("[IORI] 見つからない: " + UrpAssetPath);
            // light strengths are meant in linear light (see CampAtmosphere.MakeSureOfLinearLights)
            GraphicsSettings.lightsUseLinearIntensity = true;

            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(UrpRendererPath);
            if (renderer)
            {
                var so = new SerializedObject(renderer);
                // 0 = right after the solid things, so the water and the flame can read what lies behind them
                var p = so.FindProperty("m_CopyDepthMode");
                if (p != null) p.intValue = 0; else Debug.LogWarning("[IORI] renderer has no m_CopyDepthMode");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
            }
            else Debug.LogWarning("[IORI] 見つからない: " + UrpRendererPath);
        }

        static VolumeProfile EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, VolumePath); }
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(false); AssetDatabase.AddObjectToAsset(c, profile); }
                return c;
            }
            // The parts that change with the hour (exposure, color, bloom, depth of field) are set by
            // CampAtmosphere on its own volume; this one holds what stays the same.
            // No tone curve: the colors are chosen as they should appear, and a curve would dull all of them
            // (the "neutral" one shows white as light grey). What is brighter than white just stays white, and glows.
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.None);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.2f); vig.smoothness.Override(0.5f); vig.color.Override(new Color(0.05f, 0.05f, 0.2f));
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.45f);
            bloom.highQualityFiltering.Override(false);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------ materials

        static Material Mat(string file, string shaderFile, bool reset, Action<Material> setup)
        {
            string path = MaterialDir + "/" + file + ".mat";
            var shader = Load<Shader>(ShaderDir + "/" + shaderFile + ".shader");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = !m || m.shader != shader || reset;
            if (!m) { m = new Material(shader) { name = file }; AssetDatabase.CreateAsset(m, path); }
            else if (fresh)
            {
                // back to what the shader says: values saved by an older version of the shader must not linger
                var clean = new Material(shader);
                EditorUtility.CopySerialized(clean, m);
                m.name = file;
                UnityEngine.Object.DestroyImmediate(clean);
            }
            if (fresh) { setup?.Invoke(m); EditorUtility.SetDirty(m); }
            return m;
        }

        static Dictionary<string, Material> BuildMaterials(bool reset, Texture2D groundMap)
        {
            var d = new Dictionary<string, Material>();
            d["Ground"] = Mat("CampGround", "IoriGround", reset, m =>
            {
                m.SetFloat("_StrokeScale", 1f); m.SetFloat("_Variation", 0.22f); m.SetFloat("_Bristle", 0.3f); m.SetFloat("_Wrap", 0.35f);
            });
            d["Ground"].SetTexture("_GroundMap", groundMap);
            d["Prop"] = Mat("CampProp", "IoriLit", reset, m =>
            {
                m.SetFloat("_StrokeScale", 3f); m.SetFloat("_Variation", 0.14f); m.SetFloat("_Bristle", 0.22f); m.SetFloat("_Wrap", 0.25f);
                m.SetFloat("_TintAmount", 0.5f); m.SetFloat("_WetDarken", 0.25f); m.SetFloat("_WetGloss", 0.4f);
            });
            d["Rock"] = Mat("CampRock", "IoriLit", reset, m =>
            {
                m.SetFloat("_StrokeScale", 2f); m.SetFloat("_Variation", 0.2f); m.SetFloat("_Bristle", 0.3f); m.SetFloat("_Wrap", 0.2f);
                m.SetFloat("_TintAmount", 0.7f); m.SetFloat("_WetDarken", 0.35f); m.SetFloat("_WetGloss", 0.5f);
            });
            d["Bark"] = Mat("CampBark", "IoriLit", reset, m =>
            {
                m.SetFloat("_StrokeScale", 2f); m.SetFloat("_Variation", 0.22f); m.SetFloat("_Bristle", 0.45f); m.SetFloat("_Wrap", 0.3f);
                m.SetFloat("_TintAmount", 0.6f); m.SetFloat("_WetDarken", 0.3f); m.SetFloat("_WetGloss", 0.15f);
            });
            d["Tent"] = Mat("CampTent", "IoriLit", reset, m =>
            {
                m.SetFloat("_StrokeScale", 1.5f); m.SetFloat("_Variation", 0.1f); m.SetFloat("_Bristle", 0.12f); m.SetFloat("_Wrap", 0.4f);
                m.SetFloat("_Through", 0.7f); m.SetFloat("_TintAmount", 0.4f); m.SetFloat("_WetDarken", 0.15f); m.SetFloat("_WetGloss", 0.3f);
            });
            // the lamp inside shows through the cloth (IoriLit paints that; the lamps' light on the cloth itself is kept low)
            d["Tent"].SetFloat("_LampAmount", 0.2f);
            d["Tent"].SetFloat("_LampInside", 1f);
            d["Tent"].EnableKeyword("_LAMP_INSIDE");
            d["Ember"] = Mat("CampEmber", "IoriLit", reset, m =>
            {
                m.SetFloat("_StrokeScale", 4f); m.SetFloat("_Variation", 0.18f); m.SetFloat("_Bristle", 0.3f); m.SetFloat("_Wrap", 0.3f);
                m.SetFloat("_TintAmount", 0.3f); m.SetFloat("_WetDarken", 0.1f); m.SetFloat("_WetGloss", 0f);
                // logs in the fire stay dark against the flame
                m.SetFloat("_LampAmount", 0.15f);
            });
            d["Ember"].SetFloat("_Ember", 1f);
            d["Ember"].EnableKeyword("_EMBER");
            d["Grass"] = Mat("CampGrass", "IoriFoliage", reset, m =>
            {
                m.SetFloat("_Leafy", 0f); m.SetFloat("_BaseShade", 0.5f); m.SetFloat("_Variation", 0.3f); m.SetFloat("_Wrap", 0.5f);
                m.SetFloat("_Through", 0.3f); m.SetFloat("_UpNormal", 0.85f); m.SetFloat("_Sway", 1f); m.SetFloat("_WetDarken", 0.15f);
            });
            d["Leaf"] = Mat("CampLeaf", "IoriFoliage", reset, m =>
            {
                m.SetFloat("_Leafy", 1f); m.SetFloat("_BaseShade", 0.6f); m.SetFloat("_Variation", 0.32f); m.SetFloat("_Wrap", 0.5f);
                m.SetFloat("_Through", 0.5f); m.SetFloat("_UpNormal", 0.2f); m.SetFloat("_Sway", 0.6f); m.SetFloat("_WetDarken", 0.15f);
            });
            // firs stay green all year
            d["Fir"] = Mat("CampFir", "IoriFoliage", reset, m =>
            {
                m.SetFloat("_Leafy", -1f); m.SetFloat("_BaseShade", 0.6f); m.SetFloat("_Variation", 0.25f); m.SetFloat("_Wrap", 0.5f);
                m.SetFloat("_Through", 0.25f); m.SetFloat("_UpNormal", 0.2f); m.SetFloat("_Sway", 0.35f); m.SetFloat("_WetDarken", 0.15f);
            });
            // ferns, reeds, flowers: turn with the grass more than with the trees
            d["Plant"] = Mat("CampPlant", "IoriFoliage", reset, m =>
            {
                m.SetFloat("_Leafy", 0.3f); m.SetFloat("_BaseShade", 0.55f); m.SetFloat("_Variation", 0.3f); m.SetFloat("_Wrap", 0.5f);
                m.SetFloat("_Through", 0.45f); m.SetFloat("_UpNormal", 0.4f); m.SetFloat("_Sway", 1f); m.SetFloat("_WetDarken", 0.15f);
            });
            d["Lake"] = Mat("CampLake", "IoriWater", reset, m =>
            {
                m.SetFloat("_Puddle", 0f); m.SetFloat("_StrokeTiles", 9f); m.SetFloat("_Wobble", 1f); m.SetColor("_Tint", Color.white);
                m.SetFloat("_Mirror", 0.45f); m.SetFloat("_Shallow", 0.35f); m.SetFloat("_Streak", 0f); m.SetFloat("_Air", 0.5f);
            });
            // the earlier way of painting the lake, kept to switch back to (CampLook.lakeClassic)
            d["LakeClassic"] = Mat("CampLakeClassic", "IoriWaterClassic", reset, m =>
            {
                m.SetFloat("_Puddle", 0f); m.SetFloat("_StrokeTiles", 9f); m.SetFloat("_Wobble", 1f); m.SetColor("_Tint", Color.white);
                m.SetFloat("_Mirror", 0.45f); m.SetFloat("_Shallow", 0.35f); m.SetFloat("_Streak", 0f); m.SetFloat("_Air", 0.5f);
            });
            d["Puddle"] = Mat("CampPuddle", "IoriWater", reset, m =>
            {
                m.SetFloat("_Puddle", 1f); m.SetFloat("_StrokeTiles", 14f); m.SetFloat("_Wobble", 0.6f); m.SetColor("_Tint", Color.white);
                m.SetFloat("_Mirror", 0.6f); m.SetFloat("_Shallow", 0.02f); m.SetFloat("_Streak", 0.045f); m.SetFloat("_Air", 0.3f);
                m.SetFloat("_Columns", 0f); m.SetFloat("_Glitter", 0f); m.SetFloat("_Lanes", 0f);
            });
            // the lake and the puddles are two versions of the water shader
            if (d["Lake"].IsKeywordEnabled("_PUDDLE")) { d["Lake"].DisableKeyword("_PUDDLE"); EditorUtility.SetDirty(d["Lake"]); }
            if (!d["Puddle"].IsKeywordEnabled("_PUDDLE")) { d["Puddle"].EnableKeyword("_PUDDLE"); EditorUtility.SetDirty(d["Puddle"]); }
            // the flame as the main camera sees it (stops at the logs), and the plain one for mirror images and phones
            d["Flame"] = Mat("CampFlame", "IoriFire", reset, m => { });
            d["Flame"].SetFloat("_UseDepth", 1f); d["Flame"].SetFloat("_Cull", (float)CullMode.Front); d["Flame"].SetFloat("_ZTest", (float)CompareFunction.Always);
            d["FlameMirror"] = Mat("CampFlameMirror", "IoriFire", reset, m => { });
            d["FlameMirror"].SetFloat("_UseDepth", 0f); d["FlameMirror"].SetFloat("_Cull", (float)CullMode.Back); d["FlameMirror"].SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            d["Sparks"] = Mat("CampSparks", "IoriSparks", reset, m => { });
            d["Rain"] = Mat("CampRain", "IoriRain", reset, m => { });
            d["Sky"] = Mat("CampSky", "IoriSky", reset, m => { m.SetFloat("_MountTiles", 5f); });
            d["Iori"] = Mat("IoriBoard", "IoriCharacter", reset, m => { m.SetFloat("_Cutoff", 0.5f); });
            d["Contact"] = Mat("IoriContact", "IoriContact", reset, m => { });
            foreach (var m in d.Values) EditorUtility.SetDirty(m);
            return d;
        }

        // ------------------------------------------------------------------ water meshes

        static void FillGrid(Mesh m, float[] xs, float[] zs)
        {
            var v = new Vector3[xs.Length * zs.Length];
            var nrm = new Vector3[v.Length];
            for (int j = 0; j < zs.Length; j++)
                for (int i = 0; i < xs.Length; i++) { v[j * xs.Length + i] = new Vector3(xs[i], 0f, zs[j]); nrm[j * xs.Length + i] = Vector3.up; }
            var tri = new List<int>();
            for (int j = 0; j < zs.Length - 1; j++)
                for (int i = 0; i < xs.Length - 1; i++)
                {
                    int c0 = j * xs.Length + i, c1 = c0 + 1, c2 = c0 + xs.Length, c3 = c2 + 1;
                    tri.Add(c0); tri.Add(c2); tri.Add(c1);
                    tri.Add(c1); tri.Add(c2); tri.Add(c3);
                }
            m.Clear();
            m.vertices = v; m.normals = nrm; m.triangles = tri.ToArray();
            m.RecalculateBounds();
        }

        /// <summary>The lake (out to the horizon, finer near the shore) and the sheet of water under the campsite that shows in the puddles' dips.</summary>
        static void EnsureWaterMeshes(Layout lay, out Mesh lake, out Mesh puddle)
        {
            lake = null; puddle = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(WaterMeshPath))
            {
                if (o is Mesh me && me.name == "Lake") lake = me;
                else if (o is Mesh me2 && me2.name == "Puddle") puddle = me2;
            }
            bool create = !lake || !puddle;
            if (create)
            {
                AssetDatabase.DeleteAsset(WaterMeshPath);
                lake = new Mesh { name = "Lake" };
                puddle = new Mesh { name = "Puddle" };
            }
            FillGrid(lake,
                new[] { -2400f, -700f, -220f, -70f, -30f, -12f, -5f, 0f, 5f, 12f, 30f, 70f, 220f, 700f, 2400f },
                new[] { 3f, 6f, 9f, 12f, 16f, 22f, 32f, 50f, 90f, 180f, 400f, 900f, 2400f });
            bool given = lay.puddleArea != null && lay.puddleArea.halfWidth > 0f;
            float hw = given ? lay.puddleArea.halfWidth : 5.4f, z0 = given ? lay.puddleArea.z0 : -3.2f, z1 = given ? lay.puddleArea.z1 : 4.8f;
            FillGrid(puddle, new[] { -hw, -hw * 0.5f, 0f, hw * 0.5f, hw }, new[] { z0, Mathf.Lerp(z0, z1, 0.25f), Mathf.Lerp(z0, z1, 0.5f), Mathf.Lerp(z0, z1, 0.75f), z1 });
            if (create)
            {
                AssetDatabase.CreateAsset(lake, WaterMeshPath);
                AssetDatabase.AddObjectToAsset(puddle, WaterMeshPath);
            }
            else { EditorUtility.SetDirty(lake); EditorUtility.SetDirty(puddle); }
        }

        // ------------------------------------------------------------------ the scene

        public static void BuildScene() => BuildScene(false);

        /// <param name="resetLook">overwrite CampLook.asset and the materials with the code defaults</param>
        public static void BuildScene(bool resetLook)
        {
            EnsureLayer(CampLayers.Ground, CampLayers.GroundName);
            EnsureLayer(CampLayers.NoMirror, CampLayers.NoMirrorName);
            EnsureLayer(CampLayers.MirrorOnly, CampLayers.MirrorOnlyName);
            EnsureFolder(MaterialDir);
            ConfigureTextures();
            ConfigurePipeline();
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;

            var lay = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            if (lay == null || lay.objects == null || lay.objects.Length == 0) throw new Exception("読めない: " + LayoutPath);
            var meshes = new Dictionary<string, Mesh>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) if (o is Mesh me) meshes[me.name] = me;
            if (meshes.Count == 0) throw new Exception("メッシュが読めない: " + MeshPath);

            var look = AssetDatabase.LoadAssetAtPath<CampLook>(LookPath);
            // when the defaults in the code have moved on, the materials go back to them together with the look
            if (look && look.version != CampLook.CurrentVersion) resetLook = true;
            if (!look) { look = CampLook.CreateDefault(); look.name = "CampLook"; AssetDatabase.CreateAsset(look, LookPath); }
            else if (resetLook)
            {
                var fresh = CampLook.CreateDefault();
                EditorUtility.CopySerialized(fresh, look);
                look.name = "CampLook";
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(look);
            }
            var strokeTex = Load<Texture2D>(StrokePath);
            var groundTex = Load<Texture2D>(GroundMapPath);
            bool sitting = HasSittingIori;
            var ioriTex = Load<Texture2D>(sitting ? IoriSitPath : IoriTexPath);
            var noiseTex = EnsureNoise();
            var mats = BuildMaterials(resetLook, groundTex);
            mats["Iori"].SetTexture("_MainTex", ioriTex);
            // with her normals the fire and the sky light her side by side; the rims drawn from her outline step back
            mats["Iori"].SetTexture("_NormalTex", sitting ? Load<Texture2D>(IoriSitNormalPath) : null);
            mats["Iori"].SetFloat("_NormalStrength", sitting ? 1f : 0f);
            // (the cool rim is off: her normals already turn the far side blue, and both together read as a pale outline)
            mats["Iori"].SetFloat("_RimWarm", sitting ? 0.15f : 0.7f);
            mats["Iori"].SetFloat("_RimCool", sitting ? 0f : 0.35f);
            EditorUtility.SetDirty(mats["Iori"]);
            var volume = EnsureVolume();
            EnsureWaterMeshes(lay, out var lakeMesh, out var puddleMesh);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);

            // ---- camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f;
            cam.allowHDR = true; cam.allowMSAA = true;
            cam.cullingMask = CampLayers.MainMask;
            cam.fieldOfView = lay.camera != null && lay.camera.fov > 0 ? lay.camera.fov : 42f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = true;
            camData.dithering = true;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CampCameraRig>();
            Vector3 camHome = lay.camera != null && lay.camera.position != null ? V(lay.camera.position) : new Vector3(0f, 1.48f, -4.67f);
            camGo.transform.position = camHome;

            // ---- root
            var root = new GameObject("Camp");
            var atm = root.AddComponent<CampAtmosphere>();
            var quality = root.AddComponent<CampQuality>();
            root.AddComponent<CampDebugPanel>();
            atm.look = look;
            atm.skyMaterial = mats["Sky"];
            atm.strokeTexture = strokeTex;
            atm.noiseTexture = noiseTex;
            atm.ridgeTexture = Load<Texture2D>(RidgePath);
            atm.strokeOrigin = camHome;
            atm.groundMap = lay.groundMap != null && lay.groundMap.size > 0 ? new Vector3(lay.groundMap.x0, lay.groundMap.z0, lay.groundMap.size) : new Vector3(-9f, -6.5f, 18f);
            atm.puddleY = lay.puddleY;

            // ---- the models
            var models = new GameObject("景色").transform;
            models.SetParent(root.transform, false);
            var renderers = new Dictionary<string, MeshRenderer>();
            foreach (var o in lay.objects)
            {
                if (sitting && o.mesh == ChairMesh) continue;      // she is drawn sitting on her own chair
                if (!meshes.TryGetValue(o.mesh, out var mesh)) { Debug.LogWarning("[IORI] メッシュがない: " + o.mesh); continue; }
                if (!mats.TryGetValue(o.material, out var mat)) { Debug.LogWarning($"[IORI] マテリアルがない: {o.material}({o.name})"); mat = mats["Prop"]; }
                var go = new GameObject(o.name);
                go.transform.SetParent(models, false);
                go.transform.position = V(o.position);
                go.transform.rotation = Quaternion.Euler(0f, o.yaw, 0f);
                go.transform.localScale = Vector3.one * (o.scale > 0 ? o.scale : 1f);
                go.layer = LayerOf(o.layer);
                go.isStatic = false;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = o.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderers[o.name] = r;
            }

            // ---- places (things you can tap, and where the lights go)
            var anchors = new GameObject("置き場所").transform;
            anchors.SetParent(root.transform, false);
            Transform Anchor(string name, float[] pos, CampPropKind? kind, Vector3 focus, float radius, bool focusOnTap = true)
            {
                var t = new GameObject(name).transform;
                t.SetParent(anchors, false);
                t.position = pos != null ? V(pos) : Vector3.zero;
                if (kind.HasValue)
                {
                    var p = t.gameObject.AddComponent<CampProp>();
                    p.kind = kind.Value; p.focusOffset = focus; p.tapRadius = radius; p.focusOnTap = focusOnTap;
                    var c = t.GetComponent<SphereCollider>();
                    if (c) { c.center = focus; c.radius = radius; c.isTrigger = true; }
                }
                return t;
            }
            var a = lay.anchors ?? new LayoutAnchors();
            var fireAnchor = Anchor("焚き火", a.fire, CampPropKind.Fire, new Vector3(0f, 0.32f, 0f), 0.45f);
            var ioriAnchor = Anchor("イオリ", a.iori, CampPropKind.Iori, new Vector3(0f, sitting ? 0.8f : 1.0f, 0f), 0.5f);
            var lanternAnchor = Anchor("ランタン", a.lantern, CampPropKind.Lantern, new Vector3(0f, 0.13f, 0f), 0.2f, false);
            Anchor("ラジオ", a.radio, CampPropKind.Radio, new Vector3(-0.02f, 0.43f, -0.03f), 0.3f);
            Anchor("音楽プレーヤー", a.player, CampPropKind.Player, new Vector3(-0.01f, 0.38f, -0.01f), 0.28f);
            var tentAnchor = Anchor("テント", a.tent, null, Vector3.zero, 0f);
            atm.fire = fireAnchor;

            // ---- lights
            var lights = new GameObject("灯り").transform;
            lights.SetParent(root.transform, false);
            Light MakeLight(string name, LightType type, Vector3 pos, LightShadows shadows)
            {
                var go = new GameObject(name);
                go.transform.SetParent(lights, false);
                go.transform.position = pos;
                var l = go.AddComponent<Light>();
                l.type = type;
                l.shadows = shadows;
                l.renderMode = LightRenderMode.ForcePixel;
                l.bounceIntensity = 0f;
                go.AddComponent<UniversalAdditionalLightData>();
                return l;
            }
            var key = MakeLight("主な光(太陽、残照、月)", LightType.Directional, new Vector3(0f, 6f, 0f), LightShadows.Soft);
            key.transform.rotation = Quaternion.LookRotation(-CampAtmosphere.SkyDirection(-22f, 6f), Vector3.up);
            var fireLight = MakeLight("焚き火の光", LightType.Point, fireAnchor.position + new Vector3(0f, 0.32f, 0f), LightShadows.Soft);
            fireLight.shadowNearPlane = 0.1f;
            fireLight.shadowStrength = 0.9f;
            var lanternLight = MakeLight("ランタンの光", LightType.Point, lanternAnchor.position + new Vector3(0f, 0.14f, 0f), LightShadows.None);
            var tentLight = MakeLight("テントの光", LightType.Point, tentAnchor.position + new Vector3(0f, 0.6f, 0f), LightShadows.None);
            atm.keyLight = key; atm.fireLight = fireLight; atm.lanternLight = lanternLight; atm.tentLight = tentLight;
            RenderSettings.sun = key;

            // ---- the fire: the flame the main camera sees, the plain one for the mirror images, and the sparks
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            MeshRenderer Flame(string name, Material m, int layer)
            {
                var go = new GameObject(name);
                go.transform.SetParent(fireAnchor, false);
                // the box the flame lives in; its bottom is where the flame starts, just above the ash
                // (roomy: the tallest tongue at its tallest, on the brightest night, must not touch the lid)
                go.transform.localPosition = new Vector3(0f, 0.05f + 0.75f, 0f);
                go.transform.localScale = new Vector3(1.1f, 1.5f, 1.1f);
                go.layer = layer;
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return r;
            }
            var flame = Flame("炎", mats["Flame"], CampLayers.NoMirror);
            var flameMirror = Flame("炎(映り込みとスマホ用)", mats["FlameMirror"], CampLayers.MirrorOnly);
            {
                var go = new GameObject("火の粉");
                go.transform.SetParent(fireAnchor, false);
                go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats["Sparks"];
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                go.AddComponent<CampSparks>();
            }

            // ---- water
            var water = new GameObject("水").transform;
            water.SetParent(root.transform, false);
            MeshRenderer Water(string name, Mesh mesh, Material m, float y)
            {
                var go = new GameObject(name);
                go.transform.SetParent(water, false);
                go.transform.position = new Vector3(0f, y, 0f);
                go.layer = CampLayers.Water;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return r;
            }
            atm.lake = Water("湖", lakeMesh, mats["Lake"], lay.waterY);
            atm.lakeMaterial = mats["Lake"]; atm.lakeClassicMaterial = mats["LakeClassic"];
            var puddle = Water("水たまり", puddleMesh, mats["Puddle"], lay.puddleY);
            atm.puddle = puddle;

            // ---- rain
            {
                var go = new GameObject("雨");
                go.transform.SetParent(root.transform, false);
                go.layer = CampLayers.NoMirror;
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats["Rain"];
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                go.AddComponent<CampRain>();
            }

            // ---- Iori (a picture on a board until her real model exists)
            {
                var board = new GameObject(sitting ? "イオリの板(座り絵)" : "イオリの板(仮の立ち絵)");
                board.transform.SetParent(ioriAnchor, false);
                board.AddComponent<MeshFilter>();
                var br = board.AddComponent<MeshRenderer>();
                br.sharedMaterial = mats["Iori"];
                br.shadowCastingMode = ShadowCastingMode.On;
                br.receiveShadows = false;
                var bb = board.AddComponent<CampBillboard>();
                bb.picture = ioriTex;
                if (sitting) bb.height = IoriSitHeight;
                bb.viewer = cam;
            }
            // the shade under her and her chair (a flat picture cannot shade the ground under itself)
            if (sitting)
            {
                var shade = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shade.name = "イオリの足もとの影";
                UnityEngine.Object.DestroyImmediate(shade.GetComponent<Collider>());
                shade.transform.SetParent(ioriAnchor, false);
                // lying on the ground, a little behind the board (the chair has depth) and toward the chair's side of the picture
                shade.transform.localPosition = new Vector3(0.04f, 0.02f, 0.12f);
                shade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shade.transform.localScale = new Vector3(1.0f, 0.62f, 1f);
                var sr = shade.GetComponent<MeshRenderer>();
                sr.sharedMaterial = mats["Contact"];
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
            }

            // ---- mirror images for the lake and for the puddles
            var lakeMirror = root.AddComponent<CampReflection>();
            lakeMirror.source = cam; lakeMirror.textureName = "_IoriReflectionTex"; lakeMirror.planeY = lay.waterY; lakeMirror.followPuddle = false;
            lakeMirror.mask = CampLayers.MirrorMask;
            var puddleMirror = root.AddComponent<CampReflection>();
            puddleMirror.source = cam; puddleMirror.textureName = "_IoriReflectionTexPuddle"; puddleMirror.planeY = lay.puddleY; puddleMirror.followPuddle = true;
            puddleMirror.mask = CampLayers.MirrorMask; puddleMirror.fallback = lakeMirror;

            // ---- what a phone leaves out
            var dense = new List<Renderer>();
            if (renderers.TryGetValue("Grass1", out var g1)) dense.Add(g1);
            if (renderers.TryGetValue("Grass2", out var g2)) dense.Add(g2);
            quality.denseGrass = dense.ToArray();
            quality.fireLight = fireLight; quality.keyLight = key;
            quality.flame = flame; quality.flameMirror = flameMirror;
            quality.lakeMirror = lakeMirror; quality.puddleMirror = puddleMirror;

            // ---- the picture's finish (the part that does not change with the hour)
            var volGo = new GameObject("Post-processing");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = volume;

            RenderSettings.skybox = mats["Sky"];
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            quality.Apply();
            atm.Snap();
            rig.Snap();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[IORI] Camp scene built: {lay.objects.Length} models.");
            LogShaderMessages();
        }

        /// <summary>Compile errors of the camp shaders, so a batch run can report them.</summary>
        static void LogShaderMessages()
        {
            foreach (var f in ShaderFiles)
            {
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/" + f + ".shader");
                if (!sh) { Debug.LogError("[IORI] shader missing: " + f); continue; }
                foreach (var msg in ShaderUtil.GetShaderMessages(sh))
                    if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        Debug.LogError($"[IORI] shader error {f}: {msg.message} ({msg.file}:{msg.line}) {msg.platform}");
            }
        }

        // ------------------------------------------------------------------ screenshots

        public struct Shot
        {
            public string name;
            public float hour;
            public CampWeather weather;
            public bool sleep;
            public bool landscape;
            public CampPropKind? focus;
            public CampSeason season;
            public float yaw;
            public bool phone;
            /// <summary>Seconds on the scene's clock (flames, wind, ripples). 0 means the usual moment.</summary>
            public float time;
            /// <summary>Smaller picture (for sequences).</summary>
            public bool small;
            /// <summary>The lake painted the earlier way (CampLook.lakeClassic).</summary>
            public bool lakeClassic;
        }

        const float ShotTime = 37.3f;

        public static readonly Shot[] DefaultShots =
        {
            new Shot { name = "01_twilight", hour = 18.6f },
            new Shot { name = "02_twilight_rain", hour = 18.6f, weather = CampWeather.Rain },
            new Shot { name = "03_twilight_afterrain", hour = 18.6f, weather = CampWeather.AfterRain },
            new Shot { name = "04_predawn", hour = 4.9f },
            new Shot { name = "05_dawn", hour = 6.4f },
            new Shot { name = "06_day", hour = 12f },
            new Shot { name = "07_evening", hour = 17.2f },
            new Shot { name = "08_night", hour = 22f },
            new Shot { name = "09_night_sleep", hour = 22f, sleep = true },
            new Shot { name = "10_night_afterrain", hour = 22f, weather = CampWeather.AfterRain },
            new Shot { name = "11_focus_fire", hour = 18.6f, focus = CampPropKind.Fire },
            new Shot { name = "12_focus_radio", hour = 18.6f, focus = CampPropKind.Radio },
            new Shot { name = "13_focus_iori", hour = 18.6f, focus = CampPropKind.Iori },
            new Shot { name = "14_focus_player", hour = 18.6f, focus = CampPropKind.Player },
            new Shot { name = "15_landscape", hour = 18.6f, landscape = true },
            new Shot { name = "16_landscape_afterrain", hour = 4.9f, landscape = true, weather = CampWeather.AfterRain },
            new Shot { name = "17_autumn", hour = 17.2f, season = CampSeason.Autumn },
            new Shot { name = "18_winter_day", hour = 12f, season = CampSeason.Winter },
            new Shot { name = "19_spring_dawn", hour = 6.4f, season = CampSeason.Spring },
            new Shot { name = "20_look_left", hour = 18.6f, yaw = 0.4f },
            new Shot { name = "21_look_right", hour = 18.6f, yaw = -0.4f },
            new Shot { name = "22_phone_twilight", hour = 18.6f, phone = true },
            new Shot { name = "23_phone_afterrain", hour = 18.6f, phone = true, weather = CampWeather.AfterRain },
            new Shot { name = "30_fire_a", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 0.15f, small = true },
            new Shot { name = "31_fire_b", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 0.30f, small = true },
            new Shot { name = "32_fire_c", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 0.45f, small = true },
            new Shot { name = "33_fire_d", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 0.60f, small = true },
            new Shot { name = "34_fire_e", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 2.0f, small = true },
            new Shot { name = "35_fire_f", hour = 18.6f, focus = CampPropKind.Fire, time = ShotTime + 5.0f, small = true },
            // the lake late at night (the moon's path lies clear of Iori then), and a moment and a few seconds later:
            // to see how the water moves (it must not flicker)
            new Shot { name = "40_lake_a", hour = 2.5f },
            new Shot { name = "41_lake_b", hour = 2.5f, time = ShotTime + 0.5f },
            new Shot { name = "42_lake_c", hour = 2.5f, time = ShotTime + 4.0f },
            new Shot { name = "43_landscape_night", hour = 22f, landscape = true },
            // the lake painted the earlier way, to compare with 08
            new Shot { name = "44_lake_classic", hour = 22f, lakeClassic = true },
            // Iori by the fire at night (13 is the same view at twilight)
            new Shot { name = "45_night_focus_iori", hour = 22f, focus = CampPropKind.Iori },
        };

        /// <summary>Iori by the fire at night, a tenth of a second apart: to see the firelight move on her
        /// (names 50 to 73, so "-shots 50,51,..." picks them like the others).</summary>
        static Shot[] FlickerShots()
        {
            var shots = new Shot[24];
            for (int i = 0; i < shots.Length; i++)
                shots[i] = new Shot { name = (50 + i) + "_iori_flicker", hour = 22f, focus = CampPropKind.Iori, time = ShotTime + 0.1f * i + 0.05f, small = true };
            return shots;
        }

        [MenuItem("IORI/Camp/スクショを撮る")]
        public static void CaptureMenu()
        {
            var dir = EditorUtility.SaveFolderPanel("保存先", "", "camp-shots");
            if (!string.IsNullOrEmpty(dir)) Capture(dir, DefaultShots);
        }

        /// <summary>-shotDir where to save; -shots 01,02,11 picks shots by the first two characters of their names.</summary>
        public static void CaptureBatch()
        {
            try
            {
                string dir = ArgValue("-shotDir") ?? Path.GetFullPath("camp-shots");
                string only = ArgValue("-shots");
                _flatIori = HasArg("-ioriFlat");
                _plainFire = HasArg("-ioriPlainFire");
                var shots = new List<Shot>(DefaultShots);
                shots.AddRange(FlickerShots());
                if (!string.IsNullOrEmpty(only))
                {
                    var keep = new HashSet<string>(only.Split(','));
                    shots = shots.FindAll(s => keep.Contains(s.name.Substring(0, 2)));
                }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Capture(dir, shots.ToArray());
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>-ioriFlat: take the pictures with Iori's normals switched off (the board lit as one flat thing), to compare.</summary>
        static bool _flatIori;
        /// <summary>-ioriPlainFire: take the pictures with the care for the firelight on Iori switched off (lit from where
        /// the fire is, no glowing border, no glints, no flicker of her own), to compare.</summary>
        static bool _plainFire;
        static readonly string[] FireCare = { "_LampFromSide", "_LampUpFade", "_EdgeGlow", "_Glint", "_Flicker", "_Dance" };

        public static void Capture(string dir, Shot[] shots)
        {
            Directory.CreateDirectory(dir);
            var atm = UnityEngine.Object.FindFirstObjectByType<CampAtmosphere>();
            var rig = UnityEngine.Object.FindFirstObjectByType<CampCameraRig>();
            var quality = UnityEngine.Object.FindFirstObjectByType<CampQuality>();
            var board = UnityEngine.Object.FindFirstObjectByType<CampBillboard>();
            var rain = UnityEngine.Object.FindFirstObjectByType<CampRain>();
            var mirrors = UnityEngine.Object.FindObjectsByType<CampReflection>(FindObjectsSortMode.None);
            if (!atm || !rig || !quality) throw new Exception("Camp scene is not set up (IORI/Camp/シーンを組み立てる)");
            var cam = rig.GetComponent<Camera>();
            var props = UnityEngine.Object.FindObjectsByType<CampProp>(FindObjectsSortMode.None);
            // the lake's mirror first: the puddles' one may borrow its picture
            Array.Sort(mirrors, (x, y) => x.followPuddle.CompareTo(y.followPuddle));

            // a single picture asked for by script does not switch this on by itself (see CampAtmosphere)
            GraphicsSettings.lightsUseLinearIntensity = true;
            // every shader variant must be ready before a picture is taken (no placeholder shaders in the shots)
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            bool oldClock = atm.followClock; float oldHour = atm.previewHour; var oldWeather = atm.weather; var oldSeason = atm.season;
            bool oldSway = rig.autoSway; var oldLevel = quality.level;
            bool oldLakeClassic = atm.look && atm.look.lakeClassic;
            var boardMat = board ? board.GetComponent<MeshRenderer>().sharedMaterial : null;
            float oldNormal = boardMat && boardMat.HasProperty("_NormalStrength") ? boardMat.GetFloat("_NormalStrength") : 0f;
            if (_flatIori && boardMat) boardMat.SetFloat("_NormalStrength", 0f);
            var oldFire = new float[FireCare.Length];
            if (_plainFire && boardMat)
                for (int i = 0; i < FireCare.Length; i++)
                {
                    oldFire[i] = boardMat.GetFloat(FireCare[i]);
                    boardMat.SetFloat(FireCare[i], 0f);
                }
            try
            {
                foreach (var s in shots)
                {
                    int w = s.landscape ? 1600 : 720, h = s.landscape ? 720 : 1600;
                    if (s.small) { w = w * 3 / 4; h = h * 3 / 4; }
                    var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                    rt.Create();
                    cam.targetTexture = rt;

                    quality.level = s.phone ? CampQualityLevel.Phone : CampQualityLevel.Full;
                    quality.Apply();
                    atm.followClock = false; atm.previewHour = s.hour; atm.weather = s.weather; atm.sleep = s.sleep;
                    atm.season = s.season == CampSeason.Auto ? CampSeason.Summer : s.season;
                    atm.timeOverride = s.time > 0f ? s.time : ShotTime;
                    if (atm.look) atm.look.lakeClassic = s.lakeClassic;
                    atm.Snap();
                    rig.autoSway = false;
                    rig.focus = null;
                    if (s.focus.HasValue) foreach (var p in props) if (p.kind == s.focus.Value) rig.focus = p;
                    rig.yaw = s.yaw; rig.pitch = 0f; rig.zoom = 1f;
                    rig.Snap();
                    atm.Snap();          // again: the camera has now said how far away its subject is
                    if (board) board.Place();
                    if (rain) rain.Refresh();
                    foreach (var m in mirrors)
                    {
                        m.UpdateNow();
                        if (m.Active && m.ReflectionCamera && m.Texture) RenderCamera(m.ReflectionCamera, m.Texture, false);
                    }
                    RenderCamera(cam, rt, true);

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, s.name + ".jpg"), tex.EncodeToJPG(90));
                    UnityEngine.Object.DestroyImmediate(tex);
                    cam.targetTexture = null;
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                    Debug.Log("[IORI] shot " + s.name);
                }
            }
            finally
            {
                cam.targetTexture = null;
                if (_flatIori && boardMat) boardMat.SetFloat("_NormalStrength", oldNormal);
                if (_plainFire && boardMat)
                    for (int i = 0; i < FireCare.Length; i++) boardMat.SetFloat(FireCare[i], oldFire[i]);
                atm.followClock = oldClock; atm.previewHour = oldHour; atm.weather = oldWeather; atm.season = oldSeason; atm.sleep = false; atm.timeOverride = -1f;
                rig.focus = null; rig.yaw = 0f; rig.autoSway = oldSway;
                if (atm.look) atm.look.lakeClassic = oldLakeClassic;
                quality.level = oldLevel;
                quality.Apply();
                atm.Snap();
                rig.Snap();
                ShaderUtil.allowAsyncCompilation = oldAsync;
            }
            Debug.Log("[IORI] shots saved to " + dir);
            LogShaderMessages();
            LogStats(mirrors.Length > 0 ? mirrors[0] : null);
        }

        /// <param name="finished">true for the picture itself: asks for the whole pipeline, so the picture's finish
        /// (glow, blur, color) is applied as it is when playing. A "single camera" request skips working out
        /// which volumes apply, and the picture comes out without any finish; that is fine for the mirror
        /// images, which have none.</param>
        static void RenderCamera(Camera c, RenderTexture target, bool finished)
        {
            if (finished)
            {
                var whole = new RenderPipeline.StandardRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(c, whole)) { RenderPipeline.SubmitRenderRequest(c, whole); return; }
                Debug.LogWarning("[IORI] the pipeline takes no standard request; the shots lack the picture's finish");
            }
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(c, req)) RenderPipeline.SubmitRenderRequest(c, req);
            else c.Render();
        }

        /// <summary>Rough cost of the scene for phones: renderers and triangles, main view and mirror image.</summary>
        static void LogStats(CampReflection refl)
        {
            long tris = 0, reflTris = 0; int renderers = 0, reflRenderers = 0;
            int mask = refl ? refl.mask.value : 0;
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!mf.sharedMesh) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (!r || !r.enabled) continue;
                long t = 0;
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) t += mf.sharedMesh.GetIndexCount(i) / 3;
                tris += t; renderers++;
                if ((mask & (1 << mf.gameObject.layer)) != 0) { reflTris += t; reflRenderers++; }
                Debug.Log($"[IORI] stats mesh {mf.name}: {t} tris, layer {mf.gameObject.layer}");
            }
            Debug.Log($"[IORI] stats total {renderers} renderers, {tris} tris; mirror image {reflRenderers} renderers, {reflTris} tris");
        }

        // ------------------------------------------------------------------ desktop play test

        /// <summary>
        /// A Windows player of the camp scene for checking the real, running thing (the on-screen panel,
        /// the fire moving, rain coming on). Start it with
        ///   IoriCamp.exe -ioriShots C:\\path -screen-width 540 -screen-height 1200 -screen-fullscreen 0
        /// and CampShotRunner takes pictures by itself and quits. -exe says where to build it.
        /// </summary>
        public static void BuildWindowsPlayBatch()
        {
            string exe = ArgValue("-exe") ?? Path.GetFullPath("Builds/campplay/IoriCamp.exe");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    target = BuildTarget.StandaloneWindows64,
                    locationPathName = exe,
                    options = BuildOptions.Development,
                });
                Debug.Log("[IORI] windows build " + report.summary.result + " " + report.summary.totalSize + " bytes, " + report.summary.totalTime);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ------------------------------------------------------------------ phone performance test

        /// <summary>
        /// Development APK of the camp scene under a separate package id (com.iuti.iori.bgtest), so it
        /// does not replace the installed app. -apk path. The frame rate shows up in logcat as "[IORI] perf".
        /// </summary>
        public static void BuildAndroidPerfBatch()
        {
            string apk = ArgValue("-apk") ?? Path.GetFullPath("Builds/iori-bgtest.apk");
            string oldId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            string oldName = PlayerSettings.productName;
            try
            {
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.iuti.iori.bgtest");
                PlayerSettings.productName = "IORI bg test";
                Directory.CreateDirectory(Path.GetDirectoryName(apk));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    target = BuildTarget.Android,
                    locationPathName = apk,
                    options = BuildOptions.Development,
                });
                Debug.Log("[IORI] apk build " + report.summary.result + " " + report.summary.totalSize + " bytes, " + report.summary.totalTime);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, oldId);
                PlayerSettings.productName = oldName;
            }
        }
    }
}
