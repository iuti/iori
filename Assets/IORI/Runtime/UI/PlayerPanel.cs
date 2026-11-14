using Iori.Audio;
using TMPro;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The music player, over the whole screen: the jacket of the song that is on, large, with room
    /// around it; under it the title and the artist, the playing position, and the buttons (shuffle,
    /// previous, stop or play, next, repeat). The arrow at the top left puts the screen away, the
    /// lines at the top right open the list of songs.
    /// </summary>
    public sealed class PlayerPanel : UiPanel
    {
        const float Side = 32f, JacketTop = 76f, JacketSize = 326f, LowerTop = 432f;
        const float NamesHeight = 60f, SeekHeight = 43f, ButtonsHeight = 64f;

        public override bool FullScreen => true;
        public override float Height => Room;

        UiJacket _jacket;
        TextMeshProUGUI _title, _artist, _now, _total;
        RectTransform _seekBlock, _buttons;
        UiSlider _seek;
        UiButton _play, _shuffle, _repeat;
        UiShape _shuffleMark, _repeatMark;
        Texture _pauseIcon, _playIcon, _repeatIcon, _repeatOneIcon;
        int _version = -1;
        long _shownNow = -1, _shownTotal = -1;
        bool _lit = true;

        protected override ShapeStyle Pane()
        {
            var th = UiTheme.Current;
            return ShapeStyle.Gradient(th.playerTop, th.playerBottom, 0f).Glass();
        }

        static MusicDirector Music => MusicDirector.Instance;

        protected override void Build()
        {
            var th = UiTheme.Current;
            float w = Width - Side * 2f;

            UiKit.IconButton("Close", Content, 8, 4, 44, "chevron_down", 20, th.text, () => UiRoot.Instance.Pop(), UiSounds.Close);
            UiKit.IconButton("List", Content, Width - 8 - 44, 4, 44, "list", 20, th.text, () => UiRoot.Instance.ShowLibrary(), UiSounds.Open);

            _jacket = UiJacket.Create("Jacket", Content, Side, JacketTop, JacketSize, MusicArt.Large, true, true);

            _title = UiKit.Text("Title", Content, Side, LowerTop, w, 34, "", UiFonts.Mincho, 26, th.textBright, TextAlignmentOptions.Left, 0.02f);
            _artist = UiKit.Text("Artist", Content, Side, LowerTop + 40, w, 20, "", UiFonts.Regular, 14, th.textSoft);
            UiKit.Clip(_title);
            UiKit.Clip(_artist);

            // the playing position: a line 26 high in the look proposal, with the two times under it
            _seekBlock = UiKit.Node("Seek", Content, Side, 0, w, SeekHeight);
            _seek = UiSlider.Create("Position", _seekBlock, 0, -9, w, 14f);
            _seek.SetFillColor(th.amber);
            _seek.onReleased = v => { if (Music) Music.Seek(v); };
            _now = UiKit.Text("Now", _seekBlock, 0, 26, 80, 17, "0:00", UiFonts.Regular, 11.5f, th.textSoft);
            _total = UiKit.Text("Total", _seekBlock, w - 80, 26, 80, 17, "0:00", UiFonts.Regular, 11.5f, th.textSoft, TextAlignmentOptions.Right);

            // the buttons, spread over the width (press areas 44, 52, 64, 52, 44 with 23 between)
            _buttons = UiKit.Node("Buttons", Content, 21, 0, 348, ButtonsHeight);
            _shuffle = UiKit.IconButton("Shuffle", _buttons, 0, 10, 44, "shuffle", 22, th.textSoft,
                () => { if (Music) Music.SetShuffle(!Music.Queue.Shuffle); }, UiSounds.Toggle);
            UiKit.IconButton("Previous", _buttons, 67, 6, 52, "prev", 32, th.text, () => { if (Music) Music.Previous(); });
            _play = UiKit.IconButton("Play", _buttons, 142, 0, 64, "pause", 46, th.text, () => { if (Music) Music.TogglePlay(); }, UiSounds.Toggle);
            UiKit.IconButton("Next", _buttons, 229, 6, 52, "next", 32, th.text, () => { if (Music) Music.Next(); });
            _repeat = UiKit.IconButton("Repeat", _buttons, 304, 10, 44, "repeat", 22, th.textSoft,
                () => { if (Music) Music.CycleRepeat(); }, UiSounds.Toggle);
            // a switched-on button is brighter and underlined, as the chosen tab of the list is
            _shuffleMark = UiKit.Box("On", _shuffle.visual, 17, 38, 10, 2, th.text);
            _repeatMark = UiKit.Box("On", _repeat.visual, 17, 38, 10, 2, th.text);

            _pauseIcon = _play.icon.Picture;
            _playIcon = UiKit.LoadIcon("play");
            _repeatIcon = _repeat.icon.Picture;
            _repeatOneIcon = UiKit.LoadIcon("repeat_one");
        }

        /// <summary>The names stay under the jacket and the buttons near the bottom edge; the position line sits halfway between them.</summary>
        protected override void Layout()
        {
            float bottom = Height - Mathf.Max(30f, 50f - BottomInset);
            float gap = Mathf.Max(8f, (bottom - LowerTop - NamesHeight - SeekHeight - ButtonsHeight) * 0.5f);
            UiKit.Place(_seekBlock, Side, LowerTop + NamesHeight + gap, Width - Side * 2f, SeekHeight);
            UiKit.Place(_buttons, 21, bottom - ButtonsHeight, 348, ButtonsHeight);
        }

        protected override void OnOpened() => _version = -1;

        protected override void Refresh()
        {
            var music = Music;
            if (!music) return;
            var th = UiTheme.Current;

            // the position follows the song, except while a finger holds it
            long total = music.DurationMs, now = music.PositionMs;
            if (_seek.IsHeld) now = (long)(_seek.Value * total);
            else _seek.SetValue(total > 0 ? Mathf.Clamp01((float)now / total) : 0f);
            if (now / 1000 != _shownNow)
            {
                _shownNow = now / 1000;
                _now.text = MusicLibrary.FormatTime(now);
            }
            if (total / 1000 != _shownTotal)
            {
                _shownTotal = total / 1000;
                _total.text = MusicLibrary.FormatTime(total);
            }

            if (music.Version == _version) return;
            _version = music.Version;

            var track = music.Current;
            _title.text = track != null ? track.Title : "";
            _artist.text = track != null ? track.Artist : "";
            _jacket.Show(track);

            bool playing = music.IsPlaying;
            _play.icon.Picture = playing ? _pauseIcon : _playIcon;
            // (amber says "this sounds now": the position is pale while the song is stopped)
            if (playing != _lit)
            {
                _lit = playing;
                _seek.SetFillColor(playing ? th.amber : th.text);
            }

            bool shuffle = music.Queue.Shuffle;
            UiKit.Tint(_shuffle.icon, shuffle ? th.textBright : th.textSoft);
            _shuffleMark.enabled = shuffle;
            var repeat = music.Queue.Repeat;
            _repeat.icon.Picture = repeat == RepeatMode.One && _repeatOneIcon ? _repeatOneIcon : _repeatIcon;
            UiKit.Tint(_repeat.icon, repeat != RepeatMode.Off ? th.textBright : th.textSoft);
            _repeatMark.enabled = repeat != RepeatMode.Off;
        }
    }
}
