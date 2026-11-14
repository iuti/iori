using System;
using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>設定の値の種類。</summary>
    public enum SettingKind { Number, Toggle, Text }

    /// <summary>
    /// 設定 1 つの決まり(鍵、種類、既定値、数なら範囲、まとまり)。画面はこれを見て部品を並べる。
    /// </summary>
    public sealed class SettingDef
    {
        public string Key;
        public SettingKind Kind = SettingKind.Number;
        /// <summary>まとまりの名前(volume、voice、talk など)。設定の画面で並べるときの区切り。</summary>
        public string Group = "";
        public double DefaultNumber;
        public bool DefaultToggle;
        public string DefaultText = "";
        public double Min = 0, Max = 1;

        public static SettingDef Number(string key, double def, string group, double min = 0, double max = 1) =>
            new SettingDef { Key = key, Kind = SettingKind.Number, DefaultNumber = def, Group = group, Min = min, Max = max };

        public static SettingDef Toggle(string key, bool def, string group) =>
            new SettingDef { Key = key, Kind = SettingKind.Toggle, DefaultToggle = def, Group = group };

        public static SettingDef Text(string key, string def, string group) =>
            new SettingDef { Key = key, Kind = SettingKind.Text, DefaultText = def ?? "", Group = group };
    }

    /// <summary>
    /// アプリの設定を 1 か所で持つ入れ物。音量(ラジオの曲、プレーヤーの曲、声、環境音ごと、画面の音)のほか、
    /// 後から声のオンオフや会話の設定も同じ形で足す。
    ///
    /// - 値は鍵(<c>volume.radio</c> など)で読み書きする。決まり(<see cref="SettingDef"/>)を <see cref="Define"/> した鍵だけが
    ///   範囲や既定値を持つ。決まりのない鍵の値も消さずに持っておく(読み込みが決まりより先でも、新しい版の鍵でも失わない)。
    /// - 値が変わると <see cref="Changed"/> が鍵を 1 つ持って来る。<see cref="Watch"/> で鍵(または <c>.</c> で終わる頭)ごとに聞ける。
    ///   同じ値を入れ直しても来ない。ラジオのシートと設定の画面が同じ鍵を見れば、片方を動かすともう片方も動く。
    /// - 保存は JSON(<see cref="Save"/>、<see cref="Load"/>)。いつファイルに書くかは Unity 側が決める(<see cref="Version"/> が変わったら)。
    /// </summary>
    public sealed class SettingsStore
    {
        const int FormatVersion = 1;

        readonly Dictionary<string, SettingDef> _defs = new Dictionary<string, SettingDef>();
        readonly List<SettingDef> _order = new List<SettingDef>();
        readonly Dictionary<string, object> _values = new Dictionary<string, object>(); // 既定のままの鍵は持たない

        /// <summary>値が変わった鍵。</summary>
        public event Action<string> Changed;

        /// <summary>値が変わるたびに増える(保存の要不要を見るため)。</summary>
        public int Version { get; private set; }

        /// <summary>決めた順の、設定の決まりの一覧。</summary>
        public IReadOnlyList<SettingDef> Defs => _order;

        public bool IsDefined(string key) => key != null && _defs.ContainsKey(key);

        public SettingDef Find(string key) => key != null && _defs.TryGetValue(key, out var d) ? d : null;

        /// <summary>
        /// 設定の決まりを足す。同じ鍵をもう一度決めると、既定値と範囲を新しいほうに替える(環境音の既定の音量は番組表から来るため)。
        /// 既に入っている値が新しい範囲の外なら、範囲に収めて見せる。値が見かけ上変わったら <see cref="Changed"/> を出す。
        /// </summary>
        public void Define(SettingDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Key)) throw new ArgumentException("設定の鍵がありません");
            if (def.Kind == SettingKind.Number && def.Max < def.Min) throw new ArgumentException("範囲が逆です: " + def.Key);
            object before = _defs.ContainsKey(def.Key) ? Effective(_defs[def.Key]) : null;
            if (_defs.TryGetValue(def.Key, out var old)) _order[_order.IndexOf(old)] = def;
            else _order.Add(def);
            _defs[def.Key] = def;
            if (before != null && !Equals(before, Effective(def))) Raise(def.Key);
        }

        public void Define(IEnumerable<SettingDef> defs)
        {
            foreach (var d in defs) Define(d);
        }

        // ---- 読む

        public double GetNumber(string key)
        {
            var def = Find(key);
            if (def != null) return def.Kind == SettingKind.Number ? (double)Effective(def) : 0;
            return _values.TryGetValue(key ?? "", out var v) && v is double d ? d : 0;
        }

        public float GetFloat(string key) => (float)GetNumber(key);

        public bool GetToggle(string key)
        {
            var def = Find(key);
            if (def != null) return def.Kind == SettingKind.Toggle && (bool)Effective(def);
            return _values.TryGetValue(key ?? "", out var v) && v is bool b && b;
        }

        public string GetText(string key)
        {
            var def = Find(key);
            if (def != null) return def.Kind == SettingKind.Text ? (string)Effective(def) : "";
            return _values.TryGetValue(key ?? "", out var v) && v is string s ? s : "";
        }

        /// <summary>既定のままか(一度も変えていないか、既定に戻したか)。</summary>
        public bool IsDefault(string key)
        {
            var def = Find(key);
            if (def == null) return !_values.ContainsKey(key ?? "");
            return Equals(Effective(def), DefaultOf(def));
        }

        // ---- 書く(変わったら true)

        public bool SetNumber(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            var def = Find(key);
            if (def != null)
            {
                if (def.Kind != SettingKind.Number) throw new InvalidOperationException("数の設定ではありません: " + key);
                value = Clamp(value, def.Min, def.Max);
            }
            return Put(key, value);
        }

        public bool SetToggle(string key, bool value)
        {
            var def = Find(key);
            if (def != null && def.Kind != SettingKind.Toggle) throw new InvalidOperationException("オンオフの設定ではありません: " + key);
            return Put(key, value);
        }

        public bool SetText(string key, string value)
        {
            var def = Find(key);
            if (def != null && def.Kind != SettingKind.Text) throw new InvalidOperationException("文字の設定ではありません: " + key);
            return Put(key, value ?? "");
        }

        /// <summary>既定の値に戻す。</summary>
        public bool Reset(string key)
        {
            if (key == null || !_values.ContainsKey(key)) return false;
            var def = Find(key);
            object before = def != null ? Effective(def) : _values[key];
            _values.Remove(key);
            Version++;
            if (def == null || !Equals(before, Effective(def))) Raise(key);
            return true;
        }

        // ---- 聞く

        /// <summary>
        /// 鍵の値が変わったら <paramref name="onChanged"/> を呼ぶ(引数は変わった鍵)。<paramref name="key"/> が <c>.</c> で終わるなら、
        /// その頭で始まる鍵すべて(<c>volume.ambient.</c> なら環境音ぜんぶ)。返り値を Dispose すると聞くのをやめる。
        /// </summary>
        public IDisposable Watch(string key, Action<string> onChanged)
        {
            if (string.IsNullOrEmpty(key) || onChanged == null) throw new ArgumentException("鍵と聞き手が要ります");
            bool prefix = key.EndsWith(".", StringComparison.Ordinal);
            Action<string> h = k =>
            {
                if (prefix ? k.StartsWith(key, StringComparison.Ordinal) : k == key) onChanged(k);
            };
            Changed += h;
            return new Unwatch(() => Changed -= h);
        }

        sealed class Unwatch : IDisposable
        {
            Action _off;
            public Unwatch(Action off) { _off = off; }
            public void Dispose() { _off?.Invoke(); _off = null; }
        }

        // ---- 保存

        /// <summary>保存用の JSON。既定のままの鍵は書かない(既定を後で変えたら、触っていない人には新しい既定が効く)。</summary>
        public string Save()
        {
            var values = new Dictionary<string, object>();
            foreach (var kv in _values) values[kv.Key] = kv.Value;
            return Json.Write(new Dictionary<string, object> { ["version"] = (double)FormatVersion, ["values"] = values });
        }

        /// <summary>
        /// 保存した JSON を読む。いま持っている値は読んだものに置き換わり、見かけの値が変わった鍵ごとに <see cref="Changed"/> を出す。
        /// 壊れていたら何も変えずに false。
        /// </summary>
        public bool Load(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return false;
            Dictionary<string, object> values;
            try
            {
                var root = JsonRead.Obj(Json.Parse(saved), "settings");
                if (!root.TryGetValue("values", out var vo) || !(vo is Dictionary<string, object> v)) return false;
                values = v;
            }
            catch (FormatException) { return false; }

            var before = new Dictionary<string, object>();
            foreach (var d in _order) before[d.Key] = Effective(d);
            var touched = new HashSet<string>(_values.Keys);

            _values.Clear();
            foreach (var kv in values)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (kv.Value is double || kv.Value is bool || kv.Value is string) _values[kv.Key] = kv.Value;
                touched.Add(kv.Key);
            }
            Version++;
            foreach (var key in touched)
            {
                var def = Find(key);
                if (def == null) Raise(key);
                else if (!Equals(before[key], Effective(def))) Raise(key);
            }
            return true;
        }

        // ---- 中身

        bool Put(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("設定の鍵がありません");
            var def = Find(key);
            object before = def != null ? Effective(def) : (_values.TryGetValue(key, out var b) ? b : null);
            if (def != null && Equals(value, DefaultOf(def))) _values.Remove(key);
            else _values[key] = value;
            if (Equals(before, value)) return false;
            Version++;
            Raise(key);
            return true;
        }

        /// <summary>決まりに合わせた、いまの値(保存の値が種類違いなら既定、数は範囲に収める)。</summary>
        object Effective(SettingDef def)
        {
            _values.TryGetValue(def.Key, out var v);
            switch (def.Kind)
            {
                case SettingKind.Number: return v is double d ? Clamp(d, def.Min, def.Max) : Clamp(def.DefaultNumber, def.Min, def.Max);
                case SettingKind.Toggle: return v is bool b ? b : def.DefaultToggle;
                default: return v is string s ? s : def.DefaultText ?? "";
            }
        }

        static object DefaultOf(SettingDef def)
        {
            switch (def.Kind)
            {
                case SettingKind.Number: return Clamp(def.DefaultNumber, def.Min, def.Max);
                case SettingKind.Toggle: return def.DefaultToggle;
                default: return def.DefaultText ?? "";
            }
        }

        void Raise(string key) => Changed?.Invoke(key);

        static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
