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
    /// The music player is shown with made-up songs written to a temporary folder (silent files with
    /// names and jackets), never with the songs of the computer it runs on, and the queue the user
    /// was listening to is left alone. With "-ioriUiShotJackets folder" the jackets are taken from
    /// that folder (jacket_1.jpg to jacket_6.jpg; they are in Docs/ui-shots/sample-jackets).
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
            // (the same picture every time: with "-ioriQuality phone" the render scale does not fit itself to this machine)
            Iori.Camp.CampQuality.AdaptPaused = true;
            // before the music player looks for songs: give it the made-up ones, and keep it from saving anything
            MusicDirector.Persist = false;
            SettingsDirector.Persist = false; // (the volumes moved for the pictures are not written to the settings file)
            string jackets = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ioriUiShotJackets") jackets = args[i + 1];
            try
            {
                UnityMusicBackend.FolderOverride = MakeSampleMusic(jackets);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IORI] 見本の曲を作れませんでした: " + e.Message);
            }
            var go = new GameObject("UI shot runner");
            DontDestroyOnLoad(go);
            go.AddComponent<UiShotRunner>()._dir = dir;
        }

        static WaitForSecondsRealtime Wait(float s) => new WaitForSecondsRealtime(s);

        // ---- made-up songs for the pictures of the music player

        /// <summary>(title, artist, album, number in the album, seconds). The albums are numbered in the order they first appear.</summary>
        static readonly (string title, string artist, string album, int number, int seconds)[] SampleSongs =
        {
            ("遠まわりの帰り道", "みなと通りトリオ", "港の見える坂", 1, 228),
            ("山の向こうの灯り", "みなと通りトリオ", "山あいの夜", 1, 252),
            ("朝のコーヒーは二杯", "小舟ラジオ楽団", "小舟ラジオ", 1, 177),
            ("雨やどりのワルツ", "小舟ラジオ楽団", "小舟ラジオ", 2, 211),
            ("引き潮のサンバ", "かなた ゆづき", "潮目", 1, 304),
            ("夜行バスの窓", "かなた ゆづき", "潮目", 2, 266),
            ("縁側と扇風機", "はこにわ", "夏の箱庭", 1, 195),
            ("星を数える係", "はこにわ", "夏の箱庭", 2, 362),
            ("最終便のあとで", "北の停車場", "終着駅", 1, 288),
            ("灯台までの地図", "小舟ラジオ楽団", "小舟ラジオ", 3, 220),
            ("川べりの自転車", "みなと通りトリオ", "山あいの夜", 2, 202),
            // (one with names too long for a row, to see them cut short)
            ("夏の終わりに港で聴いた古いレコードのこと", "みなと通りトリオとゆかいな仲間たちの小さなオーケストラ", "港の見える坂", 2, 245),
        };
        const string SampleFirst = "遠まわりの帰り道", SampleAlbum = "小舟ラジオ";

        static string MakeSampleMusic(string jackets)
        {
            string root = Path.Combine(Application.temporaryCachePath, "ui-shot-music");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            var albums = new System.Collections.Generic.List<string>();
            foreach (var song in SampleSongs)
            {
                int album = albums.IndexOf(song.album);
                string folder = Path.Combine(root, song.album);
                if (album < 0)
                {
                    album = albums.Count;
                    albums.Add(song.album);
                    Directory.CreateDirectory(folder);
                    string jacket = string.IsNullOrEmpty(jackets) ? null : Path.Combine(jackets, $"jacket_{album + 1}.jpg");
                    if (jacket != null && File.Exists(jacket)) File.Copy(jacket, Path.Combine(folder, "cover.jpg"));
                }
                WriteSilentSong(Path.Combine(folder, $"{song.number:00} {song.title}.wav"), song.title, song.artist, song.album, song.number, song.seconds);
            }
            return root;
        }

        /// <summary>A WAV file of silence of the given length, with its names in a LIST/INFO chunk.</summary>
        static void WriteSilentSong(string path, string title, string artist, string album, int number, int seconds)
        {
            const int rate = 8000;
            var info = new MemoryStream();
            void Tag(string id, string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                int n = bytes.Length + 1;
                info.Write(Encoding.ASCII.GetBytes(id), 0, 4);
                info.Write(BitConverter.GetBytes(n), 0, 4);
                info.Write(bytes, 0, bytes.Length);
                info.WriteByte(0);
                if ((n & 1) == 1) info.WriteByte(0);
            }
            Tag("INAM", title);
            Tag("IART", artist);
            Tag("IPRD", album);
            Tag("ITRK", number.ToString());
            int dataBytes = rate * 2 * seconds;
            int listBytes = 4 + (int)info.Length;
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(file))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(4 + (8 + 16) + (8 + listBytes) + (8 + dataBytes));
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)1);          // one channel
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("LIST"));
                w.Write(listBytes);
                w.Write(Encoding.ASCII.GetBytes("INFO"));
                w.Write(info.ToArray());
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                w.Flush();
                // (the rest of the file is zeros: silence)
                file.SetLength(file.Position + dataBytes);
            }
        }

        static bool OnlySampleSongs(MusicDirector music)
        {
            string root = UnityMusicBackend.FolderOverride;
            if (string.IsNullOrEmpty(root) || music.Library.Count != SampleSongs.Length) return false;
            foreach (var t in music.Library.Songs)
                if (!t.Uri.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        IEnumerator WaitForSongs(MusicDirector music)
        {
            float waited = 0f;
            yield return null;
            while ((music.Scanning || !music.Scanned) && waited < 20f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }
        }

        /// <summary>The music player's screens: the list, a song playing, the player, the albums, the home screen with the strip, the radio meanwhile.</summary>
        IEnumerator MusicShots(UiRoot ui, RadioDirector radio)
        {
            var music = MusicDirector.Instance;
            if (!music || music.Backend == null || string.IsNullOrEmpty(UnityMusicBackend.FolderOverride))
            {
                _report.AppendLine("music player: not shown (no player on this device, or the made-up songs could not be written)");
                yield break;
            }
            yield return WaitForSongs(music);
            if (!OnlySampleSongs(music))
            {
                music.Rescan();
                yield return WaitForSongs(music);
            }
            if (!OnlySampleSongs(music))
            {
                _report.AppendLine($"music player: not shown (the made-up songs were not read: {music.Library.Count} songs found)");
                yield break;
            }
            _report.AppendLine($"music: {music.Library.Count} songs, {music.Library.Albums.Count} albums, {music.Library.Artists.Count} artists, {music.Backend.Name}");

            // no song chosen yet: the music player opens its list
            _report.AppendLine("open player: " + ui.Open(CampPropKind.Player));
            yield return Wait(0.14f);
            yield return Shot("ui_09_library_opening");
            yield return Wait(3.2f);
            yield return Shot("ui_10_library");

            int first = 0;
            for (int i = 0; i < music.Library.Songs.Count; i++) if (music.Library.Songs[i].Title == SampleFirst) first = i;
            ui.Library.PressItem(first);
            yield return Wait(1.2f);
            music.Seek(72f / 228f);
            yield return Wait(1.3f);
            yield return Shot("ui_11_library_playing");
            _report.AppendLine($"playing: {(music.Current != null ? music.Current.Title : "none")}, {music.Backend.Status}, player has the floor {music.Active}, radio silent {radio.PlayerOn}");

            ui.ShowPlayer();
            yield return Wait(0.16f);
            yield return Shot("ui_12_player_opening");
            yield return Wait(2.4f);
            yield return Shot("ui_13_player");
            _report.AppendLine("player: " + music.Backend.Status);

            music.Pause();
            music.SetShuffle(true);
            music.CycleRepeat();
            music.CycleRepeat();
            yield return Wait(0.8f);
            yield return Shot("ui_14_player_paused_shuffle_repeat_one");
            music.SetShuffle(false);
            music.CycleRepeat();
            music.Play();
            yield return Wait(0.5f);

            ui.ShowLibrary();
            yield return Wait(1.6f);
            ui.Library.ShowTab(1);
            yield return Wait(1.2f);
            yield return Shot("ui_15_albums");
            int album = 0;
            for (int i = 0; i < music.Library.Albums.Count; i++) if (music.Library.Albums[i].Title == SampleAlbum) album = i;
            ui.Library.PressItem(album);
            yield return Wait(1.2f);
            yield return Shot("ui_16_album");
            ui.Library.ShowTab(2);
            yield return Wait(1.2f);
            yield return Shot("ui_17_artists");
            yield return ListShots(ui, music);
            ui.Library.ShowTab(0);

            // a finger draws the player down: the scene shows above it (not the list under it), and let go it goes back to the home screen
            ui.ShowPlayer();
            yield return Wait(2.4f);
            if (ui.OpenPanel)
            {
                ui.OpenPanel.HoldPulled(260f);
                yield return Wait(0.5f);
                yield return Shot("ui_23_player_pulled");
                ui.OpenPanel.OnEndDrag(null);
                yield return Wait(0.12f);
                yield return Shot("ui_24_player_leaving");
            }
            _report.AppendLine($"player drawn down 260 and let go: closed {!ui.OpenPanel}");
            ui.Close();
            yield return Wait(3.4f);

            // home, with the strip of the song that is on
            yield return Shot("ui_18_home_playing");
            _report.AppendLine("open radio while the player plays: " + ui.Open(CampPropKind.Radio));
            yield return Wait(3.2f);
            yield return Shot("ui_19_radio_while_player");
            ui.Close();
            yield return Wait(1.0f);
            music.Release();
            yield return Wait(1.0f);
            _report.AppendLine($"released: player has the floor {music.Active}, radio silent {radio.PlayerOn}");
        }

        int SongIndex(MusicDirector music, string title)
        {
            for (int i = 0; i < music.Library.Songs.Count; i++) if (music.Library.Songs[i].Title == title) return i;
            return 0;
        }

        /// <summary>The lists the user makes, and the search. (The lists are made up here and kept only while the pictures are taken.)</summary>
        IEnumerator ListShots(UiRoot ui, MusicDirector music)
        {
            var lib = ui.Library;
            lib.ShowTab(LibraryPanel.ListsTab);
            yield return Wait(1.0f);
            yield return Shot("ui_25_lists_empty");

            // a new list: opened to be changed, with its name ready to type
            lib.NewList();
            yield return Wait(0.8f);
            lib.TypeName("焚き火の夜");
            yield return Wait(0.4f);
            yield return Shot("ui_26_list_new");
            var list = music.Playlists.Lists[0];
            foreach (var title in new[] { "星を数える係", "山の向こうの灯り", "最終便のあとで", "雨やどりのワルツ", "夜行バスの窓" })
                music.Playlists.Add(list, music.Library.Songs[SongIndex(music, title)]);
            music.SavePlaylists();
            yield return Wait(0.6f);
            yield return Shot("ui_27_list_editing");
            lib.SetEditing(false);
            yield return Wait(0.8f);
            yield return Shot("ui_28_list");
            _report.AppendLine($"list: {list.Name}, {music.Playlists.Resolve(list, music.Library).Count} songs, items shown {lib.ItemCount}");

            // holding a song's row in the songs tab: the small sheet of lists
            var morning = music.Playlists.Create("朝のコーヒー");
            music.Playlists.Add(morning, music.Library.Songs[SongIndex(music, "朝のコーヒーは二杯")]);
            music.SavePlaylists();
            lib.ShowTab(LibraryPanel.SongsTab);
            yield return Wait(0.6f);
            lib.HoldItem(SongIndex(music, "縁側と扇風機"));
            yield return Wait(0.6f);
            yield return Shot("ui_29_picker");
            lib.Picker.PressRow(2);
            yield return Wait(0.5f);
            yield return Shot("ui_30_picker_added");
            _report.AppendLine($"picker: song in list {music.Playlists.Contains(list, lib.Picker.Track)}");
            lib.Back();
            yield return Wait(0.6f);

            lib.ShowTab(LibraryPanel.ListsTab);
            yield return Wait(0.8f);
            yield return Shot("ui_31_lists");

            // the bin made ready: the songs grow faint
            lib.OpenList(1);
            lib.SetEditing(true);
            yield return Wait(0.5f);
            lib.PressBin();
            yield return Wait(0.3f);
            yield return Shot("ui_32_list_bin_ready");
            lib.SetEditing(false);
            _report.AppendLine($"after the bin was pressed once and the change ended: {music.Playlists.Count} lists");

            // the search: an artist, half-width katakana, and two words in different fields
            lib.StartSearch("みなと");
            yield return Wait(0.8f);
            yield return Shot("ui_33_search");
            _report.AppendLine($"search みなと: {lib.ItemCount} songs");
            lib.StartSearch("ﾜﾙﾂ");
            yield return Wait(0.6f);
            _report.AppendLine($"search ﾜﾙﾂ: {lib.ItemCount} songs");
            lib.StartSearch("はこにわ 星");
            yield return Wait(0.6f);
            yield return Shot("ui_34_search_two_words");
            _report.AppendLine($"search はこにわ 星: {lib.ItemCount} songs");
            lib.EndSearch();
            yield return Wait(0.4f);
        }

        /// <summary>
        /// The volumes live in one settings store: two sliders bound to one key are one bar, and the radio, the
        /// player and the fire follow the store whoever moved it. Written to the report, then put back.
        /// </summary>
        void CheckSettings(RadioDirector radio)
        {
            var store = SettingsDirector.Store;
            var host = new GameObject("Settings check", typeof(RectTransform));
            var a = UiSlider.Create("A", host.transform, 0, 0, 200).Bind(IoriSettings.RadioVolume);
            var b = UiSlider.Create("B", host.transform, 0, 50, 200).Bind(IoriSettings.RadioVolume);
            float wasRadio = store.GetFloat(IoriSettings.RadioVolume), wasFire = store.GetFloat(IoriSettings.AmbientVolume("fire"));
            float wasPlayer = store.GetFloat(IoriSettings.PlayerVolume);
            a.SetValue(0.31f, true);
            bool barsFollow = Mathf.Abs(b.Value - 0.31f) < 1e-4f;
            bool radioFollows = Mathf.Abs(radio.MusicVolume - 0.31f) < 1e-4f && radio.Plan != null && Mathf.Abs(radio.Plan.MusicVolume - 0.31f) < 1e-4f;
            store.SetNumber(IoriSettings.AmbientVolume("fire"), 0.42);
            bool mixerFollows = Mathf.Abs(radio.Mixer.GetUserVolume("fire") - 0.42f) < 1e-4f;
            var music = MusicDirector.Instance;
            store.SetNumber(IoriSettings.PlayerVolume, 0.27);
            bool playerFollows = music && Mathf.Abs(music.Volume - 0.27f) < 1e-4f;
            _report.AppendLine($"settings: two bars on one key follow each other {barsFollow}, radio follows {radioFollows}, fire layer follows {mixerFollows}, player follows {playerFollows}, saving off {!SettingsDirector.Persist}, file {SettingsDirector.FilePath}");
            store.SetNumber(IoriSettings.RadioVolume, wasRadio);
            store.SetNumber(IoriSettings.AmbientVolume("fire"), wasFire);
            store.SetNumber(IoriSettings.PlayerVolume, wasPlayer);
            Destroy(host);
        }

        /// <summary>The sheet to talk to Iori, with made-up words (nothing is sent anywhere).</summary>
        IEnumerator TalkShots(UiRoot ui)
        {
            _report.AppendLine("open talk: " + ui.OpenTalk(null, true));
            yield return Wait(3.2f);
            yield return Shot("ui_35_talk");
            ui.Talk.Stage("この曲どう?", "", false);
            yield return Wait(0.4f);
            yield return Shot("ui_36_talk_typed");
            ui.Talk.Stage("", "", true);
            yield return Wait(0.5f);
            yield return Shot("ui_37_talk_waiting");
            ui.Talk.Stage("", "いいね。夜に合う。", false);
            yield return Wait(0.4f);
            yield return Shot("ui_38_talk_reply");
            ui.Talk.Stage("", "そっか。……座ってなよ、何もしなくていいから。火は私が見てるし、曲もこのままにしておく。", false);
            yield return Wait(0.4f);
            yield return Shot("ui_39_talk_reply_long");
            ui.Close();
            yield return Wait(2.5f);
        }

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
            try
            {
                if (!string.IsNullOrEmpty(UnityMusicBackend.FolderOverride)) Directory.Delete(UnityMusicBackend.FolderOverride, true);
            }
            catch (Exception) { } // (a song may still be held open: the next run clears the folder)
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
            // (the flame's size follows the fire's volume: the pictures are taken with the slider at its first place)
            float wasFire = radio.Mixer.GetUserVolume("fire"), firstFire = wasFire;
            foreach (var layer in radio.Mixer.Layers) if (layer.Id == "fire") firstFire = layer.DefaultVolume;
            radio.SetAmbientVolume("fire", firstFire);

            // the look of the proposal: a clear summer evening, the radio's clock at 17:20
            atm.followClock = false; atm.hoursPerSecond = 0f; atm.previewHour = 18.6f;
            atm.followRadioWeather = false; atm.SetWeatherByHand(CampWeather.Clear); atm.season = CampSeason.Summer;
            atm.Snap();
            SetRadioClock(radio, 17, 20);
            if (debug) debug.hidden = true;
            yield return Wait(2.5f);
            yield return Shot("ui_01_idle");

            // "-ioriUiShotTalkOnly": only the sheet to talk to Iori
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ioriUiShotTalkOnly") >= 0)
            {
                yield return TalkShots(ui);
                radio.SetClockOffset(0);
                radio.SetWeather(wasWeather);
                radio.SetQuiet(wasQuiet);
                radio.SetAmbientVolume("fire", wasFire);
                yield return null;
                Finish(0);
                yield break;
            }

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
            yield return Wait(1.2f);

            // the flame follows the fire's volume: the slider at its left end (the fire is out), then at its right end
            radio.SetAmbientVolume("fire", 0f);
            ui.Open(CampPropKind.Fire);
            yield return Wait(3.2f);
            yield return Shot("ui_20_fire_volume_low");
            ui.Close();
            yield return Wait(1.2f);
            radio.SetAmbientVolume("fire", 1f);
            ui.Open(CampPropKind.Fire);
            yield return Wait(3.2f);
            yield return Shot("ui_21_fire_volume_high");
            radio.SetAmbientVolume("fire", firstFire);

            // a finger draws the sheet down a little and lets go (it comes back up), then further (it goes away)
            ui.OpenPanel.HoldPulled(50f);
            yield return Wait(0.3f);
            yield return Shot("ui_22_fire_pulled");
            ui.OpenPanel.OnEndDrag(null);
            yield return Wait(0.8f);
            _report.AppendLine($"sheet drawn down 50 and let go: still open {(bool)ui.OpenPanel}, back in place {ui.OpenPanel && !ui.OpenPanel.IsPulled}");
            if (ui.OpenPanel)
            {
                ui.OpenPanel.HoldPulled(130f);
                yield return Wait(0.2f);
                ui.OpenPanel.OnEndDrag(null);
            }
            yield return null;
            _report.AppendLine($"sheet drawn down 130 and let go: closed {!ui.OpenPanel}");
            ui.Close();
            yield return Wait(2.5f);

            // night: the glass over a dark scene
            atm.previewHour = 22f;
            atm.Snap();
            SetRadioClock(radio, 22, 10);
            yield return Wait(1.5f);
            ui.Open(CampPropKind.Radio);
            yield return Wait(3.2f);
            yield return Shot("ui_08_radio_night");
            ui.Close();
            yield return Wait(2.5f);

            // the music player, in the evening again
            atm.previewHour = 18.6f;
            atm.Snap();
            SetRadioClock(radio, 17, 20);
            yield return Wait(1.5f);
            yield return MusicShots(ui, radio);
            yield return TalkShots(ui);

            radio.SetClockOffset(0);
            radio.SetWeather(wasWeather);
            radio.SetQuiet(wasQuiet);
            radio.SetAmbientVolume("fire", wasFire);
            CheckSettings(radio);
            yield return null;
            Finish(0);
        }
    }
}
#endif
