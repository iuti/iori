#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// A development aid for desktop builds: when the player is started with "-ioriShots folder" it
    /// walks through the looks by itself, saves pictures of the real running game (with the on-screen
    /// panel, which stills taken in the editor cannot show), measures the frame rate, and quits.
    /// Without that argument it does nothing. Not compiled into phone builds.
    /// </summary>
    public sealed class CampShotRunner : MonoBehaviour
    {
        string _dir;
        readonly StringBuilder _report = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            string dir = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ioriShots") dir = args[i + 1];
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("Camp shot runner");
            DontDestroyOnLoad(go);
            go.AddComponent<CampShotRunner>()._dir = dir;
        }

        static WaitForSecondsRealtime Wait(float s) => new WaitForSecondsRealtime(s);

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_dir, name + ".jpg"), tex.EncodeToJPG(90));
            Destroy(tex);
            yield return null;
        }

        IEnumerator Measure(string label)
        {
            // uncapped for a moment, to see what a frame really costs on this machine
            int oldSync = QualitySettings.vSyncCount, oldRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            yield return Wait(1.0f);
            float t = 0f, worst = 0f; int n = 0;
            while (t < 3f) { yield return null; t += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); n++; }
            _report.AppendLine($"{label}: {n / t:0.0} fps, average {t / n * 1000f:0.00} ms, worst {worst * 1000f:0.0} ms, {Screen.width}x{Screen.height}");
            QualitySettings.vSyncCount = oldSync;
            Application.targetFrameRate = oldRate;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            yield return null;
            var atm = CampAtmosphere.Instance;
            var panel = FindFirstObjectByType<CampDebugPanel>();
            var rig = FindFirstObjectByType<CampCameraRig>();
            var quality = CampQuality.Instance;
            if (!atm || !panel || !rig || !quality)
            {
                File.WriteAllText(Path.Combine(_dir, "play_report.txt"), "the camp scene is not complete");
                Application.Quit(1);
                yield break;
            }
            _report.AppendLine($"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, {SystemInfo.processorType}");

            // the real thing first: following the clock, as it starts for a player
            yield return Wait(2.5f);
            yield return Shot("play_01_as_started");
            panel.open = true;
            yield return Wait(0.5f);
            yield return Shot("play_02_panel_as_started");

            // a chosen look, the panel top and bottom
            atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = 18.6f;
            atm.followRadioWeather = false; atm.SetWeatherByHand(CampWeather.Clear); atm.season = CampSeason.Summer;
            atm.Snap();
            yield return Wait(0.6f);
            yield return Shot("play_03_panel_twilight");
            panel.ScrollTo(1f);
            yield return Wait(0.4f);
            yield return Shot("play_04_panel_bottom");
            panel.ScrollTo(0f);
            panel.open = false;
            yield return Wait(0.5f);
            yield return Shot("play_05_twilight");

            // the fire, moving: six pictures a tenth of a second apart, then two later ones
            foreach (var p in FindObjectsByType<CampProp>(FindObjectsSortMode.None)) if (p.kind == CampPropKind.Fire) rig.focus = p;
            yield return Wait(3f);
            for (int i = 0; i < 6; i++) { yield return Shot("play_1" + i + "_fire"); yield return Wait(0.1f); }
            yield return Wait(1.3f);
            yield return Shot("play_16_fire");
            yield return Wait(2.1f);
            yield return Shot("play_17_fire");
            rig.focus = null;
            yield return Wait(2.5f);

            // rain coming on by itself (the fade is part of what is being checked)
            atm.SetWeatherByHand(CampWeather.Rain);
            yield return Wait(4f);
            yield return Shot("play_20_rain_starting");
            yield return Wait(22f);
            yield return Shot("play_21_rain");
            atm.previewHour = 22f;
            yield return Wait(1.5f);
            yield return Shot("play_22_rain_night");
            atm.SetWeatherByHand(CampWeather.AfterRain);
            yield return Wait(8f);
            yield return Shot("play_23_afterrain_night");
            atm.SetSleep(true);
            yield return Wait(4.5f);
            yield return Shot("play_24_sleep");
            atm.SetSleep(false);

            // what a frame costs, with everything on and at the phone level
            atm.previewHour = 18.6f;
            quality.level = CampQualityLevel.Full;
            yield return Wait(0.5f);
            yield return Measure("full, after rain");
            quality.level = CampQualityLevel.Phone;
            yield return Wait(0.5f);
            yield return Measure("phone level, after rain");
            yield return Shot("play_30_phone_level");
            quality.level = CampQualityLevel.Full;

            // held sideways
            Screen.SetResolution(Screen.height, Screen.width, false);
            yield return Wait(1.5f);
            yield return Shot("play_40_landscape");
            panel.open = true;
            yield return Wait(0.5f);
            yield return Shot("play_41_landscape_panel");

            File.WriteAllText(Path.Combine(_dir, "play_report.txt"), _report.ToString());
            yield return null;
            Application.Quit(0);
        }
    }
}
#endif
