using System.Collections.Generic;
using Iori.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Iori.Ui
{
    /// <summary>
    /// The list of the songs on the device, over the whole screen. Three ways to look at them (songs,
    /// albums, artists) as tabs; under the tabs the rows, each a small jacket with the title and the
    /// artist. A press on a song plays it from there on in the order shown; a press on an album or an
    /// artist opens its songs, with a first row that leads back. The button at the top right plays
    /// what is shown in a mixed order. While a song is chosen, the strip at the foot shows it, and a
    /// press on the strip opens the player.
    /// Only the rows in view exist: they are handed from song to song as the list moves.
    /// </summary>
    public sealed class LibraryPanel : UiPanel
    {
        const float Pad = 26f, TabsTop = 56f, ListTop = 101f, FirstRow = 9f, RowHeight = 62f, RowWidth = Width - Pad * 2f;
        const float TextX = 58f, RightColumn = 48f;
        static readonly string[] TabNames = { "曲", "アルバム", "演奏者" };

        enum Kind { Song, Numbered, Album, Artist, Head }

        struct Item
        {
            public Kind kind;
            public MusicTrack track;
            public MusicAlbum album;
            public MusicArtist artist;
            public string title, sub, number;
        }

        sealed class Row
        {
            public RectTransform rt;
            public UiJacket jacket;
            public TextMeshProUGUI number, title, sub, time;
            public UiShape back;
            public UiBars bars;
            public int index = -1, stamp = -1, current = -1;
        }

        public override bool FullScreen => true;
        public override float Height => Room;

        readonly TextMeshProUGUI[] _tabs = new TextMeshProUGUI[TabNames.Length];
        readonly UiButton[] _tabButtons = new UiButton[TabNames.Length];
        UiShape _tabMark;
        RectTransform _view, _rows;
        ScrollRect _scroll;
        MiniBar _bar;
        TextMeshProUGUI _empty, _emptySub;
        readonly List<Row> _pool = new List<Row>();
        readonly List<Item> _items = new List<Item>();
        MusicLibrary _shown;
        MusicAlbum _album;
        MusicArtist _artist;
        int _tab, _stamp;
        float _keptOffset, _viewHeight = -1f, _lastScan = -1000f;
        bool _emptyShown;

        static MusicDirector Music => MusicDirector.Instance;

        public int Tab => _tab;
        public int ItemCount => _items.Count;

        protected override ShapeStyle Pane()
        {
            var th = UiTheme.Current;
            return ShapeStyle.Gradient(th.listTop, th.listBottom, 0f).Glass();
        }

        protected override void Build()
        {
            var th = UiTheme.Current;

            UiKit.IconButton("Close", Content, 8, 4, 44, "chevron_down", 20, th.text, () => UiRoot.Instance.Pop(), UiSounds.Close);
            UiKit.IconButton("Shuffle", Content, Width - 8 - 44, 4, 44, "shuffle", 22, th.text, PlayMixed);

            // the tabs: the chosen one is bold and underlined
            UiKit.Box("Tab line", Content, Pad, TabsTop + 44, RowWidth, 1, th.tabLine);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tab = i;
                _tabButtons[i] = UiKit.Button("Tab " + i, Content, Pad, TabsTop, 44, 44, () => ShowTab(tab));
                _tabs[i] = UiKit.Text("Tab name " + i, Content, Pad, TabsTop, 44, 44, TabNames[i], UiFonts.Medium, 14, th.textSoft);
            }
            _tabMark = UiKit.Box("Tab mark", Content, Pad, TabsTop + 43, 10, 2, th.text);
            PaintTabs();

            // the rows move inside this window; only the ones in view exist
            _view = UiKit.Node("List", Content, 0, ListTop, Width, 100);
            _view.gameObject.AddComponent<UiHit>();
            _view.gameObject.AddComponent<RectMask2D>();
            _rows = UiKit.Node("Rows", _view, 0, 0, Width, 100);
            _rows.anchorMin = new Vector2(0f, 1f);
            _rows.anchorMax = new Vector2(1f, 1f);
            _rows.pivot = new Vector2(0.5f, 1f);
            _rows.anchoredPosition = Vector2.zero;
            _rows.sizeDelta = new Vector2(0f, 100f);
            _scroll = _view.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _view;
            _scroll.content = _rows;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.1f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.135f;
            _scroll.scrollSensitivity = 40f;

            // said only when the device has no songs
            _empty = UiKit.Text("Empty", Content, Pad, 220, RowWidth, 24, "曲が見つかりません", UiFonts.Medium, 15, th.textSoft, TextAlignmentOptions.Center);
            _emptySub = UiKit.Text("Empty folder", Content, Pad, 250, RowWidth, 18, "", UiFonts.Regular, 12, th.textDim, TextAlignmentOptions.Center);
            UiKit.Clip(_emptySub);
            _empty.gameObject.SetActive(false);
            _emptySub.gameObject.SetActive(false);

            _bar = MiniBar.Create(Rect, false, Pad, () => UiRoot.Instance.ShowPlayer());
        }

        protected override void Layout()
        {
            // enough rows to fill the tallest the window gets, and two to spare
            int want = Mathf.CeilToInt((Height + BottomInset - ListTop) / RowHeight) + 2;
            while (_pool.Count < want) _pool.Add(MakeRow());
            // (the rows are dealt out again, since their number changed)
            _stamp++;
        }

        Row MakeRow()
        {
            var th = UiTheme.Current;
            var row = new Row();
            var button = UiKit.Button("Row", _rows, Pad, 0, RowWidth, RowHeight, () => Press(row));
            button.shrink = 0.02f;
            row.rt = (RectTransform)button.transform;
            var v = button.visual;
            row.jacket = UiJacket.Create("Jacket", v, 0, 9, 44, MusicArt.Small);
            row.number = UiKit.Text("Number", v, 0, 9, 44, 44, "", UiFonts.Medium, 13, th.textSoft, TextAlignmentOptions.Center);
            // (the arrow of the first row of an album or artist: the downward arrow, turned to point left)
            row.back = UiKit.Icon("Back", v, 12, 21, 20, "chevron_down", th.text);
            row.back.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            row.title = UiKit.Text("Title", v, TextX, 10.6f, 100, 21.7f, "", UiFonts.Medium, 15, th.text);
            row.sub = UiKit.Text("Sub", v, TextX, 33.3f, 100, 18.1f, "", UiFonts.Regular, 12.5f, th.textSoft);
            UiKit.Clip(row.title);
            UiKit.Clip(row.sub);
            row.time = UiKit.Text("Time", v, RowWidth - 60, 0, 60, RowHeight, "", UiFonts.Regular, 12, th.textSoft, TextAlignmentOptions.Right);
            row.bars = UiBars.Create("Playing", v, RowWidth - UiBars.Width, (RowHeight - UiBars.Height) * 0.5f);
            row.rt.gameObject.SetActive(false);
            return row;
        }

        // ---- what is shown

        /// <summary>The songs a press on a song row plays, in the order shown.</summary>
        IReadOnlyList<MusicTrack> ShownTracks()
        {
            if (_album != null) return _album.Tracks;
            if (_artist != null) return _artist.Tracks;
            return Music ? Music.Library.Songs : null;
        }

        void Rebuild(float offset)
        {
            _items.Clear();
            _stamp++;
            var music = Music;
            var lib = music ? music.Library : null;
            _shown = lib;
            if (lib != null)
            {
                if (_album != null)
                {
                    _items.Add(new Item { kind = Kind.Head, title = _album.Title, sub = _album.Artist });
                    for (int i = 0; i < _album.Tracks.Count; i++)
                    {
                        var t = _album.Tracks[i];
                        _items.Add(new Item { kind = Kind.Numbered, track = t, title = t.Title, sub = t.Artist, number = (t.Number > 0 ? t.Number : i + 1).ToString() });
                    }
                }
                else if (_artist != null)
                {
                    _items.Add(new Item { kind = Kind.Head, title = _artist.Name, sub = "" });
                    foreach (var t in _artist.Tracks) _items.Add(new Item { kind = Kind.Song, track = t, title = t.Title, sub = t.Album });
                }
                else if (_tab == 1)
                    foreach (var a in lib.Albums) _items.Add(new Item { kind = Kind.Album, album = a, track = a.Tracks.Count > 0 ? a.Tracks[0] : null, title = a.Title, sub = a.Artist });
                else if (_tab == 2)
                    foreach (var a in lib.Artists) _items.Add(new Item { kind = Kind.Artist, artist = a, track = a.Tracks.Count > 0 ? a.Tracks[0] : null, title = a.Name, sub = "" });
                else
                    foreach (var t in lib.Songs) _items.Add(new Item { kind = Kind.Song, track = t, title = t.Title, sub = t.Artist });
            }

            float height = FirstRow + _items.Count * RowHeight + 16f + BottomInset;
            _rows.sizeDelta = new Vector2(0f, height);
            _scroll.StopMovement();
            float most = Mathf.Max(0f, height - Mathf.Max(0f, _viewHeight));
            _rows.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset, 0f, most));

            bool empty = music && music.Scanned && !music.Scanning && lib != null && lib.Count == 0;
            if (empty != _emptyShown)
            {
                _emptyShown = empty;
                _empty.gameObject.SetActive(empty);
                _emptySub.gameObject.SetActive(empty);
            }
            if (empty) _emptySub.text = music.Backend is UnityMusicBackend ? UnityMusicBackend.MusicFolderPath : "";
        }

        void PaintTabs()
        {
            var th = UiTheme.Current;
            float x = Pad;
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool on = i == _tab;
                _tabs[i].font = on ? UiFonts.Bold : UiFonts.Medium;
                _tabs[i].color = on ? th.textBright : th.textSoft;
                float w = Mathf.Ceil(_tabs[i].GetPreferredValues(TabNames[i]).x);
                UiKit.Place(_tabs[i].rectTransform, x, TabsTop, w, 44);
                UiKit.Place((RectTransform)_tabButtons[i].transform, x - 14, TabsTop, w + 28, 44);
                if (on) UiKit.Place(_tabMark.rectTransform, x, TabsTop + 43, w, 2);
                x += w + 28f;
            }
        }

        /// <summary>Chooses a way to look at the songs: 0 songs, 1 albums, 2 artists. (Also leaves an opened album or artist.)</summary>
        public void ShowTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TabNames.Length - 1);
            _album = null;
            _artist = null;
            PaintTabs();
            Rebuild(0f);
        }

        /// <summary>What a press on the row of that place in the list does (also used by the screenshot runner).</summary>
        public void PressItem(int index)
        {
            var music = Music;
            if (!music || index < 0 || index >= _items.Count) return;
            var it = _items[index];
            switch (it.kind)
            {
                case Kind.Head:
                    Back();
                    break;
                case Kind.Album:
                    _keptOffset = _rows.anchoredPosition.y;
                    _album = it.album;
                    Rebuild(0f);
                    break;
                case Kind.Artist:
                    _keptOffset = _rows.anchoredPosition.y;
                    _artist = it.artist;
                    Rebuild(0f);
                    break;
                default:
                    var tracks = ShownTracks();
                    int at = _album != null || _artist != null ? index - 1 : index;
                    if (tracks != null && at >= 0 && at < tracks.Count) music.PlayList(tracks, at);
                    break;
            }
        }

        void Press(Row row)
        {
            if (row.index >= 0) PressItem(row.index);
        }

        /// <summary>Plays what is shown (all songs, or the opened album or artist) in a mixed order.</summary>
        void PlayMixed()
        {
            var music = Music;
            var tracks = ShownTracks();
            if (!music || tracks == null || tracks.Count == 0) return;
            music.PlayList(tracks, Random.Range(0, tracks.Count), true);
        }

        /// <summary>The back key leaves an opened album or artist first.</summary>
        public override bool Back()
        {
            if (_album == null && _artist == null) return false;
            _album = null;
            _artist = null;
            Rebuild(_keptOffset);
            return true;
        }

        protected override void OnOpened()
        {
            // songs may have been added since the last look
            var music = Music;
            if (music && Time.realtimeSinceStartup - _lastScan > 30f)
            {
                _lastScan = Time.realtimeSinceStartup;
                if (music.Scanned) music.Rescan();
            }
            Rebuild(_rows.anchoredPosition.y);
        }

        // ---- every frame

        protected override void Refresh()
        {
            var music = Music;
            if (!music) return;

            _bar.SetInset(BottomInset);
            _bar.shown = music.Current != null;
            float viewHeight = Height + BottomInset - ListTop - _bar.Rise;
            if (!Mathf.Approximately(viewHeight, _viewHeight))
            {
                _viewHeight = viewHeight;
                UiKit.Place(_view, 0, ListTop, Width, Mathf.Max(0f, viewHeight));
            }

            // the songs were gathered again: show the new ones (an opened album or artist is found again by its name)
            if (!ReferenceEquals(music.Library, _shown) || (_emptyShown != (music.Scanned && !music.Scanning && music.Library.Count == 0)))
            {
                if (_album != null)
                {
                    MusicAlbum found = null;
                    foreach (var a in music.Library.Albums) if (a.Key == _album.Key) found = a;
                    _album = found;
                }
                if (_artist != null)
                {
                    MusicArtist found = null;
                    foreach (var a in music.Library.Artists) if (string.Equals(a.Name, _artist.Name, System.StringComparison.OrdinalIgnoreCase)) found = a;
                    _artist = found;
                }
                Rebuild(_rows.anchoredPosition.y);
            }

            // deal the rows out to the places in view
            int n = _pool.Count;
            if (n == 0) return;
            var current = music.Current;
            bool playing = music.IsPlaying;
            int first = Mathf.Max(0, Mathf.FloorToInt((_rows.anchoredPosition.y - FirstRow) / RowHeight));
            for (int i = first; i < first + n; i++)
            {
                var row = _pool[i % n];
                if (i >= _items.Count)
                {
                    if (row.index != -1)
                    {
                        row.index = -1;
                        row.rt.gameObject.SetActive(false);
                    }
                    continue;
                }
                if (row.index != i || row.stamp != _stamp) Bind(row, i);
                var it = _items[i];
                if (it.kind != Kind.Song && it.kind != Kind.Numbered) continue;
                // the song that is on shows the moving bars where the others show their length
                int now = current != null && it.track.Id == current.Id ? 1 : 0;
                if (now != row.current)
                {
                    row.current = now;
                    row.bars.gameObject.SetActive(now == 1);
                    row.time.gameObject.SetActive(now == 0);
                }
                if (now == 1) row.bars.live = playing;
            }
        }

        void Bind(Row row, int index)
        {
            var th = UiTheme.Current;
            var it = _items[index];
            row.index = index;
            row.stamp = _stamp;
            row.current = -1;
            row.rt.gameObject.SetActive(true);
            UiKit.Place(row.rt, Pad, FirstRow + index * RowHeight, RowWidth, RowHeight);

            bool song = it.kind == Kind.Song || it.kind == Kind.Numbered;
            bool jacket = it.kind == Kind.Song || it.kind == Kind.Album || it.kind == Kind.Artist;
            row.jacket.gameObject.SetActive(jacket);
            if (jacket) row.jacket.Show(it.track);
            row.number.gameObject.SetActive(it.kind == Kind.Numbered);
            if (it.kind == Kind.Numbered) row.number.text = it.number;
            row.back.gameObject.SetActive(it.kind == Kind.Head);

            float textWidth = RowWidth - TextX - (song ? RightColumn : 0f);
            bool oneLine = string.IsNullOrEmpty(it.sub);
            row.title.font = it.kind == Kind.Head ? UiFonts.Bold : UiFonts.Medium;
            row.title.color = it.kind == Kind.Head ? th.textBright : th.text;
            row.title.text = it.title;
            // (a row with one line only, an artist's name, has it in the middle of its height)
            if (oneLine) UiKit.Place(row.title.rectTransform, TextX, 0, textWidth, RowHeight);
            else UiKit.Place(row.title.rectTransform, TextX, 10.6f, textWidth, 21.7f);
            row.sub.gameObject.SetActive(!oneLine);
            if (!oneLine)
            {
                row.sub.text = it.sub;
                UiKit.Place(row.sub.rectTransform, TextX, 33.3f, textWidth, 18.1f);
            }

            row.time.gameObject.SetActive(song);
            if (song) row.time.text = it.track.DurationMs > 0 ? MusicLibrary.FormatTime(it.track.DurationMs) : "";
            row.bars.gameObject.SetActive(false);
        }
    }
}
