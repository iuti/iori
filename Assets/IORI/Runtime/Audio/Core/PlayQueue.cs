using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    public enum RepeatMode
    {
        /// <summary>最後の曲が終わったら止まる。</summary>
        Off,
        /// <summary>最後の曲の次は最初の曲。</summary>
        All,
        /// <summary>同じ曲をくり返す(「次の曲」を押せば先へ進む)。</summary>
        One,
    }

    /// <summary>
    /// 音楽プレーヤーの再生の列。どの順に鳴らすかはここだけが決める(まぜるときの順もここで作る)。
    /// 再生係(PC の Unity、Android のサービス)は、ここで決まった順(Order)とくり返し方をそのまま鳴らす。
    /// 再生係が持つ同じ決まり: 曲が終わったら Off は次へ進み最後で止まる、All は最後の次が最初、One は同じ曲をもう一度。
    /// </summary>
    public sealed class PlayQueue
    {
        /// <summary>「前の曲」を押したとき、曲のこの位置(ミリ秒)を過ぎていたら前の曲へ行かずに頭に戻る。</summary>
        public const long RestartAfterMs = 3000;

        readonly List<string> _source = new List<string>();
        readonly List<string> _order = new List<string>();

        /// <summary>鳴らす順(まぜているときはまぜた後の順)。</summary>
        public IReadOnlyList<string> Order => _order;
        public int Index { get; private set; } = -1;
        public bool Shuffle { get; private set; }
        public RepeatMode Repeat { get; private set; }
        public int Count => _order.Count;
        public string Current => Index >= 0 && Index < _order.Count ? _order[Index] : null;

        /// <summary>
        /// 列を入れ替える。ids は選んだ並び(アルバムの曲順、一覧の順)、start はその中で最初に鳴らす曲。
        /// まぜるときは start の曲を先頭にして、残りを seed から決まる順にまぜる(同じ seed なら同じ順)。
        /// </summary>
        public void Set(IEnumerable<string> ids, int start, bool shuffle, int seed)
        {
            _source.Clear();
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _source.Add(id);
            Shuffle = shuffle;
            Arrange(_source.Count == 0 ? -1 : Math.Max(0, Math.Min(start, _source.Count - 1)), seed);
        }

        /// <summary>まぜる、まぜないを切り替える。いまの曲はそのまま続く。</summary>
        public void SetShuffle(bool shuffle, int seed)
        {
            if (Shuffle == shuffle) return;
            Shuffle = shuffle;
            var current = Current;
            Arrange(current == null ? (_source.Count == 0 ? -1 : 0) : _source.IndexOf(current), seed);
        }

        public void SetRepeat(RepeatMode mode) => Repeat = mode;

        void Arrange(int sourceIndex, int seed)
        {
            _order.Clear();
            if (_source.Count == 0 || sourceIndex < 0)
            {
                Index = -1;
                return;
            }
            if (!Shuffle)
            {
                _order.AddRange(_source);
                Index = sourceIndex;
                return;
            }
            // いまの曲を先頭に、残りをまぜる(フィッシャー–イェーツ。乱数は seed から決まる自前のもの)。
            for (var i = 0; i < _source.Count; i++)
                if (i != sourceIndex) _order.Add(_source[i]);
            var state = unchecked((uint)seed * 2654435761u + 0x9E3779B9u);
            if (state == 0) state = 1;
            for (var i = _order.Count - 1; i > 0; i--)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                var j = (int)(state % (uint)(i + 1));
                var tmp = _order[i]; _order[i] = _order[j]; _order[j] = tmp;
            }
            _order.Insert(0, _source[sourceIndex]);
            Index = 0;
        }

        /// <summary>
        /// 次の曲へ。byUser は「次の曲」を押したとき true、曲が終わって進むとき false。
        /// 戻り値が false なら再生は終わり(Off で最後の曲が終わった。Index は最後のまま)。
        /// </summary>
        public bool Next(bool byUser)
        {
            if (_order.Count == 0) return false;
            if (!byUser && Repeat == RepeatMode.One) return true;
            if (Index + 1 < _order.Count)
            {
                Index++;
                return true;
            }
            if (Repeat == RepeatMode.Off && !byUser) return false;
            // 最後の曲で「次の曲」を押したら、くり返しなしでも最初へ回る
            Index = 0;
            return true;
        }

        /// <summary>
        /// 「前の曲」。曲の頭から RestartAfterMs を過ぎていたら同じ曲の頭に戻る(戻り値 false)。
        /// そうでなければ前の曲へ(戻り値 true)。最初の曲では、All なら最後の曲へ、それ以外は頭に戻る。
        /// </summary>
        public bool Previous(long positionMs)
        {
            if (_order.Count == 0) return false;
            if (positionMs > RestartAfterMs) return false;
            if (Index > 0)
            {
                Index--;
                return true;
            }
            if (Repeat == RepeatMode.All && _order.Count > 1)
            {
                Index = _order.Count - 1;
                return true;
            }
            return false;
        }

        /// <summary>列の中の曲へ飛ぶ(列に無ければ何もしないで false)。</summary>
        public bool JumpTo(string id)
        {
            var i = _order.IndexOf(id);
            if (i < 0) return false;
            Index = i;
            return true;
        }

        /// <summary>再生係が自分で曲を進めたとき(Android で裏にいる間など)、いまの位置を合わせる。</summary>
        public void SyncIndex(int index)
        {
            if (index >= 0 && index < _order.Count) Index = index;
        }

        /// <summary>消えた曲(ファイルが無くなった)を列から外す。いまの曲が消えたら次の曲がいまの曲になる。</summary>
        public void Remove(Predicate<string> gone)
        {
            var current = Current;
            var index = Index;
            _source.RemoveAll(gone);
            for (var i = _order.Count - 1; i >= 0; i--)
            {
                if (!gone(_order[i])) continue;
                _order.RemoveAt(i);
                if (i < index) index--;
            }
            if (_order.Count == 0) Index = -1;
            else if (current != null && !gone(current)) Index = _order.IndexOf(current);
            else Index = Math.Min(index, _order.Count - 1);
        }

        // ---- 保存(アプリを閉じても列を覚えておく)

        public string Save()
        {
            return Json.Write(new Dictionary<string, object>
            {
                ["source"] = new List<object>(_source),
                ["order"] = new List<object>(_order),
                ["index"] = Index,
                ["shuffle"] = Shuffle,
                ["repeat"] = Repeat == RepeatMode.All ? "all" : Repeat == RepeatMode.One ? "one" : "off",
            });
        }

        /// <summary>保存した文字列から戻す。壊れていたら空の列にする。</summary>
        public void Load(string saved)
        {
            _source.Clear();
            _order.Clear();
            Index = -1;
            Shuffle = false;
            Repeat = RepeatMode.Off;
            if (string.IsNullOrEmpty(saved)) return;
            try
            {
                var d = JsonRead.Obj(Json.Parse(saved), "列");
                _source.AddRange(JsonRead.StrList(d, "source"));
                _order.AddRange(JsonRead.StrList(d, "order"));
                Shuffle = d.TryGetValue("shuffle", out var s) && s is bool b && b;
                var r = JsonRead.Str(d, "repeat", "off");
                Repeat = r == "all" ? RepeatMode.All : r == "one" ? RepeatMode.One : RepeatMode.Off;
                var index = (int)JsonRead.Num(d, "index", -1);
                // 順は元の並びと同じ曲でできていること
                var same = _order.Count == _source.Count;
                if (same)
                {
                    var set = new HashSet<string>(_source);
                    foreach (var id in _order) if (!set.Contains(id)) same = false;
                }
                if (!same) { _order.Clear(); _order.AddRange(_source); Shuffle = false; }
                Index = _order.Count == 0 ? -1 : Math.Max(0, Math.Min(index, _order.Count - 1));
            }
            catch (FormatException)
            {
                _source.Clear();
                _order.Clear();
                Index = -1;
            }
        }
    }
}
