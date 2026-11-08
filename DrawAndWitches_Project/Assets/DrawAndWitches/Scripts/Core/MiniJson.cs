using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrawAndWitches.Core
{
    /// <summary>
    /// キー順を保持して書き出すための JSON オブジェクト（書き出し専用）。
    /// Web版 JSON.stringify と同じキー順で出力するために使う。
    /// </summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();

        public int Count => items.Count;

        public JsonObject Add(string key, object value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].Key, key, StringComparison.Ordinal))
                {
                    items[i] = new KeyValuePair<string, object>(key, value);
                    return this;
                }
            }
            items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return items.GetEnumerator();
        }
    }

    /// <summary>
    /// 外部ライブラリを使わない JSON パーサー／ライター。
    /// UnityEngine.JsonUtility は Dictionary・ネスト配列・多言語オブジェクト（文字列 or {ja,en,..}）を扱えないため自作した。
    /// 解析結果の型: Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 64;

        /// <summary>JSON 文字列を解析する。失敗時は false と原因（位置付き）を返し、例外は投げない。</summary>
        public static bool TryParse(string text, out object result, out string error)
        {
            result = null;
            error = null;
            if (text == null)
            {
                error = "input is null";
                return false;
            }
            Parser parser = new Parser(text);
            try
            {
                parser.SkipWhitespace();
                result = parser.ParseValue(0);
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = "unexpected trailing characters at " + parser.Position.ToString(CultureInfo.InvariantCulture);
                    result = null;
                    return false;
                }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                result = null;
                return false;
            }
        }

        private sealed class Parser
        {
            private const int HexLength = 4;
            private const char FirstPrintable = (char)0x20;

            private readonly string s;
            private readonly StringBuilder sb = new StringBuilder();
            private int i;

            public Parser(string text)
            {
                s = text;
                i = 0;
            }

            public bool AtEnd => i >= s.Length;
            public int Position => i;

            public void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        break;
                    }
                    i++;
                }
            }

            private FormatException Error(string message)
            {
                return new FormatException(message + " at " + i.ToString(CultureInfo.InvariantCulture));
            }

            public object ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("nesting too deep");
                }
                if (i >= s.Length)
                {
                    throw Error("unexpected end of input");
                }
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || IsDigit(c))
                        {
                            return ParseNumber();
                        }
                        throw Error("unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                {
                    throw Error("invalid literal");
                }
                i += word.Length;
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                SkipWhitespace();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return dict;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != '"')
                    {
                        throw Error("object key expected");
                    }
                    string key = ParseString();
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != ':')
                    {
                        throw Error("':' expected");
                    }
                    i++;
                    SkipWhitespace();
                    // 重複キーは JSON.parse と同じく後勝ち
                    dict[key] = ParseValue(depth + 1);
                    SkipWhitespace();
                    if (i >= s.Length)
                    {
                        throw Error("unterminated object");
                    }
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        return dict;
                    }
                    throw Error("',' or '}' expected");
                }
            }

            private List<object> ParseArray(int depth)
            {
                List<object> list = new List<object>();
                i++;
                SkipWhitespace();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (i >= s.Length)
                    {
                        throw Error("unterminated array");
                    }
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        return list;
                    }
                    throw Error("',' or ']' expected");
                }
            }

            private string ParseString()
            {
                sb.Length = 0;
                i++;
                while (true)
                {
                    if (i >= s.Length)
                    {
                        throw Error("unterminated string");
                    }
                    char c = s[i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c < FirstPrintable)
                    {
                        throw Error("control character in string");
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length)
                    {
                        throw Error("unterminated escape");
                    }
                    char e = s[i++];
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
                            if (i + HexLength > s.Length || !int.TryParse(s.Substring(i, HexLength), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("invalid unicode escape");
                            }
                            sb.Append((char)code);
                            i += HexLength;
                            break;
                        default:
                            throw Error("invalid escape");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = i;
                if (s[i] == '-')
                {
                    i++;
                }
                if (i >= s.Length || !IsDigit(s[i]))
                {
                    throw Error("invalid number");
                }
                if (s[i] == '0')
                {
                    i++;
                }
                else
                {
                    while (i < s.Length && IsDigit(s[i])) i++;
                }
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    if (i >= s.Length || !IsDigit(s[i]))
                    {
                        throw Error("invalid fraction");
                    }
                    while (i < s.Length && IsDigit(s[i])) i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    if (i >= s.Length || !IsDigit(s[i]))
                    {
                        throw Error("invalid exponent");
                    }
                    while (i < s.Length && IsDigit(s[i])) i++;
                }
                string token = s.Substring(start, i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
                {
                    throw Error("number out of range");
                }
                return value;
            }

            private static bool IsDigit(char c)
            {
                return c >= '0' && c <= '9';
            }
        }

        // ------------------------------------------------------------------ 型安全な取り出し

        /// <summary>JS の Number.isInteger 相当で、int の範囲内か</summary>
        public static bool TryGetInt(object value, out int result)
        {
            result = 0;
            if (!(value is double d) || double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d || d < int.MinValue || d > int.MaxValue)
            {
                return false;
            }
            result = (int)d;
            return true;
        }

        /// <summary>JS の安全な整数（±2^53-1）範囲の整数か</summary>
        public static bool TryGetLong(object value, out long result)
        {
            const double MaxSafeInteger = 9007199254740991.0;
            result = 0;
            if (!(value is double d) || double.IsNaN(d) || Math.Floor(d) != d || d < -MaxSafeInteger || d > MaxSafeInteger)
            {
                return false;
            }
            result = (long)d;
            return true;
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static object Get(Dictionary<string, object> obj, string key)
        {
            if (obj == null || key == null)
            {
                return null;
            }
            return obj.TryGetValue(key, out object v) ? v : null;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback)
        {
            return Get(obj, key) is string s ? s : fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            return TryGetInt(Get(obj, key), out int v) ? v : fallback;
        }

        public static long GetLong(Dictionary<string, object> obj, string key, long fallback)
        {
            return TryGetLong(Get(obj, key), out long v) ? v : fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback)
        {
            return Get(obj, key) is bool b ? b : fallback;
        }

        public static double GetDouble(Dictionary<string, object> obj, string key, double fallback)
        {
            return Get(obj, key) is double d && !double.IsNaN(d) ? d : fallback;
        }

        // ------------------------------------------------------------------ 書き出し

        /// <summary>
        /// 値を JSON 文字列へ変換する。対応型: null / bool / string / 整数型 / double / float /
        /// JsonObject / IDictionary&lt;string, object&gt; / IEnumerable（配列）。それ以外は例外。
        /// </summary>
        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder();
            Write(builder, value, 0);
            return builder.ToString();
        }

        private static void Write(StringBuilder b, object value, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException("MiniJson.Serialize: nesting too deep");
            }
            switch (value)
            {
                case null: b.Append("null"); return;
                case bool flag: b.Append(flag ? "true" : "false"); return;
                case string text: WriteString(b, text); return;
                case int n: b.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: b.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case uint u: b.Append(u.ToString(CultureInfo.InvariantCulture)); return;
                case double d: WriteNumber(b, d); return;
                case float f: WriteNumber(b, f); return;
                case JsonObject jo:
                    WriteObject(b, jo, depth);
                    return;
                case IDictionary<string, object> dict:
                    WriteObject(b, dict, depth);
                    return;
                case IEnumerable seq:
                    b.Append('[');
                    bool first = true;
                    foreach (object item in seq)
                    {
                        if (!first) b.Append(',');
                        first = false;
                        Write(b, item, depth + 1);
                    }
                    b.Append(']');
                    return;
                default:
                    throw new InvalidOperationException("MiniJson.Serialize: unsupported type " + value.GetType().FullName);
            }
        }

        private static void WriteObject(StringBuilder b, IEnumerable<KeyValuePair<string, object>> pairs, int depth)
        {
            b.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in pairs)
            {
                if (!first) b.Append(',');
                first = false;
                WriteString(b, kv.Key);
                b.Append(':');
                Write(b, kv.Value, depth + 1);
            }
            b.Append('}');
        }

        private static void WriteNumber(StringBuilder b, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                // JSON.stringify と同じく非有限値は null
                b.Append("null");
                return;
            }
            if (Math.Floor(d) == d && Math.Abs(d) < 1e15)
            {
                b.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                return;
            }
            b.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder b, string s)
        {
            const char FirstPrintable = (char)0x20;
            const char LineSeparator = '\u2028';
            const char ParagraphSeparator = '\u2029';
            b.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    default:
                        if (c < FirstPrintable || c == LineSeparator || c == ParagraphSeparator)
                        {
                            b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            b.Append(c);
                        }
                        break;
                }
            }
            b.Append('"');
        }
    }

    /// <summary>Base64URL（パディングなし）と UTF-8 の相互変換。リプレイ文字列に使用。</summary>
    public static class Base64Url
    {
        private const int BlockSize = 4;

        public static string EncodeUtf8(string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
            string b64 = Convert.ToBase64String(bytes);
            return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>不正な文字・不正な UTF-8 を検出した場合は false</summary>
        public static bool TryDecodeUtf8(string encoded, out string text, out string error)
        {
            text = null;
            error = null;
            if (encoded == null)
            {
                error = "input is null";
                return false;
            }
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok)
                {
                    error = "invalid base64 characters";
                    return false;
                }
            }
            StringBuilder sb = new StringBuilder(encoded.Length + BlockSize);
            sb.Append(encoded).Replace('-', '+').Replace('_', '/');
            while (sb.Length % BlockSize != 0)
            {
                sb.Append('=');
            }
            try
            {
                byte[] bytes = Convert.FromBase64String(sb.ToString());
                text = new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return false;
            }
            catch (ArgumentException e)
            {
                // DecoderFallbackException（不正な UTF-8）は ArgumentException の派生
                error = e.Message;
                return false;
            }
        }
    }
}

/*
## このアセットの役割
外部ライブラリなしで JSON の解析・書き出しと、Base64URL⇔UTF-8 変換を行います。ステージ・リプレイ・保存データの読み書きに使います。

## 他アセットとの依存関係
StageValidator / ReplaySerializer / 各 Service / GameDatabase から使用されます。Web版の JSON.parse / JSON.stringify / btoa・atob と互換の出力を行います。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
解析結果の数値はすべて double です。整数が必要な箇所は TryGetInt（JS の Number.isInteger 相当）で取り出してください。
*/
