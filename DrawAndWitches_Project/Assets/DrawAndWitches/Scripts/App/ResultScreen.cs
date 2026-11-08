using System;
using System.Collections.Generic;
using DrawAndWitches.Core;
using DrawAndWitches.Data;
using DrawAndWitches.Logic;
using DrawAndWitches.Online;
using DrawAndWitches.Presentation;
using DrawAndWitches.Services;
using DrawAndWitches.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.App
{
    /// <summary>リザルト画面（Web版 ResultScreen の移植）</summary>
    public sealed class ResultScreen : ScreenBase
    {
        private const float CountDelay = 0.4f;
        private const float CountDuration = 1.3f;

        private readonly ResultParams p;
        private Text scoreText;
        private int scoreTarget;
        private float t;
        private Button primary;
        private bool registering;

        public ResultScreen(ResultParams parameters)
        {
            p = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>UI 焼き込み用の見本（ストーリー・クリア）</summary>
        public static ResultParams BakeSample(StageData stage)
        {
            BattleResult r = new BattleResult { Outcome = BattleOutcome.Clear };
            return new ResultParams
            {
                Context = new BattleContext { Kind = BattleKind.Story, StageId = stage.Id },
                Player = BattlePlayer.Human,
                Result = r,
                Score = ScoreCalculator.ComputeStage(r),
                Stage = stage,
                ReplayStr = string.Empty,
                NewBest = true,
            };
        }

        private bool Clear => p.Result != null && p.Result.Outcome == BattleOutcome.Clear;
        private bool Final => p.Endless.HasValue && p.EndlessFinal;
        private bool EndlessMid => p.Endless.HasValue && !p.EndlessFinal;

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            BattleContext c = p.Context;
            CharacterProfile enemy = App.Db.EnemyOf(p.Stage);
            bool showClear = Final ? false : Clear;
            Common.Background(App, Root, enemy.BackgroundKey, 0.6f);
            bool playerWins = showClear || (Final && p.Endless.Value.Cleared > 0);
            Common.Portrait(App, Root, playerWins ? App.Db.Player : enemy, new Vector2(0.2f, 0.45f), Vector2.zero, 980, CharacterPortrait.Win);

            string stampText = Final ? T("result.runOver") : T(Clear ? "result.clear" : "result.lose");
            Text stamp = UiKit.Label(Root, stampText, 120, showClear ? UiKit.Gold : UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Place(stamp.rectTransform, new Vector2(0.66f, 0.86f), Vector2.zero, new Vector2(1000, 160));
            UiKit.AddShadow(stamp, new Color(0, 0, 0, 0.7f), new Vector2(4, -6));
            string sub = EndlessMid ? "#" + c.Run.StageNo + " · " + Txt(p.Stage.Name) : Txt(p.Stage.Name);
            if (p.Player == BattlePlayer.Bot) sub += "  · BOT";
            if (c.Kind == BattleKind.Replay) sub += "  · REPLAY";
            Text subT = UiKit.Label(Root, sub, 30, UiKit.TextColor);
            UiKit.Place(subT.rectTransform, new Vector2(0.66f, 0.76f), Vector2.zero, new Vector2(1000, 50));

            Image card = UiKit.Panel(Root, "Card");
            UiKit.Place(card.rectTransform, new Vector2(0.66f, 0.45f), Vector2.zero, new Vector2(1000, 500));
            RectTransform col = UiKit.Stretch(UiKit.Rect(card.transform, "Col"), 40, 40, 26, 26);
            UiKit.Column(col, 10);
            RectTransform scoreRow = UiKit.Rect(col, "Score");
            UiKit.Height(scoreRow, 110);
            Text lbl = UiKit.Label(scoreRow, "SCORE", 30, UiKit.SubTextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Stretch(lbl.rectTransform);
            scoreText = UiKit.Label(scoreRow, "0", 84, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Stretch(scoreText.rectTransform, 0, 0, 0, 0);
            scoreTarget = Final ? p.Endless.Value.Total : p.Score.Total;
            if (p.NewBest)
            {
                Text best = UiKit.Label(scoreRow, "NEW BEST", 26, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
                UiKit.Stretch(best.rectTransform, 140, 0, 0, 0);
            }
            if (Final)
            {
                EndlessScore e = p.Endless.Value;
                StatRow(col, "endless.cleared", e.Cleared.ToString(), null, UiKit.Success);
                StatRow(col, "endless.stageSum", UiKit.FormatNumber(e.Sum), null, UiKit.Gold);
                StatRow(col, "endless.bonus", UiKit.FormatNumber(e.Bonus), null, new Color(1f, 0.7f, 0.36f));
                StatRow(col, "endless.stages", (c.Run != null ? c.Run.History.Count : 0).ToString(), null, new Color(0.7f, 0.55f, 1f));
            }
            else
            {
                StageScore s = p.Score;
                StatRow(col, "battle.ops", p.Result.Ops.ToString(), Clear ? Pts(s.OpsPoints) : null, new Color(0.37f, 0.83f, 1f));
                StatRow(col, "battle.cancels", p.Result.Cancels.ToString(), Clear ? Pts(s.CancelPoints) : null, UiKit.Gold);
                StatRow(col, "battle.damage", p.Result.Damage.ToString(), Clear ? Pts(s.DamagePoints) : null, new Color(1f, 0.44f, 0.57f));
                StatRow(col, "battle.maxChain", p.Result.MaxChain.ToString(), null, new Color(0.7f, 0.55f, 1f));
            }
            if (EndlessMid)
            {
                EndlessScore e = p.Endless.Value;
                UiKit.Height(UiKit.Label(col, "★ " + UiKit.FormatNumber(e.Total) + "     +" + UiKit.FormatNumber(ScoreCalculator.NextEndlessBonus(e.Cleared)), 28, UiKit.Gold), 50);
            }

            RectTransform online = UiKit.Rect(Root, "Online");
            UiKit.Place(online, new Vector2(0.66f, 0.17f), Vector2.zero, new Vector2(1000, 64));
            UiKit.Row(online, 12);
            BuildOnline(online);

            RectTransform actions = UiKit.Rect(Root, "Actions");
            UiKit.Place(actions, new Vector2(0.66f, 0.07f), Vector2.zero, new Vector2(1000, 84));
            UiKit.Row(actions, 14);
            BuildActions(actions);
        }

        private static string Pts(int v)
        {
            return v >= 0 ? "+" + UiKit.FormatNumber(v) : UiKit.FormatNumber(v);
        }

        private void StatRow(RectTransform col, string key, string value, string pts, Color color)
        {
            RectTransform row = UiKit.Rect(col, "Row");
            UiKit.Height(row, 66);
            Image bar = UiKit.Image(row, "Bar", UiKit.Rounded, color);
            bar.rectTransform.anchorMin = new Vector2(0, 0.15f);
            bar.rectTransform.anchorMax = new Vector2(0, 0.85f);
            bar.rectTransform.sizeDelta = new Vector2(8, 0);
            bar.rectTransform.anchoredPosition = new Vector2(4, 0);
            Text l = UiKit.Label(row, T(key), 28, UiKit.TextColor, TextAnchor.MiddleLeft);
            UiKit.Stretch(l.rectTransform, 24, 0, 0, 0).anchorMax = new Vector2(0.5f, 1);
            Text v = UiKit.Label(row, value, 34, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            RectTransform vr = UiKit.Stretch(v.rectTransform);
            vr.anchorMin = new Vector2(0.45f, 0);
            vr.anchorMax = new Vector2(0.72f, 1);
            if (pts != null)
            {
                Text pt = UiKit.Label(row, pts, 28, color, TextAnchor.MiddleRight);
                RectTransform pr = UiKit.Stretch(pt.rectTransform);
                pr.anchorMin = new Vector2(0.72f, 0);
            }
        }

        private Button Act(RectTransform parent, string label, Action onClick, bool isPrimary)
        {
            Button b = UiKit.Button(parent, label, () => onClick(), isPrimary ? ButtonVariant.Primary : ButtonVariant.Normal, isPrimary ? 30 : 24);
            if (isPrimary && primary == null) primary = b;
            return b;
        }

        private void BuildActions(RectTransform row)
        {
            BattleContext c = p.Context;
            BattlePlayer pl = p.Player;
            switch (c.Kind)
            {
                case BattleKind.Endless:
                    if (EndlessMid)
                    {
                        Act(row, T("common.next"), () => App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Endless, Run = c.Run }, pl)), true);
                        Act(row, T("endless.giveUp"), GiveUp, false);
                    }
                    else
                    {
                        Act(row, T("common.retry"), () => App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Endless, Run = new EndlessRun() }, pl)), true);
                        Act(row, T("home.endless"), () => App.Go(() => new EndlessScreen()), false);
                        Act(row, T("common.home"), App.GoHome, false);
                    }
                    return;
                case BattleKind.Story:
                    {
                        int idx = App.Db.IndexOfOfficial(c.StageId);
                        List<string> ids = App.Db.OfficialIds();
                        bool hasNext = Clear && idx >= 0 && idx + 1 < ids.Count && App.Progress.IsUnlocked(ids, idx + 1);
                        if (hasNext)
                        {
                            string nextId = ids[idx + 1];
                            Act(row, T("result.nextStage"), () => App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Story, StageId = nextId }, BattlePlayer.Human)), true);
                        }
                        Act(row, T("common.retry"), RetrySame, !hasNext);
                        string sid = c.StageId;
                        Act(row, T("home.story"), () => App.Go(() => new StoryScreen(sid)), false);
                        Act(row, T("common.home"), App.GoHome, false);
                        return;
                    }
                case BattleKind.Custom:
                    {
                        string cid = c.CreationId;
                        Act(row, T("editor.backToEditor"), () => App.Go(() => new StageEditorScreen(cid, null)), true);
                        Act(row, T("common.retry"), RetrySame, false);
                        return;
                    }
                case BattleKind.Shared:
                    Act(row, T("common.retry"), RetrySame, true);
                    Act(row, T("editor.sharedList"), () => App.Go(() => new SharedStagesScreen()), false);
                    Act(row, T("common.home"), App.GoHome, false);
                    return;
                default:
                    Act(row, T("replay.again"), () => App.Go(() => new BattleScreen(c, BattlePlayer.Replay)), true);
                    Act(row, T("home.replay"), () => App.Go(() => new RecordsScreen()), false);
                    Act(row, T("common.home"), App.GoHome, false);
                    return;
            }
        }

        private void RetrySame()
        {
            BattleContext c = p.Context;
            BattlePlayer pl = p.Player;
            App.Go(() => new BattleScreen(c, pl));
        }

        private void GiveUp()
        {
            EndlessRun run = p.Context.Run;
            EndlessScore total = ScoreCalculator.ComputeEndless(run.Sum, run.Cleared);
            bool best = App.Progress.RecordEndless(total.Total, run.Cleared);
            ResultParams np = new ResultParams
            {
                Context = p.Context,
                Player = p.Player,
                Result = p.Result,
                Score = p.Score,
                Stage = p.Stage,
                ReplayStr = p.ReplayStr,
                Endless = total,
                EndlessFinal = true,
                NewBest = best,
            };
            App.Go(() => new ResultScreen(np));
        }

        private void BuildOnline(RectTransform box)
        {
            if (EndlessMid) return;
            BattleContext c = p.Context;
            string rankKey = null;
            int entryScore = 0;
            int entryCleared = 0;
            if (c.Kind == BattleKind.Story && p.Player == BattlePlayer.Human && Clear)
            {
                rankKey = LeaderboardService.StageKey(p.Stage.Id);
                entryScore = p.Score.Total;
            }
            else if (Final && p.Player == BattlePlayer.Human && p.Endless.Value.Total > 0 && c.Run != null && c.Run.History.Count > 0)
            {
                rankKey = LeaderboardService.EndlessKey;
                entryScore = p.Endless.Value.Total;
                entryCleared = p.Endless.Value.Cleared;
            }
            if (rankKey != null)
            {
                int submitted = App.Progress.RankingSubmittedScore(rankKey);
                // ストーリーはステージごとに1回。エンドレスは登録済みスコアを上回る場合のみ再登録可
                bool already = c.Kind == BattleKind.Story ? submitted >= 0 : submitted >= 0 && submitted >= entryScore;
                string key = rankKey;
                bool story = c.Kind == BattleKind.Story;
                Button btn = null;
                btn = UiKit.Button(box, already ? T("online.registered") : T("online.register"), async () =>
                {
                    if (registering) return;
                    if (App.Remote == null || !App.Remote.IsConfigured)
                    {
                        App.Notice(T("online.unavailable"), NoticeKind.Warn, 6f);
                        return;
                    }
                    registering = true;
                    btn.interactable = false;
                    OnlineSubmitResult res;
                    try
                    {
                        res = await App.Leaderboard.SubmitAsync(key, App.PlayerName(), entryScore, story ? p.Result.Ops : 0, story ? p.Result.Cancels : 0, story ? p.Result.Damage : 0, entryCleared);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Result] ranking submit failed: " + e);
                        res = new OnlineSubmitResult { Error = "error" };
                    }
                    registering = false;
                    if (Disposed || btn == null) return;
                    if (res.Ok)
                    {
                        App.Progress.MarkRankingSubmitted(key, entryScore);
                        UiKit.ButtonLabel(btn).text = T("online.registered");
                        App.Notice(T("online.registerOk"), NoticeKind.Success);
                    }
                    else
                    {
                        btn.interactable = true;
                        App.Notice(App.OnlineErrorText(res.Error), NoticeKind.Error);
                    }
                }, already ? ButtonVariant.Ghost : ButtonVariant.Primary, 22);
                btn.interactable = !already;
            }
            if (rankKey != null || c.Kind == BattleKind.Story)
            {
                string key = rankKey ?? LeaderboardService.StageKey(p.Stage.Id);
                UiKit.Button(box, T("common.ranking"), () => Common.OpenRanking(App, key, T("common.ranking")), ButtonVariant.Normal, 22);
            }
            UiKit.Button(box, T("share.button"), Share, ButtonVariant.Normal, 22);
            if (!string.IsNullOrEmpty(p.ReplayStr))
            {
                UiKit.Button(box, T("replay.copy"), () =>
                {
                    bool ok = App.CopyToClipboard(p.ReplayStr);
                    App.Notice(T(ok ? "replay.copied" : "replay.copyFailed"), ok ? NoticeKind.Success : NoticeKind.Warn);
                    if (!ok) App.ShowText(T("replay.copy"), p.ReplayStr);
                }, ButtonVariant.Normal, 22);
                if (p.Player == BattlePlayer.Bot)
                {
                    Button save = null;
                    save = UiKit.Button(box, T("bot.save"), () =>
                    {
                        bool ok = App.Replays.AddBot(new ReplayEntry
                        {
                            Id = "bot_" + ToBase36(IdGenerator.NowMs()),
                            Data = p.ReplayStr,
                            At = IdGenerator.NowMs(),
                            Title = Txt(p.Stage.Name),
                            Outcome = BattleResult.OutcomeToString(p.Result.Outcome),
                            Score = p.Score.Total,
                        });
                        App.Notice(T(ok ? "bot.saved" : "notice.saveFailed"), ok ? NoticeKind.Success : NoticeKind.Error);
                        if (ok) save.interactable = false;
                    }, ButtonVariant.Normal, 22);
                }
            }
        }

        private static string ToBase36(long v)
        {
            const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (v <= 0) return "0";
            char[] buf = new char[16];
            int i = buf.Length;
            while (v > 0)
            {
                buf[--i] = Digits[(int)(v % 36)];
                v /= 36;
            }
            return new string(buf, i, buf.Length - i);
        }

        /// <summary>X シェア（Web版と同じ文面キーと TweetManager の URL 形式）</summary>
        private void Share()
        {
            BattleContext c = p.Context;
            string stageName = Txt(p.Stage.Name);
            string msg;
            if (Final) msg = T("share.endless", "score", UiKit.FormatNumber(p.Endless.Value.Total), "n", p.Endless.Value.Cleared);
            else if (c.Kind == BattleKind.Shared || c.Kind == BattleKind.Custom) msg = T(Clear ? "share.stageClear" : "share.stageTry", "name", stageName, "summary", Txt(p.Stage.Summary), "score", UiKit.FormatNumber(p.Score.Total));
            else msg = T(Clear ? "share.storyClear" : "share.storyLose", "name", stageName, "score", UiKit.FormatNumber(p.Score.Total), "ops", p.Result.Ops, "cancels", p.Result.Cancels, "damage", p.Result.Damage);
            App.Share(msg);
        }

        public override void Tick(float dt)
        {
            t += dt;
            if (scoreText != null)
            {
                float k = Mathf.Clamp01((t - CountDelay) / CountDuration);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                scoreText.text = UiKit.FormatNumber(Mathf.RoundToInt(scoreTarget * e));
                if (k > 0f && k < 1f && UnityEngine.Random.value < 0.25f) App.Audio.PlaySe("tick");
            }
            if (App.ModalOpen) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) && primary != null && primary.interactable)
            {
                primary.onClick.Invoke();
            }
            else if (App.Settings.IsKeyDown(KeyAction.Retry) && p.Context.Kind != BattleKind.Endless && p.Context.Kind != BattleKind.Replay)
            {
                RetrySame();
            }
        }
    }
}

/*
## このアセットの役割
リザルト画面です。スコアのカウントアップ、内訳、次の行動（次の魔女へ・再挑戦・エディターへ戻る等）、ランキング登録、X シェア、リプレイ文字列のコピー、bot 記録の保存を担当します。

## 他アセットとの依存関係
BattleScreen から ResultParams を受け取ります。LeaderboardService / TweetManager（GameApp.Share 経由）/ ReplayStore。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
ランキング送信はボタン操作時のみ行います（自動送信はしません）。ストーリーはステージごとに1回、エンドレスは自己ベスト更新時のみ再登録できます（Web版と同じ）。
*/
