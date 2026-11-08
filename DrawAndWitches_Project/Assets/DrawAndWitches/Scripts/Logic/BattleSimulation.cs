using System;
using System.Collections.Generic;
using DrawAndWitches.Core;

namespace DrawAndWitches.Logic
{
    public enum BattlePhase
    {
        Edit,
        Exec,
        Ended,
    }

    public enum BattleOutcome
    {
        None = 0,
        Clear = 1,
        Lose = 2,
    }

    public enum BulletState
    {
        Pending = 0,
        Active = 1,
        Dead = 2,
    }

    /// <summary>弾の消滅原因（ハッシュに数値で混ぜるため値を固定）</summary>
    public enum DeathCause
    {
        None = 0,
        Collide = 1,
        Explosion = 2,
        Enemy = 3,
        Out = 4,
        Player = 5,
        Base = 6,
    }

    public enum SimEventType
    {
        Grab,
        Release,
        Undo,
        ExecStart,
        Spawn,
        PlayerHit,
        BaseHit,
        EnemyHit,
        Collide,
        Chain,
        Explosion,
        Out,
        End,
    }

    /// <summary>1tick中に発生したイベント（表示・効果音用。ロジック結果には影響しない）</summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public int A;
        public int B;
        public int C;
    }

    /// <summary>入力コマンド（t は適用 tick）</summary>
    public struct InputCommand
    {
        public int T;
        public int Op;
        public int A;
        public int B;

        public InputCommand(int t, int op, int a, int b)
        {
            T = t;
            Op = op;
            A = a;
            B = b;
        }
    }

    public sealed class Bullet
    {
        public int Id;
        public int Barrage;
        public int Index;
        public int SpawnTick;
        public BulletState State;
        public double D;
        public double X;
        public double Y;
        public int Seg;
        public int R;
        public double Speed;
        public int Damage;
        public bool Armed;
        public DeathCause Cause;
        public int DeathTick;
        public double MinGapSq;
    }

    public sealed class BarrageRuntime
    {
        public int Index;
        public string Type;
        public BulletTypeDef Def;
        public int Count;
        public int IntervalTicks;
        public int StartDelayTicks;
        public IntPoint[] OrigPoints;
        public IntPoint[] Points;
        public TrajectoryPath Path;
    }

    public sealed class ExplosionData
    {
        public int Id;
        public double X;
        public double Y;
        public int Level;
        public int StartTick;
    }

    public sealed class BattleResult
    {
        public BattleOutcome Outcome;
        public DeathCause LoseCause;
        public bool TimedOut;
        public int EndTick;
        public int ExecTicks;
        public int Ops;
        public int Cancels;
        public int Damage;
        public int MaxChain;
        public int Hits;
        public string Hash;

        public static string OutcomeToString(BattleOutcome o)
        {
            return o == BattleOutcome.Clear ? "clear" : (o == BattleOutcome.Lose ? "lose" : "none");
        }
    }

    public struct BattleOptions
    {
        public bool ContinueAfterHit;
        public bool RecordEvents;
        public bool TrackProximity;

        public static BattleOptions Default => new BattleOptions { RecordEvents = true };
    }

    /// <summary>bot 用の直接配置指定</summary>
    public struct PointMove
    {
        public int B;
        public int P;
        public int X;
        public int Y;
    }

    /// <summary>
    /// 1ステージ分の決定論的ゲームロジック（Web版 BattleSimulation の移植）。
    /// 固定ステップ（tick）だけで進み、実時間・フレームレート・暗黙の乱数を使わない。
    /// 入力は InputCommand で与え、適用 tick ごとに記録すれば完全に再現できる。
    /// </summary>
    public sealed class BattleSimulation
    {
        private const double HalfFactor = 0.5;

        public readonly StageData Stage;
        public readonly uint Seed;
        public readonly int DragRadius;
        public readonly int EditTicks;
        public readonly bool ContinueAfterHit;
        public readonly bool TrackProximity;
        public bool RecordEvents;

        public BattlePhase Phase { get; private set; }
        public int Tick { get; private set; }
        public int EditTick { get; private set; }
        public int ExecTick { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public DeathCause LoseCause { get; private set; }
        public int LoseBulletId { get; private set; }
        public bool TimedOut { get; private set; }
        public int EndTick { get; private set; }
        public uint Hash { get; private set; }
        public int LastSpawnTick { get; private set; }

        public int Ops { get; private set; }
        public int Cancels { get; private set; }
        public int DamageDealt { get; private set; }
        public int MaxChain { get; private set; }
        public int Hits { get; private set; }

        public readonly List<SimEvent> Events = new List<SimEvent>();
        public readonly BarrageRuntime[] Barrages;
        public readonly List<Bullet> Bullets = new List<Bullet>();
        public readonly List<ExplosionData> Explosions = new List<ExplosionData>();
        public int ExplosionCursor { get; private set; }

        // ドラッグ状態
        public bool DragActive { get; private set; }
        public int DragBarrage { get; private set; } = -1;
        public int DragPoint { get; private set; } = -1;
        private IntPoint[] dragSnapshot;

        private struct HistoryEntry
        {
            public int Barrage;
            public IntPoint[] Snapshot;
        }

        private readonly LinkedList<HistoryEntry> history = new LinkedList<HistoryEntry>();
        private readonly List<Bullet> active = new List<Bullet>();
        private IntPoint[] scratch = new IntPoint[GameConstants.StageLimits.MaxPoints];

        public BattleSimulation(StageData stage, uint seed, BattleOptions options)
        {
            if (stage == null || stage.Barrages == null || stage.Barrages.Count == 0)
            {
                throw new ArgumentException("BattleSimulation: stage is missing or has no barrages (validate the stage first)", nameof(stage));
            }
            Stage = stage;
            Seed = seed;
            // Web版と同じく seed から乱数器を作る（現行ロジックでは戦闘中に乱数を消費しないが、互換のため保持）
            Rng = new DeterministicRng(seed);
            ContinueAfterHit = options.ContinueAfterHit;
            TrackProximity = options.TrackProximity;
            RecordEvents = options.RecordEvents;
            DragRadius = stage.DragRadius;
            EditTicks = stage.EditTimeSec * GameConstants.Timing.TicksPerSecond;
            Phase = BattlePhase.Edit;
            EndTick = -1;
            LoseBulletId = -1;
            Hash = Fnv1a.Offset;

            Barrages = new BarrageRuntime[stage.Barrages.Count];
            for (int bi = 0; bi < stage.Barrages.Count; bi++)
            {
                BarrageData src = stage.Barrages[bi];
                BulletTypeDef def = BulletTypeDef.Find(src.Type);
                if (def == null)
                {
                    throw new ArgumentException("BattleSimulation: unknown bullet type '" + src.Type + "' in barrage " + bi, nameof(stage));
                }
                BarrageRuntime br = new BarrageRuntime
                {
                    Index = bi,
                    Type = src.Type,
                    Def = def,
                    Count = src.Count,
                    IntervalTicks = src.IntervalTicks,
                    StartDelayTicks = src.StartDelayTicks,
                    OrigPoints = src.Points.ToArray(),
                    Points = src.Points.ToArray(),
                };
                br.Path = new TrajectoryPath(br.Points, def.Smooth);
                Barrages[bi] = br;
                if (br.Points.Length > scratch.Length)
                {
                    scratch = new IntPoint[br.Points.Length];
                }
            }
            int id = 0;
            for (int bi = 0; bi < Barrages.Length; bi++)
            {
                BarrageRuntime b = Barrages[bi];
                for (int j = 0; j < b.Count; j++)
                {
                    int spawnTick = b.StartDelayTicks + j * b.IntervalTicks;
                    if (spawnTick > LastSpawnTick) LastSpawnTick = spawnTick;
                    Bullets.Add(new Bullet
                    {
                        Id = id++,
                        Barrage = b.Index,
                        Index = j,
                        SpawnTick = spawnTick,
                        State = BulletState.Pending,
                        X = b.Points[0].X,
                        Y = b.Points[0].Y,
                        R = b.Def.Radius,
                        Speed = b.Def.Speed,
                        Damage = b.Def.Damage,
                        DeathTick = -1,
                        MinGapSq = double.PositiveInfinity,
                    });
                }
            }
        }

        public DeterministicRng Rng { get; }

        public int EditRemainingTicks => Math.Max(0, EditTicks - EditTick);
        public bool CanUndo => !DragActive && history.Count > 0 && Phase == BattlePhase.Edit;

        // ------------------------------------------------------------------ 入力

        /// <summary>起点の移動先をドラッグ半径・フィールド範囲・整数座標へ制限する（人間・bot・リプレイ共通）</summary>
        public static IntPoint ClampPointPosition(int origX, int origY, double x, double y, int dragRadius)
        {
            double dx = x - origX;
            double dy = y - origY;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > dragRadius)
            {
                double s = dragRadius / len;
                dx *= s;
                dy *= s;
            }
            int nx = Fnv1a.Clamp(Fnv1a.RoundHalfUp(origX + dx), StageValidator.FieldMinX, StageValidator.FieldMaxX);
            int ny = Fnv1a.Clamp(Fnv1a.RoundHalfUp(origY + dy), StageValidator.FieldMinY, StageValidator.FieldMaxY);
            return new IntPoint(nx, ny);
        }

        /// <returns>コマンドが受理されたか</returns>
        public bool ApplyCommand(int op, int a, int b)
        {
            if (Phase != BattlePhase.Edit)
            {
                return false;
            }
            switch (op)
            {
                case GameConstants.Cmd.Grab:
                    if (DragActive || a < 0 || a >= Barrages.Length) return false;
                    if (b < 1 || b >= Barrages[a].Points.Length) return false;
                    DragActive = true;
                    DragBarrage = a;
                    DragPoint = b;
                    dragSnapshot = (IntPoint[])Barrages[a].Points.Clone();
                    Emit(SimEventType.Grab, a, b, 0);
                    return true;
                case GameConstants.Cmd.Move:
                    if (!DragActive) return false;
                    if (a < -GameConstants.World.Width || a > GameConstants.World.Width * 2 || b < -GameConstants.World.Height || b > GameConstants.World.Height * 2) return false;
                    DragTo(a, b);
                    return true;
                case GameConstants.Cmd.Release:
                    if (!DragActive) return false;
                    Release();
                    return true;
                case GameConstants.Cmd.Undo:
                    if (DragActive || history.Count == 0) return false;
                    HistoryEntry h = history.Last.Value;
                    history.RemoveLast();
                    BarrageRuntime br = Barrages[h.Barrage];
                    Array.Copy(h.Snapshot, br.Points, br.Points.Length);
                    Rebuild(h.Barrage);
                    Ops = Math.Max(0, Ops - 1);
                    Emit(SimEventType.Undo, h.Barrage, 0, 0);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// ドラッグ中の起点を目標へ動かす。曲線弾は紐のように隣接起点も追従する。
        /// 結果は「つかんだ時点の配置」と「最終移動量」だけで決まり、途中の MOVE 回数に依存しない。
        /// </summary>
        private void DragTo(int x, int y)
        {
            int bi = DragBarrage;
            int pi = DragPoint;
            BarrageRuntime br = Barrages[bi];
            IntPoint[] snap = dragSnapshot;
            IntPoint o = br.OrigPoints[pi];
            IntPoint np = ClampPointPosition(o.X, o.Y, x, y, DragRadius);
            double dx = np.X - snap[pi].X;
            double dy = np.Y - snap[pi].Y;
            double[] falloff = br.Def.Smooth ? GameConstants.Path.RopeFalloff : GameConstants.Path.RigidFalloff;
            int count = br.Points.Length;
            for (int j = 0; j < count; j++)
            {
                int dist = j > pi ? j - pi : pi - j;
                if (j == 0 || dist >= falloff.Length)
                {
                    scratch[j] = snap[j];
                    continue;
                }
                if (j == pi)
                {
                    scratch[j] = np;
                    continue;
                }
                double k = falloff[dist];
                IntPoint oj = br.OrigPoints[j];
                scratch[j] = ClampPointPosition(oj.X, oj.Y, snap[j].X + dx * k, snap[j].Y + dy * k, DragRadius);
            }
            for (int j = 1; j < count; j++)
            {
                if (scratch[j].Equals(scratch[j - 1]))
                {
                    return;
                }
            }
            bool changed = false;
            for (int j = 1; j < count; j++)
            {
                if (!br.Points[j].Equals(scratch[j]))
                {
                    br.Points[j] = scratch[j];
                    changed = true;
                }
            }
            if (changed)
            {
                Rebuild(bi);
            }
        }

        private void Rebuild(int bi)
        {
            BarrageRuntime br = Barrages[bi];
            br.Path = new TrajectoryPath(br.Points, br.Def.Smooth);
            IntPoint first = br.Points[0];
            for (int i = 0; i < Bullets.Count; i++)
            {
                Bullet bu = Bullets[i];
                if (bu.Barrage == bi)
                {
                    bu.X = first.X;
                    bu.Y = first.Y;
                    bu.Seg = 0;
                }
            }
        }

        private void Release()
        {
            BarrageRuntime br = Barrages[DragBarrage];
            bool moved = false;
            for (int j = 0; j < br.Points.Length; j++)
            {
                if (!br.Points[j].Equals(dragSnapshot[j]))
                {
                    moved = true;
                    break;
                }
            }
            if (moved)
            {
                Ops++;
                history.AddLast(new HistoryEntry { Barrage = DragBarrage, Snapshot = dragSnapshot });
                if (history.Count > GameConstants.UndoLimit)
                {
                    history.RemoveFirst();
                }
            }
            Emit(SimEventType.Release, DragBarrage, DragPoint, moved ? 1 : 0);
            DragActive = false;
            DragBarrage = -1;
            DragPoint = -1;
            dragSnapshot = null;
        }

        /// <summary>起点移動列を即時適用する（bot 探索専用。最初の tick より前のみ）</summary>
        public void ApplyPositionsDirect(IList<PointMove> positions)
        {
            if (Phase != BattlePhase.Edit || Tick != 0)
            {
                throw new InvalidOperationException("ApplyPositionsDirect: only allowed before the first tick");
            }
            bool rec = RecordEvents;
            RecordEvents = false;
            for (int i = 0; i < positions.Count; i++)
            {
                PointMove m = positions[i];
                if (!ApplyCommand(GameConstants.Cmd.Grab, m.B, m.P)) continue;
                ApplyCommand(GameConstants.Cmd.Move, m.X, m.Y);
                ApplyCommand(GameConstants.Cmd.Release, 0, 0);
            }
            RecordEvents = rec;
        }

        /// <summary>編集フェーズを即座に終了する（bot 評価専用）</summary>
        public void SkipEditPhase()
        {
            if (Phase != BattlePhase.Edit) return;
            if (DragActive) Release();
            EditTick = EditTicks;
            Phase = BattlePhase.Exec;
        }

        // ------------------------------------------------------------------ 進行

        /// <summary>1tick 進める。commands はこの tick の先頭で適用する（null 可）。</summary>
        public void Step(IList<InputCommand> commands)
        {
            Events.Clear();
            if (Phase == BattlePhase.Ended)
            {
                return;
            }
            if (Phase == BattlePhase.Edit)
            {
                if (commands != null)
                {
                    for (int i = 0; i < commands.Count; i++)
                    {
                        InputCommand c = commands[i];
                        ApplyCommand(c.Op, c.A, c.B);
                    }
                }
                EditTick++;
                if (EditTick >= EditTicks)
                {
                    if (DragActive) Release();
                    Phase = BattlePhase.Exec;
                    Emit(SimEventType.ExecStart, 0, 0, 0);
                }
            }
            else
            {
                ExecStep();
            }
            Tick++;
        }

        public BattleResult RunToEnd()
        {
            int limit = EditTicks + GameConstants.Timing.MaxExecTicks + 2;
            while (Phase != BattlePhase.Ended && Tick <= limit)
            {
                Step(null);
            }
            return GetResult();
        }

        private void ExecStep()
        {
            int t = ExecTick;
            // 1. 発射
            for (int i = 0; i < Bullets.Count; i++)
            {
                Bullet bu = Bullets[i];
                if (bu.State != BulletState.Pending || bu.SpawnTick != t) continue;
                bu.State = BulletState.Active;
                bu.D = 0;
                bu.Seg = 0;
                IntPoint p0 = Barrages[bu.Barrage].Points[0];
                bu.X = p0.X;
                bu.Y = p0.Y;
                double dx = bu.X - GameConstants.Enemy.X;
                double dy = bu.Y - GameConstants.Enemy.Y;
                double rr = GameConstants.Enemy.Radius + bu.R;
                bu.Armed = dx * dx + dy * dy >= rr * rr;
                Emit(SimEventType.Spawn, bu.Id, bu.Barrage, 0);
            }
            // 2. 移動と衝突（サブステップ）
            const int Sub = GameConstants.Timing.Substeps;
            for (int s = 0; s < Sub; s++)
            {
                CollectActive();
                for (int i = 0; i < active.Count; i++)
                {
                    Bullet bu = active[i];
                    bu.D += bu.Speed / Sub;
                    int seg = bu.Seg;
                    Barrages[bu.Barrage].Path.PositionAt(bu.D, ref seg, out double px, out double py);
                    bu.X = px;
                    bu.Y = py;
                    bu.Seg = seg;
                }
                if (ResolvePlayerAndBase())
                {
                    return;
                }
                ResolveEnemyHits();
                ResolveBulletCollisions(t);
            }
            // 3. 連鎖爆発
            ResolveExplosions(t);
            // 4. 画面外
            for (int i = 0; i < Bullets.Count; i++)
            {
                Bullet bu = Bullets[i];
                if (bu.State != BulletState.Active) continue;
                double m = GameConstants.World.OutMargin + bu.R;
                if (bu.X < -m || bu.X > GameConstants.World.Width + m || bu.Y < -m || bu.Y > GameConstants.World.Height + m)
                {
                    Kill(bu, DeathCause.Out);
                    Emit(SimEventType.Out, bu.Id, 0, 0);
                }
            }
            // 5. 終了判定
            ExecTick++;
            if (ExecTick > LastSpawnTick)
            {
                bool anyAlive = false;
                for (int i = 0; i < Bullets.Count; i++)
                {
                    if (Bullets[i].State != BulletState.Dead)
                    {
                        anyAlive = true;
                        break;
                    }
                }
                bool anyExplosion = false;
                for (int e = ExplosionCursor; e < Explosions.Count; e++)
                {
                    if (Explosions[e].StartTick + GameConstants.Explosion.ActiveTicks > ExecTick)
                    {
                        anyExplosion = true;
                        break;
                    }
                }
                if (!anyAlive && !anyExplosion)
                {
                    End(Hits > 0 ? BattleOutcome.Lose : BattleOutcome.Clear);
                    return;
                }
            }
            if (ExecTick >= GameConstants.Timing.MaxExecTicks)
            {
                TimedOut = true;
                End(Hits > 0 ? BattleOutcome.Lose : BattleOutcome.Clear);
            }
        }

        /// <returns>敗北で終了した場合 true</returns>
        private bool ResolvePlayerAndBase()
        {
            for (int i = 0; i < active.Count; i++)
            {
                Bullet bu = active[i];
                if (bu.State != BulletState.Active) continue;
                double dx = bu.X - GameConstants.Player.X;
                double dy = bu.Y - GameConstants.Player.Y;
                double rr = GameConstants.Player.Radius + bu.R;
                DeathCause cause = DeathCause.None;
                if (dx * dx + dy * dy < rr * rr) cause = DeathCause.Player;
                else if (CircleRectHit(bu.X, bu.Y, bu.R, GameConstants.Base.X0, GameConstants.Base.Y0, GameConstants.Base.X1, GameConstants.Base.Y1)) cause = DeathCause.Base;
                if (cause == DeathCause.None) continue;
                Hits++;
                Kill(bu, cause);
                Emit(cause == DeathCause.Player ? SimEventType.PlayerHit : SimEventType.BaseHit, bu.Id, 0, 0);
                if (!ContinueAfterHit)
                {
                    LoseCause = cause;
                    LoseBulletId = bu.Id;
                    End(BattleOutcome.Lose);
                    return true;
                }
            }
            return false;
        }

        private void ResolveEnemyHits()
        {
            for (int i = 0; i < active.Count; i++)
            {
                Bullet bu = active[i];
                if (bu.State != BulletState.Active) continue;
                double dx = bu.X - GameConstants.Enemy.X;
                double dy = bu.Y - GameConstants.Enemy.Y;
                double rr = GameConstants.Enemy.Radius + bu.R;
                bool inside = dx * dx + dy * dy < rr * rr;
                if (!bu.Armed)
                {
                    if (!inside) bu.Armed = true;
                    continue;
                }
                if (inside)
                {
                    DamageDealt += bu.Damage;
                    Kill(bu, DeathCause.Enemy);
                    Emit(SimEventType.EnemyHit, bu.Id, bu.Damage, 0);
                }
            }
        }

        private void ResolveBulletCollisions(int t)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Bullet a = active[i];
                if (a.State != BulletState.Active) continue;
                for (int j = i + 1; j < active.Count; j++)
                {
                    Bullet b = active[j];
                    if (b.State != BulletState.Active) continue;
                    if (a.Barrage == b.Barrage)
                    {
                        double minArc = (a.R + b.R) * GameConstants.Path.SameBarrageMinArcFactor;
                        double diff = a.D > b.D ? a.D - b.D : b.D - a.D;
                        if (diff <= minArc) continue;
                    }
                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double rr = a.R + b.R;
                    double d2 = dx * dx + dy * dy;
                    if (TrackProximity)
                    {
                        if (d2 < a.MinGapSq) a.MinGapSq = d2;
                        if (d2 < b.MinGapSq) b.MinGapSq = d2;
                    }
                    if (d2 < rr * rr)
                    {
                        double mx = (a.X + b.X) * HalfFactor;
                        double my = (a.Y + b.Y) * HalfFactor;
                        Kill(a, DeathCause.Collide);
                        Kill(b, DeathCause.Collide);
                        Cancels += 2;
                        Emit(SimEventType.Collide, a.Id, b.Id, 0);
                        AddExplosion(mx, my, 0, t);
                        break;
                    }
                }
            }
        }

        private void ResolveExplosions(int t)
        {
            while (ExplosionCursor < Explosions.Count && Explosions[ExplosionCursor].StartTick + GameConstants.Explosion.ActiveTicks <= t)
            {
                ExplosionCursor++;
            }
            int exCount = Explosions.Count;
            for (int e = ExplosionCursor; e < exCount; e++)
            {
                ExplosionData xp = Explosions[e];
                if (xp.StartTick > t || xp.StartTick + GameConstants.Explosion.ActiveTicks <= t) continue;
                for (int i = 0; i < Bullets.Count; i++)
                {
                    Bullet bu = Bullets[i];
                    if (bu.State != BulletState.Active) continue;
                    double dx = bu.X - xp.X;
                    double dy = bu.Y - xp.Y;
                    double rr = GameConstants.Explosion.Radius + bu.R;
                    if (dx * dx + dy * dy < rr * rr)
                    {
                        Kill(bu, DeathCause.Explosion);
                        Cancels += 1;
                        int level = xp.Level + 1;
                        if (level > MaxChain) MaxChain = level;
                        Emit(SimEventType.Chain, bu.Id, level, 0);
                        if (level <= GameConstants.Explosion.MaxChainDepth) AddExplosion(bu.X, bu.Y, level, t + 1);
                    }
                }
            }
        }

        private static bool CircleRectHit(double cx, double cy, double r, double x0, double y0, double x1, double y1)
        {
            double px = cx < x0 ? x0 : (cx > x1 ? x1 : cx);
            double py = cy < y0 ? y0 : (cy > y1 ? y1 : cy);
            double dx = cx - px;
            double dy = cy - py;
            return dx * dx + dy * dy < r * r;
        }

        private void CollectActive()
        {
            active.Clear();
            for (int i = 0; i < Bullets.Count; i++)
            {
                if (Bullets[i].State == BulletState.Active) active.Add(Bullets[i]);
            }
        }

        private void Kill(Bullet bu, DeathCause cause)
        {
            bu.State = BulletState.Dead;
            bu.Cause = cause;
            bu.DeathTick = ExecTick;
            uint h = Hash;
            h = Fnv1a.MixInt(h, bu.Id);
            h = Fnv1a.MixInt(h, ExecTick);
            h = Fnv1a.MixInt(h, (int)cause);
            h = Fnv1a.MixInt(h, Fnv1a.Quantize(bu.X));
            h = Fnv1a.MixInt(h, Fnv1a.Quantize(bu.Y));
            Hash = h;
        }

        private void AddExplosion(double x, double y, int level, int startTick)
        {
            if (Explosions.Count >= GameConstants.Explosion.MaxExplosions) return;
            ExplosionData xp = new ExplosionData { Id = Explosions.Count, X = x, Y = y, Level = level, StartTick = startTick };
            Explosions.Add(xp);
            Emit(SimEventType.Explosion, xp.Id, level, 0);
        }

        private void End(BattleOutcome outcome)
        {
            if (Phase == BattlePhase.Ended) return;
            Phase = BattlePhase.Ended;
            Outcome = outcome;
            EndTick = Tick;
            uint h = Hash;
            h = Fnv1a.MixInt(h, outcome == BattleOutcome.Clear ? 1 : 2);
            h = Fnv1a.MixInt(h, Ops);
            h = Fnv1a.MixInt(h, Cancels);
            h = Fnv1a.MixInt(h, DamageDealt);
            h = Fnv1a.MixInt(h, MaxChain);
            h = Fnv1a.MixInt(h, ExecTick);
            for (int bi = 0; bi < Barrages.Length; bi++)
            {
                IntPoint[] pts = Barrages[bi].Points;
                for (int p = 0; p < pts.Length; p++)
                {
                    h = Fnv1a.MixInt(h, pts[p].X);
                    h = Fnv1a.MixInt(h, pts[p].Y);
                }
            }
            Hash = h;
            Emit(SimEventType.End, outcome == BattleOutcome.Clear ? 1 : 2, (int)LoseCause, 0);
        }

        private void Emit(SimEventType type, int a, int b, int c)
        {
            if (!RecordEvents || Events.Count > GameConstants.Input.MaxCommands) return;
            Events.Add(new SimEvent { Type = type, A = a, B = b, C = c });
        }

        // ------------------------------------------------------------------ 参照

        public BattleResult GetResult()
        {
            return new BattleResult
            {
                Outcome = Outcome,
                LoseCause = LoseCause,
                TimedOut = TimedOut,
                EndTick = EndTick,
                ExecTicks = ExecTick,
                Ops = Ops,
                Cancels = Cancels,
                Damage = DamageDealt,
                MaxChain = MaxChain,
                Hits = Hits,
                Hash = Fnv1a.ToHex(Hash),
            };
        }

        /// <summary>bot 評価用：自機・自陣へ命中した弾の、他弾への最接近距離の合計（cap で打ち切り）</summary>
        public double ProximityPenalty(double cap)
        {
            double sum = 0;
            for (int i = 0; i < Bullets.Count; i++)
            {
                Bullet bu = Bullets[i];
                if (bu.Cause != DeathCause.Player && bu.Cause != DeathCause.Base) continue;
                double d = double.IsPositiveInfinity(bu.MinGapSq) ? cap : Math.Sqrt(bu.MinGapSq);
                sum += d < cap ? d : cap;
            }
            return sum;
        }

        /// <summary>コマンド列（T 昇順）でヘッドレス実行する。リプレイ検証・決定論テスト用。</summary>
        public static BattleResult RunWithCommands(StageData stage, uint seed, IList<InputCommand> commands)
        {
            BattleSimulation sim = new BattleSimulation(stage, seed, new BattleOptions { RecordEvents = false });
            int limit = sim.EditTicks + GameConstants.Timing.MaxExecTicks + 2;
            int ci = 0;
            List<InputCommand> batch = new List<InputCommand>();
            while (sim.Phase != BattlePhase.Ended && sim.Tick <= limit)
            {
                batch.Clear();
                while (ci < commands.Count && commands[ci].T == sim.Tick)
                {
                    batch.Add(commands[ci]);
                    ci++;
                }
                if (ci < commands.Count && commands[ci].T < sim.Tick)
                {
                    throw new ArgumentException("RunWithCommands: commands are not sorted at index " + ci, nameof(commands));
                }
                sim.Step(batch);
            }
            return sim.GetResult();
        }
    }
}

/*
## このアセットの役割
戦闘1回分の決定論ロジック（編集フェーズのドラッグ・取り消し、実行フェーズの発射・移動・衝突・連鎖爆発・勝敗判定・状態ハッシュ）です。企画書の BattleManager / EditPhaseController / ProjectileController / ProjectileCollisionResolver / ChainExplosionResolver / BattleOutcomeResolver / ScoreTracker の責務を、表示に依存しない純粋 C# として実装しています。

## 他アセットとの依存関係
StageData / TrajectoryPath / StageValidator / GameConstants / Fnv1a に依存。BattleController（固定ステップ駆動）、BotPlanner（探索評価）、ReplaySerializer（検証）、FieldGraphic（描画）から使用されます。

## Inspector設定
なし（MonoBehaviour ではありません）。

## USERがUnity上で行う作業
なし。

## 注意点
UnityEngine を参照しないため、Unity 外（dotnet）でもテストできます。Web版と同じ seed・同じコマンド列なら同じ Hash になります（tools/parity で検証）。IL2CPP ビルドでは C++ コンパイラの浮動小数点縮約（FMA）で値が変わる可能性があるため、Player Settings の IL2CPP Code Generation に関する注意を docs/UNITY_PHASE2.md に記載しています。
*/
