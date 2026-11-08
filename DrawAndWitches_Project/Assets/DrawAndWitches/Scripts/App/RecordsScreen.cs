using System;
using DrawAndWitches.Logic;
using DrawAndWitches.Presentation;
using DrawAndWitches.Services;
using DrawAndWitches.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.App
{
    /// <summary>リプレイ・bot 記録画面（直近リプレイ／リプレイ文字列の読込／保存済み bot 記録）</summary>
    public sealed class RecordsScreen : ScreenBase
    {
        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_lantern", 0.65f);
            Common.Header(App, Root, T("home.replay"), App.GoHome);

            Image left = UiKit.Panel(Root, "Last");
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0.45f, 1);
            left.rectTransform.offsetMin = new Vector2(60, 60);
            left.rectTransform.offsetMax = new Vector2(-20, -150);
            RectTransform col = UiKit.Stretch(UiKit.Rect(left.transform, "Col"), 36, 36, 30, 30);
            UiKit.Column(col, 14);
            UiKit.Height(UiKit.Label(col, T("replay.last"), 34, UiKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 56);
            ReplayEntry last = App.Replays.Last;
            if (last == null)
            {
                UiKit.Height(UiKit.Label(col, T("replay.none"), 26, UiKit.SubTextColor, TextAnchor.MiddleLeft), 60);
            }
            else
            {
                UiKit.Height(UiKit.Label(col, Describe(last), 26, UiKit.TextColor, TextAnchor.UpperLeft), 100);
                RectTransform row = UiKit.Rect(col, "Row");
                UiKit.Height(row, 76);
                UiKit.Row(row, 12);
                UiKit.Button(row, T("replay.play"), () => Play(last.Data), ButtonVariant.Primary, 26);
                UiKit.Button(row, T("replay.copy"), () => Copy(last.Data), ButtonVariant.Normal, 22);
            }
            RectTransform spacer = UiKit.Rect(col, "Spacer");
            spacer.gameObject.GetOrAdd<LayoutElement>().flexibleHeight = 1;
            UiKit.Height(UiKit.Button(col, T("replay.import"), Import, ButtonVariant.Normal, 26), 80);

            Image right = UiKit.Panel(Root, "Bots");
            right.rectTransform.anchorMin = new Vector2(0.45f, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.offsetMin = new Vector2(20, 60);
            right.rectTransform.offsetMax = new Vector2(-60, -150);
            Text title = UiKit.Label(right.transform, T("bot.records"), 34, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(title.rectTransform, 36, 36, 26, 0).anchorMin = new Vector2(0, 1);
            title.rectTransform.offsetMin = new Vector2(36, -80);
            RectTransform host = UiKit.Stretch(UiKit.Rect(right.transform, "Host"), 24, 24, 90, 24);
            RectTransform list = UiKit.ScrollList(host, out ScrollRect _, 10);
            UiKit.Stretch((RectTransform)list.parent.parent);
            if (App.Replays.Bots.Count == 0)
            {
                UiKit.Height(UiKit.Label(list, T("bot.none"), 26, UiKit.SubTextColor), 80);
                return;
            }
            foreach (ReplayEntry e in App.Replays.Bots)
            {
                ReplayEntry entry = e;
                Image row = UiKit.Image(list, "Row", UiKit.Rounded, new Color(1, 1, 1, 0.06f));
                UiKit.Height(row, 110);
                Text d = UiKit.Label(row.transform, Describe(entry), 22, UiKit.TextColor, TextAnchor.MiddleLeft);
                UiKit.Stretch(d.rectTransform, 20, 0, 6, 6).anchorMax = new Vector2(0.55f, 1);
                RectTransform btns = UiKit.Stretch(UiKit.Rect(row.transform, "Btns"), 0, 14, 18, 18);
                btns.anchorMin = new Vector2(0.55f, 0);
                UiKit.Row(btns, 8);
                UiKit.Button(btns, T("replay.play"), () => Play(entry.Data), ButtonVariant.Primary, 20);
                UiKit.Button(btns, T("replay.copy"), () => Copy(entry.Data), ButtonVariant.Normal, 18);
                UiKit.Button(btns, T("common.delete"), () => App.Confirm(T("bot.deleteConfirm"), T("common.delete"), () =>
                {
                    if (!App.Replays.RemoveBot(entry.Id)) App.Notice(T("notice.saveFailed"), NoticeKind.Error);
                    Refresh();
                }), ButtonVariant.Danger, 18);
            }
        }

        private string Describe(ReplayEntry e)
        {
            string when = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, e.At)).ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            string outcome = e.Outcome == "clear" ? T("result.clear") : T("result.lose");
            return e.Title + "\n" + outcome + "  ·  " + UiKit.FormatNumber(e.Score) + "  ·  " + when;
        }

        private void Copy(string data)
        {
            bool ok = App.CopyToClipboard(data);
            App.Notice(T(ok ? "replay.copied" : "replay.copyFailed"), ok ? NoticeKind.Success : NoticeKind.Warn);
            if (!ok) App.ShowText(T("replay.copy"), data);
        }

        private void Import()
        {
            App.PromptText(T("replay.import"), T("replay.importHint"), text => Play(text));
        }

        /// <summary>リプレイ文字列を解析・検証して再生する。成功したら true。</summary>
        private bool Play(string data)
        {
            ReplayParseResult r = ReplaySerializer.Parse(data, App.Db.EnemyIds);
            if (!r.Ok)
            {
                App.Notice(T("replay.invalid", "code", r.Error), NoticeKind.Error, 5f);
                return false;
            }
            ReplayVerifyResult v;
            try
            {
                v = ReplaySerializer.Verify(r.Replay);
            }
            catch (Exception e)
            {
                Debug.LogError("[Records] replay verify failed: " + e);
                v = new ReplayVerifyResult { Ok = false, Error = "exception" };
            }
            if (!v.Ok)
            {
                App.Notice(T("replay.mismatch"), NoticeKind.Error, 5f);
                return false;
            }
            ReplayData replay = r.Replay;
            App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Replay, Replay = replay }, BattlePlayer.Replay));
            return true;
        }
    }
}

/*
## このアセットの役割
直近のリプレイと保存済み bot 記録の一覧・再生・コピー・削除、共有されたリプレイ文字列（DWR1:）の読込を行います。

## 他アセットとの依存関係
ReplayStore / ReplaySerializer（解析・決定論検証）/ BattleScreen（再生）。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
再生前に必ず決定論検証（記録結果と再計算結果の一致）を行い、一致しない文字列は再生しません。Web版で作ったリプレイ文字列もそのまま読み込めます。
*/
