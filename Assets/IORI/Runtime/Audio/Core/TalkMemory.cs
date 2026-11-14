using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>1 往復(キミの言葉とイオリの返事)。</summary>
    public sealed class TalkTurn
    {
        public long AtMs;
        public string User = "";
        public string Iori = "";
    }

    /// <summary>イオリが覚えていること 1 つ。</summary>
    public sealed class TalkNote
    {
        public long AtMs;
        public string Text = "";
    }

    /// <summary>
    /// イオリの記憶。端末の中だけに置く: 直近のやり取り(話の続きが分かるように)と、短い覚え書き(キミの好きな曲、言っていたこと)。
    /// 覚え書きは見せて消せる(<see cref="Notes"/>、<see cref="RemoveNoteAt"/>、<see cref="Clear"/>)。
    /// 保存は JSON(<see cref="Save"/>、<see cref="Load"/>)。壊れた保存は捨てて空から始める。
    /// </summary>
    public sealed class TalkMemory
    {
        /// <summary>しまっておくやり取りの数(渡す数は <see cref="Recent"/> で決める)。</summary>
        public const int KeptTurns = 24;

        readonly List<TalkTurn> _turns = new List<TalkTurn>();
        readonly List<TalkNote> _notes = new List<TalkNote>();

        public IReadOnlyList<TalkTurn> Turns => _turns;
        public IReadOnlyList<TalkNote> Notes => _notes;

        public void AddTurn(long atMs, string user, string iori)
        {
            _turns.Add(new TalkTurn { AtMs = atMs, User = user ?? "", Iori = iori ?? "" });
            while (_turns.Count > KeptTurns) _turns.RemoveAt(0);
        }

        /// <summary>LLM に渡す直近のやり取り: 新しいほうから count 個まで、hours 時間より前のものは渡さない。古い順で返す。</summary>
        public List<TalkTurn> Recent(long nowMs, int count, double hours)
        {
            var from = nowMs - (long)(hours * 3600_000);
            var list = new List<TalkTurn>();
            for (int i = _turns.Count - 1; i >= 0 && list.Count < count; i--)
            {
                if (_turns[i].AtMs < from) break;
                list.Insert(0, _turns[i]);
            }
            return list;
        }

        /// <summary>
        /// 覚え書きを足す。同じ中身(大文字小文字、全角半角、空白の違いは同じとみなす)がもうあれば、それを新しい日付で後ろへ回す。
        /// max 個を越えたら古いものから忘れる。足したら true。
        /// </summary>
        public bool AddNote(long atMs, string text, int max, int maxChars)
        {
            text = TalkReply.Cut((text ?? "").Trim(), maxChars);
            if (text.Length == 0 || max <= 0) return false;
            var key = TextFold.Fold(text);
            _notes.RemoveAll(n => TextFold.Fold(n.Text) == key);
            _notes.Add(new TalkNote { AtMs = atMs, Text = text });
            while (_notes.Count > max) _notes.RemoveAt(0);
            return true;
        }

        public void RemoveNoteAt(int index)
        {
            if (index >= 0 && index < _notes.Count) _notes.RemoveAt(index);
        }

        /// <summary>全部忘れる。</summary>
        public void Clear()
        {
            _turns.Clear();
            _notes.Clear();
        }

        public string Save()
        {
            var turns = new List<object>();
            foreach (var t in _turns)
                turns.Add(new Dictionary<string, object> { ["at"] = t.AtMs, ["user"] = t.User, ["iori"] = t.Iori });
            var notes = new List<object>();
            foreach (var n in _notes)
                notes.Add(new Dictionary<string, object> { ["at"] = n.AtMs, ["text"] = n.Text });
            return Json.Write(new Dictionary<string, object> { ["version"] = 1, ["turns"] = turns, ["notes"] = notes });
        }

        /// <summary>保存を読む。読めなければ空にして false。</summary>
        public bool Load(string json)
        {
            Clear();
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var root = JsonRead.Obj(Json.Parse(json), "talk memory");
                foreach (var o in JsonRead.Arr(root, "turns") ?? new List<object>())
                {
                    var d = JsonRead.Obj(o, "turn");
                    _turns.Add(new TalkTurn { AtMs = (long)JsonRead.Num(d, "at", 0), User = JsonRead.Str(d, "user", ""), Iori = JsonRead.Str(d, "iori", "") });
                }
                foreach (var o in JsonRead.Arr(root, "notes") ?? new List<object>())
                {
                    var d = JsonRead.Obj(o, "note");
                    var text = JsonRead.Str(d, "text", "");
                    if (text.Length > 0) _notes.Add(new TalkNote { AtMs = (long)JsonRead.Num(d, "at", 0), Text = text });
                }
                while (_turns.Count > KeptTurns) _turns.RemoveAt(0);
                return true;
            }
            catch (FormatException)
            {
                Clear();
                return false;
            }
        }
    }
}
