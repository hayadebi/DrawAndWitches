using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// <summary>エディター画面群の共通部品</summary>
    public static class EditorCommon
    {
        /// <summary>ステージ縮図（軌道線のみ）</summary>
        public static void Thumb(Transform parent, StageData stage, float width)
        {
            float scale = width / GameConstants.World.Width;
            Image bg = UiKit.Image(parent, "Thumb", UiKit.Rounded, new Color(0.04f, 0.03f, 0.12f, 1f));
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0, 0.5f);
            bg.rectTransform.pivot = new Vector2(0, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(width, GameConstants.World.Height * scale);
            bg.rectTransform.anchoredPosition = new Vector2(14, 0);
            RectTransform rt = UiKit.Rect(bg.transform, "Lines");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(GameConstants.World.Width, GameConstants.World.Height);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = new Vector3(scale, scale, 1);
            FieldGraphic g = rt.gameObject.GetOrAdd<FieldGraphic>();
            g.raycastTarget = false;
            StageData s = stage;
            g.Drawer = fg =>
            {
                for (int i = 0; i < s.Barrages.Count; i++)
                {
                    List<IntPoint> pts = s.Barrages[i].Points;
                    Color c = FieldRenderer.BarrageColor(i);
                    for (int k = 1; k < pts.Count; k++) fg.Line(pts[k - 1].X, pts[k - 1].Y, pts[k].X, pts[k].Y, 16, c, c);
                    if (pts.Count > 0) fg.Disc(pts[0].X, pts[0].Y, 24, c);
                }
            };
            g.Redraw();
        }

        /// <summary>弾種ごとの数（例: 通常弾×3 巨弾×1）</summary>
        public static string TypeSummary(GameApp app, StageData stage)
        {
            Dictionary<string, int> n = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BarrageData b in stage.Barrages)
            {
                n.TryGetValue(b.Type, out int v);
                n[b.Type] = v + b.Count;
            }
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in n)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(app.BulletTypeName(kv.Key)).Append('×').Append(kv.Value);
            }
            return sb.ToString();
        }

        public static string FormatDate(long ms)
        {
            if (ms <= 0) return "—";
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>制御文字除去・前後空白除去・コードポイント数での切り詰め（Web版 register と同じ）</summary>
        public static string CleanText(string raw, int maxCodePoints)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            StringBuilder sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if (ch <= '\u001F' || (ch >= '\u007F' && ch <= '\u009F') || ch == '\u2028' || ch == '\u2029') continue;
                sb.Append(ch);
            }
            string s = sb.ToString().Trim();
            StringBuilder outSb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < s.Length && count < maxCodePoints; i++)
            {
                outSb.Append(s[i]);
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    outSb.Append(s[i + 1]);
                    i++;
                }
                count++;
            }
            return outSb.ToString();
        }

        public static string ErrorKey(MyStageError e)
        {
            switch (e)
            {
                case MyStageError.Full: return "editor.full";
                case MyStageError.Locked: return "editor.lockedHint";
                default: return "editor.saveFailed";
            }
        }
    }

    // ========================================================================= 作成／プレイの選択
    public sealed class EditorMenuScreen : ScreenBase
    {
        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_editor", 0.55f);
            Common.Header(App, Root, T("home.editor"), App.GoHome);
            RectTransform row = UiKit.Rect(Root, "Choices");
            UiKit.Place(row, new Vector2(0.5f, 0.46f), Vector2.zero, new Vector2(1300, 520));
            UiKit.Row(row, 60);
            Tile(row, T("editor.create"), T("editor.myStages"), new Color(0.37f, 0.83f, 1f), () => App.Go(() => new MyStagesScreen()));
            Tile(row, T("editor.play"), T("editor.sharedList"), UiKit.Pink, () => App.Go(() => new SharedStagesScreen()));
        }

        private void Tile(RectTransform parent, string label, string sub, Color c, Action onClick)
        {
            Button b = UiKit.Button(parent, string.Empty, () => onClick(), ButtonVariant.Normal);
            Image band = UiKit.Image(b.transform, "Band", UiKit.Rounded, new Color(c.r, c.g, c.b, 0.25f));
            UiKit.Stretch(band.rectTransform, 8, 8, 8, 8);
            Text t = UiKit.Label(b.transform, label, 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 10, 10, 10, 80);
            UiKit.AddShadow(t, new Color(c.r, c.g, c.b, 0.9f), new Vector2(0, -4));
            Text s = UiKit.Label(b.transform, sub, 28, UiKit.SubTextColor, TextAnchor.LowerCenter);
            UiKit.Stretch(s.rectTransform, 10, 10, 10, 60);
        }
    }

    // ========================================================================= 自作ステージ一覧
    public sealed class MyStagesScreen : ScreenBase
    {
        public override void OnEscape()
        {
            App.Go(() => new EditorMenuScreen());
        }

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_editor", 0.6f);
            Common.Header(App, Root, T("editor.create"), () => App.Go(() => new EditorMenuScreen()));
            RectTransform top = UiKit.Rect(Root, "Top");
            UiKit.Place(top, new Vector2(1, 1), new Vector2(-380, -70), new Vector2(640, 70));
            UiKit.Row(top, 14);
            UiKit.Button(top, T("editor.import"), OpenImport, ButtonVariant.Ghost, 22);
            UiKit.Button(top, T("editor.new"), () => App.Go(() => new StageEditorScreen(IdGenerator.NewCreationId(), null)), ButtonVariant.Primary, 24);

            Image panel = UiKit.Panel(Root, "Panel");
            UiKit.Stretch(panel.rectTransform, 80, 80, 150, 60);
            RectTransform host = UiKit.Stretch(UiKit.Rect(panel.transform, "Host"), 20, 20, 20, 20);
            RectTransform list = UiKit.ScrollList(host, out ScrollRect _, 10);
            UiKit.Stretch((RectTransform)list.parent.parent);
            List<MyStageItem> items = App.MyStages.List();
            if (items.Count == 0)
            {
                UiKit.Height(UiKit.Label(list, T("editor.noStages"), 28, UiKit.SubTextColor), 120);
                return;
            }
            foreach (MyStageItem it in items) Row(list, it);
        }

        private void Row(RectTransform list, MyStageItem it)
        {
            MyStageItem item = it;
            bool cleared = App.MyStages.IsTestCleared(item.CreationId);
            Button row = UiKit.Button(list, string.Empty, () =>
            {
                if (item.Registered)
                {
                    App.Notice(T("editor.lockedHint"), NoticeKind.Info);
                    return;
                }
                App.Go(() => new StageEditorScreen(item.CreationId, null));
            }, ButtonVariant.Normal);
            UiKit.Height(row, 150);
            EditorCommon.Thumb(row.transform, item.Stage, 96);
            Text name = UiKit.Label(row.transform, Txt(item.Stage.Name), 32, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(name.rectTransform, 140, 520, 22, 70);
            string sub = EditorCommon.TypeSummary(App, item.Stage) + "   " + T(cleared ? "editor.testCleared" : "editor.testNotCleared")
                + (item.Registered ? "   " + T("editor.registered") : string.Empty) + "   " + EditorCommon.FormatDate(item.UpdatedAt);
            Text s = UiKit.Label(row.transform, sub, 22, cleared ? UiKit.Success : UiKit.SubTextColor, TextAnchor.LowerLeft);
            UiKit.Stretch(s.rectTransform, 140, 520, 70, 22);
            RectTransform acts = UiKit.Rect(row.transform, "Acts");
            acts.anchorMin = new Vector2(1, 0);
            acts.anchorMax = new Vector2(1, 1);
            acts.pivot = new Vector2(1, 0.5f);
            acts.sizeDelta = new Vector2(500, -40);
            acts.anchoredPosition = new Vector2(-20, 0);
            UiKit.Row(acts, 10);
            UiKit.Button(acts, T("editor.duplicate"), () =>
            {
                string id = App.MyStages.Duplicate(item.CreationId);
                App.Notice(T(id != null ? "editor.duplicated" : "editor.saveFailed"), id != null ? NoticeKind.Success : NoticeKind.Error);
                Refresh();
            }, ButtonVariant.Normal, 20);
            UiKit.Button(acts, T("editor.export"), () => Export(item), ButtonVariant.Normal, 20);
            UiKit.Button(acts, T("common.delete"), () => App.Confirm(T("editor.deleteConfirm"), T("common.delete"), () =>
            {
                if (!App.MyStages.Remove(item.CreationId)) App.Notice(T("notice.saveFailed"), NoticeKind.Error);
                Refresh();
            }), ButtonVariant.Danger, 20);
        }

        private void Export(MyStageItem item)
        {
            string baseName = item.Stage.Name.Resolve("en");
            if (string.IsNullOrEmpty(baseName)) baseName = item.CreationId;
            if (MyStageService.ExportStageFile(item.Stage, baseName, out string path, out string _))
            {
                App.ShowText(T("editor.export"), T("editor.exportedTo") + "\n" + path);
            }
            else
            {
                App.Notice(T("editor.exportFailed"), NoticeKind.Error);
            }
        }

        /// <summary>
        /// 読込元の選択。ブラウザのファイル選択に相当する UI として、ステージフォルダ内のファイル一覧と
        /// JSON テキストの貼り付けを提供する（追加ライブラリ不要で全プラットフォーム共通）。
        /// </summary>
        private void OpenImport()
        {
            List<string> files = MyStageService.ListStageFiles();
            GameObject modal = null;
            modal = App.OpenModal(T("editor.import"), body =>
            {
                Text hint = UiKit.Label(body, T("editor.importFolderHint") + "\n" + MyStageService.StageFolder, 20, UiKit.SubTextColor, TextAnchor.UpperLeft);
                UiKit.Stretch(hint.rectTransform, 10, 10, 0, 0).anchorMin = new Vector2(0, 1);
                hint.rectTransform.offsetMin = new Vector2(10, -80);
                RectTransform host = UiKit.Stretch(UiKit.Rect(body, "Host"), 0, 0, 90, 0);
                RectTransform list = UiKit.ScrollList(host, out ScrollRect _, 8);
                UiKit.Stretch((RectTransform)list.parent.parent);
                if (files.Count == 0) UiKit.Height(UiKit.Label(list, T("editor.noFiles"), 24, UiKit.SubTextColor), 70);
                foreach (string f in files)
                {
                    string path = f;
                    Button b = UiKit.Button(list, Path.GetFileName(path), () =>
                    {
                        App.CloseModal(modal);
                        Import(App.MyStages.ImportStageFile(path));
                    }, ButtonVariant.Normal, 22);
                    UiKit.Height(b, 64);
                }
            }, new[]
            {
                GameApp.Btn(T("common.cancel"), ButtonVariant.Ghost, null),
                GameApp.Btn(T("editor.openFolder"), ButtonVariant.Normal, OpenFolder),
                GameApp.Btn(T("editor.pasteJson"), ButtonVariant.Normal, () => App.PromptText(T("editor.pasteJson"), T("editor.pasteJsonHint"), text =>
                {
                    Import(App.MyStages.ImportStageText(text));
                    return true;
                })),
            }, 1100, 720);
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(MyStageService.StageFolder);
                Application.OpenURL(new Uri(MyStageService.StageFolder).AbsoluteUri);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyStages] cannot open folder: " + e.Message);
                App.ShowText(T("editor.openFolder"), MyStageService.StageFolder);
            }
        }

        private void Import(StageValidationResult res)
        {
            if (!res.Ok)
            {
                Debug.LogWarning("[Editor] import failed: " + res.ErrorSummary);
                App.Notice(T("editor.importFailed"), NoticeKind.Error, 5f);
                return;
            }
            string id = IdGenerator.NewCreationId();
            StageData st = res.Stage.Clone();
            st.Id = id;
            MyStageError e = App.MyStages.Upsert(id, st, out string detail);
            if (e != MyStageError.None)
            {
                if (!string.IsNullOrEmpty(detail)) Debug.LogWarning("[Editor] import rejected: " + detail);
                App.Notice(T(e == MyStageError.Full ? "editor.full" : "editor.saveFailed"), NoticeKind.Error);
                return;
            }
            App.Notice(T("editor.imported"), NoticeKind.Success);
            Refresh();
        }
    }

    // ========================================================================= エディター本体
    public sealed class StageEditorScreen : ScreenBase
    {
        private enum PointerMode
        {
            None,
            Empty,
            Origin,
            Point,
            Stroke,
        }

        private readonly string creationId;
        private StageData draft;
        private int selected = -1;
        private bool dirty;
        private BattleSimulation sim;
        private FieldGraphic field;
        private FieldPointer pointer;
        private RectTransform tools;
        private readonly FieldViewState view = new FieldViewState { ShowPreview = true };

        private PointerMode mode;
        private int pBarrage;
        private int pPoint;
        private Vector2 pStart;
        private float pTime;
        private bool pMoved;
        private List<IntPoint> pOrig;
        private readonly List<double> strokeX = new List<double>();
        private readonly List<double> strokeY = new List<double>();
        private bool redirectPending;

        /// <param name="creationId">作成 ID</param>
        /// <param name="initialDraft">新規作成時の下書き（null なら既存または空の下書き）</param>
        public StageEditorScreen(string creationId, StageData initialDraft)
        {
            if (!IdGenerator.CreationIdPattern.IsMatch(creationId ?? string.Empty)) throw new ArgumentException("invalid creation id", nameof(creationId));
            this.creationId = creationId;
            draft = initialDraft;
        }

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            MyStageItem existing = App.MyStages.Get(creationId);
            if (existing != null && existing.Registered)
            {
                App.Notice(T("editor.lockedHint"), NoticeKind.Info);
                redirectPending = true; // Build 中の画面遷移を避け、次フレームで戻る
                return;
            }
            if (draft == null)
            {
                draft = existing != null ? existing.Stage.Clone() : NewDraft();
            }
            draft.Id = creationId;
            CharacterProfile doll = App.Db.EditorEnemy;
            Common.Background(App, Root, doll.BackgroundKey, 0.5f);

            RectTransform frame = UiKit.Rect(Root, "FieldFrame");
            float k = BattleScreen.FieldScale;
            UiKit.Place(frame, new Vector2(0.43f, 0.5f), Vector2.zero, new Vector2(GameConstants.World.Width * k, GameConstants.World.Height * k));
            Image fbg = UiKit.Image(frame, "FieldBg", UiKit.Rounded, new Color(0.04f, 0.02f, 0.12f, 0.8f));
            UiKit.Stretch(fbg.rectTransform, -8, -8, -8, -8);
            Image face = UiKit.Image(frame, "Doll", App.Assets.Sprite(doll.PortraitKey), new Color(1, 1, 1, 0.85f));
            Common.AttachFace(App, face, doll);
            face.preserveAspect = true;
            UiKit.Place(face.rectTransform, new Vector2(0, 1), new Vector2(GameConstants.Enemy.X * k, -GameConstants.Enemy.Y * k + 10), new Vector2(240, 240));
            RectTransform frt = UiKit.Rect(frame, "Field");
            frt.anchorMin = frt.anchorMax = new Vector2(0, 1);
            frt.pivot = new Vector2(0, 1);
            frt.sizeDelta = new Vector2(GameConstants.World.Width, GameConstants.World.Height);
            frt.anchoredPosition = Vector2.zero;
            frt.localScale = new Vector3(k, k, 1);
            field = frt.gameObject.GetOrAdd<FieldGraphic>();
            field.Drawer = Draw;
            pointer = frt.gameObject.GetOrAdd<FieldPointer>();
            pointer.Down = (w, touch) => PointerDown(w);
            pointer.Drag = (w, touch) => PointerMove(w);
            pointer.Up = PointerUp;

            tools = UiKit.Rect(Root, "Tools");
            tools.anchorMin = tools.anchorMax = new Vector2(1, 0.5f);
            tools.pivot = new Vector2(1, 0.5f);
            tools.anchoredPosition = new Vector2(-40, 0);
            tools.sizeDelta = new Vector2(600, 1010);
            RebuildSim();
            BuildTools();
        }

        private StageData NewDraft()
        {
            return new StageData
            {
                FormatVersion = GameConstants.StageFormatVersion,
                Id = creationId,
                Name = new LocalizedText(T("editor.defaultName", "n", App.MyStages.List().Count + 1)),
                Summary = LocalizedText.Empty,
                EnemyId = GameConstants.Editor.EnemyId,
                EditTimeSec = GameConstants.Editor.DefaultEditSec,
                DragRadius = GameConstants.Editor.DefaultDragRadius,
                Barrages = new List<BarrageData>(),
            };
        }

        private void RebuildSim()
        {
            sim = null;
            if (draft.Barrages.Count == 0) return;
            StageValidationResult v = StageValidator.Validate(draft.ToJson(), new[] { GameConstants.Editor.EnemyId });
            if (!v.Ok)
            {
                Debug.LogWarning("[Editor] draft invalid: " + v.ErrorSummary);
                return;
            }
            try
            {
                sim = new BattleSimulation(v.Stage, 1, new BattleOptions { RecordEvents = false });
            }
            catch (Exception e)
            {
                Debug.LogError("[Editor] preview sim failed: " + e);
            }
        }

        private void Changed()
        {
            dirty = true;
            RebuildSim();
            BuildTools();
        }

        // ------------------------------------------------------------------ 右パネル

        private Text SliderRow(RectTransform col, string label, int min, int max, int step, int value, Func<int, string> fmt, Action<int> set)
        {
            RectTransform row = UiKit.Rect(col, "Row");
            UiKit.Height(row, 60);
            Text l = UiKit.Label(row, label, 22, UiKit.SubTextColor, TextAnchor.MiddleLeft);
            UiKit.Stretch(l.rectTransform).anchorMax = new Vector2(0.3f, 1);
            Text v = UiKit.Label(row, fmt(value), 24, UiKit.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Stretch(v.rectTransform).anchorMin = new Vector2(0.8f, 0);
            Slider sl = UiKit.Slider(row, min, max, value, f =>
            {
                int iv = Mathf.Clamp(min + Mathf.RoundToInt((f - min) / step) * step, min, max);
                v.text = fmt(iv);
                set(iv);
                dirty = true;
                RebuildSim();
            });
            RectTransform sr = UiKit.Stretch((RectTransform)sl.transform, 0, 0, 12, 12);
            sr.anchorMin = new Vector2(0.3f, 0);
            sr.anchorMax = new Vector2(0.78f, 1);
            return v;
        }

        private static string Sec2(int ticks)
        {
            return (ticks / 60.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        private void BuildTools()
        {
            for (int i = tools.childCount - 1; i >= 0; i--)
            {
                Transform c = tools.GetChild(i);
                c.SetParent(null, false);
                UiKit.DestroySafe(c.gameObject);
            }
            VerticalLayoutGroup vl = tools.GetComponent<VerticalLayoutGroup>() ?? UiKit.Column(tools, 12);
            vl.childControlHeight = true;
            StageData d = draft;
            int pts = d.TotalPoints;
            bool canTest = d.Barrages.Count >= 1 && pts >= GameConstants.StageLimits.MinTotalPointsForTest;
            bool cleared = App.MyStages.IsTestCleared(creationId) && !dirty;
            MyStageItem existing = App.MyStages.Get(creationId);
            bool registered = existing != null && existing.Registered;

            Image head = UiKit.Panel(tools, "Head");
            UiKit.Height(head, 100);
            Text sub = UiKit.Label(head.transform, T("editor.title"), 20, UiKit.SubTextColor, TextAnchor.UpperLeft);
            UiKit.Stretch(sub.rectTransform, 26, 140, 14, 50);
            Text nm = UiKit.Label(head.transform, Txt(d.Name), 32, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Stretch(nm.rectTransform, 26, 140, 40, 14);
            Button back = UiKit.Button(head.transform, T("common.back"), Leave, ButtonVariant.Ghost, 22);
            UiKit.Place((RectTransform)back.transform, new Vector2(1, 0.5f), new Vector2(-75, 0), new Vector2(130, 64));

            Image props = UiKit.Panel(tools, "Props");
            UiKit.Height(props, 150);
            RectTransform pc = UiKit.Stretch(UiKit.Rect(props.transform, "Col"), 22, 22, 12, 12);
            UiKit.Column(pc, 4);
            SliderRow(pc, T("settings.editTime"), GameConstants.StageLimits.MinEditSec, GameConstants.StageLimits.MaxEditSec, 1, d.EditTimeSec, v => v + "s", v => d.EditTimeSec = v);
            SliderRow(pc, T("settings.dragRadius"), 60, 240, 5, d.DragRadius, v => v + "px", v => d.DragRadius = v);

            Image box = UiKit.Panel(tools, "Barrage");
            UiKit.Height(box, 360);
            RectTransform bc = UiKit.Stretch(UiKit.Rect(box.transform, "Col"), 22, 22, 16, 16);
            UiKit.Column(bc, 6);
            BarrageData b = selected >= 0 && selected < d.Barrages.Count ? d.Barrages[selected] : null;
            if (b != null)
            {
                Color col = FieldRenderer.BarrageColor(selected);
                RectTransform title = UiKit.Rect(bc, "Title");
                UiKit.Height(title, 56);
                Text tt = UiKit.Label(title, "● #" + (selected + 1), 30, col, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Stretch(tt.rectTransform);
                Button del = UiKit.Button(title, T("common.delete"), DeleteSelected, ButtonVariant.Danger, 20);
                UiKit.Place((RectTransform)del.transform, new Vector2(1, 0.5f), new Vector2(-65, 0), new Vector2(130, 52));
                RectTransform seg = UiKit.Rect(bc, "Types");
                UiKit.Height(seg, 60);
                UiKit.Row(seg, 8);
                foreach (BulletTypeDef def in BulletTypeDef.All)
                {
                    string id = def.Id;
                    UiKit.Button(seg, App.BulletTypeName(id), () =>
                    {
                        b.Type = id;
                        b.IntervalTicks = Math.Max(b.IntervalTicks, StageValidator.MinIntervalTicksFor(id));
                        App.Audio.PlaySe("ui_click");
                        Changed();
                    }, b.Type == id ? ButtonVariant.Primary : ButtonVariant.Ghost, 20);
                }
                int minInt = StageValidator.MinIntervalTicksFor(b.Type);
                SliderRow(bc, T("editor.count"), GameConstants.StageLimits.MinCount, GameConstants.StageLimits.MaxCount, 1, b.Count, v => "×" + v, v => b.Count = v);
                SliderRow(bc, T("editor.interval"), minInt, 120, 1, Math.Max(minInt, b.IntervalTicks), Sec2, v => b.IntervalTicks = v);
                SliderRow(bc, T("editor.delay"), 0, 600, 5, b.StartDelayTicks, v => "+" + (v / 60.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s", v => b.StartDelayTicks = v);
            }
            else
            {
                UiKit.Height(UiKit.Label(bc, "＋ " + T("editor.hintPlace"), 22, UiKit.TextColor, TextAnchor.UpperLeft), 90);
                UiKit.Height(UiKit.Label(bc, "✎ " + T("editor.hintDraw"), 22, UiKit.TextColor, TextAnchor.UpperLeft), 90);
                UiKit.Height(UiKit.Label(bc, "☝ " + T("editor.hintSelect"), 22, UiKit.TextColor, TextAnchor.UpperLeft), 90);
            }

            Image reqs = UiKit.Panel(tools, "Reqs");
            UiKit.Height(reqs, 150);
            RectTransform rc = UiKit.Stretch(UiKit.Rect(reqs.transform, "Col"), 26, 26, 12, 12);
            UiKit.Column(rc, 2);
            Req(rc, d.Barrages.Count >= 1, T("editor.reqBarrage") + "  " + d.Barrages.Count + " / 1+");
            Req(rc, pts >= GameConstants.StageLimits.MinTotalPointsForTest, T("editor.reqPoints") + "  " + pts + " / " + GameConstants.StageLimits.MinTotalPointsForTest + "+");
            Req(rc, cleared, T(cleared ? "editor.testCleared" : "editor.testNotCleared"));

            RectTransform ctl = UiKit.Rect(tools, "Controls");
            UiKit.Height(ctl, 84);
            UiKit.Row(ctl, 10);
            UiKit.Button(ctl, T("common.save"), () => Save(true), ButtonVariant.Normal, 24);
            Button test = UiKit.Button(ctl, T("editor.testPlay"), TestPlay, ButtonVariant.Primary, 24);
            test.interactable = canTest;
            Button reg = UiKit.Button(ctl, T("editor.register"), Register, ButtonVariant.Gold, 24);
            reg.interactable = cleared && !registered;
        }

        private static void Req(RectTransform col, bool ok, string text)
        {
            UiKit.Height(UiKit.Label(col, (ok ? "✓  " : "✕  ") + text, 22, ok ? UiKit.Success : UiKit.Danger, TextAnchor.MiddleLeft), 40);
        }

        private void DeleteSelected()
        {
            if (selected < 0 || selected >= draft.Barrages.Count) return;
            draft.Barrages.RemoveAt(selected);
            selected = -1;
            App.Audio.PlaySe("ui_back");
            Changed();
        }

        private bool Save(bool notify)
        {
            if (draft.Barrages.Count == 0)
            {
                if (notify) App.Notice(T("editor.needBarrage"), NoticeKind.Warn);
                return false;
            }
            MyStageError e = App.MyStages.Upsert(creationId, draft, out string detail);
            if (e != MyStageError.None)
            {
                App.Notice(T(EditorCommon.ErrorKey(e)), NoticeKind.Error);
                if (!string.IsNullOrEmpty(detail)) Debug.LogWarning("[Editor] save rejected: " + detail);
                return false;
            }
            dirty = false;
            if (notify) App.Notice(T("editor.saved"), NoticeKind.Success);
            BuildTools();
            return true;
        }

        private void TestPlay()
        {
            if (!Save(false)) return;
            MyStageItem it = App.MyStages.Get(creationId);
            if (it == null)
            {
                App.Notice(T("editor.saveFailed"), NoticeKind.Error);
                return;
            }
            string id = creationId;
            StageData st = it.Stage.Clone();
            App.Go(() => new BattleScreen(new BattleContext { Kind = BattleKind.Custom, CreationId = id, Stage = st }, BattlePlayer.Human));
        }

        private void Leave()
        {
            if (dirty && draft.Barrages.Count > 0)
            {
                App.OpenModal(T("editor.unsavedTitle"), body =>
                {
                    Text t = UiKit.Label(body, T("editor.unsaved"), 28, UiKit.TextColor);
                    UiKit.Stretch(t.rectTransform);
                }, new[]
                {
                    GameApp.Btn(T("editor.discard"), ButtonVariant.Ghost, () => App.Go(() => new MyStagesScreen())),
                    GameApp.Btn(T("common.save"), ButtonVariant.Primary, () =>
                    {
                        if (Save(false)) App.Go(() => new MyStagesScreen());
                    }),
                }, 820, 360);
                return;
            }
            App.Go(() => new MyStagesScreen());
        }

        public override void OnEscape()
        {
            Leave();
        }

        private void Register()
        {
            if (App.Remote == null || !App.Remote.IsConfigured)
            {
                App.Notice(T("online.unavailable"), NoticeKind.Warn, 6f);
                return;
            }
            if (!App.MyStages.IsTestCleared(creationId) || dirty)
            {
                App.Notice(T("editor.needClear"), NoticeKind.Warn);
                return;
            }
            InputField nameIn = null;
            InputField sumIn = null;
            bool sending = false;
            GameObject modal = null;
            modal = App.OpenModal(T("editor.register"), body =>
            {
                RectTransform col = UiKit.Stretch(UiKit.Rect(body, "Col"));
                UiKit.Column(col, 10);
                UiKit.Height(UiKit.Label(col, T("editor.stageName"), 22, UiKit.SubTextColor, TextAnchor.MiddleLeft), 36);
                nameIn = UiKit.Input(col, Txt(draft.Name), string.Empty, GameConstants.StageLimits.NameMaxChars, null);
                UiKit.Height(nameIn, 70);
                UiKit.Height(UiKit.Label(col, T("editor.stageSummary"), 22, UiKit.SubTextColor, TextAnchor.MiddleLeft), 36);
                sumIn = UiKit.Input(col, Txt(draft.Summary), string.Empty, GameConstants.StageLimits.SummaryMaxChars, null);
                UiKit.Height(sumIn, 70);
                UiKit.Height(UiKit.Label(col, T("editor.registerOnce"), 20, UiKit.SubTextColor, TextAnchor.UpperLeft), 70);
            }, new[]
            {
                GameApp.Btn(T("common.cancel"), ButtonVariant.Ghost, null),
                new GameApp.ModalButton
                {
                    Label = T("editor.register"),
                    Variant = ButtonVariant.Gold,
                    OnClick = () =>
                    {
                        if (sending) return false;
                        string name = EditorCommon.CleanText(nameIn != null ? nameIn.text : string.Empty, GameConstants.StageLimits.NameMaxChars);
                        string summary = EditorCommon.CleanText(sumIn != null ? sumIn.text : string.Empty, GameConstants.StageLimits.SummaryMaxChars);
                        if (name.Length == 0)
                        {
                            App.Notice(T("editor.needName"), NoticeKind.Warn);
                            return false;
                        }
                        sending = true;
                        SubmitRegister(name, summary, modal, () => sending = false);
                        return false; // 送信完了時に閉じる
                    },
                },
            }, 1000, 640);
        }

        private async void SubmitRegister(string name, string summary, GameObject modal, Action done)
        {
            try
            {
                MyStageItem it = App.MyStages.Get(creationId);
                if (it == null)
                {
                    App.Notice(T("editor.saveFailed"), NoticeKind.Error);
                    return;
                }
                StageData st = it.Stage.Clone();
                st.Name = new LocalizedText(name);
                st.Summary = new LocalizedText(summary);
                // 名称・概要の変更はクリア判定ハッシュに影響しない
                MyStageError e = App.MyStages.Upsert(creationId, st, out string detail);
                if (e != MyStageError.None)
                {
                    if (!string.IsNullOrEmpty(detail)) Debug.LogWarning("[Editor] register save rejected: " + detail);
                    App.Notice(T("editor.saveFailed"), NoticeKind.Error);
                    return;
                }
                draft = st.Clone();
                OnlineSubmitResult res = await App.SharedStages.RegisterAsync(creationId, st, App.PlayerName());
                if (Disposed) return;
                if (!res.Ok)
                {
                    App.Notice(App.OnlineErrorText(res.Error), NoticeKind.Error);
                    return;
                }
                if (!App.MyStages.MarkRegistered(creationId)) Debug.LogWarning("[Editor] registered flag could not be saved");
                if (modal != null) App.CloseModal(modal);
                App.Notice(T("editor.registerOk"), NoticeKind.Success);
                App.Go(() => new MyStagesScreen());
            }
            catch (Exception ex)
            {
                Debug.LogError("[Editor] register failed: " + ex);
                if (!Disposed) App.Notice(App.OnlineErrorText("error"), NoticeKind.Error);
            }
            finally
            {
                done();
            }
        }

        // ------------------------------------------------------------------ フィールド操作

        private int PickOrigin(Vector2 w)
        {
            int best = -1;
            float bd = GameConstants.Editor.HandlePickRadius * GameConstants.Editor.HandlePickRadius;
            for (int i = 0; i < draft.Barrages.Count; i++)
            {
                IntPoint p = draft.Barrages[i].Points[0];
                float d = (p.X - w.x) * (p.X - w.x) + (p.Y - w.y) * (p.Y - w.y);
                if (d <= bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        private int PickPoint(Vector2 w)
        {
            if (selected < 0 || selected >= draft.Barrages.Count) return -1;
            List<IntPoint> pts = draft.Barrages[selected].Points;
            int best = -1;
            float bd = GameConstants.Editor.HandlePickRadius * GameConstants.Editor.HandlePickRadius;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = (pts[i].X - w.x) * (pts[i].X - w.x) + (pts[i].Y - w.y) * (pts[i].Y - w.y);
                if (d <= bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        private static IntPoint ClampField(double x, double y)
        {
            return new IntPoint(
                Fnv1a.Clamp(Fnv1a.RoundHalfUp(x), StageValidator.FieldMinX, StageValidator.FieldMaxX),
                Fnv1a.Clamp(Fnv1a.RoundHalfUp(y), StageValidator.FieldMinY, StageValidator.FieldMaxY));
        }

        private void PointerDown(Vector2 w)
        {
            int oi = PickOrigin(w);
            int pi = PickPoint(w);
            pStart = w;
            pTime = Time.unscaledTime;
            pMoved = false;
            if (pi > 0)
            {
                mode = PointerMode.Point;
                pBarrage = selected;
                pPoint = pi;
            }
            else if (oi >= 0)
            {
                selected = oi;
                mode = PointerMode.Origin;
                pBarrage = oi;
                pOrig = new List<IntPoint>(draft.Barrages[oi].Points);
                BuildTools();
            }
            else
            {
                mode = PointerMode.Empty;
            }
        }

        private void PointerMove(Vector2 w)
        {
            BarrageData b = pBarrage >= 0 && pBarrage < draft.Barrages.Count ? draft.Barrages[pBarrage] : null;
            switch (mode)
            {
                case PointerMode.Point:
                    if (b == null) return;
                    IntPoint np = ClampField(w.x, w.y);
                    if ((pPoint - 1 >= 0 && b.Points[pPoint - 1].Equals(np)) || (pPoint + 1 < b.Points.Count && b.Points[pPoint + 1].Equals(np))) return;
                    b.Points[pPoint] = np;
                    RebuildSim();
                    break;
                case PointerMode.Origin:
                    if (b == null) return;
                    float held = Time.unscaledTime - pTime;
                    float dist = Vector2.Distance(w, pStart);
                    if (!pMoved && held >= GameConstants.Editor.LongPressSeconds)
                    {
                        // 長押し後のドラッグ：線を描いて起点を自動配置
                        mode = PointerMode.Stroke;
                        strokeX.Clear();
                        strokeY.Clear();
                        strokeX.Add(b.Points[0].X);
                        strokeY.Add(b.Points[0].Y);
                        strokeX.Add(w.x);
                        strokeY.Add(w.y);
                        return;
                    }
                    if (dist > 6f)
                    {
                        pMoved = true;
                        // 短押しドラッグ：弾幕全体を平行移動（発射位置の上限を守る）
                        int dx = Mathf.RoundToInt(w.x - pStart.x);
                        int dy = Mathf.RoundToInt(w.y - pStart.y);
                        bool ok = true;
                        for (int k = 0; k < pOrig.Count && ok; k++)
                        {
                            int x = pOrig[k].X + dx;
                            int y = pOrig[k].Y + dy;
                            ok = x >= StageValidator.FieldMinX && x <= StageValidator.FieldMaxX && y >= StageValidator.FieldMinY && y <= StageValidator.FieldMaxY
                                && (k > 0 || y <= GameConstants.Editor.MaxSpawnY);
                        }
                        if (ok)
                        {
                            for (int k = 0; k < pOrig.Count; k++) b.Points[k] = new IntPoint(pOrig[k].X + dx, pOrig[k].Y + dy);
                            RebuildSim();
                        }
                    }
                    break;
                case PointerMode.Stroke:
                    int last = strokeX.Count - 1;
                    double ddx = w.x - strokeX[last];
                    double ddy = w.y - strokeY[last];
                    if (ddx * ddx + ddy * ddy >= 16 && strokeX.Count < GameConstants.Editor.MaxStrokeSamples)
                    {
                        IntPoint q = ClampField(w.x, w.y);
                        strokeX.Add(q.X);
                        strokeY.Add(q.Y);
                    }
                    break;
            }
        }

        private void PointerUp()
        {
            PointerMode m = mode;
            mode = PointerMode.None;
            switch (m)
            {
                case PointerMode.Empty:
                    PlaceBarrage();
                    break;
                case PointerMode.Stroke:
                    double len = 0;
                    for (int i = 1; i < strokeX.Count; i++) len += Math.Sqrt((strokeX[i] - strokeX[i - 1]) * (strokeX[i] - strokeX[i - 1]) + (strokeY[i] - strokeY[i - 1]) * (strokeY[i] - strokeY[i - 1]));
                    List<IntPoint> pts = TrajectoryPath.ResampleStroke(strokeX, strokeY, GameConstants.Editor.ControlPointSpacing, GameConstants.StageLimits.MaxPoints);
                    strokeX.Clear();
                    strokeY.Clear();
                    if (pts.Count < 2 || len < GameConstants.Editor.MinStrokeLength)
                    {
                        App.Notice(T("editor.strokeShort"), NoticeKind.Warn, 1.8f);
                        return;
                    }
                    if (pBarrage >= 0 && pBarrage < draft.Barrages.Count)
                    {
                        List<IntPoint> clamped = new List<IntPoint>(pts.Count);
                        foreach (IntPoint q in pts) clamped.Add(ClampField(q.X, q.Y));
                        draft.Barrages[pBarrage].Points = clamped;
                    }
                    App.Audio.PlaySe("edit_end");
                    Changed();
                    break;
                case PointerMode.Point:
                    App.Audio.PlaySe("release");
                    Changed();
                    break;
                case PointerMode.Origin:
                    if (pMoved)
                    {
                        App.Audio.PlaySe("release");
                        Changed();
                    }
                    break;
            }
        }

        private void PlaceBarrage()
        {
            IntPoint s = ClampField(pStart.x, pStart.y);
            if (s.Y > GameConstants.Editor.MaxSpawnY)
            {
                App.Notice(T("editor.tooLow"), NoticeKind.Warn, 1.8f);
                return;
            }
            if (draft.Barrages.Count >= GameConstants.StageLimits.MaxBarrages)
            {
                App.Notice(T("editor.maxBarrages"), NoticeKind.Warn);
                return;
            }
            int total = 0;
            foreach (BarrageData b in draft.Barrages) total += b.Count;
            if (total + GameConstants.Editor.DefaultCount > GameConstants.StageLimits.MaxTotalBullets)
            {
                App.Notice(T("editor.maxBullets"), NoticeKind.Warn);
                return;
            }
            // 既定の軌道：下方向へ一定間隔の起点
            List<IntPoint> pts = new List<IntPoint> { s };
            for (int k = 1; k <= 3; k++)
            {
                IntPoint q = new IntPoint(s.X, Math.Min(StageValidator.FieldMaxY, s.Y + k * GameConstants.Editor.ControlPointSpacing));
                if (q.Y != pts[pts.Count - 1].Y) pts.Add(q);
            }
            if (pts.Count < 2) pts.Add(new IntPoint(Math.Min(StageValidator.FieldMaxX, s.X + GameConstants.Editor.ControlPointSpacing), s.Y));
            draft.Barrages.Add(new BarrageData
            {
                Type = BulletTypeDef.Normal.Id,
                Count = GameConstants.Editor.DefaultCount,
                IntervalTicks = GameConstants.Editor.DefaultIntervalTicks,
                StartDelayTicks = 0,
                Points = pts,
            });
            selected = draft.Barrages.Count - 1;
            App.Audio.PlaySe("release");
            Changed();
        }

        // ------------------------------------------------------------------ 描画

        private void Draw(FieldGraphic g)
        {
            view.Time = Time.unscaledTime;
            view.PreviewTick = Time.unscaledTime * GameConstants.Timing.TicksPerSecond;
            view.ShowPreview = true;
            if (sim != null) FieldRenderer.Draw(g, sim, view, null);
            else FieldRenderer.DrawArena(g, view.Time, 0f, 0f);

            // 配置禁止域（自キャラクターに近すぎる範囲）
            g.RectFill(0, GameConstants.Editor.MaxSpawnY, GameConstants.World.Width, GameConstants.World.Height, new Color(1f, 0.3f, 0.45f, 0.10f), new Color(1f, 0.3f, 0.45f, 0.02f));
            g.Line(0, GameConstants.Editor.MaxSpawnY, GameConstants.World.Width, GameConstants.Editor.MaxSpawnY, 2, new Color(1f, 0.4f, 0.55f, 0.6f), new Color(1f, 0.4f, 0.55f, 0.6f));

            // 全弾幕の起点（sim が無い場合も見えるように）
            for (int i = 0; i < draft.Barrages.Count; i++)
            {
                IntPoint o = draft.Barrages[i].Points[0];
                Color c = FieldRenderer.BarrageColor(i);
                g.Ring(o.X, o.Y, 16, 3, i == selected ? Color.white : c);
            }
            if (selected >= 0 && selected < draft.Barrages.Count)
            {
                List<IntPoint> pts = draft.Barrages[selected].Points;
                g.Glow(pts[0].X, pts[0].Y, 40, new Color(1f, 1f, 1f, 0.35f));
                for (int k = 1; k < pts.Count; k++)
                {
                    g.Disc(pts[k].X, pts[k].Y, 8, Color.white);
                    g.Ring(pts[k].X, pts[k].Y, 12, 2, new Color(0, 0, 0, 0.5f));
                }
            }
            if (mode == PointerMode.Stroke && strokeX.Count > 1)
            {
                g.Polyline(strokeX.ToArray(), strokeY.ToArray(), strokeX.Count, 5, new Color(1f, 0.95f, 0.7f, 0.9f));
            }
            if (mode == PointerMode.Origin && !pMoved && pBarrage >= 0 && pBarrage < draft.Barrages.Count)
            {
                float prog = Mathf.Clamp01((Time.unscaledTime - pTime) / GameConstants.Editor.LongPressSeconds);
                IntPoint o = draft.Barrages[pBarrage].Points[0];
                g.Ring(o.X, o.Y, 26 + 10 * prog, 3, new Color(1f, 0.85f, 0.4f, prog));
            }
        }

        public override void Tick(float dt)
        {
            if (redirectPending)
            {
                redirectPending = false;
                App.Go(() => new MyStagesScreen());
                return;
            }
            if (field == null) return;
            // 長押し判定は移動が無くても進める（進捗リング表示のため）
            field.Redraw();
            if (App.ModalOpen) return;
            if ((UnityEngine.Input.GetKeyDown(KeyCode.Delete) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) && selected >= 0)
            {
                DeleteSelected();
            }
        }

        public override void Relocalize()
        {
            if (tools != null) BuildTools();
        }
    }

    // ========================================================================= 共有ステージ一覧
    public sealed class SharedStagesScreen : ScreenBase
    {
        private RectTransform list;
        private Text status;
        private bool loading;

        public override void OnEscape()
        {
            App.Go(() => new EditorMenuScreen());
        }

        public override void Build()
        {
            App.Audio.PlayBgm("bgm_menu");
            Common.Background(App, Root, "bg_editor", 0.6f);
            Common.Header(App, Root, T("editor.play"), () => App.Go(() => new EditorMenuScreen()));
            list = Column(0f, 0.55f, T("editor.sharedList"), out status);
            RectTransform mine = Column(0.55f, 1f, T("editor.myStages"), out Text _);
            List<MyStageItem> items = App.MyStages.List();
            if (items.Count == 0) UiKit.Height(UiKit.Label(mine, T("editor.noStages"), 24, UiKit.SubTextColor), 90);
            foreach (MyStageItem it in items)
            {
                MyStageItem item = it;
                Row(mine, item.Stage, EditorCommon.FormatDate(item.UpdatedAt), () => ConfirmPlay(item.Stage, App.PlayerName(), item.CreationId));
            }
            UiKit.Height(UiKit.Label(list, T("online.loading"), 24, UiKit.SubTextColor), 90);
            Load();
        }

        private RectTransform Column(float x0, float x1, string title, out Text statusText)
        {
            Image p = UiKit.Panel(Root, "Col");
            p.rectTransform.anchorMin = new Vector2(x0, 0);
            p.rectTransform.anchorMax = new Vector2(x1, 1);
            p.rectTransform.offsetMin = new Vector2(x0 == 0 ? 60 : 16, 60);
            p.rectTransform.offsetMax = new Vector2(x1 >= 1 ? -60 : -16, -150);
            Text t = UiKit.Label(p.transform, title, 30, UiKit.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 30, 30, 22, 0).anchorMin = new Vector2(0, 1);
            t.rectTransform.offsetMin = new Vector2(30, -70);
            statusText = UiKit.Label(p.transform, string.Empty, 20, UiKit.SubTextColor, TextAnchor.UpperRight);
            UiKit.Stretch(statusText.rectTransform, 30, 30, 30, 0).anchorMin = new Vector2(0, 1);
            statusText.rectTransform.offsetMin = new Vector2(30, -70);
            RectTransform host = UiKit.Stretch(UiKit.Rect(p.transform, "Host"), 16, 16, 84, 16);
            RectTransform l = UiKit.ScrollList(host, out ScrollRect _, 10);
            UiKit.Stretch((RectTransform)l.parent.parent);
            return l;
        }

        private async void Load()
        {
            if (Baking || loading) return;
            loading = true;
            SharedListResult res;
            try
            {
                res = await App.SharedStages.GetListAsync();
            }
            catch (Exception e)
            {
                Debug.LogError("[Shared] list failed: " + e);
                res = new SharedListResult { Status = OnlineStatus.Error };
            }
            loading = false;
            if (Disposed || list == null) return;
            for (int i = list.childCount - 1; i >= 0; i--) UiKit.DestroySafe(list.GetChild(i).gameObject);
            status.text = App.OnlineStatusText(res.Status) + (res.FetchedAt > 0 ? " · " + EditorCommon.FormatDate(res.FetchedAt) : string.Empty)
                + (res.Skipped > 0 ? " · " + T("online.skipped", "n", res.Skipped) : string.Empty);
            if (res.Status == OnlineStatus.NotConfigured)
            {
                UiKit.Height(UiKit.Label(list, T("online.unavailable"), 20, UiKit.Gold, TextAnchor.UpperLeft), 110);
            }
            if (res.Items.Count == 0)
            {
                UiKit.Height(UiKit.Label(list, T("editor.noShared"), 24, UiKit.SubTextColor), 90);
                return;
            }
            foreach (SharedStageItem it in res.Items)
            {
                SharedStageItem item = it;
                Row(list, item.Stage, item.Author + " · " + EditorCommon.FormatDate(item.At), () => ConfirmPlay(item.Stage, item.Author, null));
            }
        }

        private void Row(RectTransform parent, StageData stage, string sub, Action onClick)
        {
            Button row = UiKit.Button(parent, string.Empty, () => onClick(), ButtonVariant.Normal);
            UiKit.Height(row, 140);
            EditorCommon.Thumb(row.transform, stage, 90);
            Text n = UiKit.Label(row.transform, Txt(stage.Name), 28, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(n.rectTransform, 130, 20, 20, 64);
            Text s = UiKit.Label(row.transform, EditorCommon.TypeSummary(App, stage) + "   " + sub, 20, UiKit.SubTextColor, TextAnchor.LowerLeft);
            UiKit.Stretch(s.rectTransform, 130, 20, 64, 20);
        }

        /// <summary>名前・概要を確認してから読み込む</summary>
        private void ConfirmPlay(StageData stage, string author, string creationId)
        {
            string summary = Txt(stage.Summary);
            App.OpenModal(Txt(stage.Name), body =>
            {
                RectTransform col = UiKit.Stretch(UiKit.Rect(body, "Col"));
                UiKit.Column(col, 12);
                UiKit.Height(UiKit.Label(col, string.IsNullOrEmpty(summary) ? "—" : summary, 26, UiKit.TextColor, TextAnchor.UpperLeft), 110);
                UiKit.Height(UiKit.Label(col, author + "   ·   " + stage.EditTimeSec + "s   ·   " + EditorCommon.TypeSummary(App, stage), 22, UiKit.SubTextColor, TextAnchor.MiddleLeft), 50);
            }, new[]
            {
                GameApp.Btn(T("common.cancel"), ButtonVariant.Ghost, null),
                GameApp.Btn(T("common.botPlay"), ButtonVariant.Normal, () => Play(stage, author, creationId, BattlePlayer.Bot)),
                GameApp.Btn(T("common.play"), ButtonVariant.Primary, () => Play(stage, author, creationId, BattlePlayer.Human)),
            }, 1000, 460);
        }

        private void Play(StageData stage, string author, string creationId, BattlePlayer p)
        {
            BattleContext ctx = creationId != null
                ? new BattleContext { Kind = BattleKind.Custom, CreationId = creationId, Stage = stage.Clone() }
                : new BattleContext { Kind = BattleKind.Shared, Stage = stage.Clone(), Author = author };
            App.Go(() => new BattleScreen(ctx, p));
        }
    }
}

/*
## このアセットの役割
ステージエディター関連の画面です。作成／プレイの選択、自作ステージ一覧（新規・複製・ファイル書き出し・読み込み・削除）、
エディター本体（弾幕配置・長押しドラッグで線に沿った起点自動配置・起点の微調整・平行移動・テストプレイ・保存・共有登録）、
共有ステージ一覧（名前・概要を確認してから読み込み）を含みます。

## 他アセットとの依存関係
MyStageService / SharedStageService / StageValidator / TrajectoryPath.ResampleStroke / FieldGraphic / FieldRenderer / BattleScreen。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。ステージファイルは Application.persistentDataPath/Stages に「*.dwstage.json」として書き出されます。Web版で書き出した JSON もこのフォルダへ置けば読み込めます。

## 注意点
共有登録はテストプレイでクリアし、その後に変更していない場合のみ可能です。登録後は編集できません（複製は可能）。共有ステージは必ず StageValidator で検証してから使います。
*/
