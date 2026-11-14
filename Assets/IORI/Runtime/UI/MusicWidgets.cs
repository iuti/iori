using System;
using Iori.Audio;
using TMPro;
using UnityEngine;

namespace Iori.Ui
{
    /// <summary>
    /// The square picture of a song's album. Until the picture has been read (or when the song has
    /// none) it shows a plain pane with a record drawn on it in two fine rings.
    /// </summary>
    public sealed class UiJacket : MonoBehaviour
    {
        UiShape _picture;
        GameObject _blank;
        MusicTrack _track;
        int _artSize, _serial;
        bool _keep, _has;

        /// <summary>
        /// artSize is one of <see cref="MusicArt"/>. keep: while the next song's picture is being read the
        /// last one stays (for a jacket that follows the playing song); without it the pane shows meanwhile
        /// (for the rows of a list, which are handed from song to song).
        /// </summary>
        public static UiJacket Create(string name, Transform parent, float x, float y, float size, int artSize, bool shadow = false, bool keep = false)
        {
            var th = UiTheme.Current;
            var rt = UiKit.Node(name, parent, x, y, size, size);
            var j = rt.gameObject.AddComponent<UiJacket>();
            j._artSize = artSize;
            j._keep = keep;
            var clear = new Color32(255, 255, 255, 0);
            if (shadow)
                UiKit.Shape("Shadow", rt, 0, 0, size, size, ShapeStyle.Fill(clear, 0f).Glow(th.jacketShadow, th.jacketShadowBlur, th.jacketShadowDrop));
            var blank = UiKit.Box("Blank", rt, 0, 0, size, size, th.jacketBlank);
            float disc = Mathf.Round(size * 0.44f), hole = Mathf.Max(4f, Mathf.Round(size * 0.09f));
            UiKit.Shape("Disc", blank.transform, (size - disc) * 0.5f, (size - disc) * 0.5f, disc, disc, ShapeStyle.Fill(clear, disc * 0.5f).Ring(0.30f));
            UiKit.Shape("Hole", blank.transform, (size - hole) * 0.5f, (size - hole) * 0.5f, hole, hole, ShapeStyle.Fill(clear, hole * 0.5f).Ring(0.30f));
            j._blank = blank.gameObject;
            j._picture = UiKit.Box("Picture", rt, 0, 0, size, size, new Color32(255, 255, 255, 255));
            j._picture.enabled = false;
            return j;
        }

        public void Show(MusicTrack track)
        {
            if (_serial > 0 && ReferenceEquals(track, _track)) return;
            _track = track;
            Ask();
        }

        void Ask()
        {
            int serial = ++_serial;
            var music = MusicDirector.Instance;
            if (_track == null || !music)
            {
                Set(null);
                return;
            }
            bool answered = false;
            music.LoadArt(_track, _artSize, tex =>
            {
                // (the answer may come much later: by then this jacket may show another song, or be gone)
                if (!this || serial != _serial) return;
                answered = true;
                Set(tex);
            });
            if (!answered && !_keep) Set(null);
        }

        void Set(Texture2D tex)
        {
            _has = tex;
            _picture.Picture = tex;
            _picture.enabled = tex;
            _blank.SetActive(!tex);
        }

        void Update()
        {
            // the picture belongs to the player, which drops the ones asked for longest ago: ask again
            if (_has && !_picture.Picture)
            {
                Set(null);
                Ask();
            }
        }
    }

    /// <summary>
    /// The mark of the song that is on: three thin amber bars that move while it sounds and lie flat,
    /// pale, while it is stopped. 11 wide, 13 high.
    /// </summary>
    public sealed class UiBars : MonoBehaviour
    {
        public const float Width = 11f, Height = 13f;
        const float BarWidth = 2f, Gap = 2.5f;

        /// <summary>True while the song sounds.</summary>
        public bool live;
        readonly UiShape[] _bars = new UiShape[3];
        float _level = -1f;
        bool _flat;

        public static UiBars Create(string name, Transform parent, float x, float y)
        {
            var rt = UiKit.Node(name, parent, x, y, Width, Height);
            var b = rt.gameObject.AddComponent<UiBars>();
            for (int i = 0; i < 3; i++)
                b._bars[i] = UiKit.Box("Bar", rt, i * (BarWidth + Gap), Height - 2f, BarWidth, 2f, UiTheme.Current.amber);
            return b;
        }

        void OnEnable() => _level = -1f;

        void Update()
        {
            float target = live ? 1f : 0f;
            _level = _level < 0f ? target : Mathf.MoveTowards(_level, target, Time.unscaledDeltaTime * 4f);
            if (_level <= 0f && _flat) return;
            _flat = _level <= 0f;
            var th = UiTheme.Current;
            float t = Time.unscaledTime;
            for (int i = 0; i < 3; i++)
            {
                float wave = Mathf.Clamp01(0.5f + 0.34f * Mathf.Sin(t * (5.1f + i * 1.3f) + i * 1.9f) + 0.22f * Mathf.Sin(t * (8.7f - i) + i * 0.7f));
                float h = Mathf.Lerp(2f, 3f + (Height - 3f) * wave, _level);
                UiKit.Place(_bars[i].rectTransform, i * (BarWidth + Gap), Height - h, BarWidth, h);
                _bars[i].Style = ShapeStyle.Fill(Color32.Lerp(th.textDim, th.amber, _level), 0f);
            }
        }
    }

    /// <summary>
    /// The strip along the bottom edge that shows the song that is on: its jacket, title and artist,
    /// a button to stop it and one for the next song, and along its top edge a thin line with the
    /// playing position. A press on it opens the player. It is used on the home screen (as glass over
    /// the scene) and at the foot of the list of songs (as a solid strip). Set <see cref="shown"/> and
    /// it slides in or out by itself.
    /// </summary>
    public sealed class MiniBar : MonoBehaviour
    {
        public const float BarHeight = 76f;

        public bool shown;
        RectTransform _rt, _progress;
        CanvasGroup _group;
        UiShape _progressShape;
        UiJacket _jacket;
        TextMeshProUGUI _title, _artist;
        UiButton _play;
        Texture _pauseIcon, _playIcon;
        float _k, _inset, _fraction = -1f;
        int _version = -1;
        bool _lit;

        /// <summary>True while any of it is on the screen.</summary>
        public bool Visible => _k > 0f;
        /// <summary>How far it stands above the bottom edge now, in UI units.</summary>
        public float Rise => (shown ? UiKit.EaseOut(_k) : UiKit.EaseInOut(_k)) * (BarHeight + _inset);

        /// <summary>left: where the jacket starts in the 390 column (the buttons end 10 nearer to the right edge).</summary>
        public static MiniBar Create(Transform parent, bool glass, float left, Action open)
        {
            var th = UiTheme.Current;
            var rt = UiKit.Node("Mini bar", parent, 0, 0, 0, BarHeight);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, BarHeight);
            rt.anchoredPosition = new Vector2(0f, -BarHeight);
            var bar = rt.gameObject.AddComponent<MiniBar>();
            bar._rt = rt;
            bar._group = rt.gameObject.AddComponent<CanvasGroup>();
            bar._group.alpha = 0f;
            bar._group.blocksRaycasts = false;

            var style = glass ? ShapeStyle.Gradient(th.barTop, th.barBottom, 0f).Glass() : ShapeStyle.Fill(th.barSolid, 0f);
            var pane = UiKit.Shape("Pane", rt, 0, 0, 0, 0, style.Glow(th.barShadow, th.barShadowBlur, -th.barShadowRise));
            UiKit.Stretch(pane.rectTransform);
            pane.raycastTarget = true;
            var press = rt.gameObject.AddComponent<UiButton>();
            press.onClick = open;

            var line = UiKit.Box("Line", rt, 0, 0, 0, 2, th.barLine);
            Along(line.rectTransform, 1f);
            bar._progressShape = UiKit.Box("Position", rt, 0, 0, 0, 2, th.amber);
            bar._progress = bar._progressShape.rectTransform;
            Along(bar._progress, 0f);
            bar._lit = true;

            var column = UiKit.Node("Column", rt, 0, 0, UiRoot.DesignWidth, BarHeight);
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.anchoredPosition = Vector2.zero;

            float right = UiRoot.DesignWidth - (left - 10f);
            float textX = left + 44f + 14f, textW = right - 88f - 14f - textX;
            bar._jacket = UiJacket.Create("Jacket", column, left, 12, 44, MusicArt.Small, false, true);
            bar._title = UiKit.Text("Title", column, textX, 14.3f, textW, 21f, "", UiFonts.Medium, 14.5f, th.text);
            bar._artist = UiKit.Text("Artist", column, textX, 36.3f, textW, 17.4f, "", UiFonts.Regular, 12f, th.textSoft);
            UiKit.Clip(bar._title);
            UiKit.Clip(bar._artist);
            bar._play = UiKit.IconButton("Play", column, right - 88f, 12, 44, "pause", 28, th.text,
                () => { if (MusicDirector.Instance) MusicDirector.Instance.TogglePlay(); }, UiSounds.Toggle);
            bar._pauseIcon = bar._play.icon.Picture;
            bar._playIcon = UiKit.LoadIcon("play");
            UiKit.IconButton("Next", column, right - 44f, 12, 44, "next", 26, th.text,
                () => { if (MusicDirector.Instance) MusicDirector.Instance.Next(); });
            return bar;
        }

        /// <summary>A line 2 high along the top edge, from the left edge to the given share of the width.</summary>
        static void Along(RectTransform rt, float share)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(share, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(0f, -2f);
            rt.offsetMax = new Vector2(0f, 0f);
        }

        /// <summary>Room to keep free under it (the phone's gesture bar); the strip runs through it.</summary>
        public void SetInset(float inset) => _inset = Mathf.Max(0f, inset);

        public bool Contains(Vector2 screenPoint) => RectTransformUtility.RectangleContainsScreenPoint(_rt, screenPoint, null);

        void Update()
        {
            var th = UiTheme.Current;
            float target = shown ? 1f : 0f;
            if (!Mathf.Approximately(_k, target))
                _k = Mathf.MoveTowards(_k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, shown ? th.openSeconds : th.closeSeconds));
            float h = BarHeight + _inset;
            _rt.sizeDelta = new Vector2(0f, h);
            _rt.anchoredPosition = new Vector2(0f, Rise - h);
            _group.alpha = _k > 0f ? 1f : 0f;
            _group.blocksRaycasts = shown;
            if (_k <= 0f) return;

            var music = MusicDirector.Instance;
            if (!music) return;
            long duration = music.DurationMs;
            float fraction = duration > 0 ? Mathf.Clamp01((float)music.PositionMs / duration) : 0f;
            if (Mathf.Abs(fraction - _fraction) > 0.0004f)
            {
                _fraction = fraction;
                _progress.anchorMax = new Vector2(fraction, 1f);
            }

            if (music.Version == _version) return;
            _version = music.Version;
            bool playing = music.IsPlaying;
            _play.icon.Picture = playing ? _pauseIcon : _playIcon;
            // (amber says "this sounds now": the position is pale while the song is stopped)
            if (playing != _lit)
            {
                _lit = playing;
                _progressShape.Style = ShapeStyle.Fill(playing ? th.amber : th.text, 0f);
            }
            var track = music.Current;
            if (track == null) return; // (it is sliding out: it keeps the last song until it is gone)
            _title.text = track.Title;
            _artist.text = track.Artist;
            _jacket.Show(track);
        }
    }
}
