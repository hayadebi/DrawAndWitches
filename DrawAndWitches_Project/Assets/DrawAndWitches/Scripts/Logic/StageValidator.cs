using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;

namespace DrawAndWitches.Logic
{
    /// <summary>検証結果</summary>
    public sealed class StageValidationResult
    {
        public bool Ok;
        public StageData Stage;
        public readonly List<string> Errors = new List<string>();

        public string ErrorSummary => string.Join("; ", Errors);
    }

    /// <summary>
    /// 外部入力（同梱JSON・自作・共有・リプレイ内）のステージを検証・正規化する。Web版 validateStage と同じ規則・同じエラー文言。
    /// 共有ステージは実行可能コードとして扱わず、ここで検証済みのデータだけを使う。
    /// </summary>
    public static class StageValidator
    {
        private static readonly Regex IdPattern = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

        /// <summary>同一弾幕内の弾が発射直後に重ならない最小間隔 tick</summary>
        public static int MinIntervalTicksFor(string typeId)
        {
            BulletTypeDef t = BulletTypeDef.Find(typeId);
            if (t == null)
            {
                return GameConstants.StageLimits.MinIntervalTicks;
            }
            int need = (int)Math.Ceiling((t.Radius * 2 + GameConstants.StageLimits.SpawnGapPadding) / t.Speed);
            return Math.Max(GameConstants.StageLimits.MinIntervalTicks, need);
        }

        public static int FieldMinX => GameConstants.World.FieldMargin;
        public static int FieldMaxX => GameConstants.World.Width - GameConstants.World.FieldMargin;
        public static int FieldMinY => GameConstants.World.FieldMargin;
        public static int FieldMaxY => GameConstants.World.Height - GameConstants.World.FieldMargin;

        /// <summary>JSON 文字列から検証（サイズ上限込み）</summary>
        public static StageValidationResult ParseJson(string text, ICollection<string> enemyIds)
        {
            StageValidationResult r = new StageValidationResult();
            if (string.IsNullOrEmpty(text))
            {
                r.Errors.Add("empty input");
                return r;
            }
            if (text.Length > GameConstants.StageLimits.MaxJsonBytes)
            {
                r.Errors.Add("stage data too large");
                return r;
            }
            if (!MiniJson.TryParse(text, out object raw, out string error))
            {
                r.Errors.Add("invalid JSON: " + error);
                return r;
            }
            return Validate(raw, enemyIds);
        }

        /// <param name="raw">MiniJson の解析結果</param>
        /// <param name="enemyIds">既知の敵ID。null なら敵IDの存在確認を省略</param>
        public static StageValidationResult Validate(object raw, ICollection<string> enemyIds)
        {
            StageValidationResult result = new StageValidationResult();
            List<string> errors = result.Errors;
            Dictionary<string, object> obj = MiniJson.AsObject(raw);
            if (obj == null)
            {
                errors.Add("stage: must be an object");
                return result;
            }
            if (!MiniJson.TryGetInt(MiniJson.Get(obj, "formatVersion"), out int fv) || fv != GameConstants.StageFormatVersion)
            {
                errors.Add("formatVersion: unsupported (" + DescribeValue(MiniJson.Get(obj, "formatVersion")) + ")");
                return result;
            }
            StageData stage = new StageData();
            string id = MiniJson.Get(obj, "id") as string;
            if (id == null || !IdPattern.IsMatch(id)) errors.Add("id: invalid");
            else stage.Id = id;

            stage.Name = ValidateText(obj, "name", GameConstants.StageLimits.NameMaxChars, errors, true);
            stage.Summary = ValidateText(obj, "summary", GameConstants.StageLimits.SummaryMaxChars, errors, false);
            stage.SpellName = ValidateText(obj, "spellName", GameConstants.StageLimits.NameMaxChars, errors, false);

            string enemyId = MiniJson.Get(obj, "enemyId") as string;
            if (enemyId == null || !IdPattern.IsMatch(enemyId)) errors.Add("enemyId: invalid");
            else if (enemyIds != null && !enemyIds.Contains(enemyId)) errors.Add("enemyId: unknown (" + enemyId + ")");
            else stage.EnemyId = enemyId;

            if (!IntInRange(MiniJson.Get(obj, "editTimeSec"), GameConstants.StageLimits.MinEditSec, GameConstants.StageLimits.MaxEditSec, out int editSec)) errors.Add("editTimeSec: out of range");
            else stage.EditTimeSec = editSec;
            if (!IntInRange(MiniJson.Get(obj, "dragRadius"), GameConstants.StageLimits.MinDragRadius, GameConstants.StageLimits.MaxDragRadius, out int dragRadius)) errors.Add("dragRadius: out of range");
            else stage.DragRadius = dragRadius;

            List<object> rawBarrages = MiniJson.AsArray(MiniJson.Get(obj, "barrages"));
            if (rawBarrages == null || rawBarrages.Count < 1 || rawBarrages.Count > GameConstants.StageLimits.MaxBarrages)
            {
                errors.Add("barrages: count out of range");
            }
            else
            {
                int totalBullets = 0;
                for (int bi = 0; bi < rawBarrages.Count; bi++)
                {
                    BarrageData b = ValidateBarrage(rawBarrages[bi], bi, errors, ref totalBullets);
                    if (b != null) stage.Barrages.Add(b);
                }
                if (totalBullets > GameConstants.StageLimits.MaxTotalBullets)
                {
                    errors.Add("barrages: total bullets exceed " + GameConstants.StageLimits.MaxTotalBullets.ToString(CultureInfo.InvariantCulture));
                }
            }
            if (errors.Count > 0)
            {
                return result;
            }
            result.Ok = true;
            result.Stage = stage;
            return result;
        }

        private static BarrageData ValidateBarrage(object rawBarrage, int bi, List<string> errors, ref int totalBullets)
        {
            string f = "barrages[" + bi.ToString(CultureInfo.InvariantCulture) + "]";
            Dictionary<string, object> rb = MiniJson.AsObject(rawBarrage);
            if (rb == null)
            {
                errors.Add(f + ": must be object");
                return null;
            }
            string type = MiniJson.Get(rb, "type") as string;
            if (BulletTypeDef.Find(type) == null)
            {
                errors.Add(f + ".type: invalid");
                return null;
            }
            if (!IntInRange(MiniJson.Get(rb, "count"), GameConstants.StageLimits.MinCount, GameConstants.StageLimits.MaxCount, out int count))
            {
                errors.Add(f + ".count: out of range");
                return null;
            }
            totalBullets += count;
            int minInt = MinIntervalTicksFor(type);
            if (!IntInRange(MiniJson.Get(rb, "intervalTicks"), minInt, GameConstants.StageLimits.MaxIntervalTicks, out int interval))
            {
                errors.Add(f + ".intervalTicks: out of range (min " + minInt.ToString(CultureInfo.InvariantCulture) + ")");
                return null;
            }
            if (!IntInRange(MiniJson.Get(rb, "startDelayTicks"), 0, GameConstants.StageLimits.MaxStartDelayTicks, out int delay))
            {
                errors.Add(f + ".startDelayTicks: out of range");
                return null;
            }
            List<object> rawPoints = MiniJson.AsArray(MiniJson.Get(rb, "points"));
            if (rawPoints == null || rawPoints.Count < GameConstants.StageLimits.MinPoints || rawPoints.Count > GameConstants.StageLimits.MaxPoints)
            {
                errors.Add(f + ".points: count out of range");
                return null;
            }
            BarrageData b = new BarrageData { Type = type, Count = count, IntervalTicks = interval, StartDelayTicks = delay };
            for (int pi = 0; pi < rawPoints.Count; pi++)
            {
                List<object> p = MiniJson.AsArray(rawPoints[pi]);
                if (p == null || p.Count != 2
                    || !IntInRange(p[0], FieldMinX, FieldMaxX, out int x)
                    || !IntInRange(p[1], FieldMinY, FieldMaxY, out int y))
                {
                    errors.Add(f + ".points[" + pi.ToString(CultureInfo.InvariantCulture) + "]: invalid or outside field");
                    return null;
                }
                b.Points.Add(new IntPoint(x, y));
            }
            // 隣接起点の重複は形状が退化するため拒否
            for (int pi = 1; pi < b.Points.Count; pi++)
            {
                if (b.Points[pi].Equals(b.Points[pi - 1]))
                {
                    errors.Add(f + ".points[" + pi.ToString(CultureInfo.InvariantCulture) + "]: duplicated with previous");
                    return null;
                }
            }
            return b;
        }

        private static bool IntInRange(object value, int min, int max, out int result)
        {
            return MiniJson.TryGetInt(value, out result) && result >= min && result <= max;
        }

        /// <summary>制御文字（改行・タブ含む）・行区切り文字を含むか</summary>
        public static bool ContainsControlChars(string s)
        {
            const char C0End = '\u001F';
            const char DeleteChar = '\u007F';
            const char C1End = '\u009F';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c <= C0End || (c >= DeleteChar && c <= C1End) || c == '\u2028' || c == '\u2029')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>コードポイント数（JS の Array.from(str).length 相当）</summary>
        public static int CodePointLength(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                }
                n++;
            }
            return n;
        }

        private static LocalizedText ValidateText(Dictionary<string, object> obj, string field, int maxChars, List<string> errors, bool required)
        {
            object value = MiniJson.Get(obj, field);
            if (value == null)
            {
                if (required) errors.Add(field + ": required");
                return LocalizedText.Empty;
            }
            if (value is string s)
            {
                string checkedText = CheckString(s, maxChars, field, errors, required);
                return checkedText == null ? LocalizedText.Empty : new LocalizedText(checkedText);
            }
            if (value is Dictionary<string, object> dict)
            {
                Dictionary<string, string> outTexts = new Dictionary<string, string>(StringComparer.Ordinal);
                bool any = false;
                for (int i = 0; i < LocalizedText.Languages.Length; i++)
                {
                    string lang = LocalizedText.Languages[i];
                    if (!dict.TryGetValue(lang, out object lv))
                    {
                        continue;
                    }
                    string sub = field + "." + lang;
                    string text;
                    if (lv == null)
                    {
                        text = string.Empty;
                    }
                    else if (lv is string ls)
                    {
                        text = CheckString(ls, maxChars, sub, errors, false);
                        if (text == null) return LocalizedText.Empty;
                    }
                    else
                    {
                        errors.Add(sub + ": must be string or localized object");
                        return LocalizedText.Empty;
                    }
                    outTexts[lang] = text;
                    if (text.Length > 0) any = true;
                }
                if (!any && required)
                {
                    errors.Add(field + ": no localized text");
                    return LocalizedText.Empty;
                }
                return new LocalizedText(outTexts);
            }
            errors.Add(field + ": must be string or localized object");
            return LocalizedText.Empty;
        }

        private static string CheckString(string value, int maxChars, string field, List<string> errors, bool required)
        {
            int len = CodePointLength(value);
            if (len > maxChars)
            {
                errors.Add(field + ": too long (" + len.ToString(CultureInfo.InvariantCulture) + ">" + maxChars.ToString(CultureInfo.InvariantCulture) + ")");
                return null;
            }
            if (ContainsControlChars(value))
            {
                errors.Add(field + ": control characters are not allowed");
                return null;
            }
            if (required && value.Trim().Length == 0)
            {
                errors.Add(field + ": empty");
                return null;
            }
            return value;
        }

        private static string DescribeValue(object v)
        {
            if (v == null) return "undefined";
            if (v is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            return v.ToString();
        }
    }

    /// <summary>
    /// 軌道線の形状。表示用の線と弾の実際の進行経路は必ずこのサンプル列を共有する。
    /// 通常弾・巨弾：一様 Catmull-Rom スプライン／直線弾：折れ線。四則演算と平方根のみで Web版と同一の値になる。
    /// </summary>
    public sealed class TrajectoryPath
    {
        public readonly int Count;
        public readonly double[] Xs;
        public readonly double[] Ys;
        public readonly double[] Cum;
        public readonly double Total;
        public readonly double EndDirX;
        public readonly double EndDirY;

        private const double Half = 0.5;

        public TrajectoryPath(IList<IntPoint> points, bool smooth)
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("TrajectoryPath: points must contain >= 2 entries (got " + (points == null ? "null" : points.Count.ToString(CultureInfo.InvariantCulture)) + ")", nameof(points));
            }
            int n = points.Count;
            int perSeg = smooth ? GameConstants.Path.SmoothSamplesPerSegment : 1;
            Count = (n - 1) * perSeg + 1;
            Xs = new double[Count];
            Ys = new double[Count];
            int w = 0;
            for (int i = 0; i < n - 1; i++)
            {
                IntPoint p1 = points[i];
                IntPoint p2 = points[i + 1];
                if (!smooth)
                {
                    Xs[w] = p1.X;
                    Ys[w] = p1.Y;
                    w++;
                    continue;
                }
                double p0x, p0y, p3x, p3y;
                if (i > 0) { p0x = points[i - 1].X; p0y = points[i - 1].Y; }
                else { p0x = 2 * p1.X - p2.X; p0y = 2 * p1.Y - p2.Y; }
                if (i + 2 < n) { p3x = points[i + 2].X; p3y = points[i + 2].Y; }
                else { p3x = 2 * p2.X - p1.X; p3y = 2 * p2.Y - p1.Y; }
                for (int k = 0; k < perSeg; k++)
                {
                    double t = (double)k / perSeg;
                    double t2 = t * t;
                    double t3 = t2 * t;
                    Xs[w] = CatmullRom(p0x, p1.X, p2.X, p3x, t, t2, t3);
                    Ys[w] = CatmullRom(p0y, p1.Y, p2.Y, p3y, t, t2, t3);
                    w++;
                }
            }
            IntPoint last = points[n - 1];
            Xs[w] = last.X;
            Ys[w] = last.Y;
            Cum = new double[Count];
            Cum[0] = 0;
            for (int i = 1; i < Count; i++)
            {
                double dx = Xs[i] - Xs[i - 1];
                double dy = Ys[i] - Ys[i - 1];
                Cum[i] = Cum[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }
            Total = Cum[Count - 1];
            EndDirX = -1;
            EndDirY = 0;
            for (int i = Count - 1; i > 0; i--)
            {
                double dx = Xs[i] - Xs[i - 1];
                double dy = Ys[i] - Ys[i - 1];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > GameConstants.Path.MinSegmentLength)
                {
                    EndDirX = dx / len;
                    EndDirY = dy / len;
                    break;
                }
            }
        }

        /// <summary>Web版と演算順序を一致させた Catmull-Rom 補間</summary>
        private static double CatmullRom(double p0, double p1, double p2, double p3, double t, double t2, double t3)
        {
            return Half * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        /// <summary>距離 d の位置。seg は前回の区間 index（単調増加移動の高速化用、結果は seg に依存しない）。</summary>
        public void PositionAt(double d, ref int seg, out double x, out double y)
        {
            if (d <= 0)
            {
                x = Xs[0];
                y = Ys[0];
                seg = 0;
                return;
            }
            if (d >= Total)
            {
                double over = d - Total;
                x = Xs[Count - 1] + EndDirX * over;
                y = Ys[Count - 1] + EndDirY * over;
                seg = Count - 1;
                return;
            }
            int i = seg;
            if (i < 0 || i >= Count - 1 || Cum[i] > d)
            {
                int lo = 0;
                int hi = Count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (Cum[mid] <= d) lo = mid; else hi = mid;
                }
                i = lo;
            }
            else
            {
                while (i < Count - 2 && Cum[i + 1] <= d) i++;
            }
            double segLen = Cum[i + 1] - Cum[i];
            double r = segLen > GameConstants.Path.MinInterpolationLength ? (d - Cum[i]) / segLen : 0;
            x = Xs[i] + (Xs[i + 1] - Xs[i]) * r;
            y = Ys[i] + (Ys[i + 1] - Ys[i]) * r;
            seg = i;
        }

        /// <summary>
        /// 手描きストロークから等間隔の起点列を生成する（エディター用）。距離不足なら空リスト。
        /// </summary>
        public static List<IntPoint> ResampleStroke(IList<double> strokeXs, IList<double> strokeYs, double spacing, int maxPoints)
        {
            List<IntPoint> result = new List<IntPoint>();
            if (strokeXs == null || strokeYs == null || strokeXs.Count != strokeYs.Count || strokeXs.Count < 2 || !(spacing > 0) || maxPoints < 2)
            {
                return result;
            }
            int n = strokeXs.Count;
            double total = 0;
            for (int i = 1; i < n; i++)
            {
                double dx = strokeXs[i] - strokeXs[i - 1];
                double dy = strokeYs[i] - strokeYs[i - 1];
                total += Math.Sqrt(dx * dx + dy * dy);
            }
            if (total <= 0)
            {
                return result;
            }
            double step = spacing;
            if (Math.Floor(total / step) + 1 > maxPoints) step = total / (maxPoints - 1);
            result.Add(new IntPoint(Fnv1a.RoundHalfUp(strokeXs[0]), Fnv1a.RoundHalfUp(strokeYs[0])));
            double nextD = step;
            double acc = 0;
            for (int i = 1; i < n && result.Count < maxPoints; i++)
            {
                double ax = strokeXs[i - 1];
                double ay = strokeYs[i - 1];
                double dx = strokeXs[i] - ax;
                double dy = strokeYs[i] - ay;
                double len = Math.Sqrt(dx * dx + dy * dy);
                while (len > 0 && acc + len >= nextD && result.Count < maxPoints)
                {
                    double r = (nextD - acc) / len;
                    result.Add(new IntPoint(Fnv1a.RoundHalfUp(ax + dx * r), Fnv1a.RoundHalfUp(ay + dy * r)));
                    nextD += step;
                }
                acc += len;
            }
            int ex = Fnv1a.RoundHalfUp(strokeXs[n - 1]);
            int ey = Fnv1a.RoundHalfUp(strokeYs[n - 1]);
            IntPoint lastPt = result[result.Count - 1];
            double tdx = ex - lastPt.X;
            double tdy = ey - lastPt.Y;
            if (Math.Sqrt(tdx * tdx + tdy * tdy) >= step * Half)
            {
                if (result.Count < maxPoints) result.Add(new IntPoint(ex, ey));
                else result[result.Count - 1] = new IntPoint(ex, ey);
            }
            if (result.Count < 2)
            {
                result.Clear();
            }
            return result;
        }
    }
}

/*
## このアセットの役割
StageValidator は外部ステージの検証・正規化、TrajectoryPath は軌道線（曲線・折れ線）の形状計算とストロークの再標本化を行います。

## 他アセットとの依存関係
StageData / MiniJson / GameConstants に依存。BattleSimulation・FieldGraphic（表示）・StageEditorScreen から使用されます。Web版 stageData.js / trajectory.js と同一の規則・数値です。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
表示用の線と弾の進路は同じ TrajectoryPath を使うため、見た目と当たり判定がずれません。float（Vector2）に変換するのは描画直前だけにしてください。
*/
