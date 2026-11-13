using System;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// 段階 1 の動作確認用パネル(景色がないシーン用の仮 UI)。
    /// 天気と静けさモードの切り替え、時計を進めて時間帯の切り替わりを聞く、音量の調整ができる。
    /// キャンプ地のシーンでは景色のパネル(CampDebugPanel)の「音」タブが同じ操作を持つので、こちらは出さない。
    /// </summary>
    public sealed class RadioDebugPanel : MonoBehaviour
    {
        /// <summary>ほかのパネルが同じ操作を持っている間は true。二つのパネルが重ならないよう、こちらは描かない。</summary>
        public static bool HostedElsewhere;

        bool _open;
        Vector2 _scroll;

        void OnGUI()
        {
            if (HostedElsewhere) return;
            var director = RadioDirector.Instance;
            if (director == null) return;

            // 画面の大きさに合わせて拡大する(スマホで押せる大きさにする)。
            var scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var w = Screen.width / scale;
            var h = Screen.height / scale;

            if (!_open)
            {
                if (GUI.Button(new Rect(8, 8, 120, 36), "IORI ▶")) _open = true;
                return;
            }

            GUILayout.BeginArea(new Rect(8, 8, Mathf.Min(w - 16, 460), h - 16), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.BeginHorizontal();
            GUILayout.Label("IORI ラジオ(動作確認)");
            if (GUILayout.Button("閉じる", GUILayout.Width(80))) _open = false;
            GUILayout.EndHorizontal();

            if (director.LoadError != null)
            {
                GUILayout.Label("読み込みエラー: " + director.LoadError);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            if (director.Plan == null)
            {
                GUILayout.Label("読み込み中…");
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            var now = director.VirtualNow;
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
            var span = director.Plan.SpanAt(director.Plan.ToPlanTime(now.ToUnixTimeMilliseconds()));
            GUILayout.Label($"時刻 {local:HH:mm:ss}" + (director.ClockOffsetMs != 0 ? $"(ずらし {director.ClockOffsetMs / 60000.0:+0;-0} 分)" : ""));
            GUILayout.Label($"時間帯 {span?.Phase ?? "-"} / {(span?.Mode == RadioMode.Quiet ? "静けさ" : "テーマ曲")} / 天気 {director.Weather}");
            GUILayout.Label("再生係: " + director.Backend.Name);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(director.Weather == WeatherIds.Clear, "晴れ", "Button") && director.Weather != WeatherIds.Clear)
                director.SetWeather(WeatherIds.Clear);
            if (GUILayout.Toggle(director.Weather == WeatherIds.Rain, "雨", "Button") && director.Weather != WeatherIds.Rain)
                director.SetWeather(WeatherIds.Rain);
            var quiet = GUILayout.Toggle(director.Quiet, "静けさの時間", "Button");
            if (quiet != director.Quiet) director.SetQuiet(quiet);
            GUILayout.EndHorizontal();

            GUILayout.Label("時計を動かす(切り替わりを聞く用)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("次の時間帯の15秒前")) director.JumpBeforeNextPhase(15);
            if (GUILayout.Button("次の正時の5秒前")) director.JumpBeforeNextHour(5);
            if (GUILayout.Button("戻す")) director.SetClockOffset(0);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            var music = Slider("曲", director.MusicVolume);
            if (Math.Abs(music - director.MusicVolume) > 0.001f) director.SetMusicVolume(music);
            foreach (var layer in director.Mixer.Layers)
            {
                var v = director.Mixer.GetUserVolume(layer.Id);
                var label = layer.Id + (layer.IsActiveIn(director.Weather) ? "" : "(この天気では鳴らない)");
                var nv = Slider(label, v);
                if (Math.Abs(nv - v) > 0.001f) director.SetAmbientVolume(layer.Id, nv);
            }

            GUILayout.Space(6);
            GUILayout.Label(director.Backend.Status);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static float Slider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170));
            var v = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.MinWidth(120));
            GUILayout.Label(Mathf.RoundToInt(v * 100).ToString(), GUILayout.Width(36));
            GUILayout.EndHorizontal();
            return v;
        }
    }
}
