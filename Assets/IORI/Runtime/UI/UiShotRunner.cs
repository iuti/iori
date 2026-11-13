#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using Iori.Audio;
using Iori.Camp;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// A development aid for desktop builds: started with "-ioriUiShots folder", the player opens
    /// each panel by itself, saves pictures of the real running UI (to compare with the look
    /// proposal), writes a short report and quits. Without that argument it does nothing.
    /// Not compiled into phone builds.
    /// </summary>
    public sealed class UiShotRunner : MonoBehaviour
    {
        string _dir;
        readonly StringBuilder _report = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            string dir = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ioriUiShots") dir = args[i + 1];
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("UI shot runner");
            DontDestroyOnLoad(go);
            go.AddComponent<UiShotRunner>()._dir = dir;
        }

        static WaitForSecondsRealtime Wait(float s) => new WaitForSecondsRealtime(s);

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            yield return null;
        }

        void Finish(int code)
        {
            File.WriteAllText(Path.Combine(_dir, "ui_report.txt"), _report.ToString());
            Application.Quit(code);
        }

        static void SetRadioClock(RadioDirector radio, int hour, int minute)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            var want = new DateTimeOffset(local.Year, local.Month, local.Day, hour, minute, 0, local.Offset);
            radio.SetClockOffset((long)(want - local).TotalMilliseconds);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_dir);
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning) _report.AppendLine("[" + type + "] " + msg);
            };
            yield return null;
            yield return null;
            var atm = CampAtmosphere.Instance;
            var ui = UiRoot.Instance;
            var debug = FindFirstObjectByType<CampDebugPanel>();
            if (!atm || !ui)
            {
                _report.AppendLine("the camp scene or the UI is missing (atmosphere " + (bool)atm + ", ui " + (bool)ui + ")");
                Finish(1);
                yield break;
            }
            // wait for the radio to read its programme
            float waited = 0f;
            while ((!RadioDirector.Instance || RadioDirector.Instance.Program == null) && waited < 15f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }
            var radio = RadioDirector.Instance;
            if (!radio || radio.Program == null)
            {
                _report.AppendLine("the radio did not load: " + (radio ? radio.LoadError : "no director"));
                Finish(1);
                yield break;
            }
            _report.AppendLine($"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, {Screen.width}x{Screen.height}, colour space {QualitySettings.activeColorSpace}");
            _report.AppendLine($"fonts: medium {(UiFonts.Medium ? UiFonts.Medium.name : "none")}, regular {(UiFonts.Regular ? UiFonts.Regular.name : "none")}, bold {(UiFonts.Bold ? UiFonts.Bold.name : "none")}, mincho {(UiFonts.Mincho ? UiFonts.Mincho.name : "none")}");

            bool wasQuiet = radio.Quiet;
            string wasWeather = radio.Weather;
            radio.SetQuiet(false);
            radio.SetWeather(WeatherIds.Clear);

            // the look of the proposal: a clear summer evening, the radio's clock at 17:20
            atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = 18.6f;
            atm.followRadioWeather = false; atm.SetWeatherByHand(CampWeather.Clear); atm.season = CampSeason.Summer;
            atm.Snap();
            SetRadioClock(radio, 17, 20);
            if (debug) debug.hidden = true;
            yield return Wait(2.5f);
            yield return Shot("ui_01_idle");

            _report.AppendLine("open radio: " + ui.Open(CampPropKind.Radio));
            yield return Wait(0.12f);
            yield return Shot("ui_02_radio_opening");
            yield return Wait(3.2f);
            yield return Shot("ui_03_radio");
            _report.AppendLine($"backdrop on {Shader.GetGlobalFloat("_IoriUiBackdropOn")}, canvas scale {ui.GetComponent<Canvas>().scaleFactor:0.000}");

            // stopped: the radio keeps the quiet hours
            radio.SetQuiet(true);
            yield return Wait(0.9f);
            yield return Shot("ui_04_radio_quiet");
            radio.SetQuiet(false);
            SetRadioClock(radio, 6, 5);
            yield return Wait(0.9f);
            yield return Shot("ui_05_radio_early_morning");
            SetRadioClock(radio, 17, 20);
            ui.Close();
            yield return Wait(2.5f);

            _report.AppendLine("open fire: " + ui.Open(CampPropKind.Fire));
            yield return Wait(3.2f);
            yield return Shot("ui_06_fire");
            // (only the radio's weather: the row of the rain wakes up, the scene stays clear)
            radio.SetWeather(WeatherIds.Rain);
            yield return Wait(0.7f);
            yield return Shot("ui_07_fire_rain");
            radio.SetWeather(WeatherIds.Clear);
            ui.Close();
            yield return Wait(2.5f);

            // night: the glass over a dark scene
            atm.previewHour = 22f;
            SetRadioClock(radio, 22, 10);
            yield return Wait(1.5f);
            ui.Open(CampPropKind.Radio);
            yield return Wait(3.2f);
            yield return Shot("ui_08_radio_night");
            ui.Close();
            yield return Wait(0.5f);

            radio.SetClockOffset(0);
            radio.SetWeather(wasWeather);
            radio.SetQuiet(wasQuiet);
            yield return null;
            Finish(0);
        }
    }
}
#endif
