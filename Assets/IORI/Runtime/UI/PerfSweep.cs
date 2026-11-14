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
    /// the app runs by itself for about five minutes: it measures the picture as it is, then with each of
    /// the phone's savings undone (the far view painted every frame, the larger mirror image and so on),
    /// then with the costly parts switched off or drawn plainly one at a time, then with the sheets open.
    /// It writes one line per measurement to the log ("[IORI] sweep ...") and all of them to
    /// perf_sweep.txt in the app's data folder. Read gpu_ms first: the graphics chip's own time for a
    /// frame. avg_ms is the time between frames, which a phone rounds up to the screen's next refresh
    /// (16.7 ms steps). main, render and wait are the scripts' thread, the thread that hands the picture
    /// to the chip, and the time spent waiting for the screen. The frame rate limit is lifted meanwhile.
    /// When it has finished it draws nothing and keeps the screen on for half an hour, so the next
    /// version can be started without unlocking the phone. Without the argument it does nothing.
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
        IEnumerator Measure(string name, bool capped = false)
        {
            // (settings applied anew put the phone's frame rate limit back)
            if (!capped) Uncap();
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
            Line($"{name}: {parts} avg_ms={avg:0.0} fps={1000f / avg:0.0} p95_ms={p95:0.0} frames={_frames.Count}");
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
            var canvas = ui.GetComponent<Canvas>();
            var lake = q.lakeMirror;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            // (every measurement at the same render scale: the scale does not fit itself meanwhile)
            CampQuality.AdaptPaused = true;
            q.Apply();
            Uncap();

            Line($"device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} screen={Screen.width}x{Screen.height} refresh={Screen.currentResolution.refreshRateRatio.value:0} quality={q.Applied}");
            Line($"settings: scale={urp.renderScale:0.00} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} shadow cascades={urp.shadowCascadeCount} lake mirror scale={(lake ? lake.scale : 0f):0.00} every {(lake ? lake.interval : 0)} flame={(q.FlameIsCard ? "card" : "volume")} sky={(q.SkyIsCached ? "from the painted picture" : "painted every frame")}");

            // the same sky for every measurement
            bool followClock = atm.followClock, followWeather = atm.followRadioWeather;
            float hour = atm.previewHour, hoursPerSecond = atm.hoursPerSecond;
            atm.followClock = false; atm.hoursPerSecond = 0f;
            atm.followRadioWeather = false; atm.SetWeatherByHand(CampWeather.Clear);

            atm.previewHour = 12f; atm.Snap();
            yield return Measure("day, home");
            atm.previewHour = 21f; atm.Snap();
            yield return Measure("night, home (as it is now)");

            // ---- each of the phone's savings undone (night, home view)
            bool skyCache = q.skyCacheOnPhone, lightShadows = q.lightShadowsOnPhone, lightBloom = q.lightBloomOnPhone;
            float scale = q.phoneRenderScale, mirrorScale = q.phoneMirrorScale;
            int mirrorEvery = q.phoneMirrorInterval;
            Action asNow = () =>
            {
                q.skyCacheOnPhone = skyCache; q.lightShadowsOnPhone = lightShadows; q.lightBloomOnPhone = lightBloom;
                q.phoneRenderScale = scale; q.phoneMirrorScale = mirrorScale; q.phoneMirrorInterval = mirrorEvery;
                q.Apply();
            };
            yield return With("as before: the far view painted every frame", () => { q.skyCacheOnPhone = false; q.Apply(); }, asNow);
            yield return With("as before: lake mirror 0.33 of the screen, every frame", () => { q.phoneMirrorScale = 0.33f; q.phoneMirrorInterval = 1; q.Apply(); }, asNow);
            yield return With("as before: render scale 0.85", () => { q.phoneRenderScale = 0.85f; q.Apply(); }, asNow);
            yield return With("as before: shadows in two parts, 2048", () => { q.lightShadowsOnPhone = false; q.Apply(); }, asNow);
            yield return With("as before: the glow made from half the picture", () => { q.lightBloomOnPhone = false; q.Apply(); }, asNow);
            yield return With("as before: all five", () =>
            {
                q.skyCacheOnPhone = false; q.lightShadowsOnPhone = false; q.lightBloomOnPhone = false;
                q.phoneRenderScale = 0.85f; q.phoneMirrorScale = 0.33f; q.phoneMirrorInterval = 1;
                q.Apply();
            }, asNow);

            // ---- one part at a time
            // everything that is drawn in the scene, by the shader that draws it
            var groups = new SortedDictionary<string, List<Renderer>>();
            var all = new List<Renderer>();
            var solid = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                all.Add(r);
                string shader = r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "(no material)";
                if (!groups.TryGetValue(shader, out var list)) groups[shader] = list = new List<Renderer>();
                list.Add(r);
                if (shader.EndsWith("/Lit") || shader.EndsWith("/Ground") || shader.EndsWith("/Foliage") || shader.EndsWith("/Water")) solid.Add(r);
            }
            // (the baked flame makes itself and hides from such a search)
            var card = q.FlameCardRenderer;
            if (card && card.gameObject.activeInHierarchy) all.Add(card);
            Action<List<Renderer>, bool> show = (list, on) => { foreach (var r in list) if (r) r.enabled = on; };

            // a thing drawn in its plain vertex color: what is saved is what its real shader costs
            var flatShader = Resources.Load<Shader>("IoriCamp/IoriPerfFlat");
            var flat = flatShader && flatShader.isSupported ? new Material(flatShader) : null;
            var kept = new Dictionary<Renderer, Material[]>();
            Action<List<Renderer>> plain = list =>
            {
                foreach (var r in list)
                {
                    if (!r) continue;
                    var real = r.sharedMaterials;
                    kept[r] = real;
                    var m = new Material[real.Length];
                    for (int i = 0; i < m.Length; i++) m[i] = flat;
                    r.sharedMaterials = m;
                }
            };
            Action<List<Renderer>> real2 = list => { foreach (var r in list) if (r && kept.TryGetValue(r, out var m)) r.sharedMaterials = m; };

            var clear = cam.clearFlags;
            var back = cam.backgroundColor;
            int mask = cam.cullingMask;
            var sky = atm.skyMaterial;
            Action plainSky = () => { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f); };
            Action realSky = () => { cam.clearFlags = clear; cam.backgroundColor = back; };
            Action nothing = () => { plainSky(); cam.cullingMask = 0; if (lake) lake.render = false; };
            Action everything = () => { realSky(); cam.cullingMask = mask; if (lake) lake.render = true; };

            yield return With("nothing drawn (plain background, no mirror image)", nothing, everything);
            yield return With("sky only (nothing else drawn, no mirror image)", () => { show(all, false); if (lake) lake.render = false; }, () => { show(all, true); if (lake) lake.render = true; });
            yield return With("no sky anywhere", () => { atm.skyMaterial = null; RenderSettings.skybox = null; plainSky(); }, () => { atm.skyMaterial = sky; RenderSettings.skybox = sky; realSky(); });
            if (flat)
            {
                yield return With($"all solid things plain ({solid.Count} things: no strokes, light, shadow or air on them)", () => plain(solid), () => real2(solid));
                foreach (var g in groups)
                {
                    if (!(g.Key.EndsWith("/Lit") || g.Key.EndsWith("/Ground") || g.Key.EndsWith("/Water"))) continue;
                    var list = g.Value;
                    yield return With($"{g.Key} plain ({list.Count} things)", () => plain(list), () => real2(list));
                }
            }
            else Line("plain shader missing: the solid things were not measured");
            foreach (var g in groups)
            {
                if (!(g.Key.EndsWith("/Character") || g.Key.EndsWith("/Foliage"))) continue;
                var list = g.Value;
                yield return With($"without {g.Key} ({list.Count} things)", () => show(list, false), () => show(list, true));
            }

            if (lake) yield return With("no lake mirror", () => lake.render = false, () => lake.render = true);
            // the fire, the lantern and the lamp in the tent: every solid thing works out each of them for every pixel
            var lamps = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l.type != LightType.Directional) lamps.Add(l);
            yield return With($"no lamps ({lamps.Count} lights off)", () => { foreach (var l in lamps) l.enabled = false; }, () => { foreach (var l in lamps) l.enabled = true; });
            if (q.keyLight)
            {
                var shadows = q.keyLight.shadows;
                yield return With("no shadows", () => q.keyLight.shadows = LightShadows.None, () => q.keyLight.shadows = shadows);
            }
            int msaa = urp.msaaSampleCount;
            yield return With("no msaa", () => urp.msaaSampleCount = 1, () => urp.msaaSampleCount = msaa);
            if (camData)
                yield return With("no finish (bloom, colour)", () => camData.renderPostProcessing = false, () => camData.renderPostProcessing = true);
            if (canvas) yield return With("no ui", () => canvas.enabled = false, () => canvas.enabled = true);
            yield return With("render scale 0.6", () => { q.phoneRenderScale = 0.6f; q.Apply(); }, asNow);
            yield return With("render scale 0.5", () => { q.phoneRenderScale = 0.5f; q.Apply(); }, asNow);
            yield return With("lake mirror every frame", () => { q.phoneMirrorInterval = 1; q.Apply(); }, asNow);
            yield return With("lake mirror every 3rd frame", () => { q.phoneMirrorInterval = 3; q.Apply(); }, asNow);
            yield return With("lake mirror every 4th frame", () => { q.phoneMirrorInterval = 4; q.Apply(); }, asNow);
            yield return With("no msaa, render scale 0.6", () => { q.phoneRenderScale = 0.6f; q.Apply(); urp.msaaSampleCount = 1; }, () => { asNow(); });

            // ---- close to the fire, with its sheet open
            if (ui.Open(CampPropKind.Fire))
            {
                yield return Wait(1.5f);
                yield return Measure("fire sheet");
                yield return With("fire sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
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
                yield return Measure("music sheet (full screen; the scene behind it drawn small)");
                float covered = q.phoneCoveredScale;
                yield return With("music sheet, the scene behind it at its usual size", () => { q.phoneCoveredScale = 1f; q.Apply(); }, () => { q.phoneCoveredScale = covered; q.Apply(); });
                yield return With("music sheet, no blur", () => UiBackdrop.Off = true, () => UiBackdrop.Off = false);
                yield return With("music sheet, nothing of the scene drawn", nothing, everything);
                ui.Close();
                yield return Wait(1.2f);
            }
            else Line("music sheet: did not open");

            // (the phone warms up while this runs: the same measurement as at the start shows by how much it slowed)
            yield return Measure("night, home, again at the end");

            // ---- as it ships: the 30 fps limit on, the render scale fitting itself to the phone
            CampQuality.AdaptPaused = false;
            asNow();
            yield return Wait(26f);
            yield return Measure("as it ships (30 fps limit, the render scale fits itself), night, home", true);
            Line($"  the render scale settled at {q.Scale:0.00}");
            if (ui.Open(CampPropKind.Fire))
            {
                yield return Wait(22f);
                yield return Measure("as it ships, fire sheet", true);
                Line($"  the render scale settled at {q.Scale:0.00}");
                ui.Close();
                yield return Wait(1.2f);
            }
            if (ui.Open(CampPropKind.Radio))
            {
                yield return Wait(14f);
                yield return Measure("as it ships, radio sheet", true);
                Line($"  the render scale settled at {q.Scale:0.00}");
                ui.Close();
                yield return Wait(1.2f);
            }

            atm.followClock = followClock; atm.followRadioWeather = followWeather;
            atm.previewHour = hour; atm.hoursPerSecond = hoursPerSecond;
            atm.Snap();
            if (flat) Destroy(flat);
            CampQuality.AdaptPaused = true;
            asNow();
            Line("done");
            Write();

            // rest: nothing drawn, a few frames a second, the screen kept on for half an hour
            nothing();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 5;
            yield return Wait(1800f);
            everything();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            q.Apply();
            Destroy(gameObject);
        }

        void Write()
        {
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "perf_sweep.txt"), _out.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[IORI] sweep: " + e.Message); }
        }

        void Finish()
        {
            Line("done");
            Write();
            Destroy(gameObject);
        }
    }
}
