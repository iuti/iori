using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Iori.Audio
{
    /// <summary>
    /// UnityEngine に依存しない最小の JSON 読み書き。
    /// 読み: object → Dictionary&lt;string, object&gt;, array → List&lt;object&gt;,
    /// 数値 → double, 文字列 → string, true/false → bool, null → null。
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var value = p.ReadValue();
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("余分な文字があります");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(FormatNumber(d)); break;
                case float f: sb.Append(FormatNumber(f)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    var first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    throw new ArgumentException("JSON に書けない型です: " + value.GetType());
            }
        }

        static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("NaN/Infinity は JSON に書けません");
            if (Math.Abs(d % 1) < double.Epsilon && Math.Abs(d) < 9e15)
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message) =>
                new FormatException($"JSON の読み込みに失敗しました({_i} 文字目): {message}");

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            char Peek() => _i < _s.Length ? _s[_i] : '\0';

            void Expect(char c)
            {
                if (Peek() != c) throw Error($"'{c}' が必要です");
                _i++;
            }

            public object ReadValue()
            {
                SkipWs();
                var c = Peek();
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("値が必要です");
                }
            }

            void ExpectWord(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error(word + " が必要です");
                _i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var dict = new Dictionary<string, object>();
                Expect('{');
                SkipWs();
                if (Peek() == '}') { _i++; return dict; }
                while (true)
                {
                    SkipWs();
                    var key = ReadString();
                    SkipWs();
                    Expect(':');
                    dict[key] = ReadValue();
                    SkipWs();
                    if (Peek() == ',') { _i++; continue; }
                    Expect('}');
                    return dict;
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                Expect('[');
                SkipWs();
                if (Peek() == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWs();
                    if (Peek() == ',') { _i++; continue; }
                    Expect(']');
                    return list;
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("文字列が閉じていません");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("エスケープが途中で終わっています");
                    var e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("\\u の桁が足りません");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error("不明なエスケープ \\" + e);
                    }
                }
            }

            double ReadNumber()
            {
                var start = _i;
                if (Peek() == '-') _i++;
                while (!AtEnd && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                var token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error("数値として読めません: " + token);
                return d;
            }
        }
    }

    /// <summary>Json.Parse の結果を型付きで読むための小さな補助。</summary>
    internal static class JsonRead
    {
        public static Dictionary<string, object> Obj(object o, string what)
        {
            if (o is Dictionary<string, object> d) return d;
            throw new FormatException(what + " はオブジェクトである必要があります");
        }

        public static List<object> Arr(Dictionary<string, object> d, string key, bool required = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                if (v is List<object> l) return l;
                throw new FormatException(key + " は配列である必要があります");
            }
            if (required) throw new FormatException(key + " がありません");
            return new List<object>();
        }

        public static string Str(Dictionary<string, object> d, string key, string fallback = null, bool required = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                if (v is string s) return s;
                throw new FormatException(key + " は文字列である必要があります");
            }
            if (required) throw new FormatException(key + " がありません");
            return fallback;
        }

        public static double Num(Dictionary<string, object> d, string key, double fallback, bool required = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                if (v is double n) return n;
                throw new FormatException(key + " は数値である必要があります");
            }
            if (required) throw new FormatException(key + " がありません");
            return fallback;
        }

        public static List<string> StrList(Dictionary<string, object> d, string key)
        {
            var result = new List<string>();
            foreach (var item in Arr(d, key))
            {
                if (item is string s) result.Add(s);
                else throw new FormatException(key + " の要素は文字列である必要があります");
            }
            return result;
        }
    }
}
