using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>リストに入れた曲 1 つ。曲の名前(Id)と、Id が変わったときに同じ曲を探し直すための鍵を覚える。</summary>
    public sealed class PlaylistEntry
    {
        public string Id = "";
        /// <summary>曲名、演奏者、アルバム名をそろえた形でつないだもの(<see cref="MusicPlaylists.MatchKey"/>)。</summary>
        public string Match = "";
    }

    /// <summary>自分で作る曲の並び 1 つ。</summary>
    public sealed class MusicPlaylist
    {
        /// <summary>リストを区別する名前(作ったときに決まり、変わらない)。</summary>
        public string Id = "";
        public string Name = "";
        public readonly List<PlaylistEntry> Entries = new List<PlaylistEntry>();
    }

    /// <summary>
    /// 自分で作る曲の並び(リスト)の集まり。作る、名前を変える、消す、曲を足す、外す、並べ替える。
    /// 同じ曲は 1 つのリストに 1 度だけ入る。リストの並びは作った順(新しいものが上)。
    /// 曲は Id(PC ではファイルのパス、Android では content:// の URI)で覚え、端末の曲を集め直して Id が変わっていたら、
    /// 曲名、演奏者、アルバム名が同じ曲を代わりに使う。見つからない曲はリストに残したまま、見せるときだけ飛ばす
    /// (SD カードを抜いている間などに消えないように)。
    /// 中身が変わるたびに <see cref="Version"/> が増える。保存は JSON(<see cref="Save"/>、<see cref="Load"/>)。
    /// </summary>
    public sealed class MusicPlaylists
    {
        readonly List<MusicPlaylist> _lists = new List<MusicPlaylist>();
        int _made;

        public IReadOnlyList<MusicPlaylist> Lists => _lists;
        public int Count => _lists.Count;
        public int Version { get; private set; }

        void Touch() => Version++;

        public MusicPlaylist Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var l in _lists) if (l.Id == id) return l;
            return null;
        }

        /// <summary>新しいリストを作っていちばん上に置く。名前が空なら「リスト 1」のように番号で呼ぶ。</summary>
        public MusicPlaylist Create(string name = null)
        {
            var list = new MusicPlaylist { Id = NewId(), Name = Clean(name) };
            if (list.Name.Length == 0) list.Name = DefaultName();
            _lists.Insert(0, list);
            Touch();
            return list;
        }

        /// <summary>名前を変える。空の名前にはしない(false を返す)。</summary>
        public bool Rename(MusicPlaylist list, string name)
        {
            var clean = Clean(name);
            if (list == null || clean.Length == 0 || !_lists.Contains(list)) return false;
            if (list.Name == clean) return true;
            list.Name = clean;
            Touch();
            return true;
        }

        public bool Delete(MusicPlaylist list)
        {
            if (list == null || !_lists.Remove(list)) return false;
            Touch();
            return true;
        }

        /// <summary>曲がリストに入っているか(Id か、名前の鍵で)。</summary>
        public bool Contains(MusicPlaylist list, MusicTrack track) => IndexOf(list, track) >= 0;

        public int IndexOf(MusicPlaylist list, MusicTrack track)
        {
            if (list == null || track == null) return -1;
            for (int i = 0; i < list.Entries.Count; i++)
                if (list.Entries[i].Id == track.Id) return i;
            var key = MatchKey(track);
            for (int i = 0; i < list.Entries.Count; i++)
                if (list.Entries[i].Match == key) return i;
            return -1;
        }

        /// <summary>曲をリストの最後に足す。もう入っていれば何もしない(false)。</summary>
        public bool Add(MusicPlaylist list, MusicTrack track)
        {
            if (list == null || track == null || string.IsNullOrEmpty(track.Id) || !_lists.Contains(list)) return false;
            if (Contains(list, track)) return false;
            list.Entries.Add(new PlaylistEntry { Id = track.Id, Match = MatchKey(track) });
            Touch();
            return true;
        }

        /// <summary>曲をリストから外す。</summary>
        public bool Remove(MusicPlaylist list, MusicTrack track)
        {
            int at = IndexOf(list, track);
            if (at < 0) return false;
            list.Entries.RemoveAt(at);
            Touch();
            return true;
        }

        /// <summary>入っていれば外し、なければ足す。足したら true。</summary>
        public bool Toggle(MusicPlaylist list, MusicTrack track)
        {
            if (Remove(list, track)) return false;
            return Add(list, track);
        }

        /// <summary>
        /// 見えている曲(<see cref="Resolve"/> の並び)の from 番目を to 番目へ動かす。見つからない曲が間にあっても、
        /// 見えている曲どうしの順だけが変わる。
        /// </summary>
        public bool Move(MusicPlaylist list, MusicLibrary library, int from, int to)
        {
            if (list == null || library == null) return false;
            var shown = ShownEntries(list, library);
            if (from < 0 || from >= shown.Count || to < 0 || to >= shown.Count || from == to) return false;
            var moving = list.Entries[shown[from]];
            int target = shown[to];
            list.Entries.RemoveAt(shown[from]);
            // (取り除いた分だけ後ろの位置は 1 つ前にずれる。下へ動かすときは、目当ての曲の後ろに入れる)
            if (shown[from] < target) target--;
            list.Entries.Insert(from < to ? target + 1 : target, moving);
            Touch();
            return true;
        }

        /// <summary>見えている曲の at 番目を外す。</summary>
        public bool RemoveAt(MusicPlaylist list, MusicLibrary library, int at)
        {
            if (list == null || library == null) return false;
            var shown = ShownEntries(list, library);
            if (at < 0 || at >= shown.Count) return false;
            list.Entries.RemoveAt(shown[at]);
            Touch();
            return true;
        }

        /// <summary>リストの曲のうち、いま端末にあるものを並びの順で。</summary>
        public List<MusicTrack> Resolve(MusicPlaylist list, MusicLibrary library)
        {
            var tracks = new List<MusicTrack>();
            if (list == null || library == null) return tracks;
            var byKey = KeyIndex(library);
            foreach (var e in list.Entries)
            {
                var t = Find(e, library, byKey);
                if (t != null) tracks.Add(t);
            }
            return tracks;
        }

        List<int> ShownEntries(MusicPlaylist list, MusicLibrary library)
        {
            var shown = new List<int>();
            var byKey = KeyIndex(library);
            for (int i = 0; i < list.Entries.Count; i++)
                if (Find(list.Entries[i], library, byKey) != null) shown.Add(i);
            return shown;
        }

        static MusicTrack Find(PlaylistEntry e, MusicLibrary library, Dictionary<string, MusicTrack> byKey)
        {
            var t = library.Find(e.Id);
            if (t != null) return t;
            return !string.IsNullOrEmpty(e.Match) && byKey.TryGetValue(e.Match, out t) ? t : null;
        }

        static Dictionary<string, MusicTrack> KeyIndex(MusicLibrary library)
        {
            var d = new Dictionary<string, MusicTrack>();
            foreach (var t in library.Songs)
            {
                var k = MatchKey(t);
                if (!d.ContainsKey(k)) d[k] = t;
            }
            return d;
        }

        /// <summary>曲名、演奏者、アルバム名をそろえた形(<see cref="TextFold"/>)でつないだ鍵。</summary>
        public static string MatchKey(MusicTrack t) =>
            t == null ? "" : TextFold.Fold(t.Title) + "\u001f" + TextFold.Fold(t.Artist) + "\u001f" + TextFold.Fold(t.Album);

        string NewId()
        {
            string id;
            do id = "l" + (++_made).ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (Find(id) != null);
            return id;
        }

        string DefaultName()
        {
            for (int n = 1; ; n++)
            {
                var name = "リスト " + n;
                bool used = false;
                foreach (var l in _lists) if (l.Name == name) used = true;
                if (!used) return name;
            }
        }

        /// <summary>名前の前後の空白と改行を取り、長すぎる名前は切る。</summary>
        static string Clean(string name)
        {
            if (name == null) return "";
            var s = name.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        // ---- 保存

        public string Save()
        {
            var lists = new List<object>();
            foreach (var l in _lists)
            {
                var entries = new List<object>();
                foreach (var e in l.Entries) entries.Add(new Dictionary<string, object> { ["id"] = e.Id, ["match"] = e.Match });
                lists.Add(new Dictionary<string, object> { ["id"] = l.Id, ["name"] = l.Name, ["songs"] = entries });
            }
            return Json.Write(new Dictionary<string, object> { ["version"] = 1, ["lists"] = lists });
        }

        /// <summary>保存したものを読む。読めないときは何も変えずに false。</summary>
        public bool Load(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return false;
            var loaded = new List<MusicPlaylist>();
            try
            {
                var root = JsonRead.Obj(Json.Parse(saved), "リストの保存");
                foreach (var item in JsonRead.Arr(root, "lists"))
                {
                    var d = JsonRead.Obj(item, "リスト");
                    var list = new MusicPlaylist { Id = JsonRead.Str(d, "id", ""), Name = Clean(JsonRead.Str(d, "name", "")) };
                    foreach (var s in JsonRead.Arr(d, "songs"))
                    {
                        var e = JsonRead.Obj(s, "リストの曲");
                        var entry = new PlaylistEntry { Id = JsonRead.Str(e, "id", ""), Match = JsonRead.Str(e, "match", "") };
                        if (entry.Id.Length == 0 && entry.Match.Length == 0) continue;
                        bool twice = false;
                        foreach (var x in list.Entries) if (x.Id == entry.Id && x.Match == entry.Match) twice = true;
                        if (!twice) list.Entries.Add(entry);
                    }
                    loaded.Add(list);
                }
            }
            catch (FormatException)
            {
                return false;
            }
            _lists.Clear();
            foreach (var l in loaded)
            {
                if (l.Id.Length == 0 || Find(l.Id) != null) l.Id = NewId();
                if (l.Name.Length == 0) l.Name = DefaultName();
                _lists.Add(l);
            }
            // (新しく作るリストの名前が、読んだものと重ならないように)
            foreach (var l in _lists)
                if (l.Id.StartsWith("l", StringComparison.Ordinal) && int.TryParse(l.Id.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > _made)
                    _made = n;
            Touch();
            return true;
        }
    }
}
