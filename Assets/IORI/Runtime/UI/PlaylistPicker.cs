using System.Collections.Generic;
using Iori.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Iori.Ui
{
    /// <summary>
    /// The small sheet that rises over the list of songs when a song's row is held: which lists the song
    /// is in. At its top the song (its jacket, title and artist), under it a row with a plus that makes a
    /// new list with the song in it (its name can be typed there and then), then the lists, each with a
    /// check where the song is already in. A press on a list puts the song in or takes it out. A press
    /// above the sheet, or the back key, puts it away.
    /// </summary>
    public sealed class PlaylistPicker : MonoBehaviour
    {
        const float Pad = 26f, HeadHeight = 80f, RowHeight = 62f, TextX = 58f, Column = UiRoot.DesignWidth;

        sealed class Row
        {
            public RectTransform rt;
            public UiJacket jacket;
            public TextMeshProUGUI name;
            public UiShape check;
            public MusicPlaylist list;
        }

        RectTransform _rt, _sheet, _column, _view, _rows;
        CanvasGroup _group;
        UiShape _scrim;
        UiJacket _jacket;
        TextMeshProUGUI _title, _artist;
        ScrollRect _scroll;
        readonly List<Row> _list = new List<Row>();
        TMP_InputField _nameField;
        MusicPlaylist _naming;
        MusicTrack _track;
        float _k, _inset, _sheetHeight;
        bool _shown;
        int _version = -1;

        public bool IsShown => _shown;
        public MusicTrack Track => _track;

        static MusicDirector Music => MusicDirector.Instance;

        public static PlaylistPicker Create(RectTransform parent)
        {
            var th = UiTheme.Current;
            var rt = UiKit.Node("Playlist picker", parent, 0, 0, 0, 0);
            UiKit.Stretch(rt);
            var p = rt.gameObject.AddComponent<PlaylistPicker>();
            p._rt = rt;
            p._group = rt.gameObject.AddComponent<CanvasGroup>();

            // the dimmed list behind: a press there puts the sheet away
            p._scrim = UiKit.Box("Scrim", rt, 0, 0, 0, 0, new Color32(6, 8, 40, 110));
            UiKit.Stretch(p._scrim.rectTransform);
            p._scrim.raycastTarget = true;
            var away = p._scrim.gameObject.AddComponent<UiButton>();
            away.onClick = () => p.Hide();
            away.sound = UiSounds.Close;

            var sheet = UiKit.Node("Sheet", rt, 0, 0, 0, 100);
            sheet.anchorMin = new Vector2(0f, 0f);
            sheet.anchorMax = new Vector2(1f, 0f);
            sheet.pivot = new Vector2(0.5f, 0f);
            p._sheet = sheet;
            // (solid: the rows of the list under it would show through the letters)
            var pane = UiKit.Shape("Pane", sheet, 0, 0, 0, 0,
                ShapeStyle.Fill(new Color32(th.barSolid.r, th.barSolid.g, th.barSolid.b, 255), 0f).TopLight(th.glassTopLight).Glow(th.glassShadow, th.glassShadowBlur, -th.glassShadowRise));
            UiKit.Stretch(pane.rectTransform);
            pane.raycastTarget = true;

            var column = UiKit.Node("Column", sheet, 0, 0, Column, 100);
            column.anchorMin = new Vector2(0.5f, 0f);
            column.anchorMax = new Vector2(0.5f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(Column, 0f);
            column.anchoredPosition = Vector2.zero;
            p._column = column;

            p._jacket = UiJacket.Create("Jacket", column, Pad, 18, 44, MusicArt.Small);
            p._title = UiKit.Text("Title", column, Pad + TextX, 19.6f, Column - Pad * 2 - TextX, 21.7f, "", UiFonts.Medium, 15, th.text);
            p._artist = UiKit.Text("Artist", column, Pad + TextX, 42.3f, Column - Pad * 2 - TextX, 18.1f, "", UiFonts.Regular, 12.5f, th.textSoft);
            UiKit.Clip(p._title);
            UiKit.Clip(p._artist);
            UiKit.Box("Line", column, Pad, HeadHeight - 1, Column - Pad * 2, 1, th.tabLine);

            p._view = UiKit.Node("Lists", column, 0, HeadHeight, Column, 100);
            p._view.gameObject.AddComponent<UiHit>();
            p._view.gameObject.AddComponent<RectMask2D>();
            p._rows = UiKit.Node("Rows", p._view, 0, 0, Column, 100);
            p._rows.anchorMin = new Vector2(0f, 1f);
            p._rows.anchorMax = new Vector2(1f, 1f);
            p._rows.pivot = new Vector2(0.5f, 1f);
            p._rows.anchoredPosition = Vector2.zero;
            p._scroll = p._view.gameObject.AddComponent<ScrollRect>();
            p._scroll.viewport = p._view;
            p._scroll.content = p._rows;
            p._scroll.horizontal = false;
            p._scroll.movementType = ScrollRect.MovementType.Clamped;
            p._scroll.inertia = true;
            p._scroll.decelerationRate = 0.135f;
            p._scroll.scrollSensitivity = 40f;

            rt.gameObject.SetActive(false);
            return p;
        }

        /// <summary>Room to keep free under the sheet (the phone's gesture bar).</summary>
        public void SetInset(float inset) => _inset = Mathf.Max(0f, inset);

        public void Show(MusicTrack track)
        {
            if (track == null || !Music) return;
            _track = track;
            _shown = true;
            _rt.SetAsLastSibling();
            gameObject.SetActive(true);
            _title.text = track.Title;
            _artist.text = track.Artist;
            _jacket.Show(track);
            EndNaming();
            Rebuild();
            _rows.anchoredPosition = Vector2.zero;
        }

        public void Hide()
        {
            if (!_shown) return;
            EndNaming();
            _shown = false;
        }

        void Rebuild()
        {
            var th = UiTheme.Current;
            var music = Music;
            foreach (var r in _list) Destroy(r.rt.gameObject);
            _list.Clear();
            if (!music) return;
            _version = music.Playlists.Version;

            // the plus: a new list with this song in it
            var add = MakeRow(null);
            UiKit.Icon("Plus", add.rt.GetComponent<UiButton>().visual, 12, 21, 20, "plus", th.text);
            foreach (var l in music.Playlists.Lists) MakeRow(l);
            for (int i = 0; i < _list.Count; i++) UiKit.Place(_list[i].rt, Pad, i * RowHeight, Column - Pad * 2, RowHeight);
            _rows.sizeDelta = new Vector2(0f, _list.Count * RowHeight + 12f);
        }

        Row MakeRow(MusicPlaylist list)
        {
            var th = UiTheme.Current;
            var row = new Row { list = list };
            var b = UiKit.Button("Row", _rows, Pad, 0, Column - Pad * 2, RowHeight, () => Press(row), UiSounds.Toggle);
            b.shrink = 0.02f;
            row.rt = (RectTransform)b.transform;
            if (list != null)
            {
                var music = Music;
                var tracks = music.Playlists.Resolve(list, music.Library);
                row.jacket = UiJacket.Create("Jacket", b.visual, 0, 9, 44, MusicArt.Small);
                row.jacket.Show(tracks.Count > 0 ? tracks[0] : null);
                row.name = UiKit.Text("Name", b.visual, TextX, 0, Column - Pad * 2 - TextX - 44, RowHeight, list.Name, UiFonts.Medium, 15, th.text);
                UiKit.Clip(row.name);
                UiKit.PlaceLine(row.name, TextX, 0, Column - Pad * 2 - TextX - 44, RowHeight);
                row.check = UiKit.Icon("Check", b.visual, Column - Pad * 2 - 20, 21, 20, "check", th.text);
                row.check.gameObject.SetActive(music.Playlists.Contains(list, _track));
            }
            _list.Add(row);
            return row;
        }

        void Press(Row row)
        {
            var music = Music;
            if (!music || _track == null) return;
            if (_naming != null)
            {
                // (ending the naming made the rows anew: use the new row of the same list)
                EndNaming();
                foreach (var r in _list) if (r.list == row.list) { row = r; break; }
            }
            if (row.list == null)
            {
                var list = music.Playlists.Create();
                music.Playlists.Add(list, _track);
                music.SavePlaylists();
                Rebuild();
                StartNaming(list);
                return;
            }
            bool inNow = music.Playlists.Toggle(row.list, _track);
            music.SavePlaylists();
            _version = music.Playlists.Version;
            if (row.check) row.check.gameObject.SetActive(inNow);
        }

        /// <summary>The name of a list just made can be typed over its row (the keyboard comes up).</summary>
        void StartNaming(MusicPlaylist list)
        {
            Row row = null;
            foreach (var r in _list) if (r.list == list) row = r;
            if (row == null) return;
            var th = UiTheme.Current;
            _naming = list;
            row.name.gameObject.SetActive(false);
            float w = Column - Pad * 2 - TextX - 44;
            _nameField = UiKit.Field("Name", row.rt, TextX, 9, w, 44, UiFonts.Medium, 15, th.textBright);
            _nameField.text = list.Name;
            _nameField.onEndEdit.AddListener(_ => EndNaming());
            _nameField.ActivateInputField();
        }

        void EndNaming()
        {
            if (_naming == null) return;
            var list = _naming;
            var field = _nameField;
            _naming = null;
            _nameField = null;
            var music = Music;
            if (music && field && music.Playlists.Rename(list, field.text)) music.SavePlaylists();
            if (field) Destroy(field.gameObject);
            if (_shown) Rebuild();
        }

        /// <summary>For the screenshot runner: types a name for the list being named, as the keyboard would.</summary>
        public void TypeName(string name)
        {
            if (_nameField) _nameField.text = name;
        }

        /// <summary>For the screenshot runner: a press on the row of the plus (0) or of a list (1 and on).</summary>
        public void PressRow(int index)
        {
            if (index >= 0 && index < _list.Count) Press(_list[index]);
        }

        void Update()
        {
            var th = UiTheme.Current;
            float target = _shown ? 1f : 0f;
            if (!Mathf.Approximately(_k, target))
                _k = Mathf.MoveTowards(_k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, _shown ? th.openSeconds : th.closeSeconds));
            if (!_shown && _k <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }
            // (the lists changed under it, but not by a press here)
            var music = Music;
            if (_shown && music && music.Playlists.Version != _version && _naming == null) Rebuild();

            // as tall as its lists, but no more than three fifths of the screen
            float room = _rt.rect.height;
            float want = HeadHeight + _rows.sizeDelta.y + _inset;
            _sheetHeight = Mathf.Min(want, Mathf.Max(HeadHeight + RowHeight * 2f, room * 0.6f));
            _sheet.sizeDelta = new Vector2(0f, _sheetHeight);
            float k = _shown ? UiKit.EaseOut(_k) : UiKit.EaseInOut(_k);
            _sheet.anchoredPosition = new Vector2(0f, -(1f - k) * (_sheetHeight + 24f));
            UiKit.Place(_view, 0, HeadHeight, Column, Mathf.Max(0f, _sheetHeight - HeadHeight - _inset));
            _scrim.Style = ShapeStyle.Fill(new Color32(6, 8, 40, (byte)Mathf.RoundToInt(110 * k)), 0f);
            _group.blocksRaycasts = _shown;
        }
    }
}
