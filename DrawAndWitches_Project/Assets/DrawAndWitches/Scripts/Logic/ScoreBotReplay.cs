using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;

namespace DrawAndWitches.Logic
{
    public struct StageScore
    {
        public int Total;
        public int Base;
        public int CancelPoints;
        public int DamagePoints;
        public int OpsPoints;
    }

    public struct EndlessScore
    {
        public int Total;
        public int Sum;
        public int Bonus;
        public int Cleared;
    }

    /// <summary>
    /// スコア式（企画書で未確定のため Web版で補完した式をそのまま移植）。
    /// クリア: 基礎 + 相殺数×100 + ダメージ×300 + max(0, 2000 − 操作数×200)／敗北: 0。
    /// エンドレス: 合計 + 500 × n(n+1)/2。
    /// </summary>
    public static class ScoreCalculator
    {
        public static StageScore ComputeStage(BattleResult result)
        {
            if (result == null || result.Outcome != BattleOutcome.Clear)
            {
                return new StageScore();
            }
            int ops = Math.Max(0, result.Ops);
            int cancels = Math.Max(0, result.Cancels);
            int damage = Math.Max(0, result.Damage);
            StageScore s = new StageScore
            {
                Base = GameConstants.Score.ClearBase,
                CancelPoints = cancels * GameConstants.Score.PerCancel,
                DamagePoints = damage * GameConstants.Score.PerDamage,
                OpsPoints = Math.Max(0, GameConstants.Score.OpsBonusMax - ops * GameConstants.Score.OpsPenalty),
            };
            s.Total = s.Base + s.CancelPoints + s.DamagePoints + s.OpsPoints;
            return s;
        }

        public static EndlessScore ComputeEndless(int stageScoresSum, int clearedCount)
        {
            int sum = Math.Max(0, stageScoresSum);
            int cleared = Math.Max(0, clearedCount);
            int bonus = GameConstants.Score.EndlessStep * (cleared * (cleared + 1) / 2);
            return new EndlessScore { Total = sum + bonus, Sum = sum, Bonus = bonus, Cleared = cleared };
        }

        /// <summary>次のクリアで得られる周回ボーナス（HUD 表示用）</summary>
        public static int NextEndlessBonus(int clearedCount)
        {
            return GameConstants.Score.EndlessStep * (Math.Max(0, clearedCount) + 1);
        }
    }

    public sealed class BotPlan
    {
        public List<PointMove> Moves;
        public BattleResult Result;
        public double Fitness;
        public int Evaluations;
    }

    /// <summary>探索途中の進捗（UI 表示用）</summary>
    public struct BotProgress
    {
        public int Evaluations;
        public double Fitness;
        public bool Clear;
    }

    /// <summary>
    /// 決定論的ルールで起点の移動先を探索する bot（Web版 BotPlanner の移植。同一 seed で同一計画）。
    /// 局所探索・最初改善法: 可動起点を seed でシャッフルし、8方向×半径比の候補と微調整候補を
    /// BattleSimulation のヘッドレス実行で評価して、改善した候補を採用する。
    /// </summary>
    public sealed class BotPlanner
    {
        private readonly StageData stage;
        private readonly uint seed;
        private readonly List<BotPoint> points = new List<BotPoint>();
        // キー (b<<16 | p) → 移動先。適用順は (b,p) 昇順に固定する
        private readonly SortedDictionary<int, IntPoint> moves = new SortedDictionary<int, IntPoint>();
        private const int KeyShift = 16;
        private const int KeyMask = 0xFFFF;

        private struct BotPoint
        {
            public int B;
            public int P;
            public int Ox;
            public int Oy;
        }

        public int Evaluations { get; private set; }
        public BotPlan Best { get; private set; }

        public BotPlanner(StageData stage, uint seed)
        {
            if (stage == null || stage.Barrages == null || stage.Barrages.Count == 0)
            {
                throw new ArgumentException("BotPlanner: invalid stage", nameof(stage));
            }
            this.stage = stage;
            this.seed = seed;
            for (int bi = 0; bi < stage.Barrages.Count; bi++)
            {
                List<IntPoint> pts = stage.Barrages[bi].Points;
                for (int pi = 1; pi < pts.Count; pi++)
                {
                    points.Add(new BotPoint { B = bi, P = pi, Ox = pts[pi].X, Oy = pts[pi].Y });
                }
            }
            DeterministicRng rng = new DeterministicRng(seed);
            for (int i = points.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                BotPoint tmp = points[i];
                points[i] = points[j];
                points[j] = tmp;
            }
        }

        public static double FitnessOf(BattleResult result, double proximity)
        {
            return -result.Hits * GameConstants.Bot.HitPenalty
                - proximity * GameConstants.Bot.ProximityWeight
                + result.Cancels * GameConstants.Bot.CancelWeight
                + result.Damage * GameConstants.Bot.DamageWeight
                - result.Ops * GameConstants.Bot.OpsWeight;
        }

        private static int KeyOf(int b, int p)
        {
            return (b << KeyShift) | p;
        }

        /// <param name="overrideKey">一時的に差し替えるキー（-1 なら無し）</param>
        /// <param name="hasOverride">false なら overrideKey を「原位置へ戻す（除外）」として扱う</param>
        private List<PointMove> MovesArray(int overrideKey, bool hasOverride, IntPoint overrideValue)
        {
            List<PointMove> arr = new List<PointMove>(moves.Count + 1);
            bool inserted = overrideKey < 0;
            foreach (KeyValuePair<int, IntPoint> kv in moves)
            {
                if (!inserted && overrideKey < kv.Key)
                {
                    if (hasOverride) arr.Add(ToMove(overrideKey, overrideValue));
                    inserted = true;
                }
                if (kv.Key == overrideKey)
                {
                    inserted = true;
                    if (hasOverride) arr.Add(ToMove(kv.Key, overrideValue));
                    continue;
                }
                arr.Add(ToMove(kv.Key, kv.Value));
            }
            if (!inserted && hasOverride)
            {
                arr.Add(ToMove(overrideKey, overrideValue));
            }
            return arr;
        }

        private static PointMove ToMove(int key, IntPoint v)
        {
            return new PointMove { B = key >> KeyShift, P = key & KeyMask, X = v.X, Y = v.Y };
        }

        private struct Evaluation
        {
            public BattleResult Result;
            public double Fitness;
        }

        private Evaluation Evaluate(List<PointMove> list)
        {
            Evaluations++;
            BattleSimulation sim = new BattleSimulation(stage, seed, new BattleOptions { ContinueAfterHit = true, RecordEvents = false, TrackProximity = true });
            sim.ApplyPositionsDirect(list);
            sim.SkipEditPhase();
            BattleResult result = sim.RunToEnd();
            return new Evaluation { Result = result, Fitness = FitnessOf(result, sim.ProximityPenalty(GameConstants.Bot.ProximityCap)) };
        }

        /// <summary>
        /// 探索を段階的に進める列挙子（Coroutine から少しずつ進めて UI を止めない）。
        /// 完了後は Best に計画が入る。
        /// </summary>
        public IEnumerator<BotProgress> PlanSteps()
        {
            Evaluation current = Evaluate(new List<PointMove>());
            int iter = 0;
            bool improved = true;
            int r = stage.DragRadius;
            List<IntPoint> candidates = new List<IntPoint>();
            List<bool> candidateIsReset = new List<bool>();
            while (improved && iter < GameConstants.Bot.MaxIterations && Evaluations < GameConstants.Bot.MaxEvaluations)
            {
                improved = false;
                iter++;
                for (int pti = 0; pti < points.Count; pti++)
                {
                    if (Evaluations >= GameConstants.Bot.MaxEvaluations) break;
                    BotPoint pt = points[pti];
                    int key = KeyOf(pt.B, pt.P);
                    candidates.Clear();
                    candidateIsReset.Clear();
                    bool hasCur = moves.TryGetValue(key, out IntPoint curPos);
                    if (hasCur)
                    {
                        candidates.Add(default);
                        candidateIsReset.Add(true);
                    }
                    double[] ratios = GameConstants.Bot.RingRatios;
                    for (int ri = 0; ri < ratios.Length; ri++)
                    {
                        for (int di = 0; di < GameConstants.Bot.DirX.Length; di++)
                        {
                            int tx = Fnv1a.RoundHalfUp(pt.Ox + GameConstants.Bot.DirX[di] * ratios[ri] * r);
                            int ty = Fnv1a.RoundHalfUp(pt.Oy + GameConstants.Bot.DirY[di] * ratios[ri] * r);
                            candidates.Add(BattleSimulation.ClampPointPosition(pt.Ox, pt.Oy, tx, ty, r));
                            candidateIsReset.Add(false);
                        }
                    }
                    if (hasCur)
                    {
                        double[] steps = GameConstants.Bot.LocalSteps;
                        for (int si = 0; si < steps.Length; si++)
                        {
                            for (int di = 0; di < GameConstants.Bot.DirX.Length; di++)
                            {
                                int tx = Fnv1a.RoundHalfUp(curPos.X + GameConstants.Bot.DirX[di] * steps[si]);
                                int ty = Fnv1a.RoundHalfUp(curPos.Y + GameConstants.Bot.DirY[di] * steps[si]);
                                candidates.Add(BattleSimulation.ClampPointPosition(pt.Ox, pt.Oy, tx, ty, r));
                                candidateIsReset.Add(false);
                            }
                        }
                    }
                    bool found = false;
                    bool bestIsReset = false;
                    IntPoint bestCand = default;
                    Evaluation bestEval = default;
                    for (int ci = 0; ci < candidates.Count; ci++)
                    {
                        bool isReset = candidateIsReset[ci];
                        IntPoint c = candidates[ci];
                        bool hasNow = moves.TryGetValue(key, out IntPoint now);
                        if (!isReset && hasNow && c.Equals(now)) continue;
                        Evaluation e = Evaluate(MovesArray(key, !isReset, c));
                        if (e.Fitness > (found ? bestEval.Fitness : current.Fitness))
                        {
                            bestEval = e;
                            bestCand = c;
                            bestIsReset = isReset;
                            found = true;
                        }
                        if (ci % GameConstants.Bot.EvalsPerChunk == 0)
                        {
                            yield return new BotProgress { Evaluations = Evaluations, Fitness = current.Fitness, Clear = current.Result.Hits == 0 };
                        }
                    }
                    if (found)
                    {
                        if (bestIsReset) moves.Remove(key);
                        else moves[key] = bestCand;
                        current = bestEval;
                        improved = true;
                    }
                }
            }
            Best = new BotPlan { Moves = MovesArray(-1, false, default), Result = current.Result, Fitness = current.Fitness, Evaluations = Evaluations };
        }

        /// <summary>同期実行（テスト・ツール用）</summary>
        public BotPlan PlanSync()
        {
            IEnumerator<BotProgress> it = PlanSteps();
            while (it.MoveNext())
            {
            }
            return Best;
        }

        /// <summary>計画を人間と同じ GRAB → MOVE×n → RELEASE のコマンド列へ変換する</summary>
        public static List<InputCommand> BuildCommands(BotPlan plan, StageData stage)
        {
            List<InputCommand> commands = new List<InputCommand>();
            if (plan == null || plan.Moves == null || stage == null)
            {
                return commands;
            }
            int editTicks = stage.EditTimeSec * GameConstants.Timing.TicksPerSecond;
            int n = plan.Moves.Count;
            if (n == 0)
            {
                return commands;
            }
            const int Steps = GameConstants.Bot.MoveSteps;
            int available = editTicks - GameConstants.Bot.FirstActionTick - 2;
            int perAction = Math.Max(Steps + 2, Math.Min(GameConstants.Bot.ActionTicks, FloorDiv(available, n)));
            int stepTicks = Math.Max(1, FloorDiv(perAction - 2, Steps));
            for (int k = 0; k < n; k++)
            {
                PointMove m = plan.Moves[k];
                IntPoint o = stage.Barrages[m.B].Points[m.P];
                int t0 = GameConstants.Bot.FirstActionTick + k * perAction;
                commands.Add(new InputCommand(t0, GameConstants.Cmd.Grab, m.B, m.P));
                for (int s = 1; s <= Steps; s++)
                {
                    double ratio = (double)s / Steps;
                    int x = s == Steps ? m.X : Fnv1a.RoundHalfUp(o.X + (m.X - o.X) * ratio);
                    int y = s == Steps ? m.Y : Fnv1a.RoundHalfUp(o.Y + (m.Y - o.Y) * ratio);
                    commands.Add(new InputCommand(t0 + s * stepTicks, GameConstants.Cmd.Move, x, y));
                }
                commands.Add(new InputCommand(t0 + Steps * stepTicks + 1, GameConstants.Cmd.Release, 0, 0));
            }
            return commands;
        }

        private static int FloorDiv(int a, int b)
        {
            return (int)Math.Floor((double)a / b);
        }
    }

    public enum ReplayMode
    {
        Story,
        Endless,
        Shared,
        Custom,
    }

    public sealed class ReplayResultRecord
    {
        public string Outcome;
        public int Ops;
        public int Cancels;
        public int Damage;
        public int MaxChain;
        public int EndTick;
        public string Hash;
    }

    public sealed class ReplayData
    {
        public uint Seed;
        public string Mode;
        public string Player;
        public long CreatedAt;
        public StageData Stage;
        public List<InputCommand> Commands = new List<InputCommand>();
        public ReplayResultRecord Result;
        public int Score;
    }

    public struct ReplayParseResult
    {
        public bool Ok;
        /// <summary>empty / tooLarge / prefix / decode / json / version / logic / field / stage / commands</summary>
        public string Error;
        public string Detail;
        public ReplayData Replay;
    }

    public struct ReplayVerifyResult
    {
        public bool Ok;
        public BattleResult Actual;
        public string Error;
    }

    /// <summary>
    /// リプレイのシリアライズ／インポート検証／再シミュレーション照合（Web版 replay.js と同じ形式）。
    /// 形式: "DWR1:" + base64url(UTF-8 JSON)。Web版で書き出した文字列をそのまま読み込める。
    /// </summary>
    public static class ReplaySerializer
    {
        public static readonly string[] Modes = { "story", "endless", "shared", "custom" };
        public static readonly string[] Players = { "human", "bot" };
        private static readonly Regex HashPattern = new Regex("^[0-9a-f]{8}$", RegexOptions.CultureInvariant);
        private const int CommandFields = 4;

        public static long NowUnixMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public static string Serialize(ReplayData rec)
        {
            if (rec == null || rec.Stage == null || rec.Result == null || rec.Commands == null)
            {
                throw new ArgumentException("ReplaySerializer.Serialize: replay, stage, result and commands are required", nameof(rec));
            }
            List<object> cmds = new List<object>(rec.Commands.Count);
            for (int i = 0; i < rec.Commands.Count; i++)
            {
                InputCommand c = rec.Commands[i];
                cmds.Add(new List<object> { c.T, c.Op, c.A, c.B });
            }
            JsonObject obj = new JsonObject()
                .Add("v", GameConstants.ReplayFormatVersion)
                .Add("logic", GameConstants.LogicVersion)
                .Add("seed", rec.Seed)
                .Add("mode", rec.Mode)
                .Add("player", rec.Player)
                .Add("createdAt", rec.CreatedAt > 0 ? rec.CreatedAt : NowUnixMilliseconds())
                .Add("stage", rec.Stage.ToJson())
                .Add("cmds", cmds)
                .Add("result", new JsonObject()
                    .Add("outcome", rec.Result.Outcome)
                    .Add("ops", rec.Result.Ops)
                    .Add("cancels", rec.Result.Cancels)
                    .Add("damage", rec.Result.Damage)
                    .Add("maxChain", rec.Result.MaxChain)
                    .Add("endTick", rec.Result.EndTick)
                    .Add("hash", rec.Result.Hash))
                .Add("score", rec.Score);
            return GameConstants.Replay.Prefix + Base64Url.EncodeUtf8(MiniJson.Serialize(obj));
        }

        public static ReplayResultRecord ToRecord(BattleResult r)
        {
            return new ReplayResultRecord
            {
                Outcome = BattleResult.OutcomeToString(r.Outcome),
                Ops = r.Ops,
                Cancels = r.Cancels,
                Damage = r.Damage,
                MaxChain = r.MaxChain,
                EndTick = r.EndTick,
                Hash = r.Hash,
            };
        }

        private static ReplayParseResult Fail(string code, string detail)
        {
            return new ReplayParseResult { Ok = false, Error = code, Detail = detail ?? string.Empty };
        }

        private static bool Contains(string[] arr, string v)
        {
            return v != null && Array.IndexOf(arr, v) >= 0;
        }

        /// <summary>外部入力の文字列を検証して読み込む（再生前に Verify を必ず実行すること）</summary>
        public static ReplayParseResult Parse(string text, ICollection<string> enemyIds)
        {
            if (text == null) return Fail("empty", null);
            string s = text.Trim();
            if (s.Length == 0) return Fail("empty", null);
            if (s.Length > GameConstants.Replay.MaxStringLength) return Fail("tooLarge", null);
            if (!s.StartsWith(GameConstants.Replay.Prefix, StringComparison.Ordinal)) return Fail("prefix", null);
            if (!Base64Url.TryDecodeUtf8(s.Substring(GameConstants.Replay.Prefix.Length), out string json, out string decodeError)) return Fail("decode", decodeError);
            if (!MiniJson.TryParse(json, out object root, out string jsonError)) return Fail("json", jsonError);
            Dictionary<string, object> obj = MiniJson.AsObject(root);
            if (obj == null) return Fail("json", "root is not object");
            if (!MiniJson.TryGetInt(MiniJson.Get(obj, "v"), out int v) || v != GameConstants.ReplayFormatVersion) return Fail("version", Convert.ToString(MiniJson.Get(obj, "v"), CultureInfo.InvariantCulture));
            if (!MiniJson.TryGetInt(MiniJson.Get(obj, "logic"), out int logic) || logic != GameConstants.LogicVersion) return Fail("logic", Convert.ToString(MiniJson.Get(obj, "logic"), CultureInfo.InvariantCulture));
            if (!MiniJson.TryGetLong(MiniJson.Get(obj, "seed"), out long seed) || seed < 0 || seed > uint.MaxValue) return Fail("field", "seed");
            string mode = MiniJson.Get(obj, "mode") as string;
            if (!Contains(Modes, mode)) return Fail("field", "mode");
            string player = MiniJson.Get(obj, "player") as string;
            if (!Contains(Players, player)) return Fail("field", "player");
            if (!MiniJson.TryGetLong(MiniJson.Get(obj, "createdAt"), out long createdAt) || createdAt < 0) return Fail("field", "createdAt");
            if (!MiniJson.TryGetInt(MiniJson.Get(obj, "score"), out int score) || score < 0) return Fail("field", "score");
            StageValidationResult sv = StageValidator.Validate(MiniJson.Get(obj, "stage"), enemyIds);
            if (!sv.Ok) return Fail("stage", sv.ErrorSummary);
            StageData stage = sv.Stage;
            List<object> rawCmds = MiniJson.AsArray(MiniJson.Get(obj, "cmds"));
            if (rawCmds == null || rawCmds.Count > GameConstants.Input.MaxCommands) return Fail("commands", "count");
            int editTicks = stage.EditTimeSec * GameConstants.Timing.TicksPerSecond;
            ReplayData replay = new ReplayData { Seed = (uint)seed, Mode = mode, Player = player, CreatedAt = createdAt, Stage = stage, Score = score };
            int lastT = 0;
            int[] f = new int[CommandFields];
            for (int i = 0; i < rawCmds.Count; i++)
            {
                string at = "index " + i.ToString(CultureInfo.InvariantCulture) + ": ";
                List<object> c = MiniJson.AsArray(rawCmds[i]);
                if (c == null || c.Count != CommandFields) return Fail("commands", at + "shape");
                for (int k = 0; k < CommandFields; k++)
                {
                    if (!MiniJson.TryGetInt(c[k], out f[k])) return Fail("commands", at + "shape");
                }
                int t = f[0], op = f[1], a = f[2], b = f[3];
                if (t < 0 || t >= editTicks) return Fail("commands", at + "tick out of range");
                if (t < lastT) return Fail("commands", at + "not sorted");
                if (op != GameConstants.Cmd.Grab && op != GameConstants.Cmd.Move && op != GameConstants.Cmd.Release && op != GameConstants.Cmd.Undo) return Fail("commands", at + "op");
                if (op == GameConstants.Cmd.Grab && (a < 0 || a >= stage.Barrages.Count || b < 1 || b >= stage.Barrages[a].Points.Count)) return Fail("commands", at + "grab target");
                if (op == GameConstants.Cmd.Move && (a < -GameConstants.World.Width || a > GameConstants.World.Width * 2 || b < -GameConstants.World.Height || b > GameConstants.World.Height * 2)) return Fail("commands", at + "move range");
                lastT = t;
                replay.Commands.Add(new InputCommand(t, op, a, b));
            }
            Dictionary<string, object> r = MiniJson.AsObject(MiniJson.Get(obj, "result"));
            string outcome = MiniJson.GetString(r, "outcome", null);
            string hash = MiniJson.GetString(r, "hash", null);
            if (r == null || (outcome != "clear" && outcome != "lose") || hash == null || !HashPattern.IsMatch(hash)
                || !NonNegativeInt(r, "ops", out int ops) || !NonNegativeInt(r, "cancels", out int cancels)
                || !NonNegativeInt(r, "damage", out int damage) || !NonNegativeInt(r, "maxChain", out int maxChain)
                || !NonNegativeInt(r, "endTick", out int endTick))
            {
                return Fail("field", "result");
            }
            replay.Result = new ReplayResultRecord { Outcome = outcome, Ops = ops, Cancels = cancels, Damage = damage, MaxChain = maxChain, EndTick = endTick, Hash = hash };
            return new ReplayParseResult { Ok = true, Replay = replay, Detail = string.Empty };
        }

        private static bool NonNegativeInt(Dictionary<string, object> obj, string key, out int value)
        {
            return MiniJson.TryGetInt(MiniJson.Get(obj, key), out value) && value >= 0;
        }

        /// <summary>再シミュレーションして記録結果と一致するか検証する</summary>
        public static ReplayVerifyResult Verify(ReplayData replay)
        {
            if (replay == null || replay.Stage == null || replay.Result == null)
            {
                return new ReplayVerifyResult { Ok = false, Error = "replay is incomplete" };
            }
            try
            {
                BattleResult actual = BattleSimulation.RunWithCommands(replay.Stage, replay.Seed, replay.Commands);
                ReplayResultRecord e = replay.Result;
                bool ok = BattleResult.OutcomeToString(actual.Outcome) == e.Outcome && actual.Hash == e.Hash && actual.Ops == e.Ops
                    && actual.Cancels == e.Cancels && actual.Damage == e.Damage && actual.MaxChain == e.MaxChain && actual.EndTick == e.EndTick;
                return new ReplayVerifyResult { Ok = ok, Actual = actual };
            }
            catch (ArgumentException ex)
            {
                return new ReplayVerifyResult { Ok = false, Error = ex.Message };
            }
            catch (InvalidOperationException ex)
            {
                return new ReplayVerifyResult { Ok = false, Error = ex.Message };
            }
        }
    }
}

/*
## このアセットの役割
ScoreCalculator（スコア式）、BotPlanner（決定論 bot の探索とコマンド化）、ReplaySerializer（リプレイ文字列の書き出し・検証・再シミュレーション照合）です。

## 他アセットとの依存関係
BattleSimulation / StageValidator / MiniJson / Base64Url に依存。BattleScreen・ResultScreen・RecordsScreen・ReplayStore から使用されます。リプレイ形式は Web版と完全互換です。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
BotPlanner.PlanSteps は数百〜数千回のヘッドレス実行を行います。必ず Coroutine で1フレームあたりの評価数を制限して進めてください（BotRunner が担当）。
*/
