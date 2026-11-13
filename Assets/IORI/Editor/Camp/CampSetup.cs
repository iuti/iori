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
    /// Menu "IORI/Camp": puts the camp scene together (anchors, camera, atmosphere, post-processing)
    /// and takes screenshots of the looks. Both also run in batch mode:
    ///   Unity.exe -batchmode -projectPath . -executeMethod Iori.Camp.EditorTools.CampSetup.BuildSceneBatch -quit
    ///   Unity.exe -batchmode -projectPath . -executeMethod Iori.Camp.EditorTools.CampSetup.CaptureBatch -shotDir C:\path -quit
    /// </summary>
    public static class CampSetup
    {
        const string ScenePath = "Assets/IORI/Scenes/Camp.unity";
        const string ArtDir = "Assets/IORI/Art/Camp";
        const string ShaderDir = ArtDir + "/Shaders";
        const string LookPath = ArtDir + "/CampLook.asset";
        const string SkyMatPath = ArtDir + "/Materials/CampSky.mat";
        const string IoriMatPath = ArtDir + "/Materials/IoriBoard.mat";
        const string VolumePath = ArtDir + "/CampVolume.asset";
        const string BrushPath = ArtDir + "/Textures/iori_brush.png";
        const string IoriTexPath = "Assets/IORI/Art/Character/iori_placeholder.png";

        [MenuItem("IORI/Camp/シーンを組み立てる")]
        public static void BuildSceneMenu()
        {
            if (!EditorUtility.DisplayDialog("キャンプのシーン", "Camp.unity の中身を作り直します(手で足したものは消えます)。", "作り直す", "やめる")) return;
            BuildScene();
        }

        public static void BuildSceneBatch()
        {
            try { BuildScene(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

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
            var layers = tm.FindProperty("layers");
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue != name) { p.stringValue = name; tm.ApplyModifiedProperties(); }
        }

        static void ConfigureTextures()
        {
            if (AssetImporter.GetAtPath(BrushPath) is TextureImporter b)
            {
                bool dirty = b.sRGBTexture || b.wrapMode != TextureWrapMode.Repeat || b.textureCompression != TextureImporterCompression.Uncompressed;
                b.sRGBTexture = false; b.wrapMode = TextureWrapMode.Repeat; b.mipmapEnabled = true; b.filterMode = FilterMode.Bilinear;
                b.textureCompression = TextureImporterCompression.Uncompressed;
                if (dirty) b.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath(IoriTexPath) is TextureImporter t)
            {
                bool dirty = !t.alphaIsTransparency || t.wrapMode != TextureWrapMode.Clamp;
                t.alphaIsTransparency = true; t.wrapMode = TextureWrapMode.Clamp; t.mipmapEnabled = true; t.maxTextureSize = 2048;
                if (dirty) t.SaveAndReimport();
            }
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }

        static VolumeProfile EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, VolumePath); }
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); }
                return c;
            }
            // Weak bloom on the lights only: lights are HDR (> 1), the sky and ground stay under the threshold.
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f); bloom.intensity.Override(0.35f); bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.26f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.06f, 0.05f, 0.2f));
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.None);
            var ca = Get<ColorAdjustments>();
            ca.saturation.Override(8f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        public static void BuildScene()
        {
            EnsureLayer(CampBuilder.GroundLayer, "CampGround");
            EnsureFolder(ArtDir + "/Materials");
            ConfigureTextures();
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;

            var look = AssetDatabase.LoadAssetAtPath<CampLook>(LookPath);
            if (!look) { look = CampLook.CreateDefault(); look.name = "CampLook"; AssetDatabase.CreateAsset(look, LookPath); }
            var skyMat = EnsureMaterial(SkyMatPath, Load<Shader>(ShaderDir + "/IoriSky.shader"));
            var ioriMat = EnsureMaterial(IoriMatPath, Load<Shader>(ShaderDir + "/IoriCharacter.shader"));
            var ioriTex = Load<Texture2D>(IoriTexPath);
            ioriMat.SetTexture("_MainTex", ioriTex);
            ioriMat.SetFloat("_FireSide", 1f);
            var volume = EnsureVolume();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);

            // camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f;
            cam.allowHDR = true; cam.allowMSAA = true;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = false;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CampCameraRig>();

            // camp root
            var root = new GameObject("Camp");
            var atm = root.AddComponent<CampAtmosphere>();
            atm.look = look;
            atm.skyMaterial = skyMat;
            atm.brushTexture = Load<Texture2D>(BrushPath);
            var builder = root.AddComponent<CampBuilder>();
            builder.enabled = false; // wire everything first, then build once
            builder.painted = Load<Shader>(ShaderDir + "/IoriPainted.shader");
            builder.flat = Load<Shader>(ShaderDir + "/IoriFlat.shader");
            builder.glow = Load<Shader>(ShaderDir + "/IoriGlow.shader");
            builder.embers = Load<Shader>(ShaderDir + "/IoriEmbers.shader");
            builder.rain = Load<Shader>(ShaderDir + "/IoriRain.shader");
            builder.water = Load<Shader>(ShaderDir + "/IoriWater.shader");
            builder.blob = Load<Shader>(ShaderDir + "/IoriBlob.shader");
            root.AddComponent<CampDebugPanel>();
            root.AddComponent<CampQuality>();

            var anchors = new GameObject("置き場所").transform;
            anchors.SetParent(root.transform, false);
            Transform Anchor(string name, Vector3 pos, CampPropKind? kind, Vector3 focus, float radius, bool focusOnTap = true)
            {
                var t = new GameObject(name).transform;
                t.SetParent(anchors, false);
                t.localPosition = pos;
                if (kind.HasValue)
                {
                    var p = t.gameObject.AddComponent<CampProp>();
                    p.kind = kind.Value; p.focusOffset = focus; p.tapRadius = radius; p.focusOnTap = focusOnTap;
                }
                return t;
            }
            builder.fireAnchor = Anchor("焚き火", new Vector3(-0.48f, 0, -1.05f), CampPropKind.Fire, new Vector3(0, 0.5f, 0), 0.45f);
            builder.ioriAnchor = Anchor("イオリ", new Vector3(0.1f, 0, 0), CampPropKind.Iori, new Vector3(0, 1.0f, 0), 0.5f);
            builder.tableAnchor = Anchor("ランタンのテーブル", new Vector3(-0.9f, 0, 0.45f), CampPropKind.Lantern, new Vector3(-0.1f, 0.74f, -0.02f), 0.25f, false);
            builder.radioAnchor = Anchor("ラジオ", new Vector3(0.86f, 0, 0.45f), CampPropKind.Radio, new Vector3(0, 0.52f, 0), 0.35f);
            builder.playerAnchor = Anchor("音楽プレーヤー", new Vector3(0.64f, 0, -1.0f), CampPropKind.Player, new Vector3(0, 0.45f, 0), 0.3f);
            builder.tentAnchor = Anchor("テント", new Vector3(-1.9f, 0, 5.2f), null, Vector3.zero, 0);
            atm.fire = builder.fireAnchor;
            atm.lantern = builder.tableAnchor;
            atm.tent = builder.tentAnchor;

            // Iori placeholder board
            var board = new GameObject("イオリの板(仮の立ち絵)");
            board.transform.SetParent(builder.ioriAnchor, false);
            board.AddComponent<MeshFilter>();
            var br = board.AddComponent<MeshRenderer>();
            br.sharedMaterial = ioriMat;
            br.shadowCastingMode = ShadowCastingMode.Off;
            var bb = board.AddComponent<CampBillboard>();
            bb.picture = ioriTex;
            bb.builder = builder;

            var refl = root.AddComponent<CampReflection>();
            refl.source = cam;
            refl.builder = builder;

            // post-processing
            var volGo = new GameObject("Post-processing");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = volume;

            RenderSettings.skybox = skyMat;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            builder.enabled = true;
            atm.Snap();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[IORI] Camp scene built.");
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
        }

        public static readonly Shot[] DefaultShots =
        {
            new Shot { name = "01_twilight_clear", hour = 18.6f },
            new Shot { name = "02_twilight_rain", hour = 18.6f, weather = CampWeather.Rain },
            new Shot { name = "03_twilight_afterrain", hour = 18.6f, weather = CampWeather.AfterRain },
            new Shot { name = "04_predawn", hour = 4.9f },
            new Shot { name = "05_dawn", hour = 6.4f },
            new Shot { name = "06_day", hour = 12f },
            new Shot { name = "07_evening", hour = 17.2f },
            new Shot { name = "08_night", hour = 22f },
            new Shot { name = "09_night_sleep", hour = 22f, sleep = true },
            new Shot { name = "10_day_rain", hour = 12f, weather = CampWeather.Rain },
            new Shot { name = "11_focus_radio", hour = 18.6f, focus = CampPropKind.Radio },
            new Shot { name = "12_landscape", hour = 18.6f, landscape = true },
        };

        [MenuItem("IORI/Camp/スクショを撮る")]
        public static void CaptureMenu()
        {
            var dir = EditorUtility.SaveFolderPanel("保存先", "", "camp-shots");
            if (!string.IsNullOrEmpty(dir)) Capture(dir, DefaultShots);
        }

        public static void CaptureBatch()
        {
            try
            {
                string dir = ArgValue("-shotDir") ?? Path.GetFullPath("camp-shots");
                string only = ArgValue("-shots");
                var shots = new List<Shot>(DefaultShots);
                if (!string.IsNullOrEmpty(only)) shots = shots.FindAll(s => only.Split(',').Contains(s.name.Substring(0, 2)));
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Capture(dir, shots.ToArray());
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        static bool Contains(this string[] arr, string s) => Array.IndexOf(arr, s) >= 0;

        public static void Capture(string dir, Shot[] shots)
        {
            Directory.CreateDirectory(dir);
            var atm = UnityEngine.Object.FindFirstObjectByType<CampAtmosphere>();
            var builder = UnityEngine.Object.FindFirstObjectByType<CampBuilder>();
            var rig = UnityEngine.Object.FindFirstObjectByType<CampCameraRig>();
            var refl = UnityEngine.Object.FindFirstObjectByType<CampReflection>();
            var board = UnityEngine.Object.FindFirstObjectByType<CampBillboard>();
            if (!atm || !builder || !rig) throw new Exception("Camp scene is not set up (IORI/Camp/シーンを組み立てる)");
            if (!builder.Root) builder.Rebuild();
            var flames = builder.FlamesRoot ? builder.FlamesRoot.GetComponent<CampFlames>() : null;
            var cam = rig.GetComponent<Camera>();
            var props = UnityEngine.Object.FindObjectsByType<CampProp>(FindObjectsSortMode.None);

            bool oldClock = atm.followClock; float oldHour = atm.previewHour; var oldWeather = atm.weather; var oldSeason = atm.season;
            foreach (var s in shots)
            {
                int w = s.landscape ? 1280 : 720, h = s.landscape ? 720 : 1280;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                rt.Create();
                cam.targetTexture = rt;

                atm.followClock = false; atm.previewHour = s.hour; atm.weather = s.weather; atm.sleep = s.sleep; atm.season = s.season == CampSeason.Auto ? CampSeason.Summer : s.season;
                atm.timeOverride = 37.3f;
                atm.Snap();
                rig.focus = null;
                if (s.focus.HasValue) foreach (var p in props) if (p.kind == s.focus.Value) rig.focus = p;
                rig.yaw = 0; rig.Snap();
                if (board) board.Place();
                if (flames) flames.Animate();
                if (refl)
                {
                    refl.UpdateNow();
                    RenderCamera(refl.ReflectionCamera, refl.ReflectionCamera.targetTexture);
                }
                RenderCamera(cam, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, s.name + ".jpg"), tex.EncodeToJPG(88));
                UnityEngine.Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                Debug.Log("[IORI] shot " + s.name);
            }
            atm.followClock = oldClock; atm.previewHour = oldHour; atm.weather = oldWeather; atm.season = oldSeason; atm.sleep = false; atm.timeOverride = -1f;
            rig.focus = null;
            atm.Snap();
            Debug.Log("[IORI] shots saved to " + dir);
        }

        static void RenderCamera(Camera c, RenderTexture target)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(c, req)) RenderPipeline.SubmitRenderRequest(c, req);
            else c.Render();
        }
    }
}
