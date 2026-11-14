using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// Android の端末の曲の一覧を読む。一覧は MusicStore.java が MediaStore から集めて JSON の配列にしたもので、
    /// 各要素は uri, title, artist, album, albumArtist, disc, number, durationMs, albumId。形を変えたら両方直す。
    /// </summary>
    public static class MediaStoreJson
    {
        /// <summary>読めない要素は飛ばす。全体が読めなければ空の一覧。</summary>
        public static List<MusicTrack> Parse(string json)
        {
            var tracks = new List<MusicTrack>();
            object root;
            try { root = Json.Parse(json ?? ""); }
            catch (FormatException) { return tracks; }
            if (!(root is List<object> list)) return tracks;
            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                try
                {
                    string uri = JsonRead.Str(d, "uri", "");
                    if (string.IsNullOrEmpty(uri)) continue;
                    long albumId = (long)JsonRead.Num(d, "albumId", 0);
                    tracks.Add(new MusicTrack
                    {
                        // (content:// の URI が、曲を区別する名前でもある)
                        Id = uri,
                        Uri = uri,
                        Title = JsonRead.Str(d, "title", ""),
                        Artist = JsonRead.Str(d, "artist", ""),
                        Album = JsonRead.Str(d, "album", ""),
                        AlbumArtist = JsonRead.Str(d, "albumArtist", ""),
                        Disc = (int)JsonRead.Num(d, "disc", 0),
                        Number = (int)JsonRead.Num(d, "number", 0),
                        DurationMs = (long)JsonRead.Num(d, "durationMs", 0),
                        // (ジャケットはアルバムの番号で引く)
                        ArtUri = albumId > 0 ? albumId.ToString() : "",
                    });
                }
                catch (FormatException) { }
            }
            return tracks;
        }
    }
}
