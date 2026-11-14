using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// IORI の設定の鍵と決まりの一覧。音量はすべてここ(<see cref="SettingsStore"/> の <c>volume.*</c>)にあり、
    /// ラジオのシート、焚き火のシート、プレーヤー、後で作る設定の画面が同じ鍵を読み書きする。
    /// 新しい設定(声のオンオフ、話す頻度、会話の設定など)もここに鍵を足し、<see cref="Basics"/> に決まりを足す。
    /// </summary>
    public static class IoriSettings
    {
        public const string VolumeGroup = "volume";

        /// <summary>ラジオの曲の音量。</summary>
        public const string RadioVolume = "volume.radio";
        /// <summary>音楽プレーヤー(端末の曲)の音量。ラジオの曲とは別。</summary>
        public const string PlayerVolume = "volume.player";
        /// <summary>イオリの場面の声の音量(時報は予定表の単発音なので掛からない)。</summary>
        public const string VoiceVolume = "volume.voice";
        /// <summary>画面の小さな音(押した、開いた)の音量。</summary>
        public const string UiVolume = "volume.ui";
        /// <summary>環境音の音量の鍵の頭。後ろに環境音の id(fire、wind、insects、rain)が付く。</summary>
        public const string AmbientPrefix = "volume.ambient.";

        public const double UiVolumeDefault = 0.55;

        public static string AmbientVolume(string id) => AmbientPrefix + id;

        /// <summary>鍵が環境音の音量なら、その id。違えば null。</summary>
        public static string AmbientIdOf(string key) =>
            key != null && key.StartsWith(AmbientPrefix, StringComparison.Ordinal) && key.Length > AmbientPrefix.Length
                ? key.Substring(AmbientPrefix.Length) : null;

        /// <summary>起動してすぐ決められる設定。</summary>
        public static IEnumerable<SettingDef> Basics()
        {
            yield return SettingDef.Number(RadioVolume, 1, VolumeGroup);
            yield return SettingDef.Number(PlayerVolume, 1, VolumeGroup);
            yield return SettingDef.Number(VoiceVolume, 1, VolumeGroup);
            yield return SettingDef.Number(UiVolume, UiVolumeDefault, VolumeGroup);
        }

        /// <summary>環境音の音量の決まり。既定の音量は番組表(radio.json の ambient の defaultVolume)から。</summary>
        public static IEnumerable<SettingDef> Ambient(IEnumerable<AmbientDef> layers)
        {
            foreach (var l in layers) yield return SettingDef.Number(AmbientVolume(l.Id), l.DefaultVolume, VolumeGroup);
        }

        /// <summary>
        /// 1 か所にまとめる前の保存(PlayerPrefs の iori.radio.musicVolume、iori.music.volume、iori.radio.mixer)を、設定に移す。
        /// 無かったものは null。プレーヤーの音量が無ければ、それまで一緒だったラジオの曲の音量から始める(前と同じ)。
        /// 環境音の「消音」は画面から入れる所が無かったので移さない。移したものがあれば true。
        /// </summary>
        public static bool ImportLegacy(SettingsStore store, double? radioMusic, double? playerVolume, string mixerJson)
        {
            bool any = false;
            if (radioMusic.HasValue) { store.SetNumber(RadioVolume, radioMusic.Value); any = true; }
            if (playerVolume.HasValue) { store.SetNumber(PlayerVolume, playerVolume.Value); any = true; }
            else if (radioMusic.HasValue) store.SetNumber(PlayerVolume, radioMusic.Value);
            if (!string.IsNullOrEmpty(mixerJson))
            {
                try
                {
                    var root = JsonRead.Obj(Json.Parse(mixerJson), "mixer");
                    if (root.TryGetValue("layers", out var lo) && lo is Dictionary<string, object> layers)
                        foreach (var kv in layers)
                        {
                            if (string.IsNullOrEmpty(kv.Key) || !(kv.Value is Dictionary<string, object> o)) continue;
                            if (!(o.TryGetValue("volume", out var v) && v is double d)) continue;
                            store.SetNumber(AmbientVolume(kv.Key), d < 0 ? 0 : d > 1 ? 1 : d);
                            any = true;
                        }
                }
                catch (FormatException) { } // 壊れた保存は移さない
            }
            return any;
        }
    }
}
