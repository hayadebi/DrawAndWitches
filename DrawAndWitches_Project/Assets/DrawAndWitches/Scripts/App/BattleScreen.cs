using System;
using System.Collections.Generic;
using System.Diagnostics;
using DrawAndWitches.Core;
using DrawAndWitches.Data;
using DrawAndWitches.Logic;
using DrawAndWitches.Presentation;
using DrawAndWitches.Services;
using DrawAndWitches.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace DrawAndWitches.App
{
    /// <summary>フィールド上のポインター入力をワールド座標で通知する</summary>
    public sealed class FieldPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public Action<Vector2, bool> Down;
        public Action<Vector2, bool> Drag;
        public Action Up;
        public Action RightClick;
        public bool Pressed { get; private set; }

        public bool TryToWorld(Vector2 screen, out Vector2 world)
        {
            RectTransform rt = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out Vector2 local))
            {
                world = new Vector2(local.x, -local.y);
                return true;
            }
            world = Vector2.zero;
            return false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right)
            {
                RightClick?.Invoke();
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!TryToWorld(e.position, out Vector2 w)) return;
            Pressed = true;
            Down?.Invoke(w, e.pointerId >= 0);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Pressed || e.button != PointerEventData.InputButton.Left) return;
            if (TryToWorld(e.position, out Vector2 w)) Drag?.Invoke(w, e.pointerId >= 0);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !Pressed) return;
            Pressed = false;
            Up?.Invoke();
        }

        private void OnDisable()
        {
            if (!Pressed) return;
            Pressed = false;
            Up?.Invoke();
        }
    }

    /// <summary>
    /// 戦闘画面（Web版 BattleScreen の移植）。
    /// 開始演出: (ストーリー会話) → (bot計画) → カットイン → (初回チュートリアル) → 編集開始。
    /// </summary>
    public sealed class BattleScreen : ScreenBase
    {
        public const float FieldScale = 1000f / GameConstants.World.Height;
        private const float BotBudgetMs = 12f;
        private const float TypeInterval = 0.028f;
        private const float CutinSeconds = 2.1f;
        private const float BannerSeconds = 1.5f;
        private const float ComboSeconds = 1.3f;
        private const float DamageFaceSeconds = 0.6f;

        private enum IntroStep
        {
            Dialogue,
            BotPlan,
            Cutin,
            Tutorial,
            Running,
        }

        private readonly BattleContext ctx;
        private readonly BattlePlayer player;
        private StageData stage;
        private CharacterProfile enemy;
        private uint seed;
        private BattleController ctrl;
        private IntroStep step;
        private bool endedHandled;

        private RectTransform fieldFrame;
        private FieldGraphic field;
        private FieldPointer pointer;
        private RectTransform overlay;
        private Image enemyFace;
        private Image playerFace;
        private Text timerNum;
        private Image timerRing;
        private Text phaseMain;
        private Text phaseSub;
        private Text opsVal;
        private Text cancelsVal;
        private Text damageVal;
        private Text maxChainVal;
        private RectTransform combo;
        private Text comboNum;
        private float comboUntil;
        private Button undoBtn;
        private Text fastLabel;
        private Text previewLabel;
        private RectTransform banner;
        private Text bannerText;
        private float bannerUntil;
        private GameObject pauseModal;
        private Image bigPortrait;
        private bool facesCaptured;
        private Color enemyFaceBase;
        private Color playerFaceBase;
        private Vector2 fieldBasePos;

        // 会話
        private List<DialogueLine> lines;
        private int lineIndex;
        private string lineFull;
        private int lineShown;
        private float typeAcc;
        private Text dlgName;
        private Text dlgText;
        private Image dlgLeft;
        private Image dlgRight;
        private RectTransform dlgRoot;

        // bot
        private IEnumerator<BotProgress> botIter;
        private BotPlanner planner;
        private Image botBar;
        private Text botText;
        private RectTransform botRoot;

        // カットイン・チュートリアル
        private RectTransform cutinRoot;
        private RectTransform cutinPortrait;
        private float cutinT;
        private RectTransform tutorialRoot;
        private int tutorialPage;

        public BattleScreen(BattleContext context, BattlePlayer player)
        {
            ctx = context ?? throw new ArgumentNullException(nameof(context));
            this.player = player;
        }

        private StageData ResolveStage()
        {
            switch (ctx.Kind)
            {
                case BattleKind.Story:
                    return App.Db.GetOfficial(ctx.StageId) ?? throw new InvalidOperationException("story stage not found: " + ctx.StageId);
                case BattleKind.Endless:
                    if (ctx.Run == null) ctx.Run = new EndlessRun();
                    if (ctx.Stage == null) ctx.Stage = PickEndless(ctx.Run);
                    return ctx.Stage;
                case BattleKind.Custom:
                case BattleKind.Shared:
                    return ctx.Stage ?? throw new InvalidOperationException("custom/shared stage missing");
                default:
                    return ctx.Replay?.Stage ?? throw new InvalidOperationException("replay missing");
            }
        }

        /// <summary>エンドレスの出題（Web版 pickEndlessStage と同じ規則）</summary>
        private StageData PickEndless(EndlessRun run)
        {
            List<StageData> list = App.Db.OfficialStages;
            if (list.Count == 0) throw new InvalidOperationException("No official stage for endless");
            int idx = run.Rng.NextInt(list.Count);
            if (list.Count > 1 && idx == run.LastIndex) idx = (idx + 1 + run.Rng.NextInt(list.Count - 1)) % list.Count;
            run.LastIndex = idx;
            run.StageNo += 1;
            return list[idx];
        }

        public override void Build()
        {
            if (ctrl == null)
            {
                stage = ResolveStage();
                enemy = App.Db.EnemyOf(stage);
                seed = player == BattlePlayer.Replay ? ctx.Replay.Seed : DeterministicRng.CreateSeed();
                if (player != BattlePlayer.Replay && !Baking) App.Progress.MarkEncountered(stage.EnemyId);
                ctrl = new BattleController(stage, seed, player, player == BattlePlayer.Replay ? ctx.Replay.Commands : (player == BattlePlayer.Bot ? new List<InputCommand>() : null));
                ctrl.PlaySe = (k, lv) => App.Audio.PlaySe(k, lv);
                ctrl.SimEventRaised = OnSimEvent;
                ctrl.PhaseChanged = OnPhase;
                ctrl.Ended = OnEnded;
                ctrl.SetPaused(true);
                App.Audio.PlayBgm(string.IsNullOrEmpty(enemy.BgmKey) ? "bgm_battle" : enemy.BgmKey);
                lines = ctx.Kind == BattleKind.Story && player != BattlePlayer.Replay && !ctx.DialogueSeen ? App.Db.DialogueOf(ctx.StageId) : null;
                step = IntroStep.Dialogue;
                BuildLayout();
                NextIntro(IntroStep.Dialogue);
                return;
            }
            BuildLayout();
        }

        // ------------------------------------------------------------------ レイアウト

        private void BuildLayout()
        {
            Common.Background(App, Root, enemy.BackgroundKey, 0.45f);
            Image big = Common.Portrait(App, Root, enemy, new Vector2(0.11f, 0.45f), Vector2.zero, 980);
            big.color = new Color(1, 1, 1, 0.5f);
            bigPortrait = big;
            facesCaptured = false;

            fieldFrame = UiKit.Rect(Root, "FieldFrame");
            Vector2 size = new Vector2(GameConstants.World.Width * FieldScale, GameConstants.World.Height * FieldScale);
            UiKit.Place(fieldFrame, new Vector2(0.43f, 0.5f), Vector2.zero, size);
            Image fbg = UiKit.Image(fieldFrame, "FieldBg", UiKit.Rounded, new Color(0.04f, 0.02f, 0.12f, 0.72f));
            UiKit.Stretch(fbg.rectTransform, -8, -8, -8, -8);
            Image fline = UiKit.Image(fbg.transform, "Border", UiKit.RoundedOutline, new Color(enemy.Color.r, enemy.Color.g, enemy.Color.b, 0.75f));
            UiKit.Stretch(fline.rectTransform);
            fieldFrame.gameObject.GetOrAdd<RectMask2D>();

            enemyFace = UiKit.Image(fieldFrame, "EnemyFace", App.Assets.Sprite(enemy.PortraitKey), new Color(1, 1, 1, 0.9f));
            enemyFace.preserveAspect = true;
            Common.AttachFace(App, enemyFace, enemy);
            UiKit.Place(enemyFace.rectTransform, new Vector2(0, 1), new Vector2(GameConstants.Enemy.X * FieldScale, -GameConstants.Enemy.Y * FieldScale + 10), new Vector2(260, 260));
            playerFace = UiKit.Image(fieldFrame, "PlayerFace", App.Assets.Sprite(App.Db.Player.PortraitKey), new Color(1, 1, 1, 0.95f));
            playerFace.preserveAspect = true;
            Common.AttachFace(App, playerFace, App.Db.Player);
            UiKit.Place(playerFace.rectTransform, new Vector2(0, 1), new Vector2(GameConstants.Player.X * FieldScale, -GameConstants.Player.Y * FieldScale - 10), new Vector2(150, 150));

            RectTransform frt = UiKit.Rect(fieldFrame, "Field");
            frt.anchorMin = frt.anchorMax = new Vector2(0, 1);
            frt.pivot = new Vector2(0, 1);
            frt.sizeDelta = new Vector2(GameConstants.World.Width, GameConstants.World.Height);
            frt.anchoredPosition = Vector2.zero;
            frt.localScale = new Vector3(FieldScale, FieldScale, 1);
            field = frt.gameObject.GetOrAdd<FieldGraphic>();
            field.color = Color.white;
            field.Drawer = g => ctrl.Draw(g);
            pointer = frt.gameObject.GetOrAdd<FieldPointer>();
            pointer.Down = (w, touch) => ctrl.PointerDown(w.x, w.y, touch);
            pointer.Drag = (w, touch) => ctrl.PointerMove(w.x, w.y, touch);
            pointer.Up = () => ctrl.PointerUp();
            pointer.RightClick = DoUndo;

            banner = UiKit.Rect(fieldFrame, "Banner");
            UiKit.Place(banner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x + 20, 120));
            Image band = UiKit.Image(banner, "Band", null, new Color(enemy.Color.r * 0.4f, enemy.Color.g * 0.3f, enemy.Color.b * 0.5f, 0.85f));
            UiKit.Stretch(band.rectTransform);
            bannerText = UiKit.Label(banner, string.Empty, 54, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(bannerText.rectTransform);
            UiKit.AddShadow(bannerText, new Color(1f, 0.4f, 0.9f, 0.8f), new Vector2(0, -4));
            banner.gameObject.SetActive(false);

            BuildSide();
            overlay = UiKit.Stretch(UiKit.Rect(Root, "Overlay"));
        }

        private void BuildSide()
        {
            RectTransform side = UiKit.Rect(Root, "Side");
            side.anchorMin = side.anchorMax = new Vector2(1, 0.5f);
            side.pivot = new Vector2(1, 0.5f);
            side.anchoredPosition = new Vector2(-40, 0);
            side.sizeDelta = new Vector2(560, 1000);
            UiKit.Column(side, 14);

            Image head = UiKit.Panel(side, "Head");
            UiKit.Height(head, 120);
            Image face = UiKit.Image(head.transform, "Face", App.Assets.Sprite(enemy.PortraitKey), Color.white);
            face.preserveAspect = true;
            Common.AttachFace(App, face, enemy);
            UiKit.Place(face.rectTransform, new Vector2(0, 0.5f), new Vector2(70, 0), new Vector2(110, 110));
            Text rank = UiKit.Label(head.transform, App.RankLabel(enemy), 20, enemy.Color, TextAnchor.LowerLeft);
            UiKit.Stretch(rank.rectTransform, 140, 20, 12, 62);
            Text name = UiKit.Label(head.transform, Txt(enemy.Name), 34, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(name.rectTransform, 140, 20, 60, 10);
            string mode = ctx.Kind.ToString().ToUpperInvariant() + (player == BattlePlayer.Bot ? " · BOT" : player == BattlePlayer.Replay ? " · REPLAY" : string.Empty);
            Text modeT = UiKit.Label(head.transform, mode, 18, UiKit.SubTextColor, TextAnchor.UpperRight);
            UiKit.Stretch(modeT.rectTransform, 140, 18, 12, 70);

            Image timer = UiKit.Panel(side, "Timer");
            UiKit.Height(timer, 220);
            Image track = UiKit.Image(timer.transform, "Track", UiKit.Circle, new Color(1, 1, 1, 0.08f));
            UiKit.Place(track.rectTransform, new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(180, 180));
            timerRing = UiKit.Image(timer.transform, "Ring", UiKit.Circle, new Color(0.75f, 0.6f, 1f, 0.9f));
            UiKit.Place(timerRing.rectTransform, new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(180, 180));
            timerRing.type = Image.Type.Filled;
            timerRing.fillMethod = Image.FillMethod.Radial360;
            timerRing.fillOrigin = (int)Image.Origin360.Top;
            timerRing.fillClockwise = false;
            Image hole = UiKit.Image(timer.transform, "Hole", UiKit.Circle, new Color(0.08f, 0.05f, 0.2f, 1f));
            UiKit.Place(hole.rectTransform, new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(150, 150));
            timerNum = UiKit.Label(timer.transform, stage.EditTimeSec.ToString(), 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Place(timerNum.rectTransform, new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(150, 150));
            phaseMain = UiKit.Label(timer.transform, T("battle.phaseEdit"), 32, UiKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Stretch(phaseMain.rectTransform, 240, 20, 30, 110);
            phaseSub = UiKit.Label(timer.transform, T("battle.phaseEditSub"), 22, UiKit.SubTextColor, TextAnchor.UpperLeft);
            UiKit.Stretch(phaseSub.rectTransform, 240, 20, 118, 20);

            RectTransform stats = UiKit.Rect(side, "Stats");
            UiKit.Height(stats, 130);
            UiKit.Row(stats, 12);
            opsVal = Stat(stats, "battle.ops", new Color(0.37f, 0.83f, 1f));
            cancelsVal = Stat(stats, "battle.cancels", UiKit.Gold);
            damageVal = Stat(stats, "battle.damage", new Color(1f, 0.44f, 0.57f));

            Image comboBox = UiKit.Panel(side, "Combo");
            UiKit.Height(comboBox, 110);
            combo = UiKit.Stretch(UiKit.Rect(comboBox.transform, "Now"));
            comboNum = UiKit.Label(combo, "0 CHAIN", 54, UiKit.Pink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Stretch(comboNum.rectTransform, 30, 200, 0, 0);
            combo.gameObject.SetActive(false);
            maxChainVal = UiKit.Label(comboBox.transform, "MAX 0", 26, UiKit.SubTextColor, TextAnchor.MiddleRight);
            UiKit.Stretch(maxChainVal.rectTransform, 30, 30, 0, 0);

            if (ctx.Kind == BattleKind.Endless)
            {
                EndlessRun run = ctx.Run;
                EndlessScore es = ScoreCalculator.ComputeEndless(run.Sum, run.Cleared);
                Image eb = UiKit.Panel(side, "Endless");
                UiKit.Height(eb, 80);
                Text t = UiKit.Label(eb.transform, "#" + run.StageNo + "   ✓ " + run.Cleared + "   ★ " + UiKit.FormatNumber(es.Total) + "   +" + UiKit.FormatNumber(ScoreCalculator.NextEndlessBonus(run.Cleared)), 28, UiKit.Gold);
                UiKit.Stretch(t.rectTransform, 20, 20, 0, 0);
            }

            RectTransform controls = UiKit.Rect(side, "Controls");
            UiKit.Height(controls, 80);
            UiKit.Row(controls, 10);
            if (player == BattlePlayer.Human) undoBtn = UiKit.Button(controls, T("battle.undo"), DoUndo, ButtonVariant.Normal, 22);
            previewLabel = UiKit.ButtonLabel(UiKit.Button(controls, T("battle.preview"), TogglePreview, ButtonVariant.Normal, 22));
            fastLabel = UiKit.ButtonLabel(UiKit.Button(controls, T("battle.fast"), ToggleFast, ButtonVariant.Normal, 22));
            UiKit.Button(controls, T("battle.pause"), TogglePause, ButtonVariant.Ghost, 22);
            GameSettings s = App.Settings.Current;
            string hint = (player == BattlePlayer.Human ? "Ctrl+Z " + T("battle.undo") + "   " : string.Empty)
                + KeyCodeMap.Label(s.KeyOf(KeyAction.TogglePreview)) + " " + T("battle.preview") + "   "
                + KeyCodeMap.Label(s.KeyOf(KeyAction.FastForward)) + " " + T("battle.fast") + "   "
                + KeyCodeMap.Label(s.KeyOf(KeyAction.Pause)) + " " + T("battle.pause");
            UiKit.Height(UiKit.Label(side, hint, 18, UiKit.SubTextColor), 50);
            UpdateToggleLabels();
        }

        private Text Stat(RectTransform parent, string key, Color c)
        {
            Image p = UiKit.Panel(parent, "Stat");
            Text l = UiKit.Label(p.transform, T(key), 20, c, TextAnchor.UpperCenter);
            UiKit.Stretch(l.rectTransform, 6, 6, 14, 60);
            Text v = UiKit.Label(p.transform, "0", 48, Color.white, TextAnchor.LowerCenter, FontStyle.Bold);
            UiKit.Stretch(v.rectTransform, 6, 6, 40, 12);
            return v;
        }

        private void UpdateToggleLabels()
        {
            if (previewLabel != null) previewLabel.text = T("battle.preview") + (App.Settings.Current.ShowPreview ? " ON" : " OFF");
            if (fastLabel != null) fastLabel.text = T("battle.fast") + (ctrl.Fast ? " ×" + GameConstants.Timing.FastForwardMultiplier : string.Empty);
        }

        // ------------------------------------------------------------------ 開始演出

        private void NextIntro(IntroStep from)
        {
            if (Disposed) return;
            if (from <= IntroStep.Dialogue && lines != null && lines.Count > 0 && dlgRoot == null && !ctx.DialogueSeen)
            {
                step = IntroStep.Dialogue;
                ctrl.HideBarrages();
                StartDialogue();
                return;
            }
            if (from <= IntroStep.BotPlan && player == BattlePlayer.Bot && botIter == null)
            {
                step = IntroStep.BotPlan;
                StartBot();
                return;
            }
            if (from <= IntroStep.Cutin && cutinRoot == null)
            {
                step = IntroStep.Cutin;
                StartCutin();
                return;
            }
            if (from <= IntroStep.Tutorial && player == BattlePlayer.Human && !App.Progress.IsTutorialSeen && tutorialRoot == null)
            {
                step = IntroStep.Tutorial;
                tutorialPage = 0;
                ShowTutorial();
                return;
            }
            step = IntroStep.Running;
            ShowBanner(T("battle.editStart"));
            ctrl.SetPaused(false);
        }

        private void StartDialogue()
        {
            dlgRoot = UiKit.Stretch(UiKit.Rect(overlay, "Dialogue"));
            Image shade = UiKit.Image(dlgRoot, "Shade", UiKit.VerticalFade, new Color(0.03f, 0.01f, 0.08f, 0.85f), true);
            UiKit.Stretch(shade.rectTransform);
            shade.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
            Button hit = shade.gameObject.GetOrAdd<Button>();
            hit.transition = Selectable.Transition.None;
            hit.onClick.AddListener(AdvanceDialogue);
            dlgLeft = Common.Portrait(App, dlgRoot, App.Db.Player, new Vector2(0.18f, 0.42f), Vector2.zero, 820);
            dlgRight = Common.Portrait(App, dlgRoot, enemy, new Vector2(0.82f, 0.42f), Vector2.zero, 860);
            Image box = UiKit.Panel(dlgRoot, "Box");
            UiKit.Place(box.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 170), new Vector2(1500, 260));
            box.raycastTarget = false;
            dlgName = UiKit.Label(box.transform, string.Empty, 34, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(dlgName.rectTransform, 50, 50, 26, 180);
            dlgText = UiKit.Label(box.transform, string.Empty, 32, Color.white, TextAnchor.UpperLeft);
            UiKit.Stretch(dlgText.rectTransform, 50, 50, 84, 30);
            Button skip = UiKit.Button(dlgRoot, T("tutorial.skip"), FinishDialogue, ButtonVariant.Ghost, 24);
            UiKit.Place((RectTransform)skip.transform, new Vector2(1, 1), new Vector2(-150, -70), new Vector2(220, 70));
            ShowLine(0);
        }

        private void ShowLine(int i)
        {
            lineIndex = i;
            DialogueLine l = lines[i];
            CharacterProfile who = l.IsPlayer ? App.Db.Player : enemy;
            dlgName.text = Txt(who.Name);
            dlgName.color = who.Color;
            dlgLeft.color = l.IsPlayer ? Color.white : new Color(0.45f, 0.45f, 0.55f, 0.8f);
            dlgRight.color = !l.IsPlayer ? Color.white : new Color(0.45f, 0.45f, 0.55f, 0.8f);
            string speakingFace = string.IsNullOrEmpty(l.Face) ? CharacterPortrait.Talk : l.Face;
            Common.SetFace(dlgLeft, l.IsPlayer ? speakingFace : CharacterPortrait.Default);
            Common.SetFace(dlgRight, l.IsPlayer ? CharacterPortrait.Default : speakingFace);
            lineFull = Txt(l.Text);
            lineShown = 0;
            typeAcc = 0;
            dlgText.text = string.Empty;
        }

        private void AdvanceDialogue()
        {
            if (dlgRoot == null) return;
            if (lineShown < lineFull.Length)
            {
                lineShown = lineFull.Length;
                dlgText.text = lineFull;
                return;
            }
            if (lineIndex + 1 >= lines.Count)
            {
                FinishDialogue();
                return;
            }
            App.Audio.PlaySe("ui_click");
            ShowLine(lineIndex + 1);
        }

        private void FinishDialogue()
        {
            if (dlgRoot == null) return;
            UiKit.DestroySafe(dlgRoot.gameObject);
            ctx.DialogueSeen = true; // 同じ挑戦内のリトライでは再表示しない
            ctrl.RevealBarrages();
            NextIntro(IntroStep.BotPlan);
        }

        private void StartBot()
        {
            try
            {
                planner = new BotPlanner(stage, seed);
                botIter = planner.PlanSteps();
            }
            catch (Exception e)
            {
                Debug.LogError("[Bot] init failed: " + e);
                App.Notice(T("bot.failed"), NoticeKind.Error);
                Quit();
                return;
            }
            BuildBotPanel();
        }

        private void BuildBotPanel()
        {
            botRoot = UiKit.Stretch(UiKit.Rect(overlay, "Planning"));
            UiKit.Blocker(botRoot, 0.55f);
            Image p = UiKit.Panel(botRoot, "Panel");
            UiKit.Place(p.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 300));
            Text title = UiKit.Label(p.transform, "BOT", 40, UiKit.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.Stretch(title.rectTransform, 20, 20, 24, 200);
            Image track = UiKit.Image(p.transform, "Track", UiKit.Rounded, new Color(1, 1, 1, 0.1f));
            UiKit.Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(620, 30));
            botBar = UiKit.Image(track.transform, "Bar", UiKit.Rounded, UiKit.Pink);
            botBar.rectTransform.anchorMin = Vector2.zero;
            botBar.rectTransform.anchorMax = new Vector2(0, 1);
            botBar.rectTransform.offsetMin = botBar.rectTransform.offsetMax = Vector2.zero;
            botText = UiKit.Label(p.transform, "0", 24, UiKit.SubTextColor);
            UiKit.Place(botText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(600, 40));
            Button cancel = UiKit.Button(p.transform, T("common.cancel"), Quit, ButtonVariant.Ghost, 24);
            UiKit.Place((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(260, 64));
        }

        private void TickBot()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                while (sw.Elapsed.TotalMilliseconds < BotBudgetMs)
                {
                    if (!botIter.MoveNext())
                    {
                        BotPlan plan = planner.Best;
                        if (plan == null) throw new InvalidOperationException("bot produced no plan");
                        ctrl = RebuildForBot(BotPlanner.BuildCommands(plan, stage));
                        UiKit.DestroySafe(botRoot.gameObject);
                        botRoot = null;
                        NextIntro(IntroStep.Cutin);
                        return;
                    }
                    BotProgress pr = botIter.Current;
                    float ratio = Mathf.Min(1f, pr.Evaluations / (float)GameConstants.Bot.MaxEvaluations);
                    botBar.rectTransform.anchorMax = new Vector2(ratio, 1);
                    botText.text = pr.Evaluations + " · " + (pr.Clear ? "✓" : "…");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Bot] planning failed: " + e);
                App.Notice(T("bot.failed"), NoticeKind.Error);
                Quit();
            }
        }

        /// <summary>bot の計画済みコマンドで同じ seed の制御を作り直す（人間と同じ入力経路で再生）</summary>
        private BattleController RebuildForBot(List<InputCommand> commands)
        {
            BattleController c = new BattleController(stage, seed, BattlePlayer.Bot, commands)
            {
                PlaySe = ctrl.PlaySe,
                SimEventRaised = ctrl.SimEventRaised,
                PhaseChanged = ctrl.PhaseChanged,
                Ended = ctrl.Ended,
                Fast = ctrl.Fast,
            };
            c.SetPaused(true);
            ctrl.Dispose();
            return c;
        }

        private void StartCutin()
        {
            App.Audio.PlaySe("cutin");
            cutinT = 0;
            cutinRoot = UiKit.Stretch(UiKit.Rect(overlay, "Cutin"));
            Image shade = UiKit.Image(cutinRoot, "Shade", null, new Color(0, 0, 0, 0.55f), true);
            UiKit.Stretch(shade.rectTransform);
            Button hit = shade.gameObject.GetOrAdd<Button>();
            hit.transition = Selectable.Transition.None;
            hit.onClick.AddListener(() => cutinT = CutinSeconds);
            Image band = UiKit.Image(cutinRoot, "Band", null, new Color(enemy.Color.r * 0.55f, enemy.Color.g * 0.35f, enemy.Color.b * 0.6f, 0.92f));
            UiKit.Place(band.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2600, 300));
            band.rectTransform.localEulerAngles = new Vector3(0, 0, -6);
            Image pt = Common.Portrait(App, cutinRoot, enemy, new Vector2(0.72f, 0.5f), Vector2.zero, 980, CharacterPortrait.Spell);
            cutinPortrait = pt.rectTransform;
            string spell = Txt(stage.SpellName);
            if (string.IsNullOrEmpty(spell)) spell = Txt(enemy.Spell);
            Text name = UiKit.Label(cutinRoot, Txt(enemy.Name), 36, UiKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(name.rectTransform, new Vector2(0.32f, 0.5f), new Vector2(0, 80), new Vector2(1100, 60));
            Text sp = UiKit.Label(cutinRoot, spell, 64, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(sp.rectTransform, new Vector2(0.32f, 0.5f), new Vector2(0, -10), new Vector2(1100, 120));
            UiKit.AddShadow(sp, new Color(1f, 0.4f, 0.9f, 0.9f), new Vector2(0, -4));
            Text skip = UiKit.Label(cutinRoot, T("battle.skip"), 22, UiKit.SubTextColor);
            UiKit.Place(skip.rectTransform, new Vector2(0.5f, 0.06f), Vector2.zero, new Vector2(800, 40));
        }

        private void TickCutin(float dt)
        {
            cutinT += dt;
            float k = Mathf.Clamp01(cutinT / 0.35f);
            if (cutinPortrait != null) cutinPortrait.anchoredPosition = new Vector2(Mathf.Lerp(600f, 0f, 1f - Mathf.Pow(1f - k, 3f)), 0);
            if (cutinT >= CutinSeconds)
            {
                UiKit.DestroySafe(cutinRoot.gameObject);
                NextIntro(IntroStep.Tutorial);
            }
        }

        private static readonly string[] TutorialKeys = { "tutorial.drag", "tutorial.collide", "tutorial.defend", "tutorial.timer" };

        private void ShowTutorial()
        {
            if (tutorialRoot != null) UiKit.DestroySafe(tutorialRoot.gameObject);
            tutorialRoot = UiKit.Stretch(UiKit.Rect(overlay, "Tutorial"));
            UiKit.Blocker(tutorialRoot, 0.45f);
            // 説明対象の強調（フィールド上の位置）
            Vector2 spot;
            float r;
            BarrageRuntime br = ctrl.Sim.Barrages[0];
            IntPoint pt = br.Points[Math.Min(2, br.Points.Length - 1)];
            switch (tutorialPage)
            {
                case 0: spot = new Vector2(pt.X, pt.Y); r = 50; break;
                case 2: spot = new Vector2(GameConstants.Player.X, (GameConstants.Player.Y + GameConstants.Base.Y0) * 0.5f); r = 130; break;
                default: spot = new Vector2(-1, -1); r = 0; break;
            }
            if (r > 0)
            {
                Vector3 world = field.rectTransform.TransformPoint(new Vector3(spot.x, -spot.y, 0));
                Image ring = UiKit.Image(tutorialRoot, "Spot", UiKit.SoftCircle, new Color(1f, 0.95f, 0.7f, 0.5f));
                ring.rectTransform.position = world;
                ring.rectTransform.sizeDelta = new Vector2(r * 2 * FieldScale * 1.6f, r * 2 * FieldScale * 1.6f);
            }
            Image card = UiKit.Panel(tutorialRoot, "Card");
            UiKit.Place(card.rectTransform, new Vector2(0.78f, 0.5f), Vector2.zero, new Vector2(700, 460));
            Text page = UiKit.Label(card.transform, (tutorialPage + 1) + " / " + TutorialKeys.Length, 22, UiKit.SubTextColor, TextAnchor.UpperRight);
            UiKit.Stretch(page.rectTransform, 30, 30, 24, 380);
            Text body = UiKit.Label(card.transform, T(TutorialKeys[tutorialPage]), 30, Color.white, TextAnchor.UpperLeft);
            UiKit.Stretch(body.rectTransform, 40, 40, 70, 120);
            RectTransform row = UiKit.Rect(card.transform, "Actions");
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.offsetMin = new Vector2(30, 26);
            row.offsetMax = new Vector2(-30, 96);
            UiKit.Row(row, 14);
            UiKit.Button(row, T("tutorial.skip"), CloseTutorial, ButtonVariant.Ghost, 24);
            if (tutorialPage > 0)
            {
                UiKit.Button(row, T("common.prev"), () =>
                {
                    tutorialPage--;
                    ShowTutorial();
                }, ButtonVariant.Normal, 24);
            }
            bool last = tutorialPage == TutorialKeys.Length - 1;
            UiKit.Button(row, last ? T("tutorial.go") : T("common.next"), () =>
            {
                if (last)
                {
                    CloseTutorial();
                    return;
                }
                tutorialPage++;
                ShowTutorial();
            }, ButtonVariant.Primary, 26);
        }

        private void CloseTutorial()
        {
            if (tutorialRoot == null) return;
            UiKit.DestroySafe(tutorialRoot.gameObject);
            App.Progress.SetTutorialSeen(true);
            NextIntro(IntroStep.Running);
        }

        // ------------------------------------------------------------------ HUD・イベント

        private void ShowBanner(string text)
        {
            if (banner == null) return;
            bannerText.text = text;
            banner.gameObject.SetActive(true);
            bannerUntil = Time.unscaledTime + BannerSeconds;
        }

        private void OnSimEvent(SimEvent ev)
        {
            if (ev.Type == SimEventType.EnemyHit)
            {
                Common.SetFace(enemyFace, CharacterPortrait.Damage, DamageFaceSeconds);
                Common.SetFace(bigPortrait, CharacterPortrait.Damage, DamageFaceSeconds);
                return;
            }
            if (ev.Type == SimEventType.PlayerHit || ev.Type == SimEventType.BaseHit)
            {
                Common.SetFace(playerFace, CharacterPortrait.Damage, DamageFaceSeconds);
                return;
            }
            if (ev.Type != SimEventType.Chain && ev.Type != SimEventType.Collide) return;
            int level = ev.Type == SimEventType.Chain ? ev.B + 1 : 1;
            if (level < 2 || combo == null) return;
            comboNum.text = level + " CHAIN";
            combo.gameObject.SetActive(true);
            combo.localScale = Vector3.one * 1.25f;
            comboUntil = Time.unscaledTime + ComboSeconds;
        }

        private void OnPhase(BattlePhase p)
        {
            if (p != BattlePhase.Exec) return;
            ShowBanner(T("battle.release"));
            phaseMain.text = T("battle.phaseExec");
            phaseSub.text = T("battle.phaseExecSub");
            phaseMain.color = UiKit.Pink;
        }

        private void UpdateHud()
        {
            BattleSimulation sim = ctrl.Sim;
            bool edit = sim.Phase == BattlePhase.Edit;
            int remain = edit ? sim.EditRemainingTicks : 0;
            int sec = (remain + GameConstants.Timing.TicksPerSecond - 1) / GameConstants.Timing.TicksPerSecond;
            timerNum.text = sec.ToString(System.Globalization.CultureInfo.InvariantCulture);
            timerNum.color = edit && sec <= 5 ? UiKit.Danger : Color.white;
            timerRing.fillAmount = edit ? remain / (float)Math.Max(1, sim.EditTicks) : 0f;
            timerRing.color = edit && sec <= 5 ? UiKit.Danger : new Color(0.75f, 0.6f, 1f, 0.9f);
            opsVal.text = sim.Ops.ToString();
            cancelsVal.text = sim.Cancels.ToString();
            damageVal.text = sim.DamageDealt.ToString();
            maxChainVal.text = "MAX " + sim.MaxChain;
            if (undoBtn != null) undoBtn.interactable = ctrl.CanUndo;
        }

        // ------------------------------------------------------------------ 操作

        private void DoUndo()
        {
            ctrl?.Undo();
        }

        private void TogglePreview()
        {
            App.Settings.Update(s => s.ShowPreview = !s.ShowPreview);
            UpdateToggleLabels();
        }

        private void ToggleFast()
        {
            ctrl.Fast = !ctrl.Fast;
            UpdateToggleLabels();
        }

        private bool IntroBusy => step != IntroStep.Running;

        private void TogglePause()
        {
            if (IntroBusy || endedHandled) return;
            if (pauseModal != null)
            {
                ClosePause();
                return;
            }
            ctrl.SetPaused(true);
            List<GameApp.ModalButton> buttons = new List<GameApp.ModalButton>
            {
                GameApp.Btn(T("battle.quit"), ButtonVariant.Danger, Quit),
            };
            if (ctx.Kind != BattleKind.Endless) buttons.Add(GameApp.Btn(T("common.retry"), ButtonVariant.Normal, Retry));
            buttons.Add(GameApp.Btn(T("battle.resume"), ButtonVariant.Primary, ClosePause));
            pauseModal = App.OpenModal(T("battle.paused"), body =>
            {
                RectTransform col = UiKit.Stretch(UiKit.Rect(body, "Col"));
                UiKit.Column(col, 12);
                GameSettings s = App.Settings.Current;
                PauseSlider(col, "settings.bgm", s.BgmVolume, v => App.Settings.Update(x => x.BgmVolume = v));
                PauseSlider(col, "settings.se", s.SeVolume, v => App.Settings.Update(x => x.SeVolume = v));
            }, buttons, 980, 480);
        }

        private void PauseSlider(RectTransform col, string key, int value, Action<int> set)
        {
            RectTransform row = UiKit.Rect(col, "Row");
            UiKit.Height(row, 70);
            Text l = UiKit.Label(row, T(key), 26, UiKit.TextColor, TextAnchor.MiddleLeft);
            UiKit.Stretch(l.rectTransform).anchorMax = new Vector2(0.35f, 1);
            Slider sl = UiKit.Slider(row, 0, 100, value, v => set(Mathf.RoundToInt(v)));
            RectTransform sr = UiKit.Stretch((RectTransform)sl.transform, 0, 0, 10, 10);
            sr.anchorMin = new Vector2(0.38f, 0);
        }

        private void ClosePause()
        {
            if (pauseModal != null) App.CloseModal(pauseModal);
            pauseModal = null;
            if (!Disposed && !endedHandled) ctrl.SetPaused(false);
        }

        private void Retry()
        {
            if (ctx.Kind == BattleKind.Endless) return;
            BattlePlayer p = player;
            App.Go(() => new BattleScreen(ctx, p));
        }

        private void Quit()
        {
            ctrl?.Dispose();
            switch (ctx.Kind)
            {
                case BattleKind.Endless:
                    FinishEndless(null);
                    break;
                case BattleKind.Story:
                    string id = ctx.StageId;
                    App.Go(() => new StoryScreen(id));
                    break;
                case BattleKind.Custom:
                    string cid = ctx.CreationId;
                    App.Go(() => new StageEditorScreen(cid, null));
                    break;
                case BattleKind.Shared:
                    App.Go(() => new SharedStagesScreen());
                    break;
                case BattleKind.Replay:
                    App.Go(() => new RecordsScreen());
                    break;
                default:
                    App.GoHome();
                    break;
            }
        }

        public override void OnEscape()
        {
        }

        /// <summary>焼き込み：開始演出（会話・bot計画・カットイン・チュートリアル）の UI もテンプレートにする</summary>
        public override void BakeExtras()
        {
            if (overlay == null) return;
            if (dlgRoot == null && lines != null && lines.Count > 0) StartDialogue();
            if (botRoot == null) BuildBotPanel();
            if (cutinRoot == null) StartCutin();
            if (tutorialRoot == null && ctrl.Sim.Barrages.Length > 0)
            {
                tutorialPage = 0;
                ShowTutorial();
            }
            // テンプレート上では重ならないよう隠しておく（実行時は使用時に自動で表示される）
            for (int i = 0; i < overlay.childCount; i++) overlay.GetChild(i).gameObject.SetActive(false);
        }

        public override void Relocalize()
        {
            // 戦闘中は進行を保つため再構築しない（ラベルは次の画面から反映）
        }

        public override void Tick(float dt)
        {
            if (ctrl == null) return;
            switch (step)
            {
                case IntroStep.Dialogue:
                    if (dlgRoot != null && lineShown < lineFull.Length)
                    {
                        typeAcc += dt;
                        while (typeAcc >= TypeInterval && lineShown < lineFull.Length)
                        {
                            typeAcc -= TypeInterval;
                            lineShown++;
                            if (lineShown % 3 == 1) App.Audio.PlaySe("ui_hover");
                        }
                        dlgText.text = lineFull.Substring(0, lineShown);
                    }
                    if (UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.Return)) AdvanceDialogue();
                    else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) FinishDialogue();
                    break;
                case IntroStep.BotPlan:
                    if (botIter != null && botRoot != null) TickBot();
                    if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Quit();
                    break;
                case IntroStep.Cutin:
                    if (cutinRoot != null) TickCutin(dt);
                    break;
                case IntroStep.Tutorial:
                    break;
                default:
                    HandleKeys();
                    break;
            }
            if (Disposed) return;

            // ホバー（マウス移動のみ。ドラッグ中は FieldPointer が送る）
            if (player == BattlePlayer.Human && pointer != null && !pointer.Pressed && UnityEngine.Input.mousePresent && pointer.TryToWorld(UnityEngine.Input.mousePosition, out Vector2 w))
            {
                ctrl.PointerMove(w.x, w.y, false);
            }
            ctrl.Update(dt);
            if (Disposed) return;
            if (!facesCaptured)
            {
                // シーンで調整した色・位置（UiBinder 適用後）を演出の基準にする
                facesCaptured = true;
                enemyFaceBase = enemyFace.color;
                playerFaceBase = playerFace.color;
                fieldBasePos = field.rectTransform.anchoredPosition;
            }
            float shake = ctrl.Effects.Shake;
            field.rectTransform.anchoredPosition = fieldBasePos + (shake > 0.01f ? new Vector2(UnityEngine.Random.Range(-shake, shake), UnityEngine.Random.Range(-shake, shake)) : Vector2.zero);
            enemyFace.color = Color.Lerp(enemyFaceBase, new Color(1f, 0.4f, 0.6f, 1f), ctrl.Effects.EnemyFlash);
            playerFace.color = Color.Lerp(playerFaceBase, new Color(1f, 0.3f, 0.3f, 1f), ctrl.Effects.PlayerFlash);
            ctrl.View.ShowPreview = App.Settings.Current.ShowPreview;
            field.Redraw();
            UpdateHud();
            if (banner.gameObject.activeSelf && Time.unscaledTime >= bannerUntil) banner.gameObject.SetActive(false);
            if (combo.gameObject.activeSelf)
            {
                combo.localScale = Vector3.Lerp(combo.localScale, Vector3.one, dt * 12f);
                if (Time.unscaledTime >= comboUntil) combo.gameObject.SetActive(false);
            }
        }

        private void HandleKeys()
        {
            if (endedHandled) return;
            SettingsService s = App.Settings;
            if (s.IsKeyDown(KeyAction.Pause))
            {
                TogglePause();
                return;
            }
            if (pauseModal != null || App.ModalOpen) return;
            if (s.IsKeyDown(KeyAction.FastForward)) ToggleFast();
            if (s.IsKeyDown(KeyAction.TogglePreview)) TogglePreview();
            if (s.IsKeyDown(KeyAction.Retry) && player != BattlePlayer.Replay && ctx.Kind != BattleKind.Endless) Retry();
            bool ctrlKey = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl) || UnityEngine.Input.GetKey(KeyCode.LeftCommand);
            if (ctrlKey && UnityEngine.Input.GetKeyDown(KeyCode.Z)) DoUndo();
        }

        // ------------------------------------------------------------------ 終了

        private void OnEnded(BattleEndInfo info)
        {
            if (Disposed || endedHandled) return;
            endedHandled = true;
            BattleResult result = info.Result;
            StageScore score = ScoreCalculator.ComputeStage(result);
            string playerStr = player == BattlePlayer.Replay ? ctx.Replay.Player : (player == BattlePlayer.Bot ? "bot" : "human");
            string replayStr = string.Empty;
            if (player != BattlePlayer.Replay)
            {
                try
                {
                    replayStr = ReplaySerializer.Serialize(new ReplayData
                    {
                        Seed = info.Seed,
                        Mode = ctx.ModeString,
                        Player = playerStr,
                        CreatedAt = ReplaySerializer.NowUnixMilliseconds(),
                        Stage = info.Stage,
                        Commands = info.Commands,
                        Result = ReplaySerializer.ToRecord(result),
                        Score = score.Total,
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError("[Battle] replay serialization failed: " + e);
                }
            }
            if (!string.IsNullOrEmpty(replayStr))
            {
                bool ok = App.Replays.SetLast(new ReplayEntry { Id = "last", Data = replayStr, At = IdGenerator.NowMs(), Title = Txt(stage.Name), Outcome = BattleResult.OutcomeToString(result.Outcome), Score = score.Total });
                if (!ok) Debug.LogWarning("[Battle] last replay could not be stored (size or storage)");
            }
            bool newBest = false;
            if (ctx.Kind == BattleKind.Story && player == BattlePlayer.Human) newBest = App.Progress.RecordStageResult(stage.Id, stage.EnemyId, result.Outcome, score.Total, true);
            else if (ctx.Kind == BattleKind.Story && player == BattlePlayer.Bot) App.Progress.MarkEncountered(stage.EnemyId);
            else if (ctx.Kind == BattleKind.Custom && player == BattlePlayer.Human && result.Outcome == BattleOutcome.Clear) App.MyStages.MarkCleared(ctx.CreationId, stage);
            App.Audio.PlaySe(result.Outcome == BattleOutcome.Clear ? "clear" : "lose");

            ResultParams rp = new ResultParams { Context = ctx, Player = player, Result = result, Score = score, Stage = stage, ReplayStr = replayStr, NewBest = newBest };
            if (ctx.Kind == BattleKind.Endless)
            {
                EndlessRun run = ctx.Run;
                run.History.Add((stage.Id, score.Total, result.Outcome));
                if (result.Outcome == BattleOutcome.Clear)
                {
                    run.Cleared += 1;
                    run.Sum += score.Total;
                    rp.Endless = ScoreCalculator.ComputeEndless(run.Sum, run.Cleared);
                    rp.EndlessFinal = false;
                    App.Go(() => new ResultScreen(rp));
                    return;
                }
                FinishEndless(rp);
                return;
            }
            App.Go(() => new ResultScreen(rp));
        }

        private void FinishEndless(ResultParams last)
        {
            endedHandled = true;
            EndlessRun run = ctx.Run;
            EndlessScore total = ScoreCalculator.ComputeEndless(run.Sum, run.Cleared);
            bool best = App.Progress.RecordEndless(total.Total, run.Cleared);
            ResultParams rp = last ?? new ResultParams
            {
                Context = ctx,
                Player = player,
                Result = new BattleResult { Outcome = BattleOutcome.Lose },
                Score = ScoreCalculator.ComputeStage(null),
                Stage = stage,
                ReplayStr = string.Empty,
            };
            rp.Endless = total;
            rp.EndlessFinal = true;
            rp.NewBest = best;
            App.Go(() => new ResultScreen(rp));
        }

        protected override void OnDispose()
        {
            ctrl?.Dispose();
        }
    }

    /// <summary>リザルト画面へ渡す内容</summary>
    public sealed class ResultParams
    {
        public BattleContext Context;
        public BattlePlayer Player;
        public BattleResult Result;
        public StageScore Score;
        public StageData Stage;
        public string ReplayStr;
        public bool NewBest;
        public EndlessScore? Endless;
        public bool EndlessFinal;
    }
}

/*
## このアセットの役割
戦闘画面です。フィールド描画・入力（ドラッグ・右クリック取消・Ctrl+Z）、HUD（制限時間リング・操作数・相殺数・ダメージ・連鎖）、開始演出（会話・bot計画・カットイン・初回チュートリアル）、一時停止、終了処理（リプレイ保存・進行記録・エンドレス集計）を担当します。

## 他アセットとの依存関係
BattleController / FieldGraphic / BotPlanner / ReplaySerializer / ScoreCalculator / 各サービス。終了後 ResultScreen へ遷移します。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。

## 注意点
ScoreCalculator.ComputeStage(null) は「未完了ステージ＝0点」を返す仕様です（Web版と同じ）。bot は計画完了後に同じ seed で制御を作り直し、人間と同じ入力経路でコマンドを再生します。
*/
