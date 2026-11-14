using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>端末に入っている曲 1 つ。</summary>
    public sealed class MusicTrack
    {
        /// <summary>曲を区別する名前。PC ではファイルのパス、Android では content:// の URI。</summary>
        public string Id = "";
        /// <summary>再生係に渡す場所(PC ではファイルのパス、Android では content:// の URI)。</summary>
        public string Uri = "";
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        /// <summary>アルバム全体の演奏者(無ければ空)。アルバムをまとめるときに使う。</summary>
        public string AlbumArtist = "";
        public int Disc;
        public int Number;
        public long DurationMs;
        /// <summary>ジャケットの絵の場所。PC ではファイルの中の位置(ArtOffset, ArtLength)か画像ファイルのパス、Android では絵の URI。</summary>
        public string ArtUri = "";
        public long ArtOffset;
        public int ArtLength;

        /// <summary>同じアルバムの曲が同じ値になる鍵。</summary>
        public string AlbumKey => ((string.IsNullOrEmpty(AlbumArtist) ? Artist : AlbumArtist) + "\u001f" + Album).ToLowerInvariant();
    }

    public sealed class MusicAlbum
    {
        public string Key = "";
        public string Title = "";
        public string Artist = "";
        /// <summary>ディスク番号、曲番号の順。</summary>
        public readonly List<MusicTrack> Tracks = new List<MusicTrack>();
    }

    public sealed class MusicArtist
    {
        public string Name = "";
        /// <summary>アルバムの順、その中は曲番号の順。</summary>
        public readonly List<MusicTrack> Tracks = new List<MusicTrack>();
    }

    /// <summary>
    /// 端末の曲の一覧。曲の集まりを受け取り、画面の並べ方(曲、アルバム、演奏者)ごとの並びを作る。
    /// 並びは文字の番号順で、大文字と小文字は区別しない(どの端末でも同じ並びになるように、言語の設定は使わない)。
    /// </summary>
    public sealed class MusicLibrary
    {
        readonly List<MusicTrack> _songs = new List<MusicTrack>();
        readonly List<MusicAlbum> _albums = new List<MusicAlbum>();
        readonly List<MusicArtist> _artists = new List<MusicArtist>();
        readonly Dictionary<string, MusicTrack> _byId = new Dictionary<string, MusicTrack>();

        public IReadOnlyList<MusicTrack> Songs => _songs;
        public IReadOnlyList<MusicAlbum> Albums => _albums;
        public IReadOnlyList<MusicArtist> Artists => _artists;
        public int Count => _songs.Count;

        public MusicLibrary(IEnumerable<MusicTrack> tracks = null)
        {
            if (tracks != null)
                foreach (var t in tracks)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id) || _byId.ContainsKey(t.Id)) continue;
                    _byId[t.Id] = t;
                    _songs.Add(t);
                }
            _songs.Sort((a, b) => Order(a.Title, b.Title, a.Artist, b.Artist, a.Id, b.Id));

            var albums = new Dictionary<string, MusicAlbum>();
            foreach (var t in _songs)
            {
                if (string.IsNullOrEmpty(t.Album)) continue;
                if (!albums.TryGetValue(t.AlbumKey, out var album))
                {
                    album = new MusicAlbum { Key = t.AlbumKey, Title = t.Album, Artist = string.IsNullOrEmpty(t.AlbumArtist) ? t.Artist : t.AlbumArtist };
                    albums[t.AlbumKey] = album;
                    _albums.Add(album);
                }
                album.Tracks.Add(t);
            }
            foreach (var album in _albums) album.Tracks.Sort(InAlbum);
            _albums.Sort((a, b) => Order(a.Title, b.Title, a.Artist, b.Artist, a.Key, b.Key));

            var artists = new Dictionary<string, MusicArtist>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _songs)
            {
                if (string.IsNullOrEmpty(t.Artist)) continue;
                if (!artists.TryGetValue(t.Artist, out var artist))
                {
                    artist = new MusicArtist { Name = t.Artist };
                    artists[t.Artist] = artist;
                    _artists.Add(artist);
                }
                artist.Tracks.Add(t);
            }
            foreach (var artist in _artists)
                artist.Tracks.Sort((a, b) =>
                {
                    var c = Compare(a.Album, b.Album);
                    return c != 0 ? c : InAlbum(a, b);
                });
            _artists.Sort((a, b) => Compare(a.Name, b.Name));
        }

        public MusicTrack Find(string id) => id != null && _byId.TryGetValue(id, out var t) ? t : null;

        /// <summary>曲名、演奏者、アルバム名のどれかに言葉を含む曲(曲名の順)。空の言葉なら全部。</summary>
        public List<MusicTrack> Search(string words)
        {
            var found = new List<MusicTrack>();
            var q = (words ?? "").Trim();
            foreach (var t in _songs)
                if (q.Length == 0 || Has(t.Title, q) || Has(t.Artist, q) || Has(t.Album, q))
                    found.Add(t);
            return found;
        }

        static bool Has(string text, string q) => !string.IsNullOrEmpty(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        static int Compare(string a, string b) => string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        static int Order(string a1, string b1, string a2, string b2, string a3, string b3)
        {
            var c = Compare(a1, b1);
            if (c != 0) return c;
            c = Compare(a2, b2);
            return c != 0 ? c : string.CompareOrdinal(a3, b3);
        }

        static int InAlbum(MusicTrack a, MusicTrack b)
        {
            if (a.Disc != b.Disc) return a.Disc.CompareTo(b.Disc);
            if (a.Number != b.Number) return a.Number.CompareTo(b.Number);
            var c = Compare(a.Title, b.Title);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>再生時間の表示(3:48、1:02:07)。</summary>
        public static string FormatTime(long ms)
        {
            if (ms < 0) ms = 0;
            long s = ms / 1000;
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }
}
