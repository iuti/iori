using Iori.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Iori.Ui
{
    /// <summary>
    /// Iori's sheet: a thin sheet to talk to her (a long press on Iori raises it, while a key for the LLM is set).
    /// One line to type in and an arrow to send it; what she answers stands above the line, just under her, until
    /// the next thing is said (there is no page of past talk). While she has not answered yet, dots.
    /// On a phone the sheet is as much taller as the keyboard is high, so the line stays above the keyboard.
    /// What she says is decided elsewhere (<see cref="TalkDirector"/>); this only shows it.
    /// </summary>
    public sealed class TalkPanel : UiPanel
    {
        const float Pad = 26f, TopPad = 16f, ReplyGap = 12f, RowHeight = 44f, Bottom = 10f, SendSize = 44f;
        const float ReplySize = 16f, ReplyLine = 25f;

        TMP_InputField _field;
        TextMeshProUGUI _replyText;
        UiShape _line;
        UiButton _send;
        RectTransform _row;
        string _reply = "";
        bool _waiting, _refocus, _staged;
        float _replyHeight, _lift, _liftCheck;
        int _dots = -1;

        static TalkDirector Talk => TalkDirector.Instance;

        public override float Height => TopPad + (_replyHeight > 0f ? _replyHeight + ReplyGap : 0f) + RowHeight + Bottom + _lift;

        protected override void Build()
        {
            var th = UiTheme.Current;
            float width = Width - Pad * 2;
            _replyText = UiKit.Text("Reply", Content, Pad, TopPad, width, ReplyLine, "", UiFonts.Medium, ReplySize, th.textBright, TextAlignmentOptions.TopLeft);
            _replyText.textWrappingMode = TextWrappingModes.Normal;
            _replyText.lineSpacing = (ReplyLine / ReplySize - 1.2f) * 100f;

            _row = UiKit.Node("Line", Content, Pad, TopPad, width, RowHeight);
            _field = UiKit.Field("Words", _row, 0, 0, width - SendSize, RowHeight, UiFonts.Medium, 15, th.textBright);
            _field.onFocusSelectAll = false;
            _field.characterLimit = Talk && Talk.Book != null ? Talk.Book.MaxInputChars : 200;
            _field.onSubmit.AddListener(_ => Send());
            // (a stroke downward on the line draws the sheet down, as anywhere else on it)
            _field.gameObject.AddComponent<UiStrokeToSheet>();
            _line = UiKit.Box("Under", _row, 0, RowHeight - 6, width - SendSize, 1, th.tabLine);
            _send = UiKit.IconButton("Send", _row, width - SendSize + 8, 0, SendSize, "send", 18, th.text, Send);

            if (Talk) Talk.Replied += OnReplied;
        }

        void OnDestroy()
        {
            if (Talk) Talk.Replied -= OnReplied;
        }

        protected override void OnOpened()
        {
            _reply = "";
            _staged = false;
            _waiting = Talk && Talk.Busy;
            ShowReply();
            _field.SetTextWithoutNotify("");
            _field.ActivateInputField();
        }

        protected override void Layout()
        {
            if (!_row) return;
            float width = Width - Pad * 2;
            UiKit.Place(_replyText.rectTransform, Pad, TopPad, width, Mathf.Max(ReplyLine, _replyHeight));
            UiKit.Place(_row, Pad, TopPad + (_replyHeight > 0f ? _replyHeight + ReplyGap : 0f), width, RowHeight);
        }

        void Send()
        {
            var talk = Talk;
            if (!talk || !_field || !talk.Ask(_field.text))
            {
                // (nothing typed, or she has not answered the last thing yet: the words stay in the line)
                _refocus = IsOpen;
                return;
            }
            UiSounds.Play(UiSounds.Tap);
            _field.text = "";
            _reply = "";
            _waiting = true;
            ShowReply();
            // (the line gives up the keyboard as it sends; it is taken up again on the next frame)
            _refocus = true;
        }

        void OnReplied(string text)
        {
            _waiting = false;
            _reply = text ?? "";
            if (_replyText) ShowReply();
        }

        /// <summary>For the screenshot runner: the sheet as it looks with these words typed and this answer above them.</summary>
        public void Stage(string typed, string reply, bool waiting)
        {
            _staged = true;
            _field.SetTextWithoutNotify(typed ?? "");
            _reply = reply ?? "";
            _waiting = waiting;
            ShowReply();
        }

        void ShowReply()
        {
            var th = UiTheme.Current;
            _dots = -1;
            if (_waiting)
            {
                _replyText.color = th.textDim;
                _replyText.text = "…";
                _replyHeight = ReplyLine;
            }
            else
            {
                _replyText.color = th.textBright;
                _replyText.text = _reply;
                _replyHeight = _reply.Length == 0 ? 0f
                    : Mathf.Max(ReplyLine, Mathf.Ceil(_replyText.GetPreferredValues(_reply, Width - Pad * 2, 0f).y));
            }
            _replyText.gameObject.SetActive(_replyHeight > 0f);
        }

        protected override void Refresh()
        {
            if (!_field) return;
            if (!IsOpen)
            {
                // going away: the keyboard goes with it
                if (_field.isFocused) _field.DeactivateInputField();
                _lift = 0f;
                _refocus = false;
                return;
            }
            if (_refocus && !_field.isFocused)
            {
                _refocus = false;
                _field.ActivateInputField();
            }
            var th = UiTheme.Current;
            bool busy = Talk && Talk.Busy;
            if (_waiting && !busy && !_staged && _reply.Length == 0) { _waiting = false; ShowReply(); } // (the answer never came)
            if (_waiting)
            {
                // one, two, three dots while she thinks
                int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;
                if (dots != _dots)
                {
                    _dots = dots;
                    _replyText.text = new string('…', dots);
                }
            }
            if (_send.icon) UiKit.Tint(_send.icon, busy || _field.text.Trim().Length == 0 ? th.textDim : th.text);

            // the keyboard's height is asked for a few times a second, not every frame
            if (Time.unscaledTime >= _liftCheck)
            {
                _liftCheck = Time.unscaledTime + 0.12f;
                float scale = Mathf.Max(0.01f, Rect.lossyScale.y);
                _lift = _field.isFocused ? Mathf.Max(0f, KeyboardPixels() / scale - BottomInset) : 0f;
            }
        }

        /// <summary>How much of the bottom of the screen the phone's keyboard covers, in pixels (0 where there is none).</summary>
        static float KeyboardPixels()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                using (var rect = new AndroidJavaObject("android.graphics.Rect"))
                using (var root = decor.Call<AndroidJavaObject>("getRootView"))
                {
                    decor.Call("getWindowVisibleDisplayFrame", rect);
                    int covered = root.Call<int>("getHeight") - rect.Get<int>("bottom");
                    // (a little is the phone's own bar at the bottom, not a keyboard)
                    return covered > Screen.height * 0.15f ? covered : 0f;
                }
            }
            catch (System.Exception)
            {
                return 0f;
            }
#else
            return 0f;
#endif
        }
    }

    /// <summary>
    /// Put on something that takes strokes itself (a line to type in): a stroke that goes downward is also given
    /// to the sheet it is on, which follows the finger down.
    /// </summary>
    public sealed class UiStrokeToSheet : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        UiPanel _panel;

        public void OnBeginDrag(PointerEventData e)
        {
            if (!_panel) _panel = GetComponentInParent<UiPanel>();
            if (_panel) _panel.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e) { if (_panel) _panel.OnDrag(e); }
        public void OnEndDrag(PointerEventData e) { if (_panel) _panel.OnEndDrag(e); }
    }
}
