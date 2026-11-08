namespace DrawAndWitches.Core
{
    /// <summary>
    /// ゲーム全体で共有する数値定数。Web版 js/core/constants.js と同名・同値で対応する。
    /// ロジック結果（リプレイ・ハッシュ）に影響する値を変更する場合は LogicVersion を上げること。
    /// </summary>
    public static class GameConstants
    {
        public const string GameTitle = "DRAW&WITCHES";
        public const int LogicVersion = 1;
        public const int StageFormatVersion = 1;
        public const int ReplayFormatVersion = 1;

        /// <summary>ワールド座標（論理解像度）。縦長の弾幕フィールド。</summary>
        public static class World
        {
            public const int Width = 720;
            public const int Height = 960;
            public const int FieldMargin = 24;
            public const int OutMargin = 6;
        }

        public static class Timing
        {
            public const int TicksPerSecond = 60;
            public const int Substeps = 2;
            public const int MaxExecTicks = 7200;
            public const int MaxTicksPerFrame = 8;
            public const int FastForwardMultiplier = 3;
            public const double TickSeconds = 1.0 / TicksPerSecond;
        }

        /// <summary>自キャラクター（クリエ）</summary>
        public static class Player
        {
            public const int X = 360;
            public const int Y = 846;
            public const int Radius = 24;
        }

        /// <summary>自陣（矩形）</summary>
        public static class Base
        {
            public const int X0 = 54;
            public const int X1 = 666;
            public const int Y0 = 912;
            public const int Y1 = 960;
        }

        /// <summary>対戦相手の魔女</summary>
        public static class Enemy
        {
            public const int X = 360;
            public const int Y = 92;
            public const int Radius = 42;
        }

        public static class Explosion
        {
            public const int Radius = 46;
            public const int ActiveTicks = 8;
            public const int MaxChainDepth = 64;
            public const int MaxExplosions = 1024;
        }

        public static class Path
        {
            public const int SmoothSamplesPerSegment = 20;
            public const int SameBarrageMinArcFactor = 3;
            /// <summary>紐のような変形：ドラッグ起点からの距離ごとの追従率（index0は本人）</summary>
            public static readonly double[] RopeFalloff = { 1.0, 0.5, 0.18 };
            /// <summary>直線弾は追従しない</summary>
            public static readonly double[] RigidFalloff = { 1.0 };
            public const double MinSegmentLength = 1e-9;
            public const double MinInterpolationLength = 1e-12;
        }

        public static class StageLimits
        {
            public const int MaxBarrages = 16;
            public const int MinPoints = 2;
            public const int MaxPoints = 12;
            public const int MinCount = 1;
            public const int MaxCount = 10;
            public const int MaxTotalBullets = 80;
            public const int MinIntervalTicks = 4;
            public const int MaxIntervalTicks = 240;
            public const int MaxStartDelayTicks = 1800;
            public const int MinEditSec = 10;
            public const int MaxEditSec = 90;
            public const int MinDragRadius = 40;
            public const int MaxDragRadius = 320;
            public const int NameMaxChars = 32;
            public const int SummaryMaxChars = 80;
            public const int AuthorMaxChars = 16;
            public const int IdMaxChars = 64;
            public const int MaxJsonBytes = 32768;
            public const int MinTotalPointsForTest = 3;
            /// <summary>発射直後の重なり防止に使う余白(px)</summary>
            public const int SpawnGapPadding = 4;
        }

        public static class Editor
        {
            public const int ControlPointSpacing = 110;
            public const int MinStrokeLength = 60;
            public const int MaxSpawnY = 600;
            public const int DefaultEditSec = 30;
            public const int DefaultDragRadius = 120;
            public const int DefaultCount = 3;
            public const int DefaultIntervalTicks = 24;
            public const int HandlePickRadius = 22;
            public const float LongPressSeconds = 0.18f;
            public const int MaxStrokeSamples = 2000;
            public const string EnemyId = "editor_doll";
        }

        public static class Input
        {
            public const int PickRadius = 26;
            public const int MaxCommands = 20000;
        }

        /// <summary>入力コマンド種別（リプレイに数値で保存）</summary>
        public static class Cmd
        {
            public const int Grab = 1;
            public const int Move = 2;
            public const int Release = 3;
            public const int Undo = 4;
        }

        public const int UndoLimit = 64;

        public static class Score
        {
            public const int ClearBase = 3000;
            public const int PerCancel = 100;
            public const int PerDamage = 300;
            public const int OpsBonusMax = 2000;
            public const int OpsPenalty = 200;
            public const int EndlessStep = 500;
        }

        public static class Online
        {
            public const double CooldownMilliseconds = 6.0 * 60.0 * 60.0 * 1000.0;
            public const int RequestTimeoutSeconds = 30;
            public const string RankingPrefix = "Ranking_";
            public const string RankingEndlessKey = "Ranking_endless";
            public const string SharedStagesKey = "SharedStages";
            public const int RankingShowMax = 50;
            public const int SharedListMax = 500;
            public const int PlayerNameMax = 12;
        }

        public static class Replay
        {
            public const string Prefix = "DWR1:";
            public const int MaxStringLength = 600000;
            public const int BotRecordsMax = 20;
        }

        public static class Bot
        {
            public static readonly double[] RingRatios = { 0.4, 0.75, 1.0 };
            /// <summary>8方向単位ベクトル（三角関数を使わず決定論を保証）</summary>
            public static readonly double[] DirX = { 1, 0.7071067811865476, 0, -0.7071067811865476, -1, -0.7071067811865476, 0, 0.7071067811865476 };
            public static readonly double[] DirY = { 0, 0.7071067811865476, 1, 0.7071067811865476, 0, -0.7071067811865476, -1, -0.7071067811865476 };
            public const int MaxIterations = 10;
            public const int MaxEvaluations = 2600;
            public const int EvalsPerChunk = 12;
            public const double HitPenalty = 100000;
            public const double ProximityWeight = 20;
            public const double ProximityCap = 400;
            public static readonly double[] LocalSteps = { 32, 12 };
            public const double OpsWeight = 200;
            public const double CancelWeight = 100;
            public const double DamageWeight = 300;
            public const int FirstActionTick = 45;
            public const int ActionTicks = 36;
            public const int MoveSteps = 8;
        }

        /// <summary>PlayerPrefs キー（Web版 localStorage キーと同名）</summary>
        public static class Storage
        {
            public const string Settings = "dw.settings.v1";
            public const string Progress = "dw.progress.v1";
            public const string Tutorial = "dw.tutorial.v1";
            public const string Replays = "dw.replays.v1";
            public const string MyStages = "dw.mystages.v1";
            public const string CacheRanking = "dw.cache.ranking.v1";
            public const string CacheShared = "dw.cache.shared.v1";
            public const string PlayerId = "dw.playerId.v1";
            public const int MaxMyStages = 50;
        }
    }
}

/*
## このアセットの役割
ロジック・表示・保存で共有する数値定数をまとめた static class です（マジックナンバー排除）。

## 他アセットとの依存関係
Logic / Services / Game / View / UI のほぼ全スクリプトから参照されます。Web版 js/core/constants.js と値を一致させています。

## Inspector設定
なし（コード定数）。

## USERがUnity上で行う作業
なし。バランス調整時のみ値を編集します。

## 注意点
ロジック結果に影響する値（弾速・半径・爆発・tick数など）を変えると、既存リプレイの検証が失敗します。その場合は LogicVersion を上げてください。
*/
