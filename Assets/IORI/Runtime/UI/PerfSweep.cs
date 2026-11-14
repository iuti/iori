using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Iori.Camp;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Iori.Ui
{
    /// <summary>
    /// A development aid for finding out what makes the camp slow on a phone. Started with the argument
    /// "-ioriPerfSweep" (desktop), or on Android with
    ///   adb shell am start -n PACKAGE/ACTIVITY --es iori -ioriPerfSweep
    /// the app runs by itself for about five minutes: it switches the costly parts of the picture off
    /// one at a time (everything, the sky, each kind of surface by its shader, the lake's mirror image,
    /// the lamps, the finer resolution, the blur behind the glass and so on), measures the frame time of
    /// each, and writes one line per measurement to the log ("[IORI] sweep ...") and all of them to
    /// perf_sweep.txt in the app's data folder. Each line also says where the time went: main = the
    /// scripts' thread, render = the thread that hands the picture to the graphics chip, wait = the main
    /// thread waiting for the screen, gpu = the graphics chip. The frame rate limit is lifted meanwhile.
    /// Without the argument it does nothing.
    /// </summary>
    public sealed class PerfSweep : MonoBehaviour
    {
        const float SettleSeconds = 1.2f, MeasureSeconds = 4f;

        readonly StringBuilder _out = new StringBuilder();
        readonly List<float> _frames = new List<float>(2048);
        readonly FrameTiming[] _timing = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !Wanted()) return;
            var go = new GameObject("Perf sweep");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfSweep>();
        }

        static bool Wanted()
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == "-ioriPerfSweep") return true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    string extra = intent.Call<string>("getStringExtra", "iori");
                    return extra != null && extra.Contains("-ioriPerfSweep");
                }
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }

        static WaitForSecondsRealtime Wait(float s) => new WaitForSecondsRealtime(s);

        /// <summary>No limit on the frame rate while measuring (CampQuality.Apply sets the limit again).</summary>
        static void Uncap()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 300;
        }

        void Line(string text)
        {
            _out.AppendLine(text);
            Debug.Log("[IORI] sweep " + text);
        }

        /// <summary>Lets the picture settle, then measures the frames of a few seconds.</summary>
        IEnumerator Measure(string name)
        {
            yield return Wait(SettleSeconds);
            _frames.Clear();
            double gpu = 0, main = 0, render = 0, wait = 0;
            int timed = 0;
            float t = 0f;
            while (t < MeasureSeconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                _frames.Add(dt);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].cpuFrameTime > 0)
                {
                    gpu += _timing[0].gpuFrameTime;
                    main += _timing[0].cpuMainThreadFrameTime;
                    render += _timing[0].cpuRenderThreadFrameTime;
                    wait += _timing[0].cpuMainThreadPresentWaitTime;
                    timed++;
                }
            }
            if (_frames.Count == 0) yield break;
            _frames.Sort();
            float avg = t / _frames.Count * 1000f;
            float p95 = _frames[Mathf.Min(_frames.Count - 1, (int)(_frames.Count * 0.95f))] * 1000f;
            string parts = timed > 0 ? $"gpu_ms={gpu / timed:0.0} main_ms={main / timed:0.0} render_ms={render / timed:0.0} wait_ms={wait / timed:0.0}" : "gpu_ms=? main_ms=? render_ms=? wait_ms=?";
            Line($"{name}: avg_ms={avg:0.0} fps={1000f / avg:0.0} p95_ms={p95:0.0} {parts} frames={_frames.Count}");
        }

        /// <summary>Measures with one thing changed, then puts it back.</summary>
        IEnumerator With(string name, Action change, Action restore)
        {
            change();
            yield return Measure(name);
            restore();
        }

        IEnumerator Start()
        {
            // loading, the first frames, the radio starting
            yield return Wait(8f);
            var q = CampQuality.Instance;
            var atm = CampAtmosphere.Instance;
            var ui = UiRoot.Instance;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cam = Camera.main;
            if (!q || !atm || !ui || urp == null || !cam)
            {
                Line($"not run: quality {(bool)q}, atmosphere {(bool)atm}, ui {(bool)ui}, pipeline {urp != null}, camera {(bool)cam}");
                Finish();
                yield break;
            }
            var camData = cam.GetUniversalAdditionalCameraData();
            int sleep = Screen.sleepTimeout;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Uncap();

            Line($"device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} screen={Screen.width}x{Screen.height} refresh={Screen.currentResolution.refreshRateRatio.value:0} quality={q.Applied}");
            Line($"settings: scale={urp.renderScale:0.00} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} lake mirror scale={(q.lakeMirror ? q.lakeMirror.scale : 0f):0.00}");

            // the same sky for every measurement
            bool followClock = atm.followClock, followWeather = atm.followRadioWeather;
            float hour = atm.previewHour, hoursPerSecond = atm.hoursPerSecond;
            atm.followClock = false; atm.hoursPerSecond = 0f;
            atm.followRadioWeather = false; atm.SetWeatherByHand(CampWeather.Clear);

            atm.previewHour = 12f; atm.Snap();
            yield return Measure("day, home");
            atm.previewHour = 21f; atm.Snap();
            yield return Measure("night, home (as it is now)");

            // ---- one part at a time (night, home view)
            float scale = urp.renderScale;
            int msaa = urp.msaaSampleCount;
            var canvas = ui.GetComponent<Canvas>();
            var lake = q.lakeMirror;
            Line($"flame: {(q.FlameIsCard ? "card (baked flip book)" : "volume")}");

            // everything that is drawn in the scene, by the shader that draws it
            var groups = new SortedDictionary<string, List<Renderer>>();
            var all = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                all.Add(r);
                string shader = r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "(no material)";
                if (!groups.TryGetValue(shader, out var list)) groups[shader] = list = new List<Renderer>();
                list.Add(r);
            }
            // (the baked flame makes itself and hides from such a search)
            var card = q.FlameCardRenderer;
            if (card && card.gameObject.activeInHierarchy) { all.Add(card); groups["IORI/Camp/FireCard"] = new List<Renderer> { card }; }
            Action<List<Renderer>, bool> show = (list, on) => { foreach (var r in list) if (r) r.enabled = on; };

            var clear = cam.clearFlags;
            var back = cam.backgroundColor;
            int mask = cam.cullingMask;
            var sky = atm.skyMaterial;
            Action plainSky = () => { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f); };
            Action realSky = () => { cam.clearFlags = clear; cam.backgroundColor = back; };
            Action nothing = () => { plainSky(); cam.cullingMask = 0; if (lake) lake.render = false; };
            Action everything = () => { realSky(); cam.cullingMask = mask; if (lake) lake.render = true; };

            // the floor: what a frame costs before anything of the scene is drawn
            yield return With("nothing drawn (plain background, no mirror image)", nothing, everything);
            if (camData)
                yield return With("nothing drawn, no finish", () => { nothing(); camData.renderPostProcessing = false; }, () => { everything(); camData.renderPostProcessing = true; });
            yield return With("nothing drawn, no ui", () => { nothing(); canvas.enabled = false; }, () => { everything(); canvas.enabled = true; });
            yield return With("nothing drawn, render scale 0.4", () => { nothing(); urp.renderScale = 0.4f; }, () => { everything(); urp.renderScale = scale; });

            // the sky (with the mountains, the town and the far shore: all one shader)
            yield return With("sky only (nothing else drawn, no mirror image)", () => { show(all, false); if (lake) lake.render = false; }, () => { show(all, true); if (lake) lake.render = true; });
            yield return With("no sky in the main view (the mirror image keeps it)", plainSky, realSky);
            yield return With("no sky anywhere", () => { atm.skyMaterial = null; RenderSettings.skybox = null; plainSky(); }, () => { atm.skyMaterial = sky; RenderSettings.skybox = sky; realSky(); });

            // each kind of surface
            foreach (var g in groups)
            {
                long tris = 0;
                foreach (var r in g.Value)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (!mesh) continue;
                    for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
                }
                var list = g.Value;
                yield return With($"without {g.Key} ({list.Count} things, {tris} triangles)", () => show(list, false), () => show(list, true));
            }

            if (lake)
            {
                yield return With("no lake mirror", () => lake.render = false, () => lake.render = true);
                yield return With("lake mirror every 4th frame", () => lake.interval = 4, () => lake.interval = 1);
            }
            // the fire, the lantern and the lamp in the tent: every surface works out each of them for every pixel
            var lamps = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.type != LightType.Directional) lamps.Add(l);
            yield return With($"no lamps ({lamps.Count} lights off)", () => { foreach (var l in lamps) l.enabled = false; }, () => { foreach (var l in lamps) l.enabled = true; });
            if (q.keyLight)
            {
                var shadows = q.keyLight.shadows;
                yield return With("no shadows", () => q.keyLight.shadows = LightShadows.None, () => q.keyLight.shadows = shadows);
            }
            yield return With("no msaa", () => urp.msaaSampleCount = 1, () => urp.msaaSampleCount = msaa);
            if (camData)
                yield return With("no finish (bloom, colour)", () => camData.renderPostProcessing = false, () => camData.renderPostProcessing = true);
            yield return With("no ui", () => canvas.enabled = false, () => canvas.enabled = true);
            yield return With("render scale 0.7", () => urp.renderScale = 0.7f, () => urp.renderScale = scale);
            yield return With("render scale 0.6", () => urp.renderScale = 0.6f, () => urp.renderScale = scale);
            yield return With("render scale 0.4", () => urp.renderScale = 0.4f, () => urp.renderScale = scale);

            Action light = () => { if (lake) lake.interval = 4; urp.renderScale = 0.7f; urp.msaaSampleCount = 1; };
            Action heavy = () => { if (lake) lake.interval = 1; urp.renderScale = scale; urp.msaaSampleCount = msaa; };
            yield return With("light set (mirror every 4th, scale 0.7, no msaa)", light, heavy);

            // a smaller screen (the phone enlarges it by itself): everything shrinks with it, the UI and the last copy to the screen too
            int sw = Screen.width, sh = Screen.height;
            int smallW = 720, smallH = Mathf.RoundToInt(720f * sh / Mathf.Max(1, sw));
            Screen.SetResolution(smallW, smallH, Screen.fullScreenMode);
            yield return Wait(1.5f);
            urp.renderScale = 1f;
            yield return Measure($"screen {Screen.width}x{Screen.height}, render scale 1.0");
            urp.renderScale = 0.85f;
            yield return Measure($"screen {Screen.width}x{Screen.height}, render scale 0.85");
            urp.renderScale = scale;
            Screen.SetResolution(sw, sh, Screen.fullScreenMode);
            yield return Wait(1.5f);

            // ---- close to the fire, with its sheet open
            if (ui.Open(CampPropKind.Fire))
            {
                yield return Wait(1.5f);
                yield return Measure("fire sheet");
                yield return With("fire sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
                yield return With("fire sheet, light set", light, heavy);
                ui.Close();
                yield return Wait(1.2f);
            }
            else Line("fire sheet: did not open");

            // ---- the radio's sheet
            if (ui.Open(CampPropKind.Radio))
            {
                yield return Wait(1.5f);
                yield return Measure("radio sheet");
                yield return With("radio sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
                ui.Close();
                yield return Wait(1.2f);
            }
            else Line("radio sheet: did not open");

            // ---- the music player's full screen sheet (the list, or the player if a song is chosen)
            if (ui.Open(CampPropKind.Player))
            {
                yield return Wait(2f);
                yield return Measure("music sheet (full screen)");
                yield return With("music sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
                yield return With("music sheet, light set", light, heavy);
                ui.Close();
                yield return Wait(1.2f);
            }
            else Line("music sheet: did not open");

            // (the phone warms up while this runs: the same measurement as at the start shows by how much it slowed)
            yield return Measure("night, home, again at the end");

            atm.followClock = followClock; atm.followRadioWeather = followWeather;
            atm.previewHour = hour; atm.hoursPerSecond = hoursPerSecond;
            atm.Snap();
            Screen.sleepTimeout = sleep;
            q.Apply();
            Finish();
        }

        void Finish()
        {
            Line("done");
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "perf_sweep.txt"), _out.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[IORI] sweep: " + e.Message); }
            Destroy(gameObject);
        }
    }
}
