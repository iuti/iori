using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// Small on-screen panel (top right) to try the looks while playing: time of day, weather,
    /// season, sleep mode, brush and light bands. Hidden behind a "景色" button.
    /// </summary>
    public sealed class CampDebugPanel : MonoBehaviour
    {
        public bool open;
        Vector2 _scroll;

        void OnGUI()
        {
            var atm = CampAtmosphere.Instance;
            if (!atm) return;
            float s = Mathf.Max(1f, Screen.dpi / 160f);
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float w = 260f, x = Screen.width / s - w - 8;
            if (GUI.Button(new Rect(Screen.width / s - 70, 8, 62, 28), open ? "閉じる" : "景色")) open = !open;
            if (open)
            {
                GUILayout.BeginArea(new Rect(x, 40, w, Screen.height / s - 48), GUI.skin.box);
                _scroll = GUILayout.BeginScrollView(_scroll);
                GUILayout.Label($"時刻 {atm.CurrentHour:00.0}  雨 {atm.Rain:0.00}  濡れ {atm.Wet:0.00}");
                bool clock = GUILayout.Toggle(atm.followClock, "端末の時計に合わせる");
                if (clock != atm.followClock) atm.followClock = clock;
                if (!atm.followClock) atm.previewHour = GUILayout.HorizontalSlider(atm.previewHour, 0f, 24f);
                GUILayout.BeginHorizontal();
                foreach (var (label, h) in new[] { ("夜明け前", 4.9f), ("朝", 6.4f), ("昼", 12f), ("夕", 17.2f), ("日没後", 18.6f), ("夜", 22f) })
                    if (GUILayout.Button(label)) { atm.followClock = false; atm.previewHour = h; }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                bool follow = GUILayout.Toggle(atm.followRadioWeather, "ラジオの天気に合わせる");
                if (follow != atm.followRadioWeather) atm.followRadioWeather = follow;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("晴れ")) { atm.followRadioWeather = false; atm.weather = CampWeather.Clear; }
                if (GUILayout.Button("雨")) { atm.followRadioWeather = false; atm.weather = CampWeather.Rain; }
                if (GUILayout.Button("雨上がり")) { atm.followRadioWeather = false; atm.weather = CampWeather.AfterRain; }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (CampSeason se in System.Enum.GetValues(typeof(CampSeason)))
                    if (GUILayout.Toggle(atm.season == se, se.ToString(), GUI.skin.button)) atm.season = se;
                GUILayout.EndHorizontal();
                bool sleep = GUILayout.Toggle(atm.sleep, "おやすみモード(ランタンを消す)");
                if (sleep != atm.sleep) atm.SetSleep(sleep);
                GUILayout.Space(6);
                GUILayout.Label($"筆あと {atm.brushStrength:0.00}");
                atm.brushStrength = GUILayout.HorizontalSlider(atm.brushStrength, 0f, 1f);
                GUILayout.Label($"光の段 {atm.lightBands:0}");
                atm.lightBands = Mathf.Round(GUILayout.HorizontalSlider(atm.lightBands, 0f, 12f));
                GUILayout.Label($"明るさ {atm.exposure:0.00}");
                atm.exposure = GUILayout.HorizontalSlider(atm.exposure, 0.5f, 2.5f);
                GUILayout.Label($"FPS {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0}");
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            GUI.matrix = m;
        }
    }
}
