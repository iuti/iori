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
    /// the app runs by itself for about three minutes: it switches the costly parts of the picture off
    /// one at a time (the lake's mirror image, the flame, the finer resolution, the blur behind the
    /// glass and so on), measures the frame time of each, and writes one line per measurement to the log
    /// ("[IORI] sweep ...") and all of them to perf_sweep.txt in the app's data folder. The frame rate
    /// limit is lifted meanwhile. Without the argument it does nothing.
    /// </summary>
    public sealed class PerfSweep : MonoBehaviour
    {
        const float SettleSeconds = 1.5f, MeasureSeconds = 5f;

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
            double gpu = 0, cpu = 0;
            int gpuCount = 0, cpuCount = 0;
            float t = 0f;
            while (t < MeasureSeconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                _frames.Add(dt);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    if (_timing[0].gpuFrameTime > 0) { gpu += _timing[0].gpuFrameTime; gpuCount++; }
                    if (_timing[0].cpuFrameTime > 0) { cpu += _timing[0].cpuFrameTime; cpuCount++; }
                }
            }
            if (_frames.Count == 0) yield break;
            _frames.Sort();
            float avg = t / _frames.Count * 1000f;
            float p95 = _frames[Mathf.Min(_frames.Count - 1, (int)(_frames.Count * 0.95f))] * 1000f;
            string g = gpuCount > 0 ? (gpu / gpuCount).ToString("0.0") : "?";
            string c = cpuCount > 0 ? (cpu / cpuCount).ToString("0.0") : "?";
            Line($"{name}: avg_ms={avg:0.0} fps={1000f / avg:0.0} p95_ms={p95:0.0} gpu_ms={g} cpu_ms={c} frames={_frames.Count}");
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
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 300;

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
            bool hdr = urp.supportsHDR;
            var lake = q.lakeMirror;
            var flame = q.flameMirror && q.flameMirror.gameObject.activeSelf ? q.flameMirror : q.flame;
            var grass = q.denseGrass != null && q.denseGrass.Length > 0 ? q.denseGrass[0] : null;

            if (lake)
            {
                yield return With("no lake mirror", () => lake.render = false, () => lake.render = true);
                yield return With("lake mirror every 2nd frame", () => lake.interval = 2, () => lake.interval = 1);
                yield return With("lake mirror every 4th frame", () => lake.interval = 4, () => lake.interval = 1);
            }
            if (flame)
                yield return With("no flame", () => flame.gameObject.SetActive(false), () => flame.gameObject.SetActive(true));
            yield return With("render scale 0.7", () => urp.renderScale = 0.7f, () => urp.renderScale = scale);
            yield return With("render scale 0.6", () => urp.renderScale = 0.6f, () => urp.renderScale = scale);
            yield return With("no msaa", () => urp.msaaSampleCount = 1, () => urp.msaaSampleCount = msaa);
            if (camData)
                yield return With("no finish (bloom, colour)", () => camData.renderPostProcessing = false, () => camData.renderPostProcessing = true);
            yield return With("no hdr", () => urp.supportsHDR = false, () => urp.supportsHDR = hdr);
            if (q.keyLight)
            {
                var shadows = q.keyLight.shadows;
                yield return With("no shadows", () => q.keyLight.shadows = LightShadows.None, () => q.keyLight.shadows = shadows);
            }
            if (grass)
                yield return With("no grass", () => grass.enabled = false, () => grass.enabled = true);

            Action light = () => { if (lake) lake.interval = 4; urp.renderScale = 0.7f; urp.msaaSampleCount = 1; };
            Action heavy = () => { if (lake) lake.interval = 1; urp.renderScale = scale; urp.msaaSampleCount = msaa; };
            yield return With("light set (mirror every 4th, scale 0.7, no msaa)", light, heavy);
            if (flame)
                yield return With("light set, no flame", () => { light(); flame.gameObject.SetActive(false); }, () => { heavy(); flame.gameObject.SetActive(true); });

            // ---- close to the fire, with its sheet open
            if (ui.Open(CampPropKind.Fire))
            {
                yield return Wait(1.5f);
                yield return Measure("fire sheet");
                yield return With("fire sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
                if (flame)
                {
                    yield return With("fire sheet, no flame", () => flame.gameObject.SetActive(false), () => flame.gameObject.SetActive(true));
                    yield return With("fire sheet, light set, no flame, no blur",
                        () => { light(); flame.gameObject.SetActive(false); UiBackdrop.Off = true; },
                        () => { heavy(); flame.gameObject.SetActive(true); UiBackdrop.Off = false; });
                }
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
