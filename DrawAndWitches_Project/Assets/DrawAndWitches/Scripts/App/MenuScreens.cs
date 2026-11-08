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
    // ========================================================================= タイトル
    public sealed class TitleScreen : ScreenBase
    {
        private Text press;
        private float t;

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_title", 0.35f);
            Common.Portrait(App, Root, App.Db.Player, new Vector2(0.78f, 0.45f), Vector2.zero, 900);
            Text logo = UiKit.Label(Root, GameConstants.GameTitle, 132, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Place(logo.rectTransform, new Vector2(0.36f, 0.62f), Vector2.zero, new Vector2(1300, 200));
            UiKit.AddShadow(logo, new Color(0.9f, 0.3f, 0.85f, 0.9f), new Vector2(0, -6));
            UiKit.AddShadow(logo, new Color(0.2f, 0.1f, 0.5f, 0.9f), new Vector2(5, -10));
            Text sub = UiKit.Label(Root, T("title.subtitle"), 44, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Place(sub.rectTransform, new Vector2(0.36f, 0.5f), Vector2.zero, new Vector2(1200, 80));
            press = UiKit.Label(Root, T("title.start"), 34, UiKit.TextColor);
            UiKit.Place(press.rectTransform, new Vector2(0.36f, 0.28f), Vector2.zero, new Vector2(1000, 70));
            Text foot = UiKit.Label(Root, T("title.footer"), 20, UiKit.SubTextColor);
            UiKit.Place(foot.rectTransform, new Vector2(0.5f, 0.04f), Vector2.zero, new Vector2(1800, 40));
            Button all = UiKit.Image(Root, "Hit", null, new Color(0, 0, 0, 0), true).gameObject.GetOrAdd<Button>();
            UiKit.Stretch((RectTransform)all.transform);
            all.transition = Selectable.Transition.None;
            all.onClick.AddListener(Start);
        }

        private void Start()
        {
            App.Audio.PlaySe("ui_click");
            App.GoHome();
        }

        public override void Tick(float dt)
        {
            t += dt;
            if (press != null) press.color = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin(t * 3f));
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.Space)) Start();
        }

        public override void OnEscape()
        {
        }
    }

    // ========================================================================= ホーム
    public sealed class HomeScreen : ScreenBase
    {
        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_title", 0.55f);
            Common.Portrait(App, Root, App.Db.Player, new Vector2(0.17f, 0.42f), Vector2.zero, 860);
            Text logo = UiKit.Label(Root, GameConstants.GameTitle, 64, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(logo.rectTransform, new Vector2(0.62f, 0.89f), Vector2.zero, new Vector2(1100, 100));
            UiKit.AddShadow(logo, new Color(0.9f, 0.3f, 0.85f, 0.8f), new Vector2(0, -4));

            RectTransform grid = UiKit.Rect(Root, "Tiles");
            UiKit.Place(grid, new Vector2(0.62f, 0.5f), new Vector2(0, -10), new Vector2(1100, 560));
            GridLayoutGroup g = grid.gameObject.GetOrAdd<GridLayoutGroup>();
            g.cellSize = new Vector2(350, 270);
            g.spacing = new Vector2(25, 25);
            g.childAlignment = TextAnchor.MiddleCenter;
            Tile(grid, "home.story", "bg_candy", () => App.Go(() => new StoryScreen(null)));
            Tile(grid, "home.endless", "bg_star", () => App.Go(() => new EndlessScreen()));
            Tile(grid, "home.editor", "bg_editor", () => App.Go(() => new EditorMenuScreen()));
            Tile(grid, "home.figurebook", "bg_rose", () => App.Go(() => new FigurebookScreen()));
            Tile(grid, "home.settings", "bg_clock", () => App.Go(() => new SettingsScreen(null)));
            Tile(grid, "home.replay", "bg_lantern", () => App.Go(() => new RecordsScreen()));

            RectTransform bottom = UiKit.Rect(Root, "Bottom");
            UiKit.Place(bottom, new Vector2(0.62f, 0.1f), Vector2.zero, new Vector2(1100, 74));
            UiKit.Row(bottom, 20);
            UiKit.Button(bottom, T("home.playerName") + " : " + App.PlayerName(), EditName, ButtonVariant.Ghost, 24);
            UiKit.Button(bottom, T("home.backTitle"), () => App.Go(() => new TitleScreen()), ButtonVariant.Ghost, 24);
        }

        private void Tile(Transform parent, string key, string bg, Action onClick)
        {
            Button b = UiKit.Button(parent, string.Empty, () => onClick(), ButtonVariant.Normal);
            Image art = UiKit.Image(b.transform, "Art", App.Assets.Sprite(bg), new Color(1, 1, 1, 0.55f));
            UiKit.Stretch(art.rectTransform, 6, 6, 6, 6);
            art.transform.SetSiblingIndex(0);
            Image shade = UiKit.Image(b.transform, "Shade", UiKit.VerticalFade, new Color(0.05f, 0.02f, 0.12f, 0.9f));
            UiKit.Stretch(shade.rectTransform, 6, 6, 6, 6);
            shade.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
            Text t = UiKit.Label(b.transform, T(key), 38, Color.white, TextAnchor.LowerCenter, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 12, 12, 12, 26);
            UiKit.AddShadow(t, new Color(0, 0, 0, 0.8f), new Vector2(0, -3));
        }

        private void EditName()
        {
            InputField field = null;
            App.OpenModal(T("home.playerName"), body =>
            {
                Text hint = UiKit.Label(body, T("home.playerNameHint"), 22, UiKit.SubTextColor);
                UiKit.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(760, 48));
                field = UiKit.Input(body, App.Settings.Current.PlayerName, T("common.defaultPlayerName"), GameConstants.Online.PlayerNameMax, null);
                UiKit.Place((RectTransform)field.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(700, 72));
            }, new[]
            {
                GameApp.Btn(T("common.cancel"), ButtonVariant.Ghost, null),
                GameApp.Btn(T("common.save"), ButtonVariant.Primary, () =>
                {
                    string name = SettingsService.SanitizePlayerName(field != null ? field.text : string.Empty);
                    if (App.Settings.Update(s => s.PlayerName = name)) Refresh();
                }),
            }, 860, 400);
        }

        public override void OnEscape()
        {
            App.Go(() => new TitleScreen());
        }
    }

    // ========================================================================= ストーリー選択
    public sealed class StoryScreen : ScreenBase
    {
        private string selectedId;
        private RectTransform detail;

        public StoryScreen(string stageId)
        {
            selectedId = stageId;
        }

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            List<string> ids = App.Db.OfficialIds();
            if (selectedId == null || App.Db.IndexOfOfficial(selectedId) < 0)
            {
                // 未クリアの最初の解放済みステージを選ぶ
                selectedId = ids[0];
                for (int i = 0; i < ids.Count; i++)
                {
                    if (App.Progress.IsUnlocked(ids, i)) selectedId = ids[i];
                    if (!App.Progress.IsCleared(ids[i])) break;
                }
            }
            StageData sel = App.Db.GetOfficial(selectedId);
            CharacterProfile enemy = App.Db.EnemyOf(sel);
            Common.Background(App, Root, enemy.BackgroundKey, 0.6f);
            Common.Header(App, Root, T("home.story"), App.GoHome);

            RectTransform listHost = UiKit.Rect(Root, "List");
            listHost.anchorMin = new Vector2(0, 0);
            listHost.anchorMax = new Vector2(0.36f, 1);
            listHost.offsetMin = new Vector2(40, 40);
            listHost.offsetMax = new Vector2(0, -140);
            RectTransform list = UiKit.ScrollList(listHost, out ScrollRect _, 10);
            UiKit.Stretch((RectTransform)list.parent.parent);
            for (int i = 0; i < App.Db.OfficialStages.Count; i++)
            {
                StageData s = App.Db.OfficialStages[i];
                bool unlocked = App.Progress.IsUnlocked(ids, i);
                CharacterProfile e = App.Db.EnemyOf(s);
                string label = unlocked ? string.Format("{0:00}  {1}", i + 1, Txt(e.Name)) : string.Format("{0:00}  ???", i + 1);
                if (App.Progress.IsCleared(s.Id)) label += "  ★";
                string id = s.Id;
                Button b = UiKit.Button(list, label, () =>
                {
                    selectedId = id;
                    Refresh();
                }, s.Id == selectedId ? ButtonVariant.Primary : ButtonVariant.Normal, 28);
                UiKit.ButtonLabel(b).alignment = TextAnchor.MiddleLeft;
                UiKit.Height(b, 76);
                b.interactable = unlocked;
            }

            detail = UiKit.Rect(Root, "Detail");
            detail.anchorMin = new Vector2(0.38f, 0);
            detail.anchorMax = new Vector2(1, 1);
            detail.offsetMin = new Vector2(0, 40);
            detail.offsetMax = new Vector2(-40, -140);
            int index = App.Db.IndexOfOfficial(selectedId);
            bool open = App.Progress.IsUnlocked(ids, index);
            Image pt = Common.Portrait(App, detail, enemy, new Vector2(0.78f, 0.5f), Vector2.zero, 880);
            if (!open) pt.color = new Color(0, 0, 0, 0.85f);
            Image panel = UiKit.Panel(detail, "Info");
            panel.rectTransform.anchorMin = new Vector2(0, 0);
            panel.rectTransform.anchorMax = new Vector2(0.6f, 1);
            panel.rectTransform.offsetMin = Vector2.zero;
            panel.rectTransform.offsetMax = Vector2.zero;
            RectTransform col = UiKit.Stretch(UiKit.Rect(panel.transform, "Col"), 40, 40, 36, 36);
            UiKit.Column(col, 14);
            if (!open)
            {
                UiKit.Height(UiKit.Label(col, T("story.locked"), 44, UiKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold), 70);
                UiKit.Height(UiKit.Label(col, T("story.lockedHint"), 26, UiKit.SubTextColor, TextAnchor.UpperLeft), 80);
                return;
            }
            UiKit.Height(UiKit.Label(col, App.RankLabel(enemy) + "  ·  " + Txt(enemy.Title), 24, enemy.Color, TextAnchor.MiddleLeft), 40);
            UiKit.Height(UiKit.Label(col, Txt(enemy.Name), 54, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 76);
            UiKit.Height(UiKit.Label(col, Txt(sel.Name), 32, UiKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold), 50);
            UiKit.Height(UiKit.Label(col, Txt(sel.Summary), 24, UiKit.TextColor, TextAnchor.UpperLeft), 110);
            string spell = sel.SpellName != null && !string.IsNullOrEmpty(Txt(sel.SpellName)) ? Txt(sel.SpellName) : Txt(enemy.Spell);
            UiKit.Height(UiKit.Label(col, "✦ " + spell, 26, UiKit.Pink, TextAnchor.MiddleLeft), 44);
            int best = App.Progress.BestOf(sel.Id);
            UiKit.Height(UiKit.Label(col, "BEST  " + (best > 0 ? UiKit.FormatNumber(best) : "—"), 30, UiKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold), 50);
            RectTransform spacer = UiKit.Rect(col, "Spacer");
            spacer.gameObject.GetOrAdd<LayoutElement>().flexibleHeight = 1;
            RectTransform row = UiKit.Rect(col, "Actions");
            UiKit.Height(row, 84);
            UiKit.Row(row, 16);
            string sid = sel.Id;
            UiKit.Button(row, T("common.play"), () => Play(sid, BattlePlayer.Human), ButtonVariant.Primary, 30);
            UiKit.Button(row, T("common.botPlay"), () => Play(sid, BattlePlayer.Bot), ButtonVariant.Normal, 24);
            UiKit.Button(row, T("common.ranking"), () => Common.OpenRanking(App, LeaderboardService.StageKey(sid), T("common.ranking") + " · " + Txt(sel.Name)), ButtonVariant.Ghost, 24);
        }

        private void Play(string stageId, BattlePlayer player)
        {
            App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Story, StageId = stageId }, player));
        }

        public override void Tick(float dt)
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) && !App.ModalOpen && App.Progress.IsUnlocked(App.Db.OfficialIds(), App.Db.IndexOfOfficial(selectedId)))
            {
                Play(selectedId, BattlePlayer.Human);
            }
        }
    }

    // ========================================================================= エンドレス導入
    public sealed class EndlessScreen : ScreenBase
    {
        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_star", 0.55f);
            Common.Header(App, Root, T("home.endless"), App.GoHome);
            Image panel = UiKit.Panel(Root, "Panel");
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.48f), Vector2.zero, new Vector2(1100, 600));
            RectTransform col = UiKit.Stretch(UiKit.Rect(panel.transform, "Col"), 50, 50, 50, 40);
            UiKit.Column(col, 18);
            UiKit.Height(UiKit.Label(col, T("endless.desc"), 30, UiKit.TextColor), 110);
            UiKit.Height(UiKit.Label(col, "BEST  " + UiKit.FormatNumber(App.Progress.EndlessBest), 54, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold), 90);
            UiKit.Height(UiKit.Label(col, T("endless.clearedN", "n", App.Progress.EndlessBestCleared), 30, UiKit.SubTextColor), 50);
            RectTransform spacer = UiKit.Rect(col, "Spacer");
            spacer.gameObject.GetOrAdd<LayoutElement>().flexibleHeight = 1;
            RectTransform row = UiKit.Rect(col, "Actions");
            UiKit.Height(row, 90);
            UiKit.Row(row, 18);
            UiKit.Button(row, T("common.start"), () => Play(BattlePlayer.Human), ButtonVariant.Primary, 32);
            UiKit.Button(row, T("common.botPlay"), () => Play(BattlePlayer.Bot), ButtonVariant.Normal, 26);
            UiKit.Button(row, T("common.ranking"), () => Common.OpenRanking(App, LeaderboardService.EndlessKey, T("common.ranking") + " · " + T("home.endless")), ButtonVariant.Ghost, 26);
        }

        private void Play(BattlePlayer p)
        {
            App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Endless, Run = new EndlessRun() }, p));
        }
    }

    // ========================================================================= 図鑑
    public sealed class FigurebookScreen : ScreenBase
    {
        private int selected;

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            List<CharacterProfile> list = new List<CharacterProfile>(App.Db.Enemies);
            selected = Mathf.Clamp(selected, 0, list.Count - 1);
            CharacterProfile cur = list[selected];
            FigurebookState st = App.Progress.FigurebookStateOf(cur.Id);
            Common.Background(App, Root, st == FigurebookState.Locked ? "bg_title" : cur.BackgroundKey, 0.6f);
            int seen = list.FindAll(c => App.Progress.FigurebookStateOf(c.Id) != FigurebookState.Locked).Count;
            Common.Header(App, Root, T("home.figurebook") + "  " + seen + " / " + list.Count, App.GoHome);

            RectTransform grid = UiKit.Rect(Root, "Grid");
            grid.anchorMin = new Vector2(0, 0);
            grid.anchorMax = new Vector2(0.36f, 1);
            grid.offsetMin = new Vector2(40, 40);
            grid.offsetMax = new Vector2(0, -140);
            GridLayoutGroup g = grid.gameObject.GetOrAdd<GridLayoutGroup>();
            g.cellSize = new Vector2(196, 170);
            g.spacing = new Vector2(14, 14);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            for (int i = 0; i < list.Count; i++)
            {
                CharacterProfile c = list[i];
                FigurebookState s = App.Progress.FigurebookStateOf(c.Id);
                int idx = i;
                Button b = UiKit.Button(grid, string.Empty, () =>
                {
                    selected = idx;
                    Refresh();
                }, i == selected ? ButtonVariant.Primary : ButtonVariant.Normal);
                Image face = UiKit.Image(b.transform, "Face", App.Assets.Sprite(c.PortraitKey), s == FigurebookState.Locked ? new Color(0, 0, 0, 0.9f) : Color.white);
                UiKit.Stretch(face.rectTransform, 8, 8, 8, 34);
                face.preserveAspect = true;
                Common.AttachFace(App, face, c);
                Text n = UiKit.Label(b.transform, s == FigurebookState.Locked ? "???" : Txt(c.Name), 20, Color.white, TextAnchor.LowerCenter);
                UiKit.Stretch(n.rectTransform, 4, 4, 4, 6);
            }

            RectTransform detail = UiKit.Rect(Root, "Detail");
            detail.anchorMin = new Vector2(0.38f, 0);
            detail.anchorMax = new Vector2(1, 1);
            detail.offsetMin = new Vector2(0, 40);
            detail.offsetMax = new Vector2(-40, -140);
            Image pt = Common.Portrait(App, detail, cur, new Vector2(0.8f, 0.5f), Vector2.zero, 880);
            if (st == FigurebookState.Locked) pt.color = new Color(0, 0, 0, 0.9f);
            Image panel = UiKit.Panel(detail, "Info");
            panel.rectTransform.anchorMin = Vector2.zero;
            panel.rectTransform.anchorMax = new Vector2(0.62f, 1);
            panel.rectTransform.offsetMin = Vector2.zero;
            panel.rectTransform.offsetMax = Vector2.zero;
            RectTransform col = UiKit.Stretch(UiKit.Rect(panel.transform, "Col"), 40, 40, 36, 36);
            UiKit.Column(col, 14);
            if (st == FigurebookState.Locked)
            {
                UiKit.Height(UiKit.Label(col, "???", 54, UiKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold), 80);
                UiKit.Height(UiKit.Label(col, T("story.lockedHint"), 26, UiKit.SubTextColor, TextAnchor.UpperLeft), 80);
                return;
            }
            UiKit.Height(UiKit.Label(col, App.RankLabel(cur) + "  ·  " + Txt(cur.Title), 24, cur.Color, TextAnchor.MiddleLeft), 40);
            UiKit.Height(UiKit.Label(col, Txt(cur.Name), 54, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 76);
            UiKit.Height(UiKit.Label(col, Txt(cur.Intro), 25, UiKit.TextColor, TextAnchor.UpperLeft), 170);
            if (st == FigurebookState.Full)
            {
                UiKit.Height(UiKit.Label(col, "✦ " + Txt(cur.Spell), 30, UiKit.Pink, TextAnchor.MiddleLeft, FontStyle.Bold), 50);
                UiKit.Height(UiKit.Label(col, Txt(cur.SpellDetail), 24, UiKit.SubTextColor, TextAnchor.UpperLeft), 150);
            }
            else
            {
                UiKit.Height(UiKit.Label(col, T("book.defeatToUnlock"), 24, UiKit.SubTextColor, TextAnchor.UpperLeft), 80);
            }
        }
    }

    // ========================================================================= 設定
    public sealed class SettingsScreen : ScreenBase
    {
        private static readonly string[] Tabs = { "audio", "video", "controls", "language", "data" };
        private string tab = "audio";
        private KeyAction? waitingKey;
        private readonly Action onBack;

        /// <param name="back">戻り先（null ならホーム）</param>
        public SettingsScreen(Action back)
        {
            onBack = back;
        }

        private void Back()
        {
            if (onBack != null) onBack();
            else App.GoHome();
        }

        public override void OnEscape()
        {
            if (waitingKey.HasValue)
            {
                waitingKey = null;
                Refresh();
                return;
            }
            Back();
        }

        public override void Build()
        {
            Common.Background(App, Root, "bg_clock", 0.7f);
            Common.Header(App, Root, T("home.settings"), Back);
            RectTransform tabs = UiKit.Rect(Root, "Tabs");
            UiKit.Place(tabs, new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(1300, 72));
            UiKit.Row(tabs, 12);
            foreach (string t in Tabs)
            {
                string id = t;
                UiKit.Button(tabs, T("settings.tab." + t), () =>
                {
                    tab = id;
                    waitingKey = null;
                    Refresh();
                }, t == tab ? ButtonVariant.Primary : ButtonVariant.Ghost, 26);
            }
            Image panel = UiKit.Panel(Root, "Panel");
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.42f), Vector2.zero, new Vector2(1300, 680));
            RectTransform col = UiKit.Stretch(UiKit.Rect(panel.transform, "Col"), 50, 50, 40, 40);
            UiKit.Column(col, 16);
            GameSettings s = App.Settings.Current;
            switch (tab)
            {
                case "audio":
                    VolumeRow(col, "settings.master", s.MasterVolume, v => App.Settings.Update(x => x.MasterVolume = v));
                    VolumeRow(col, "settings.bgm", s.BgmVolume, v => App.Settings.Update(x => x.BgmVolume = v));
                    VolumeRow(col, "settings.se", s.SeVolume, v =>
                    {
                        App.Settings.Update(x => x.SeVolume = v);
                        App.Audio.PlaySe("ui_click");
                    });
                    break;
                case "video":
                    CycleRow(col, "settings.screenMode", SettingsService.ScreenModes, s.ScreenMode, m => T(m == "fullscreen" ? "settings.fullscreen" : "settings.windowed"), v => App.Settings.Update(x => x.ScreenMode = v));
                    CycleRow(col, "settings.resolution", SettingsService.Resolutions, s.Resolution, r => r == "auto" ? T("settings.auto") : r.Replace("x", " × "), v => App.Settings.Update(x => x.Resolution = v));
                    string[] fps = Array.ConvertAll(SettingsService.FpsOptions, f => f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    CycleRow(col, "settings.fps", fps, s.FpsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture), f => f == "0" ? T("settings.unlimited") : f, v => App.Settings.Update(x => x.FpsLimit = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
                    CycleRow(col, "settings.preview", new[] { "on", "off" }, s.ShowPreview ? "on" : "off", v => v == "on" ? "ON" : "OFF", v => App.Settings.Update(x => x.ShowPreview = v == "on"));
                    break;
                case "controls":
                    foreach (KeyAction a in SettingsService.KeyActions) KeyRow(col, a, s);
                    Row(col, "settings.key.undo", "Ctrl + Z / " + T("settings.rightClick"));
                    RectTransform r = UiKit.Rect(col, "Actions");
                    UiKit.Height(r, 76);
                    UiKit.Row(r, 16);
                    UiKit.Button(r, T("settings.resetKeys"), () =>
                    {
                        if (App.Settings.ResetKeys()) App.Notice(T("settings.keysReset"), NoticeKind.Success);
                        Refresh();
                    }, ButtonVariant.Normal, 24);
                    UiKit.Button(r, T("settings.replayTutorial"), () =>
                    {
                        if (App.Progress.SetTutorialSeen(false)) App.Notice(T("settings.tutorialReset"), NoticeKind.Success);
                    }, ButtonVariant.Normal, 24);
                    break;
                case "language":
                    string[] names = { "日本語", "English", "简体中文", "한국어" };
                    CycleRow(col, "settings.language", SettingsService.Languages, s.Language, l => names[Math.Max(0, Array.IndexOf(SettingsService.Languages, l))], v => App.Settings.Update(x => x.Language = v));
                    break;
                default:
                    Row(col, "home.playerName", App.PlayerName());
                    Row(col, "settings.online", App.Remote != null && App.Remote.IsConfigured ? "ON" : T("online.statusNotConfigured"));
                    RectTransform d = UiKit.Rect(col, "Danger");
                    UiKit.Height(d, 80);
                    UiKit.Row(d, 16);
                    UiKit.Button(d, T("settings.clearData"), () => App.Confirm(T("settings.clearConfirm"), T("common.delete"), ClearData), ButtonVariant.Danger, 26);
                    break;
            }
        }

        private void ClearData()
        {
            if (LocalSave.ClearAllGameData())
            {
                // 保存データを読み直すため Boot シーンを再読込する
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
            else
            {
                App.Notice(T("notice.saveFailed"), NoticeKind.Error);
            }
        }

        private RectTransform RowBase(RectTransform col, string labelKey)
        {
            RectTransform row = UiKit.Rect(col, "Row");
            UiKit.Height(row, 76);
            Text l = UiKit.Label(row, T(labelKey), 28, UiKit.TextColor, TextAnchor.MiddleLeft);
            RectTransform lr = UiKit.Stretch(l.rectTransform);
            lr.anchorMax = new Vector2(0.4f, 1);
            RectTransform right = UiKit.Stretch(UiKit.Rect(row, "Value"));
            right.anchorMin = new Vector2(0.42f, 0);
            return right;
        }

        private void Row(RectTransform col, string labelKey, string value)
        {
            Text v = UiKit.Label(RowBase(col, labelKey), value, 28, UiKit.Gold, TextAnchor.MiddleLeft);
            UiKit.Stretch(v.rectTransform);
        }

        private void VolumeRow(RectTransform col, string key, int value, Action<int> set)
        {
            RectTransform right = RowBase(col, key);
            Text num = UiKit.Label(right, value.ToString(System.Globalization.CultureInfo.InvariantCulture), 28, UiKit.Gold, TextAnchor.MiddleRight);
            RectTransform nr = UiKit.Stretch(num.rectTransform);
            nr.anchorMin = new Vector2(0.86f, 0);
            Slider sl = UiKit.Slider(right, 0, 100, value, v =>
            {
                int iv = Mathf.RoundToInt(v);
                num.text = iv.ToString(System.Globalization.CultureInfo.InvariantCulture);
                set(iv);
            });
            RectTransform sr = UiKit.Stretch((RectTransform)sl.transform, 0, 0, 12, 12);
            sr.anchorMax = new Vector2(0.82f, 1);
        }

        private void CycleRow(RectTransform col, string key, string[] values, string current, Func<string, string> label, Action<string> set)
        {
            RectTransform right = RowBase(col, key);
            UiKit.Row(right, 10);
            int idx = Math.Max(0, Array.IndexOf(values, current));
            UiKit.Button(right, "‹", () =>
            {
                set(values[(idx - 1 + values.Length) % values.Length]);
                Refresh();
            }, ButtonVariant.Ghost, 30).gameObject.GetOrAdd<LayoutElement>().flexibleWidth = 0.2f;
            Text v = UiKit.Label(right, label(values[idx]), 28, UiKit.Gold);
            v.gameObject.GetOrAdd<LayoutElement>().flexibleWidth = 1f;
            UiKit.Button(right, "›", () =>
            {
                set(values[(idx + 1) % values.Length]);
                Refresh();
            }, ButtonVariant.Ghost, 30).gameObject.GetOrAdd<LayoutElement>().flexibleWidth = 0.2f;
        }

        private void KeyRow(RectTransform col, KeyAction a, GameSettings s)
        {
            string key = "settings.key." + (a == KeyAction.Pause ? "pause" : a == KeyAction.Retry ? "retry" : a == KeyAction.FastForward ? "fastForward" : "togglePreview");
            RectTransform right = RowBase(col, key);
            bool waiting = waitingKey.HasValue && waitingKey.Value == a;
            Button b = UiKit.Button(right, waiting ? "…" : KeyCodeMap.Label(s.KeyOf(a)), () =>
            {
                waitingKey = a;
                Refresh();
            }, waiting ? ButtonVariant.Primary : ButtonVariant.Normal, 26);
            RectTransform br = UiKit.Stretch((RectTransform)b.transform, 0, 0, 6, 6);
            br.anchorMax = new Vector2(0.5f, 1);
        }

        public override void Tick(float dt)
        {
            if (!waitingKey.HasValue || !UnityEngine.Input.anyKeyDown) return;
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.None || k >= KeyCode.Mouse0 || !UnityEngine.Input.GetKeyDown(k)) continue;
                if (k == KeyCode.Escape && waitingKey.Value != KeyAction.Pause) return; // Esc は OnEscape でキャンセル扱い
                KeyAction action = waitingKey.Value;
                waitingKey = null;
                if (!KeyCodeMap.TryToWeb(k, out string code) || !SettingsService.IsValidKeyCode(code))
                {
                    App.Notice(T("settings.keyInvalid"), NoticeKind.Warn);
                    Refresh();
                    return;
                }
                bool dup = false;
                foreach (KeyAction other in SettingsService.KeyActions)
                {
                    if (other != action && App.Settings.Current.KeyOf(other) == code) dup = true;
                }
                if (!App.Settings.BindKey(action, code)) App.Notice(T("settings.keyInvalid"), NoticeKind.Warn);
                else if (dup) App.Notice(T("settings.keySwapped"), NoticeKind.Info);
                Refresh();
                return;
            }
        }
    }
}

/*
## このアセットの役割
タイトル・ホーム・ストーリー選択・エンドレス導入・図鑑・設定の各画面です（Web版 menu.js 相当）。

## 他アセットとの依存関係
GameApp / Common / UiKit / 各サービス。BattleScreen・RecordsScreen・EditorMenuScreen へ遷移します。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
言語変更時は画面を作り直します（進行中のデータは保存済みのため失われません）。セーブデータ削除後は Boot シーンを再読込して初期状態から起動します。
*/
