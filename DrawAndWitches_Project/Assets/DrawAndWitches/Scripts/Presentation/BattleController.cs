using System;
using System.Collections.Generic;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;

namespace DrawAndWitches.Presentation
{
    public enum BattlePlayer
    {
        Human,
        Bot,
        Replay,
    }

    /// <summary>戦闘終了時に渡す内容</summary>
    public sealed class BattleEndInfo
    {
        public BattleResult Result;
        public List<InputCommand> Commands;
        public uint Seed;
        public StageData Stage;
        public BattlePlayer Player;
    }

    /// <summary>
    /// 1ステージ分の実行制御（Web版 BattleSession の移植）。
    /// 実時間から固定 tick 数を算出してシミュレーションを進める（描画 FPS とロジックは独立）。
    /// human: ポインター入力をコマンド化し、適用 tick とともに記録する。
    /// bot / replay: 事前のコマンド列を tick に合わせて投入する（人間と同じ入力経路）。
    /// </summary>
    public sealed class BattleController
    {
        private const double TickSeconds = 1.0 / GameConstants.Timing.TicksPerSecond;
        private const float EndLingerSeconds = 1.1f;
        private const int PickTouchBonus = 6;
        private const float RevealSeconds = 0.65f;
        private const float MaxFrameSeconds = 0.25f;
        private const int CountdownSeconds = 5;

        public readonly BattleSimulation Sim;
        public readonly StageData Stage;
        public readonly uint Seed;
        public readonly BattlePlayer Player;
        public readonly FieldViewState View = new FieldViewState();
        public readonly FieldEffects Effects = new FieldEffects();

        private readonly List<InputCommand> scripted;
        private int scriptIndex;
        private readonly List<InputCommand> recorded = new List<InputCommand>();
        private readonly List<InputCommand> queue = new List<InputCommand>();
        private readonly List<InputCommand> batch = new List<InputCommand>();
        private double acc;
        private bool ended;
        private float endedFor;
        private bool finished;
        private int lastSecond = -1;
        private bool hidden;
        private float revealT = 1f;

        public bool Paused { get; private set; }
        public bool Fast { get; set; }
        public bool Disposed { get; private set; }

        /// <summary>効果音再生（キー, 連鎖段数）</summary>
        public Action<string, int> PlaySe;
        /// <summary>ロジックイベントの通知（HUD・カットイン用）</summary>
        public Action<SimEvent> SimEventRaised;
        public Action<BattlePhase> PhaseChanged;
        public Action<BattleEndInfo> Ended;

        public BattleController(StageData stage, uint seed, BattlePlayer player, List<InputCommand> commands)
        {
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Seed = seed;
            Player = player;
            Sim = new BattleSimulation(stage, seed, BattleOptions.Default);
            scripted = commands ?? new List<InputCommand>();
            if (player != BattlePlayer.Human && commands == null)
            {
                throw new ArgumentException("BattleController: bot / replay requires a command list", nameof(commands));
            }
        }

        public IReadOnlyList<InputCommand> Recorded => recorded;

        public void SetPaused(bool paused)
        {
            if (Disposed) return;
            Paused = paused;
            if (paused && Player == BattlePlayer.Human && (Sim.DragActive || QueueHas(GameConstants.Cmd.Grab)))
            {
                queue.Add(new InputCommand(0, GameConstants.Cmd.Release, 0, 0));
            }
            acc = 0;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        // ---------------------------------------------------------------- 入力（human）

        private bool QueueHas(int op)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].Op == op) return true;
            }
            return false;
        }

        public bool CanEdit => Player == BattlePlayer.Human && !Paused && Sim.Phase == BattlePhase.Edit && !ended && !hidden;

        public bool CanUndo => CanEdit && !Sim.DragActive && Sim.CanUndo;

        /// <summary>最も近い可動起点を探す</summary>
        public bool Pick(float wx, float wy, bool touch, out int barrage, out int point)
        {
            int r = GameConstants.Input.PickRadius + (touch ? PickTouchBonus : 0);
            double best = r * r;
            barrage = -1;
            point = -1;
            foreach (BarrageRuntime br in Sim.Barrages)
            {
                for (int pi = 1; pi < br.Points.Length; pi++)
                {
                    double dx = br.Points[pi].X - wx;
                    double dy = br.Points[pi].Y - wy;
                    double d = dx * dx + dy * dy;
                    if (d <= best)
                    {
                        best = d;
                        barrage = br.Index;
                        point = pi;
                    }
                }
            }
            return barrage >= 0;
        }

        public bool PointerDown(float wx, float wy, bool touch)
        {
            if (!CanEdit || Sim.DragActive || QueueHas(GameConstants.Cmd.Grab)) return false;
            if (!Pick(wx, wy, touch, out int b, out int p)) return false;
            queue.Add(new InputCommand(0, GameConstants.Cmd.Grab, b, p));
            View.HoverBarrage = b;
            View.HoverPoint = p;
            return true;
        }

        public void PointerMove(float wx, float wy, bool touch)
        {
            if (Player != BattlePlayer.Human) return;
            bool grabbing = Sim.DragActive || QueueHas(GameConstants.Cmd.Grab);
            if (grabbing && CanEdit)
            {
                int x = (int)Math.Round(Math.Max(-GameConstants.World.Width, Math.Min(GameConstants.World.Width * 2, (double)wx)), MidpointRounding.AwayFromZero);
                int y = (int)Math.Round(Math.Max(-GameConstants.World.Height, Math.Min(GameConstants.World.Height * 2, (double)wy)), MidpointRounding.AwayFromZero);
                if (queue.Count > 0 && queue[queue.Count - 1].Op == GameConstants.Cmd.Move)
                {
                    queue[queue.Count - 1] = new InputCommand(0, GameConstants.Cmd.Move, x, y);
                }
                else
                {
                    queue.Add(new InputCommand(0, GameConstants.Cmd.Move, x, y));
                }
            }
            else if (Sim.Phase == BattlePhase.Edit && Pick(wx, wy, touch, out int b, out int p))
            {
                View.HoverBarrage = b;
                View.HoverPoint = p;
            }
            else
            {
                View.HoverBarrage = -1;
                View.HoverPoint = -1;
            }
        }

        public void PointerUp()
        {
            if (Player != BattlePlayer.Human) return;
            if (Sim.DragActive || QueueHas(GameConstants.Cmd.Grab)) queue.Add(new InputCommand(0, GameConstants.Cmd.Release, 0, 0));
        }

        /// <summary>直前のドラッグを取り消す（Ctrl+Z / 右クリック / UI ボタン）</summary>
        public bool Undo()
        {
            if (!CanEdit || Sim.DragActive || !Sim.CanUndo) return false;
            if (QueueHas(GameConstants.Cmd.Undo) || QueueHas(GameConstants.Cmd.Grab)) return false;
            queue.Add(new InputCommand(0, GameConstants.Cmd.Undo, 0, 0));
            return true;
        }

        /// <summary>会話イベント中は弾幕・軌道線を隠す</summary>
        public void HideBarrages()
        {
            hidden = true;
            revealT = 0f;
        }

        public void RevealBarrages()
        {
            hidden = false;
            revealT = 0f;
        }

        public bool Hidden => hidden;

        // ---------------------------------------------------------------- ループ

        /// <summary>毎フレーム呼ぶ（dt は実時間秒）。ロジックは常に 60tick/秒。</summary>
        public void Update(float dt)
        {
            if (Disposed || finished) return;
            if (dt < 0f) dt = 0f;
            if (dt > MaxFrameSeconds) dt = MaxFrameSeconds;
            View.Time += dt;
            if (!hidden && revealT < 1f) revealT = Math.Min(1f, revealT + dt / RevealSeconds);
            float r = hidden ? 0f : revealT;
            View.RevealAlpha = r * r * (3f - 2f * r);
            if (Paused) return;
            if (!ended && !hidden)
            {
                // 人間の編集フェーズは制限時間そのものなので早送り対象外
                int mult = Fast && (Player != BattlePlayer.Human || Sim.Phase != BattlePhase.Edit) ? GameConstants.Timing.FastForwardMultiplier : 1;
                acc += dt * mult;
                int maxTicks = GameConstants.Timing.MaxTicksPerFrame * mult;
                int n = 0;
                while (acc >= TickSeconds && n < maxTicks && !ended)
                {
                    Tick();
                    acc -= TickSeconds;
                    n++;
                }
                if (n >= maxTicks) acc = 0;
                if (Sim.Phase == BattlePhase.Edit) View.PreviewTick += dt / TickSeconds;
            }
            else if (ended)
            {
                endedFor += dt;
                if (endedFor >= EndLingerSeconds)
                {
                    finished = true;
                    Ended?.Invoke(new BattleEndInfo { Result = Sim.GetResult(), Commands = new List<InputCommand>(recorded), Seed = Seed, Stage = Stage, Player = Player });
                }
            }
            Effects.Update(dt);
        }

        private void Tick()
        {
            batch.Clear();
            if (Player == BattlePlayer.Human)
            {
                batch.AddRange(queue);
                queue.Clear();
            }
            else
            {
                while (scriptIndex < scripted.Count && scripted[scriptIndex].T == Sim.Tick)
                {
                    batch.Add(scripted[scriptIndex]);
                    scriptIndex++;
                }
            }
            if (batch.Count > 0 && Sim.Phase == BattlePhase.Edit)
            {
                if (recorded.Count + batch.Count <= GameConstants.Input.MaxCommands)
                {
                    for (int i = 0; i < batch.Count; i++) recorded.Add(new InputCommand(Sim.Tick, batch[i].Op, batch[i].A, batch[i].B));
                }
                else
                {
                    batch.Clear(); // 記録上限超過時は以降の入力を無視（記録と実行の一致を保つ）
                }
                if (Player != BattlePlayer.Human) UpdateScriptCursor();
            }
            BattlePhase prev = Sim.Phase;
            Sim.Step(batch);
            foreach (SimEvent ev in Sim.Events) OnEvent(ev);
            if (prev != Sim.Phase) PhaseChanged?.Invoke(Sim.Phase);
            if (Sim.Phase == BattlePhase.Edit)
            {
                int sec = (Sim.EditRemainingTicks + GameConstants.Timing.TicksPerSecond - 1) / GameConstants.Timing.TicksPerSecond;
                if (sec != lastSecond)
                {
                    if (sec <= CountdownSeconds && sec > 0) PlaySe?.Invoke("tick", 0);
                    lastSecond = sec;
                }
            }
            if (Sim.Phase == BattlePhase.Ended && !ended)
            {
                ended = true;
                View.CursorVisible = false;
            }
        }

        private void UpdateScriptCursor()
        {
            foreach (InputCommand c in batch)
            {
                if (c.Op == GameConstants.Cmd.Grab)
                {
                    IntPoint p = Sim.Barrages[c.A].Points[c.B];
                    View.CursorVisible = true;
                    View.CursorPressed = true;
                    View.CursorX = p.X;
                    View.CursorY = p.Y;
                }
                else if (c.Op == GameConstants.Cmd.Move)
                {
                    View.CursorVisible = true;
                    View.CursorX = c.A;
                    View.CursorY = c.B;
                }
                else if (c.Op == GameConstants.Cmd.Release)
                {
                    View.CursorPressed = false;
                }
            }
        }

        private void OnEvent(SimEvent ev)
        {
            Effects.OnSimEvent(ev, Sim);
            try
            {
                SimEventRaised?.Invoke(ev);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[BattleController] SimEventRaised listener failed: " + e);
            }
            if (PlaySe == null) return;
            switch (ev.Type)
            {
                case SimEventType.Grab: PlaySe("grab", 0); break;
                case SimEventType.Release: PlaySe("release", 0); break;
                case SimEventType.Undo: PlaySe("ui_back", 0); break;
                case SimEventType.ExecStart: PlaySe("edit_end", 0); break;
                case SimEventType.Spawn:
                    if (ev.A % 3 == 0) PlaySe("fire", 0);
                    break;
                case SimEventType.Collide:
                    PlaySe("collide", 0);
                    PlaySe("explode", 0);
                    break;
                case SimEventType.Chain: PlaySe("chain", ev.B); break;
                case SimEventType.EnemyHit: PlaySe("enemy_hit", 0); break;
                case SimEventType.PlayerHit:
                case SimEventType.BaseHit:
                    PlaySe("player_hit", 0);
                    break;
                default: break;
            }
        }

        public void Draw(FieldGraphic g)
        {
            FieldRenderer.Draw(g, Sim, View, Effects);
        }
    }
}

/*
## このアセットの役割
戦闘1回分の進行制御です。実時間 → 固定 tick 変換、人間の入力のコマンド化と記録、bot・リプレイのコマンド投入、効果音・演出イベントの発行、終了後の余韻（1.1秒）を担当します。企画書の EditPhaseController / ControlPointDragHandler / BattleManager（実行制御部分）に相当します。

## 他アセットとの依存関係
BattleSimulation / FieldEffects / FieldRenderer。BattleScreen が生成し、Update と入力を渡します。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
Time.timeScale には依存しません（BattleScreen が Time.unscaledDeltaTime を渡します）。一時停止は SetPaused で行ってください。
*/
