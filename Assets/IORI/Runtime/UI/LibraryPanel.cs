using System.Collections.Generic;
using Iori.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Iori.Ui
{
    /// <summary>
    /// The list of the songs on the device, over the whole screen. Four ways to look at them as tabs:
    /// songs, albums, artists, and the lists the user makes (playlists). Under the tabs the rows, each a
    /// small jacket with the title and the artist. A press on a song plays it from there on in the order
    /// shown; a press on an album, an artist or a list opens its songs, with a first row that leads back.
    /// The button at the top right plays what is shown in a mixed order; the one beside it (the
    /// magnifier) turns the tabs into a line to type in, and the rows into the songs whose title, artist
    /// or album has those words. While a song is chosen, the strip at the foot shows it, and a press on
    /// the strip opens the player.
    ///
    /// Lists: the tab of the lists starts with a row with a plus, which makes a new list and opens it with
    /// its name ready to type. Holding any song's row brings up a small sheet (<see cref="PlaylistPicker"/>)
    /// to put the song in a list or take it out. An opened list has a pencil on its first row: with it the
    /// list is changed in place (its name typed over, a minus on each song to take it out, a grip on each
    /// song to drag it to another place, a bin that throws the list away when pressed twice), and the
    /// check where the pencil was ends that.
    ///
    /// Only the rows in view exist: they are handed from song to song as the list moves.
    /// </summary>
    public sealed class LibraryPanel : UiPanel
    {
        const float Pad = 26f, TabsTop = 56f, ListTop = 101f, FirstRow = 9f, RowHeight = 62f, RowWidth = Width - Pad * 2f;
        const float TextX = 58f, RightColumn = 48f;
        /// <summary>The bin throws the list away when pressed again within this many seconds.</summary>
        const float ArmedSeconds = 3f;
        static readonly string[] TabNames = { "曲", "アルバム", "演奏者", "リスト" };
        public const int SongsTab = 0, AlbumsTab = 1, ArtistsTab = 2, ListsTab = 3;

        enum Kind { Song, Numbered, Album, Artist, Head, NewList, Playlist }

        struct Item
        {
            public Kind kind;
            public MusicTrack track;
            public MusicAlbum album;
            public MusicArtist artist;
            public MusicPlaylist list;
            public string title, sub, number;
        }

        sealed class Row
        {
            public RectTransform rt;
            public UiButton button;
            public CanvasGroup group;
            public UiJacket jacket;
            public TextMeshProUGUI number, title, sub, time;
            public UiShape back, plus;
            public UiBars bars;
            public UiButton remove, edit, trash, done;
            public RectTransform grip;
            public float y;
            public int index = -1, stamp = -1, current = -1;
        }

        public override bool FullScreen => true;
        public override float Height => Room;

        readonly TextMeshProUGUI[] _tabs = new TextMeshProUGUI[TabNames.Length];
        readonly UiButton[] _tabButtons = new UiButton[TabNames.Length];
        UiShape _tabMark;
        RectTransform _view, _rows;
        UiListScroll _scroll;
        MiniBar _bar;
        PlaylistPicker _picker;
        TextMeshProUGUI _empty, _emptySub;
        readonly List<Row> _pool = new List<Row>();
        readonly List<Item> _items = new List<Item>();
        MusicLibrary _shown;
        MusicAlbum _album;
        MusicArtist _artist;
        MusicPlaylist _list;
        List<MusicTrack> _listTracks = new List<MusicTrack>();
        int _tab, _stamp, _shownLists = -1;
        float _keptOffset, _viewHeight = -1f, _lastScan = -1000f;
        bool _emptyShown;

        // the line to type in
        RectTransform _searchBox;
        TMP_InputField _searchField;
        UiButton _searchButton;
        bool _searching;
        string _query = "";
        List<MusicTrack> _results = new List<MusicTrack>();

        // changing a list in place
        bool _editing;
        float _armedUntil = -1f;
        bool _armedShown;
        TMP_InputField _nameField;
        // a row dragged by its grip: from which place, to which place, where it is now (in the rows' units, from their top)
        int _dragFrom = -1, _dragTo = -1;
        float _dragGrab, _dragY;
        Vector2 _dragPos;
        Camera _dragCam;
        Row _ghost;

        static MusicDirector Music => MusicDirector.Instance;

        public int Tab => _tab;
        public int ItemCount => _items.Count;
        public bool Searching => _searching;
        public bool Editing => _editing;
        public PlaylistPicker Picker => _picker;

        protected override ShapeStyle Pane()
        {
            var th = UiTheme.Current;
            return ShapeStyle.Gradient(th.listTop, th.listBottom, 0f).Glass();
        }

        protected override void Build()
        {
            var th = UiTheme.Current;

            UiKit.IconButton("Close", Content, 8, 4, 44, "chevron_down", 20, th.text, () => UiRoot.Instance.Pop(), UiSounds.Close);
            _searchButton = UiKit.IconButton("Search", Content, Width - 8 - 88, 4, 44, "search", 20, th.text, () => StartSearch());
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

            // the line to type in, in the place of the tabs: a magnifier, the words, and a cross that clears them (or, when empty, leaves)
            _searchBox = UiKit.Node("Search", Content, Pad, TabsTop, RowWidth, 44);
            UiKit.Icon("Magnifier", _searchBox, 0, 13, 18, "search", th.textSoft);
            _searchField = UiKit.Field("Words", _searchBox, 30, 0, RowWidth - 30 - 44, 44, UiFonts.Medium, 15, th.textBright);
            _searchField.onValueChanged.AddListener(OnQuery);
            UiKit.IconButton("Clear", _searchBox, RowWidth - 30, 0, 44, "close", 16, th.textSoft, ClearSearch, UiSounds.Close);
            _searchBox.gameObject.SetActive(false);

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
            _scroll = _view.gameObject.AddComponent<UiListScroll>();
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
            _picker = PlaylistPicker.Create(Rect);

            // the row that follows the finger while one is dragged by its grip
            _ghost = MakeRow();
            _ghost.rt.name = "Dragged row";
            _ghost.group.blocksRaycasts = false;
        }

        protected override void Layout()
        {
            // enough rows to fill the tallest the window gets, and two to spare
            int want = Mathf.CeilToInt((Height + BottomInset - ListTop) / RowHeight) + 2;
            while (_pool.Count < want) _pool.Add(MakeRow());
            KeepOnTop();
            // (the rows are dealt out again, since their number changed)
            _stamp++;
        }

        /// <summary>The dragged row and the name being typed lie over the rows (which are made as the window grows).</summary>
        void KeepOnTop()
        {
            if (_ghost != null) _ghost.rt.SetAsLastSibling();
            if (_nameField) _nameField.transform.SetAsLastSibling();
        }

        Row MakeRow()
        {
            var th = UiTheme.Current;
            var row = new Row();
            var button = UiKit.Button("Row", _rows, Pad, 0, RowWidth, RowHeight, () => Press(row));
            button.shrink = 0.02f;
            row.button = button;
            row.rt = (RectTransform)button.transform;
            row.group = row.rt.gameObject.AddComponent<CanvasGroup>();
            var v = button.visual;
            row.jacket = UiJacket.Create("Jacket", v, 0, 9, 44, MusicArt.Small);
            row.number = UiKit.Text("Number", v, 0, 9, 44, 44, "", UiFonts.Medium, 13, th.textSoft, TextAlignmentOptions.Center);
            // (the arrow of the first row of an album, artist or list: the downward arrow, turned to point left)
            row.back = UiKit.Icon("Back", v, 12, 21, 20, "chevron_down", th.text);
            row.back.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            row.plus = UiKit.Icon("Plus", v, 12, 21, 20, "plus", th.text);
            row.title = UiKit.Text("Title", v, TextX, 10.6f, 100, 21.7f, "", UiFonts.Medium, 15, th.text);
            row.sub = UiKit.Text("Sub", v, TextX, 33.3f, 100, 18.1f, "", UiFonts.Regular, 12.5f, th.textSoft);
            UiKit.Clip(row.title);
            UiKit.Clip(row.sub);
            row.time = UiKit.Text("Time", v, RowWidth - 60, 0, 60, RowHeight, "", UiFonts.Regular, 12, th.textSoft, TextAlignmentOptions.Right);
            row.bars = UiBars.Create("Playing", v, RowWidth - UiBars.Width, (RowHeight - UiBars.Height) * 0.5f);

            // for changing a list: take a song out, drag it by its grip; on the first row the pencil, the bin and the check
            row.remove = UiKit.IconButton("Remove", v, 0, 9, 44, "minus", 18, th.text, () => RemoveSong(row));
            row.grip = UiKit.Node("Grip", v, RowWidth - 44 + 12, 9, 44, 44);
            row.grip.gameObject.AddComponent<UiHit>();
            UiKit.Icon("Icon", row.grip, 12, 12, 20, "grip", th.textSoft);
            var handle = row.grip.gameObject.AddComponent<UiDragHandle>();
            handle.onBegin = e => BeginDrag(row, e);
            handle.onDrag = MoveDrag;
            handle.onEnd = EndDrag;
            row.edit = UiKit.IconButton("Edit", v, RowWidth - 44 + 12, 9, 44, "pencil", 20, th.text, () => SetEditing(true));
            row.done = UiKit.IconButton("Done", v, RowWidth - 44 + 12, 9, 44, "check", 20, th.text, () => SetEditing(false), UiSounds.Close);
            row.trash = UiKit.IconButton("Bin", v, RowWidth - 88 + 12, 9, 44, "trash", 20, th.textSoft, PressBin, UiSounds.Toggle);
            row.rt.gameObject.SetActive(false);
            return row;
        }

        // ---- what is shown

        /// <summary>The songs a press on a song row plays, in the order shown.</summary>
        IReadOnlyList<MusicTrack> ShownTracks()
        {
            if (_searching) return _results;
            if (_album != null) return _album.Tracks;
            if (_artist != null) return _artist.Tracks;
            if (_list != null) return _listTracks;
            return Music ? Music.Library.Songs : null;
        }

        /// <summary>True while an album, an artist or a list is opened (its first row leads back).</summary>
        bool Opened => _album != null || _artist != null || _list != null;

        static string Summary(List<MusicTrack> tracks)
        {
            long ms = 0;
            foreach (var t in tracks) ms += System.Math.Max(0L, t.DurationMs);
            return tracks.Count + "曲" + (ms > 0 ? "  " + MusicLibrary.FormatTime(ms) : "");
        }

        void Rebuild(float offset)
        {
            _items.Clear();
            _stamp++;
            var music = Music;
            var lib = music ? music.Library : null;
            _shown = lib;
            _shownLists = music ? music.Playlists.Version : -1;
            if (lib != null)
            {
                if (_searching)
                {
                    _results = lib.Search(_query);
                    foreach (var t in _results) _items.Add(new Item { kind = Kind.Song, track = t, title = t.Title, sub = t.Artist });
                }
                else if (_album != null)
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
                else if (_list != null)
                {
                    _listTracks = music.Playlists.Resolve(_list, lib);
                    _items.Add(new Item { kind = Kind.Head, list = _list, title = _list.Name, sub = Summary(_listTracks) });
                    for (int i = 0; i < _listTracks.Count; i++)
                    {
                        var t = _listTracks[i];
                        _items.Add(new Item { kind = Kind.Numbered, track = t, title = t.Title, sub = t.Artist, number = (i + 1).ToString() });
                    }
                }
                else if (_tab == AlbumsTab)
                    foreach (var a in lib.Albums) _items.Add(new Item { kind = Kind.Album, album = a, track = a.Tracks.Count > 0 ? a.Tracks[0] : null, title = a.Title, sub = a.Artist });
                else if (_tab == ArtistsTab)
                    foreach (var a in lib.Artists) _items.Add(new Item { kind = Kind.Artist, artist = a, track = a.Tracks.Count > 0 ? a.Tracks[0] : null, title = a.Name, sub = "" });
                else if (_tab == ListsTab)
                {
                    _items.Add(new Item { kind = Kind.NewList, title = "", sub = "" });
                    foreach (var l in music.Playlists.Lists)
                    {
                        var tracks = music.Playlists.Resolve(l, lib);
                        _items.Add(new Item { kind = Kind.Playlist, list = l, track = tracks.Count > 0 ? tracks[0] : null, title = l.Name, sub = Summary(tracks) });
                    }
                }
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
            if (empty) _emptySub.text = music.Backend != null ? music.Backend.EmptyHint : "";
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

        void ShowTabs(bool on)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].gameObject.SetActive(on);
                _tabButtons[i].gameObject.SetActive(on);
            }
            _tabMark.gameObject.SetActive(on);
            _searchBox.gameObject.SetActive(!on);
            _searchButton.gameObject.SetActive(on);
        }

        /// <summary>Chooses a way to look at the songs: 0 songs, 1 albums, 2 artists, 3 lists. (Also leaves an opened album, artist or list, and the search.)</summary>
        public void ShowTab(int tab)
        {
            _picker.Hide();
            EndEditing();
            if (_searching) LeaveSearch();
            _tab = Mathf.Clamp(tab, 0, TabNames.Length - 1);
            _album = null;
            _artist = null;
            _list = null;
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
                    if (_editing) SetEditing(false);
                    else Back();
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
                case Kind.Playlist:
                    _keptOffset = _rows.anchoredPosition.y;
                    _list = it.list;
                    Rebuild(0f);
                    break;
                case Kind.NewList:
                    NewList();
                    break;
                default:
                    if (_editing) break;
                    var tracks = ShownTracks();
                    int at = Opened ? index - 1 : index;
                    if (tracks != null && at >= 0 && at < tracks.Count) music.PlayList(tracks, at);
                    break;
            }
        }

        void Press(Row row)
        {
            if (row.index >= 0) PressItem(row.index);
        }

        /// <summary>Holding a song's row: the sheet to put it in a list (also used by the screenshot runner).</summary>
        public void HoldItem(int index)
        {
            if (_editing || index < 0 || index >= _items.Count) return;
            var it = _items[index];
            if ((it.kind == Kind.Song || it.kind == Kind.Numbered) && it.track != null)
            {
                if (_searchField.isFocused) _searchField.DeactivateInputField();
                _picker.Show(it.track);
            }
        }

        /// <summary>Plays what is shown (all songs, the found ones, or the opened album, artist or list) in a mixed order.</summary>
        void PlayMixed()
        {
            var music = Music;
            var tracks = ShownTracks();
            if (!music || tracks == null || tracks.Count == 0) return;
            music.PlayList(tracks, Random.Range(0, tracks.Count), true);
        }

        /// <summary>
        /// The back key, from the inside out: the small sheet of lists, the changing of a list, an opened album,
        /// artist or list, the search. Only then does the panel go.
        /// </summary>
        public override bool Back()
        {
            if (_picker.IsShown)
            {
                _picker.Hide();
                return true;
            }
            if (_editing)
            {
                SetEditing(false);
                return true;
            }
            if (Opened)
            {
                _album = null;
                _artist = null;
                _list = null;
                Rebuild(_keptOffset);
                return true;
            }
            if (_searching)
            {
                EndSearch();
                return true;
            }
            return false;
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

        // ---- search

        /// <summary>Turns the tabs into the line to type in, with the keyboard up (also used by the screenshot runner, with words).</summary>
        public void StartSearch(string words = null)
        {
            _picker.Hide();
            EndEditing();
            _album = null;
            _artist = null;
            _list = null;
            _searching = true;
            ShowTabs(false);
            _query = words ?? "";
            _searchField.SetTextWithoutNotify(_query);
            if (words == null) _searchField.ActivateInputField();
            Rebuild(0f);
        }

        /// <summary>Back to the tabs.</summary>
        public void EndSearch()
        {
            LeaveSearch();
            Rebuild(0f);
        }

        void LeaveSearch()
        {
            _searching = false;
            _query = "";
            if (_searchField.isFocused) _searchField.DeactivateInputField();
            _searchField.SetTextWithoutNotify("");
            ShowTabs(true);
        }

        void OnQuery(string words)
        {
            if (!_searching || words == _query) return;
            _query = words;
            Rebuild(0f);
        }

        void ClearSearch()
        {
            if (string.IsNullOrEmpty(_searchField.text))
            {
                EndSearch();
                return;
            }
            _searchField.text = "";
            _searchField.ActivateInputField();
        }

        // ---- lists

        /// <summary>Makes a new list and opens it to be changed, with its name ready to type (also used by the screenshot runner).</summary>
        public void NewList()
        {
            var music = Music;
            if (!music) return;
            var list = music.Playlists.Create();
            music.SavePlaylists();
            _keptOffset = 0f;
            _list = list;
            Rebuild(0f);
            SetEditing(true, true);
        }

        /// <summary>Opens the list at that place among the lists (for the screenshot runner).</summary>
        public void OpenList(int index)
        {
            var music = Music;
            if (!music || index < 0 || index >= music.Playlists.Count) return;
            ShowTab(ListsTab);
            _list = music.Playlists.Lists[index];
            Rebuild(0f);
        }

        /// <summary>Starts or ends the changing of the opened list. The name typed so far is kept when it ends.</summary>
        public void SetEditing(bool on, bool typeName = false)
        {
            if (on && _list == null) return;
            if (!on)
            {
                EndEditing();
                Rebuild(_rows.anchoredPosition.y);
                return;
            }
            if (!_editing)
            {
                _editing = true;
                _armedUntil = -1f;
                var th = UiTheme.Current;
                _nameField = UiKit.Field("Name", _rows, Pad + TextX, FirstRow + 9, RowWidth - TextX - 88 - 6, 44, UiFonts.Bold, 15, th.textBright);
                _nameField.text = _list.Name;
                _nameField.onEndEdit.AddListener(_ => KeepName());
                KeepOnTop();
            }
            Rebuild(_rows.anchoredPosition.y);
            if (typeName) _nameField.ActivateInputField();
        }

        /// <summary>For the screenshot runner: types a name over the opened list's, as the keyboard would.</summary>
        public void TypeName(string name)
        {
            if (_nameField) _nameField.text = name;
        }

        void KeepName()
        {
            var music = Music;
            if (!music || _list == null || !_nameField) return;
            if (music.Playlists.Rename(_list, _nameField.text)) music.SavePlaylists();
            // (an empty name is not taken: the old one shows again)
            _nameField.SetTextWithoutNotify(_list.Name);
        }

        void EndEditing()
        {
            if (_dragFrom >= 0) EndDrag(null);
            if (!_editing) return;
            KeepName();
            _editing = false;
            _armedUntil = -1f;
            if (_nameField)
            {
                var field = _nameField;
                _nameField = null;
                Destroy(field.gameObject);
            }
        }

        void RemoveSong(Row row)
        {
            var music = Music;
            if (!music || !_editing || _list == null || row.index < 1) return;
            if (music.Playlists.RemoveAt(_list, music.Library, row.index - 1)) music.SavePlaylists();
            Rebuild(_rows.anchoredPosition.y);
        }

        /// <summary>The bin: the first press makes it ready (the songs of the list grow faint), a second one within three seconds throws the list away. (Also used by the screenshot runner.)</summary>
        public void PressBin()
        {
            var music = Music;
            if (!music || _list == null) return;
            if (Time.unscaledTime < _armedUntil)
            {
                var gone = _list;
                EndEditing();
                _list = null;
                if (music.Playlists.Delete(gone)) music.SavePlaylists();
                Rebuild(_keptOffset);
                return;
            }
            _armedUntil = Time.unscaledTime + ArmedSeconds;
        }

        bool Armed => _editing && Time.unscaledTime < _armedUntil;

        // ---- dragging a song of a list by its grip

        float PointerY(Vector2 screen, Camera cam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rows, screen, cam, out var lp);
            return -lp.y;
        }

        void BeginDrag(Row row, PointerEventData e)
        {
            if (!_editing || e == null || row.index < 1 || row.index >= _items.Count) return;
            _scroll.StopMovement();
            _dragFrom = _dragTo = row.index;
            _dragPos = e.position;
            _dragCam = e.pressEventCamera;
            float y = PointerY(e.position, _dragCam);
            _dragGrab = y - (FirstRow + row.index * RowHeight);
            _dragY = FirstRow + row.index * RowHeight;
            _armedUntil = -1f;
            Bind(_ghost, row.index);
            KeepOnTop();
        }

        void MoveDrag(PointerEventData e)
        {
            if (_dragFrom < 0 || e == null) return;
            _dragPos = e.position;
            _dragCam = e.pressEventCamera;
        }

        void EndDrag(PointerEventData e)
        {
            if (_dragFrom < 0) return;
            int from = _dragFrom, to = _dragTo;
            _dragFrom = _dragTo = -1;
            _ghost.index = -1;
            _ghost.rt.gameObject.SetActive(false);
            var music = Music;
            if (music && _list != null && from != to && music.Playlists.Move(_list, music.Library, from - 1, to - 1))
                music.SavePlaylists();
            Rebuild(_rows.anchoredPosition.y);
        }

        /// <summary>Where the row of that place goes while another is dragged: the ones between make room.</summary>
        int Slot(int i)
        {
            if (_dragFrom < 0 || i == _dragFrom) return i;
            if (_dragFrom < _dragTo && i > _dragFrom && i <= _dragTo) return i - 1;
            if (_dragTo < _dragFrom && i >= _dragTo && i < _dragFrom) return i + 1;
            return i;
        }

        void FollowDrag()
        {
            if (_dragFrom < 0) return;
            // near the top or the bottom of the window, the list moves along
            float offset = _rows.anchoredPosition.y;
            float inView = PointerY(_dragPos, _dragCam) - offset;
            float most = Mathf.Max(0f, _rows.sizeDelta.y - Mathf.Max(0f, _viewHeight));
            float speed = 0f;
            if (inView < 56f) speed = -Mathf.Lerp(80f, 700f, Mathf.InverseLerp(56f, 0f, inView));
            else if (inView > _viewHeight - 56f) speed = Mathf.Lerp(80f, 700f, Mathf.InverseLerp(_viewHeight - 56f, _viewHeight, inView));
            if (speed != 0f)
                _rows.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset + speed * Time.unscaledDeltaTime, 0f, most));

            float y = PointerY(_dragPos, _dragCam);
            _dragY = Mathf.Clamp(y - _dragGrab, FirstRow + RowHeight, FirstRow + (_items.Count - 1) * RowHeight);
            _dragTo = Mathf.Clamp(Mathf.RoundToInt((_dragY - FirstRow) / RowHeight), 1, _items.Count - 1);
            _ghost.rt.gameObject.SetActive(true);
            UiKit.Place(_ghost.rt, Pad, _dragY, RowWidth, RowHeight);
        }

        // ---- every frame

        protected override void Refresh()
        {
            var music = Music;
            if (!music) return;

            _bar.SetInset(BottomInset);
            _bar.shown = music.Current != null;
            _picker.SetInset(BottomInset);
            float viewHeight = Height + BottomInset - ListTop - _bar.Rise;
            if (!Mathf.Approximately(viewHeight, _viewHeight))
            {
                _viewHeight = viewHeight;
                UiKit.Place(_view, 0, ListTop, Width, Mathf.Max(0f, viewHeight));
            }

            // the songs were gathered again, or the lists changed: show the new ones (an opened album, artist or list is found again)
            bool libraryChanged = !ReferenceEquals(music.Library, _shown) || (_emptyShown != (music.Scanned && !music.Scanning && music.Library.Count == 0));
            if ((libraryChanged || music.Playlists.Version != _shownLists) && _dragFrom < 0)
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
                if (_list != null && music.Playlists.Find(_list.Id) == null)
                {
                    EndEditing();
                    _list = null;
                }
                Rebuild(_rows.anchoredPosition.y);
            }

            FollowDrag();

            // deal the rows out to the places in view
            int n = _pool.Count;
            if (n == 0) return;
            var current = music.Current;
            bool playing = music.IsPlaying;
            bool armed = Armed;
            bool fade = armed != _armedShown;
            _armedShown = armed;
            float settle = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f);
            int first = Mathf.Max(0, Mathf.FloorToInt((_rows.anchoredPosition.y - FirstRow) / RowHeight) - 1);
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
                else if (fade) PaintRow(row);
                var it = _items[i];

                // while a row is dragged: it is shown by the one following the finger, and the others make room
                float target = FirstRow + Slot(i) * RowHeight;
                if (!Mathf.Approximately(row.y, target))
                {
                    row.y = Mathf.Abs(row.y - target) < 0.5f ? target : Mathf.Lerp(row.y, target, settle);
                    UiKit.Place(row.rt, Pad, row.y, RowWidth, RowHeight);
                }
                float alpha = i == _dragFrom ? 0f : RowAlpha(it.kind);
                if (!Mathf.Approximately(row.group.alpha, alpha)) row.group.alpha = alpha;

                if (it.kind != Kind.Song && it.kind != Kind.Numbered) continue;
                if (_editing) continue;
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

        /// <summary>The songs of a list grow faint while its bin is ready to throw it away.</summary>
        float RowAlpha(Kind kind) => Armed && kind != Kind.Head ? 0.35f : 1f;

        void PaintRow(Row row)
        {
            var th = UiTheme.Current;
            UiKit.Tint(row.trash.icon, Armed ? th.textBright : th.textSoft);
        }

        void Bind(Row row, int index)
        {
            var th = UiTheme.Current;
            var it = _items[index];
            row.index = index;
            row.stamp = _stamp;
            row.current = -1;
            row.rt.gameObject.SetActive(true);
            row.y = FirstRow + Slot(index) * RowHeight;
            UiKit.Place(row.rt, Pad, row.y, RowWidth, RowHeight);

            bool song = it.kind == Kind.Song || it.kind == Kind.Numbered;
            bool listSong = _editing && it.kind == Kind.Numbered;
            bool listHead = it.kind == Kind.Head && it.list != null;
            bool jacket = it.kind == Kind.Song || it.kind == Kind.Album || it.kind == Kind.Artist || it.kind == Kind.Playlist;
            row.button.onLongPress = song && !_editing ? () => HoldItem(row.index) : (System.Action)null;
            row.jacket.gameObject.SetActive(jacket);
            if (jacket) row.jacket.Show(it.track);
            row.number.gameObject.SetActive(it.kind == Kind.Numbered && !listSong);
            if (it.kind == Kind.Numbered) row.number.text = it.number;
            row.back.gameObject.SetActive(it.kind == Kind.Head);
            row.plus.gameObject.SetActive(it.kind == Kind.NewList);
            row.remove.gameObject.SetActive(listSong);
            row.grip.gameObject.SetActive(listSong);
            row.edit.gameObject.SetActive(listHead && !_editing);
            row.done.gameObject.SetActive(listHead && _editing);
            row.trash.gameObject.SetActive(listHead && _editing);
            PaintRow(row);

            float right = song ? RightColumn : listHead ? (_editing ? 88f : 44f) : 0f;
            float textWidth = RowWidth - TextX - right;
            bool oneLine = string.IsNullOrEmpty(it.sub);
            // (the name of a list being changed is typed over: its own line of text makes way)
            row.title.gameObject.SetActive(it.kind != Kind.NewList && !(listHead && _editing));
            row.title.font = it.kind == Kind.Head ? UiFonts.Bold : UiFonts.Medium;
            row.title.color = it.kind == Kind.Head ? th.textBright : th.text;
            row.title.text = it.title;
            // (a row with one line only, an artist's name, has it in the middle of its height)
            if (oneLine) UiKit.PlaceLine(row.title, TextX, 0, textWidth, RowHeight);
            else UiKit.PlaceLine(row.title, TextX, 10.6f, textWidth, 21.7f);
            // (... and so does the line under it, the number of songs)
            bool subShown = !oneLine && !(listHead && _editing);
            row.sub.gameObject.SetActive(subShown);
            if (subShown)
            {
                row.sub.text = it.sub;
                UiKit.PlaceLine(row.sub, TextX, 33.3f, textWidth, 18.1f);
            }

            row.time.gameObject.SetActive(song && !listSong);
            if (song) row.time.text = it.track.DurationMs > 0 ? MusicLibrary.FormatTime(it.track.DurationMs) : "";
            row.bars.gameObject.SetActive(false);
        }
    }
}
