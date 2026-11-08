using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DrawAndWitches.Core;
using UnityEditor;
using UnityEngine;

namespace DrawAndWitches.EditorTools
{
    /// <summary>
    /// Content Editor 用：Resources/DrawAndWitches 以下の JSON を読み書きする。
    /// 書き出しは読みやすい整形 JSON（数値だけの配列は 1 行）で、未知のキーも保持する。
    /// </summary>
    public static class ContentJson
    {
        public const string ResDir = "Assets/DrawAndWitches/Resources/DrawAndWitches";
        public const string StagesDir = ResDir + "/stages";
        public static readonly string[] Languages = { "ja", "en", "zh", "ko" };
        public static readonly string[] LanguageLabels = { "日本語", "English", "中文", "한국어" };

        /// <summary>JSON ファイルを読み込む。失敗時は InvalidOperationException（理由付き）。</summary>
        public static Dictionary<string, object> ReadObject(string assetPath)
        {
            if (!File.Exists(assetPath)) throw new InvalidOperationException("ファイルがありません: " + assetPath);
            string text;
            try
            {
                text = File.ReadAllText(assetPath, Encoding.UTF8);
            }
            catch (IOException e)
            {
                throw new InvalidOperationException("読み込みに失敗しました: " + assetPath + " (" + e.Message + ")");
            }
            if (!MiniJson.TryParse(text, out object root, out string err)) throw new InvalidOperationException(assetPath + " の JSON が不正です: " + err);
            Dictionary<string, object> obj = MiniJson.AsObject(root);
            if (obj == null) throw new InvalidOperationException(assetPath + " の最上位がオブジェクトではありません");
            return obj;
        }

        /// <summary>整形して書き出し、Unity に再インポートさせる。失敗時は InvalidOperationException。</summary>
        public static void Write(string assetPath, object value)
        {
            string text = Pretty(value) + "\n";
            string tmp = assetPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(assetPath)) File.Delete(assetPath);
                File.Move(tmp, assetPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch (IOException)
                {
                    // 一時ファイルの削除失敗は致命的ではない
                }
                throw new InvalidOperationException("書き込みに失敗しました: " + assetPath + " (" + e.Message + ")");
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>値の深いコピー（編集の取り消し用）</summary>
        public static object DeepCopy(object v)
        {
            if (v is Dictionary<string, object> d)
            {
                Dictionary<string, object> o = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> kv in d) o[kv.Key] = DeepCopy(kv.Value);
                return o;
            }
            if (v is List<object> l)
            {
                List<object> o = new List<object>(l.Count);
                foreach (object x in l) o.Add(DeepCopy(x));
                return o;
            }
            return v;
        }

        public static string Pretty(object value)
        {
            StringBuilder b = new StringBuilder();
            WriteValue(b, value, 0);
            return b.ToString();
        }

        private static void Indent(StringBuilder b, int depth)
        {
            b.Append(' ', depth * 2);
        }

        private static bool IsScalar(object v)
        {
            return v == null || v is string || v is bool || v is IFormattable;
        }

        private static void WriteValue(StringBuilder b, object v, int depth)
        {
            if (v == null)
            {
                b.Append("null");
                return;
            }
            if (v is string s)
            {
                WriteString(b, s);
                return;
            }
            if (v is bool bo)
            {
                b.Append(bo ? "true" : "false");
                return;
            }
            if (v is double dd)
            {
                if (double.IsNaN(dd) || double.IsInfinity(dd)) throw new InvalidOperationException("JSON に書けない数値です: " + dd);
                b.Append(Math.Abs(dd % 1) < 1e-12 && Math.Abs(dd) < 1e15 ? ((long)dd).ToString(CultureInfo.InvariantCulture) : dd.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is float ff)
            {
                WriteValue(b, (double)ff, depth);
                return;
            }
            if (v is IFormattable f)
            {
                b.Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            if (v is IEnumerable<KeyValuePair<string, object>> obj)
            {
                List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>(obj);
                if (items.Count == 0)
                {
                    b.Append("{}");
                    return;
                }
                b.Append("{\n");
                for (int i = 0; i < items.Count; i++)
                {
                    Indent(b, depth + 1);
                    WriteString(b, items[i].Key);
                    b.Append(": ");
                    WriteValue(b, items[i].Value, depth + 1);
                    if (i < items.Count - 1) b.Append(',');
                    b.Append('\n');
                }
                Indent(b, depth);
                b.Append('}');
                return;
            }
            if (v is IEnumerable arr)
            {
                List<object> items = new List<object>();
                foreach (object x in arr) items.Add(x);
                if (items.Count == 0)
                {
                    b.Append("[]");
                    return;
                }
                bool inline = items.TrueForAll(x => x is IFormattable && !(x is string));
                if (inline)
                {
                    b.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) b.Append(", ");
                        WriteValue(b, items[i], depth);
                    }
                    b.Append(']');
                    return;
                }
                b.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    Indent(b, depth + 1);
                    WriteValue(b, items[i], depth + 1);
                    if (i < items.Count - 1) b.Append(',');
                    b.Append('\n');
                }
                Indent(b, depth);
                b.Append(']');
                return;
            }
            throw new InvalidOperationException("JSON に書けない型です: " + v.GetType().Name);
        }

        private static void WriteString(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }

        // ------------------------------------------------------------ 値アクセス

        public static string Str(Dictionary<string, object> o, string key)
        {
            return o != null && o.TryGetValue(key, out object v) && v is string s ? s : string.Empty;
        }

        public static int Int(Dictionary<string, object> o, string key, int fallback)
        {
            return o != null && o.TryGetValue(key, out object v) && MiniJson.TryGetInt(v, out int r) ? r : fallback;
        }

        public static double Num(Dictionary<string, object> o, string key, double fallback)
        {
            return MiniJson.GetDouble(o, key, fallback);
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool fallback)
        {
            return MiniJson.GetBool(o, key, fallback);
        }

        public static List<object> List(Dictionary<string, object> o, string key)
        {
            if (o == null) return null;
            if (o.TryGetValue(key, out object v) && v is List<object> l) return l;
            List<object> created = new List<object>();
            o[key] = created;
            return created;
        }

        /// <summary>多言語テキストを 4 言語の辞書として取り出す（単一文字列なら全言語へ展開して置き換える）</summary>
        public static Dictionary<string, object> Loc(Dictionary<string, object> o, string key)
        {
            if (o.TryGetValue(key, out object v))
            {
                if (v is Dictionary<string, object> d) return d;
                if (v is string s)
                {
                    Dictionary<string, object> conv = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (string lang in Languages) conv[lang] = s;
                    o[key] = conv;
                    return conv;
                }
            }
            Dictionary<string, object> created = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string lang in Languages) created[lang] = string.Empty;
            o[key] = created;
            return created;
        }

        public static string LocJa(Dictionary<string, object> o, string key)
        {
            if (o == null || !o.TryGetValue(key, out object v)) return string.Empty;
            if (v is string s) return s;
            return v is Dictionary<string, object> d && d.TryGetValue("ja", out object j) && j is string js ? js : string.Empty;
        }

        public static Dictionary<string, object> NewLoc(string ja)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string lang in Languages) d[lang] = ja;
            return d;
        }

        public static string ToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
        }

        public static Color FromHex(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }
    }
}

/*
## このアセットの役割
Content Editor（DRAW&WITCHES > Content Editor）が使う JSON 読み書きの共通処理です。

## 他アセットとの依存関係
MiniJson（解析）。Editor フォルダのためビルドには含まれません。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
保存は一時ファイルに書いてから置き換えるため、書き込み途中で壊れたファイルが残りません。
*/
