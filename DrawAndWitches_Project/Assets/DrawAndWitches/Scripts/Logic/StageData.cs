using System;
using System.Collections.Generic;
using DrawAndWitches.Core;

namespace DrawAndWitches.Logic
{
    /// <summary>
    /// 多言語テキスト。Web版と同じく「単一文字列」または「{ja,en,zh,ko} オブジェクト」の2形式を保持する。
    /// </summary>
    public sealed class LocalizedText
    {
        public static readonly string[] Languages = { "ja", "en", "zh", "ko" };
        public static readonly LocalizedText Empty = new LocalizedText(string.Empty);

        private readonly string plain;
        private readonly Dictionary<string, string> byLanguage;

        public LocalizedText(string text)
        {
            plain = text ?? string.Empty;
            byLanguage = null;
        }

        public LocalizedText(Dictionary<string, string> texts)
        {
            plain = null;
            byLanguage = texts ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public bool IsPlain => byLanguage == null;

        /// <summary>指定言語の文字列。なければ定義順（ja→en→zh→ko）の最初の非空文字列。</summary>
        public string Resolve(string language)
        {
            if (byLanguage == null)
            {
                return plain;
            }
            if (language != null && byLanguage.TryGetValue(language, out string v) && !string.IsNullOrEmpty(v))
            {
                return v;
            }
            for (int i = 0; i < Languages.Length; i++)
            {
                if (byLanguage.TryGetValue(Languages[i], out string f) && !string.IsNullOrEmpty(f))
                {
                    return f;
                }
            }
            return string.Empty;
        }

        /// <summary>JSON 書き出し用（元の形式を保つ）</summary>
        public object ToJsonValue()
        {
            if (byLanguage == null)
            {
                return plain;
            }
            JsonObject o = new JsonObject();
            for (int i = 0; i < Languages.Length; i++)
            {
                if (byLanguage.TryGetValue(Languages[i], out string v))
                {
                    o.Add(Languages[i], v);
                }
            }
            return o;
        }

        /// <summary>検証を行わない読み込み（信頼済みの同梱データ用）。不正型は空文字。</summary>
        public static LocalizedText FromJsonTrusted(object value)
        {
            if (value is string s)
            {
                return new LocalizedText(s);
            }
            if (value is Dictionary<string, object> obj)
            {
                Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < Languages.Length; i++)
                {
                    if (obj.TryGetValue(Languages[i], out object v) && v is string str)
                    {
                        d[Languages[i]] = str;
                    }
                }
                return new LocalizedText(d);
            }
            return Empty;
        }
    }

    /// <summary>弾種定義（Web版 BULLET_TYPES と同値）</summary>
    public sealed class BulletTypeDef
    {
        public readonly string Id;
        public readonly double Speed;
        public readonly int Radius;
        public readonly int Damage;
        public readonly bool Smooth;
        /// <summary>表示名（bullets.json の name。空なら UI 文言 type.&lt;id&gt; を使う）</summary>
        public readonly LocalizedText Name;
        /// <summary>弾の表示色（#RRGGBB。空なら弾幕ごとの既定色）</summary>
        public readonly string ColorHex;

        public const double MinSpeed = 0.5;
        public const double MaxSpeed = 12.0;
        public const int MinRadius = 4;
        public const int MaxRadius = 40;
        public const int MaxDamage = 10;
        public const int MaxTypes = 16;
        private static readonly System.Text.RegularExpressions.Regex IdPattern = new System.Text.RegularExpressions.Regex("^[a-z][a-z0-9_]{0,23}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex ColorPattern = new System.Text.RegularExpressions.Regex("^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public BulletTypeDef(string id, double speed, int radius, int damage, bool smooth, LocalizedText name = null, string colorHex = null)
        {
            Id = id;
            Speed = speed;
            Radius = radius;
            Damage = damage;
            Smooth = smooth;
            Name = name ?? LocalizedText.Empty;
            ColorHex = colorHex ?? string.Empty;
        }

        public static readonly BulletTypeDef Normal = new BulletTypeDef("normal", 3.0, 10, 1, true);
        public static readonly BulletTypeDef Straight = new BulletTypeDef("straight", 5.0, 8, 1, false);
        public static readonly BulletTypeDef Giant = new BulletTypeDef("giant", 1.7, 24, 3, true);

        private static readonly BulletTypeDef[] Builtin = { Normal, Straight, Giant };
        private static BulletTypeDef[] registered = Builtin;

        /// <summary>定義順（エディターの切替順・検証用）。bullets.json があればその内容。</summary>
        public static IReadOnlyList<BulletTypeDef> All => registered;

        /// <summary>標準 3 種（Web版と同値）に戻す</summary>
        public static void ResetToBuiltin()
        {
            registered = Builtin;
        }

        /// <summary>弾種一覧を差し替える。検証に失敗したら InvalidOperationException（登録は変わらない）。</summary>
        public static void Register(IList<BulletTypeDef> defs)
        {
            if (defs == null || defs.Count == 0) throw new InvalidOperationException("bullets: 1 種類以上必要です");
            if (defs.Count > MaxTypes) throw new InvalidOperationException("bullets: 最大 " + MaxTypes + " 種類です");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BulletTypeDef d in defs)
            {
                string err = Validate(d);
                if (err != null) throw new InvalidOperationException("bullets: " + err);
                if (!ids.Add(d.Id)) throw new InvalidOperationException("bullets: id が重複しています: " + d.Id);
            }
            registered = new List<BulletTypeDef>(defs).ToArray();
        }

        /// <summary>1 種類分の検証。問題が無ければ null。</summary>
        public static string Validate(BulletTypeDef d)
        {
            if (d == null) return "定義が空です";
            if (d.Id == null || !IdPattern.IsMatch(d.Id)) return "id は英小文字で始まる英小文字・数字・_ の 1〜24 文字です: " + d.Id;
            if (double.IsNaN(d.Speed) || d.Speed < MinSpeed || d.Speed > MaxSpeed) return d.Id + ": speed は " + MinSpeed + "〜" + MaxSpeed + " です";
            if (d.Radius < MinRadius || d.Radius > MaxRadius) return d.Id + ": radius は " + MinRadius + "〜" + MaxRadius + " です";
            if (d.Damage < 1 || d.Damage > MaxDamage) return d.Id + ": damage は 1〜" + MaxDamage + " です";
            if (!string.IsNullOrEmpty(d.ColorHex) && !ColorPattern.IsMatch(d.ColorHex)) return d.Id + ": color は #RRGGBB 形式です";
            return null;
        }

        /// <summary>bullets.json の 1 要素から作る（値の範囲は Register で検証）</summary>
        public static BulletTypeDef FromJson(Dictionary<string, object> o)
        {
            if (o == null) throw new InvalidOperationException("bullets: types の要素がオブジェクトではありません");
            string id = Core.MiniJson.GetString(o, "id", null);
            double speed = Core.MiniJson.GetDouble(o, "speed", double.NaN);
            int radius = Core.MiniJson.GetInt(o, "radius", -1);
            int damage = Core.MiniJson.GetInt(o, "damage", -1);
            bool smooth = Core.MiniJson.GetBool(o, "smooth", true);
            LocalizedText name = LocalizedText.Empty;
            object rawName = Core.MiniJson.Get(o, "name");
            if (rawName is string ns) name = new LocalizedText(ns);
            else if (Core.MiniJson.AsObject(rawName) is Dictionary<string, object> nm)
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> kv in nm)
                {
                    if (kv.Value is string v) map[kv.Key] = v;
                }
                name = new LocalizedText(map);
            }
            return new BulletTypeDef(id, speed, radius, damage, smooth, name, Core.MiniJson.GetString(o, "color", string.Empty));
        }

        public static BulletTypeDef Find(string id)
        {
            BulletTypeDef[] all = registered;
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }
            return null;
        }
    }

    /// <summary>整数座標の起点</summary>
    public struct IntPoint : IEquatable<IntPoint>
    {
        public int X;
        public int Y;

        public IntPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(IntPoint other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is IntPoint p && Equals(p);
        }

        public override int GetHashCode()
        {
            return unchecked(X * 397) ^ Y;
        }
    }

    /// <summary>1つの弾幕（同じ軌道を時間差で進む弾の列）</summary>
    public sealed class BarrageData
    {
        public string Type;
        public int Count;
        public int IntervalTicks;
        public int StartDelayTicks;
        public List<IntPoint> Points = new List<IntPoint>();

        public BarrageData Clone()
        {
            return new BarrageData
            {
                Type = Type,
                Count = Count,
                IntervalTicks = IntervalTicks,
                StartDelayTicks = StartDelayTicks,
                Points = new List<IntPoint>(Points),
            };
        }
    }

    /// <summary>検証済みステージ（公式・エンドレス・エディター・自作・共有で共通の形式）</summary>
    public sealed class StageData
    {
        public int FormatVersion = GameConstants.StageFormatVersion;
        public string Id;
        public LocalizedText Name = LocalizedText.Empty;
        public LocalizedText Summary = LocalizedText.Empty;
        public LocalizedText SpellName = LocalizedText.Empty;
        public string EnemyId;
        public int EditTimeSec;
        public int DragRadius;
        public List<BarrageData> Barrages = new List<BarrageData>();

        public int TotalPoints
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Barrages.Count; i++)
                {
                    n += Barrages[i].Points.Count;
                }
                return n;
            }
        }

        public StageData Clone()
        {
            StageData s = new StageData
            {
                FormatVersion = FormatVersion,
                Id = Id,
                Name = Name,
                Summary = Summary,
                SpellName = SpellName,
                EnemyId = EnemyId,
                EditTimeSec = EditTimeSec,
                DragRadius = DragRadius,
            };
            for (int i = 0; i < Barrages.Count; i++)
            {
                s.Barrages.Add(Barrages[i].Clone());
            }
            return s;
        }

        /// <summary>Web版 JSON.stringify(stage) と同じキー順の JSON オブジェクト</summary>
        public JsonObject ToJson()
        {
            List<object> barrages = new List<object>(Barrages.Count);
            for (int i = 0; i < Barrages.Count; i++)
            {
                BarrageData b = Barrages[i];
                List<object> pts = new List<object>(b.Points.Count);
                for (int p = 0; p < b.Points.Count; p++)
                {
                    pts.Add(new List<object> { b.Points[p].X, b.Points[p].Y });
                }
                barrages.Add(new JsonObject()
                    .Add("type", b.Type)
                    .Add("count", b.Count)
                    .Add("intervalTicks", b.IntervalTicks)
                    .Add("startDelayTicks", b.StartDelayTicks)
                    .Add("points", pts));
            }
            return new JsonObject()
                .Add("formatVersion", FormatVersion)
                .Add("id", Id)
                .Add("name", Name.ToJsonValue())
                .Add("summary", Summary.ToJsonValue())
                .Add("spellName", SpellName.ToJsonValue())
                .Add("enemyId", EnemyId)
                .Add("editTimeSec", EditTimeSec)
                .Add("dragRadius", DragRadius)
                .Add("barrages", barrages);
        }

        /// <summary>名称・概要を除いたゲーム内容のハッシュ（テストプレイのクリア済み判定に使用）</summary>
        public string ContentHash()
        {
            uint h = Fnv1a.Offset;
            h = Fnv1a.MixInt(h, unchecked((int)Fnv1a.MixString(EnemyId)));
            h = Fnv1a.MixInt(h, EditTimeSec);
            h = Fnv1a.MixInt(h, DragRadius);
            for (int i = 0; i < Barrages.Count; i++)
            {
                BarrageData b = Barrages[i];
                h = Fnv1a.MixInt(h, unchecked((int)Fnv1a.MixString(b.Type)));
                h = Fnv1a.MixInt(h, b.Count);
                h = Fnv1a.MixInt(h, b.IntervalTicks);
                h = Fnv1a.MixInt(h, b.StartDelayTicks);
                for (int p = 0; p < b.Points.Count; p++)
                {
                    h = Fnv1a.MixInt(h, b.Points[p].X);
                    h = Fnv1a.MixInt(h, b.Points[p].Y);
                }
            }
            return Fnv1a.ToHex(h);
        }
    }
}

/*
## このアセットの役割
ステージ・弾幕・起点・弾種・多言語テキストのデータ構造です。Web版のステージ JSON と同じ構造で読み書きできます。

## 他アセットとの依存関係
StageValidator（読み込み・検証）、BattleSimulation（実行）、MyStageService（保存）、ReplaySerializer（リプレイ内のステージ）から使用されます。

## Inspector設定
なし（実行時データ）。

## USERがUnity上で行う作業
なし。

## 注意点
外部から受け取ったステージは必ず StageValidator を通してから使ってください。ContentHash は Web版 stageContentHash と同じ値になります。
*/
