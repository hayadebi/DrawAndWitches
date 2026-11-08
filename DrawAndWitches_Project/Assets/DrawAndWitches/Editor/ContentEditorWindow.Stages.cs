using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;
using DrawAndWitches.Presentation;
using UnityEditor;
using UnityEngine;

namespace DrawAndWitches.EditorTools
{
    public sealed partial class ContentEditorWindow
    {
        private const string IndexPath = ContentJson.StagesDir + "/index.json";
        private const string DialoguesPath = ContentJson.ResDir + "/dialogues.json";
        private const uint TestSeed = 20261028u;
        private static readonly Regex StageIdPattern = new Regex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant);
        private const float PreviewScale = 0.5f;
        private const float PickPx = 9f;

        private sealed class StageEntry
        {
            public string OriginalId;
            public string File;
            public Dictionary<string, object> Data;
            public List<object> Dialogue;
            public string LoadError;
        }

        private Dictionary<string, object> indexRoot;
        private Dictionary<string, object> dialoguesRoot;
        private List<StageEntry> stages = new List<StageEntry>();
        private readonly List<string> removedStageFiles = new List<string>();
        private int stageSel;
        private bool stageDirty;
        private Vector2 stageScroll;
        private int barrageSel;
        private string stageReport;
        private MessageType stageReportType = MessageType.None;
        private BattleSimulation previewSim;
        private string previewError;
        private bool previewStale = true;
        private int dragBarrage = -1;
        private int dragPoint = -1;
        private int stageSubTab;

        private void LoadStages()
        {
            indexRoot = ContentJson.ReadObject(IndexPath);
            dialoguesRoot = File.Exists(DialoguesPath) ? ContentJson.ReadObject(DialoguesPath) : new Dictionary<string, object>(StringComparer.Ordinal) { ["formatVersion"] = 1.0 };
            Dictionary<string, object> dlgStages = dialoguesRoot.TryGetValue("stages", out object ds) && ds is Dictionary<string, object> dsd ? dsd : null;
            if (dlgStages == null)
            {
                dlgStages = new Dictionary<string, object>(StringComparer.Ordinal);
                dialoguesRoot["stages"] = dlgStages;
            }
            stages = new List<StageEntry>();
            removedStageFiles.Clear();
            foreach (object o in ContentJson.List(indexRoot, "stages"))
            {
                if (!(o is Dictionary<string, object> e)) continue;
                string id = ContentJson.Str(e, "id");
                string file = ContentJson.Str(e, "file");
                StageEntry se = new StageEntry { OriginalId = id, File = file };
                try
                {
                    if (file.Length == 0 || file.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new InvalidOperationException("file 名が不正です: " + file);
                    se.Data = ContentJson.ReadObject(ContentJson.StagesDir + "/" + file);
                }
                catch (InvalidOperationException ex)
                {
                    se.LoadError = ex.Message;
                    se.Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id };
                }
                se.Dialogue = dlgStages.TryGetValue(id, out object dl) && dl is List<object> dll ? dll : new List<object>();
                stages.Add(se);
            }
            stageSel = Mathf.Clamp(stageSel, 0, Mathf.Max(0, stages.Count - 1));
            stageDirty = false;
            previewStale = true;
            stageReport = null;
        }

        // ------------------------------------------------------------ 他タブから使う参照チェック

        private List<string> StagesUsingEnemy(string enemyId)
        {
            List<string> r = new List<string>();
            foreach (StageEntry s in stages)
            {
                if (ContentJson.Str(s.Data, "enemyId") == enemyId) r.Add(ContentJson.Str(s.Data, "id"));
            }
            return r;
        }

        private List<string> StagesUsingBullet(string typeId)
        {
            List<string> r = new List<string>();
            foreach (StageEntry s in stages)
            {
                foreach (object b in ContentJson.List(s.Data, "barrages"))
                {
                    if (b is Dictionary<string, object> bd && ContentJson.Str(bd, "type") == typeId)
                    {
                        r.Add(ContentJson.Str(s.Data, "id"));
                        break;
                    }
                }
            }
            return r;
        }

        private int ApplyEnemyRenames(Dictionary<string, string> renames)
        {
            int n = 0;
            foreach (StageEntry s in stages)
            {
                if (renames.TryGetValue(ContentJson.Str(s.Data, "enemyId"), out string nid))
                {
                    s.Data["enemyId"] = nid;
                    n++;
                }
            }
            if (n > 0) SaveStages();
            return n;
        }

        private int ApplyBulletRenames(Dictionary<string, string> renames)
        {
            int n = 0;
            foreach (StageEntry s in stages)
            {
                foreach (object b in ContentJson.List(s.Data, "barrages"))
                {
                    if (b is Dictionary<string, object> bd && renames.TryGetValue(ContentJson.Str(bd, "type"), out string nid))
                    {
                        bd["type"] = nid;
                        n++;
                    }
                }
            }
            if (n > 0) SaveStages();
            return n;
        }

        // ------------------------------------------------------------ 画面

        private void DrawStagesTab()
        {
            if (stages.Count == 0)
            {
                EditorGUILayout.HelpBox("ステージがありません。", MessageType.Info);
                if (GUILayout.Button("ステージを追加")) AddStage(null);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    List<string> labels = new List<string>();
                    for (int i = 0; i < stages.Count; i++)
                    {
                        StageEntry s = stages[i];
                        labels.Add((i + 1) + ". " + (s.LoadError != null ? "⚠ " : string.Empty) + ContentJson.LocJa(s.Data, "name") + " (" + ContentJson.Str(s.Data, "id") + ")");
                    }
                    int ns = DrawList(labels, stageSel, 230);
                    if (ns != stageSel)
                    {
                        stageSel = ns;
                        barrageSel = 0;
                        previewStale = true;
                        stageReport = null;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("追加")) AddStage(null);
                        if (GUILayout.Button("複製")) AddStage(stages[stageSel]);
                        if (GUILayout.Button("削除")) DeleteStage(stageSel);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("↑")) MoveStage(stageSel, -1);
                        if (GUILayout.Button("↓")) MoveStage(stageSel, 1);
                    }
                    EditorGUILayout.HelpBox("一覧の順番がストーリーの順番です（エンドレスは全ステージから出題）。", MessageType.None);
                    EditorGUILayout.Space(8);
                    GUI.backgroundColor = stageDirty ? new Color(1f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(stageDirty ? "保存（未保存の変更あり）" : "保存", GUILayout.Height(32))) SaveStages();
                    GUI.backgroundColor = Color.white;
                }
                StageEntry cur = stages[stageSel];
                if (cur.LoadError != null)
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.HelpBox("このステージのファイルを読み込めません: " + cur.LoadError + "\n削除するか、ファイルを修正して再読込してください。", MessageType.Error);
                    }
                    return;
                }
                using (EditorGUILayout.ScrollViewScope sv = new EditorGUILayout.ScrollViewScope(stageScroll))
                {
                    stageScroll = sv.scrollPosition;
                    stageSubTab = GUILayout.Toolbar(stageSubTab, new[] { "ステージ・弾幕", "会話イベント" }, GUILayout.Width(300));
                    if (stageSubTab == 0) DrawStageDetail(cur);
                    else DrawDialogue(cur);
                }
                if (stageSubTab == 0)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(GameConstants.World.Width * PreviewScale + 12)))
                    {
                        DrawPreview(cur);
                    }
                }
            }
        }

        private void DrawStageDetail(StageEntry s)
        {
            Dictionary<string, object> d = s.Data;
            EditorGUI.BeginChangeCheck();
            Header("基本");
            string id = EditorGUILayout.DelayedTextField(new GUIContent("ID", "英小文字で始まる英小文字・数字・_（進行・記録のキー。公開後の変更は非推奨）"), ContentJson.Str(d, "id"));
            if (id != ContentJson.Str(d, "id"))
            {
                if (!StageIdPattern.IsMatch(id)) ShowNotification(new GUIContent("ID は英小文字で始まる英小文字・数字・_ です"));
                else d["id"] = id;
            }
            List<string> enemyIds = EnemyIds();
            string enemy = ContentJson.Str(d, "enemyId");
            List<string> enemyLabels = new List<string>();
            foreach (string eid in enemyIds)
            {
                CharEntry c = chars.Find(x => ContentJson.Str(x.Data, "id") == eid);
                enemyLabels.Add((c != null ? ContentJson.LocJa(c.Data, "name") : eid) + " (" + eid + ")");
            }
            int ei = enemyIds.IndexOf(enemy);
            int nei = EditorGUILayout.Popup("対戦相手", ei < 0 ? 0 : ei, enemyLabels.ToArray());
            if (enemyIds.Count > 0 && (ei < 0 || nei != ei)) d["enemyId"] = enemyIds[nei];
            d["editTimeSec"] = (double)EditorGUILayout.IntSlider(new GUIContent("編集時間（秒）", "軌道を描き換えられる時間"), ContentJson.Int(d, "editTimeSec", 40), GameConstants.StageLimits.MinEditSec, GameConstants.StageLimits.MaxEditSec);
            d["dragRadius"] = (double)EditorGUILayout.IntSlider(new GUIContent("ドラッグ半径（px）", "各制御点を元の位置から動かせる距離"), ContentJson.Int(d, "dragRadius", 120), GameConstants.StageLimits.MinDragRadius, GameConstants.StageLimits.MaxDragRadius);
            if (!d.ContainsKey("formatVersion")) d["formatVersion"] = (double)GameConstants.StageFormatVersion;
            if (EditorGUI.EndChangeCheck()) MarkStageChanged();

            Header("テキスト");
            LangSelector();
            bool t = LocField("ステージ名", d, "name", false, GameConstants.StageLimits.NameMaxChars);
            t |= LocField("概要", d, "summary", true, GameConstants.StageLimits.SummaryMaxChars);
            t |= LocField("スペル名（空なら敵のスペル名）", d, "spellName", false, 40);
            if (t) MarkStageChanged();

            Header("弾幕");
            List<object> barrages = ContentJson.List(d, "barrages");
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < barrages.Count; i++)
                {
                    GUI.backgroundColor = FieldRenderer.BarrageColor(i);
                    if (GUILayout.Toggle(barrageSel == i, "#" + (i + 1), EditorStyles.miniButton, GUILayout.Width(36)) && barrageSel != i)
                    {
                        barrageSel = i;
                        GUI.FocusControl(null);
                    }
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = barrages.Count < GameConstants.StageLimits.MaxBarrages;
                if (GUILayout.Button("＋弾幕", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    barrages.Add(NewBarrage(barrages.Count));
                    barrageSel = barrages.Count - 1;
                    MarkStageChanged();
                }
                GUI.enabled = true;
            }
            if (barrages.Count == 0)
            {
                EditorGUILayout.HelpBox("弾幕がありません。「＋弾幕」で追加してください。", MessageType.Warning);
            }
            else
            {
                barrageSel = Mathf.Clamp(barrageSel, 0, barrages.Count - 1);
                if (barrages[barrageSel] is Dictionary<string, object> b) DrawBarrage(barrages, b);
            }

            Header("検証・テストプレイ");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("検証", GUILayout.Height(26))) ValidateCurrent(true);
                if (GUILayout.Button(new GUIContent("無操作テスト", "何もしないと負けるか（＝攻略が必要か）を確認"), GUILayout.Height(26))) RunNoInputTest();
                if (GUILayout.Button(new GUIContent("bot 攻略テスト", "自動プレイヤーがクリアできるか（＝攻略可能か）を確認"), GUILayout.Height(26))) RunBotTest();
            }
            if (!string.IsNullOrEmpty(stageReport)) EditorGUILayout.HelpBox(stageReport, stageReportType);
        }

        private void DrawBarrage(List<object> barrages, Dictionary<string, object> b)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                List<string> typeIds = new List<string>();
                List<string> typeLabels = new List<string>();
                foreach (BulletEntry be in bullets)
                {
                    typeIds.Add(ContentJson.Str(be.Data, "id"));
                    typeLabels.Add(ContentJson.LocJa(be.Data, "name") + " (" + ContentJson.Str(be.Data, "id") + ")");
                }
                string type = ContentJson.Str(b, "type");
                int ti = typeIds.IndexOf(type);
                if (ti < 0)
                {
                    typeIds.Add(type);
                    typeLabels.Add("（未定義）" + type);
                    ti = typeIds.Count - 1;
                }
                int nti = EditorGUILayout.Popup("弾の種類", ti, typeLabels.ToArray());
                b["type"] = typeIds[nti];
                BulletEntry def = bullets.Find(x => ContentJson.Str(x.Data, "id") == typeIds[nti]);
                int minInt = def != null ? MinIntervalFor(ToDef(def.Data)) : GameConstants.StageLimits.MinIntervalTicks;
                b["count"] = (double)EditorGUILayout.IntSlider("弾数", ContentJson.Int(b, "count", 3), GameConstants.StageLimits.MinCount, GameConstants.StageLimits.MaxCount);
                b["intervalTicks"] = (double)EditorGUILayout.IntSlider(new GUIContent("発射間隔（tick）", "最小 " + minInt + " tick（弾が重ならない間隔）"), Math.Max(minInt, ContentJson.Int(b, "intervalTicks", 24)), minInt, GameConstants.StageLimits.MaxIntervalTicks);
                b["startDelayTicks"] = (double)EditorGUILayout.IntSlider(new GUIContent("開始遅延（tick）", "実行開始から最初の弾までの時間。60 tick = 1 秒"), ContentJson.Int(b, "startDelayTicks", 0), 0, GameConstants.StageLimits.MaxStartDelayTicks);
                List<object> points = ContentJson.List(b, "points");
                EditorGUILayout.LabelField("制御点（始点 → 終点。プレビュー上でドラッグ可）", EditorStyles.miniBoldLabel);
                int removeAt = -1;
                for (int i = 0; i < points.Count; i++)
                {
                    List<object> p = points[i] as List<object>;
                    if (p == null || p.Count < 2)
                    {
                        p = new List<object> { (double)GameConstants.Enemy.X, (double)GameConstants.Enemy.Y };
                        points[i] = p;
                    }
                    MiniJson.TryGetInt(p[0], out int x);
                    MiniJson.TryGetInt(p[1], out int y);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(i == 0 ? "始点" : i == points.Count - 1 ? "終点" : "点 " + i, GUILayout.Width(44));
                        x = EditorGUILayout.IntField(x, GUILayout.Width(70));
                        y = EditorGUILayout.IntField(y, GUILayout.Width(70));
                        p[0] = (double)Mathf.Clamp(x, StageValidator.FieldMinX, StageValidator.FieldMaxX);
                        p[1] = (double)Mathf.Clamp(y, StageValidator.FieldMinY, StageValidator.FieldMaxY);
                        GUI.enabled = points.Count > GameConstants.StageLimits.MinPoints;
                        if (GUILayout.Button("×", GUILayout.Width(24))) removeAt = i;
                        GUI.enabled = true;
                    }
                }
                if (removeAt >= 0) points.RemoveAt(removeAt);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = points.Count < GameConstants.StageLimits.MaxPoints;
                    if (GUILayout.Button("点を追加（終点の手前）") && points.Count >= 1)
                    {
                        List<object> last = (List<object>)points[points.Count - 1];
                        List<object> prev = points.Count >= 2 ? (List<object>)points[points.Count - 2] : last;
                        MiniJson.TryGetInt(last[0], out int lx);
                        MiniJson.TryGetInt(last[1], out int ly);
                        MiniJson.TryGetInt(prev[0], out int px);
                        MiniJson.TryGetInt(prev[1], out int py);
                        points.Insert(points.Count - 1, new List<object> { (double)((lx + px) / 2), (double)((ly + py) / 2) });
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("この弾幕を複製") && barrages.Count < GameConstants.StageLimits.MaxBarrages)
                    {
                        barrages.Insert(barrageSel + 1, ContentJson.DeepCopy(b));
                        barrageSel++;
                    }
                    if (GUILayout.Button("この弾幕を削除"))
                    {
                        barrages.RemoveAt(barrageSel);
                        barrageSel = Mathf.Max(0, barrageSel - 1);
                        MarkStageChanged();
                        GUIUtility.ExitGUI();
                    }
                }
                if (EditorGUI.EndChangeCheck()) MarkStageChanged();
            }
        }

        private static Dictionary<string, object> NewBarrage(int index)
        {
            int x = 160 + (index % 4) * 130;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "normal",
                ["count"] = 3.0,
                ["intervalTicks"] = 24.0,
                ["startDelayTicks"] = (double)(index * 30),
                ["points"] = new List<object>
                {
                    new List<object> { (double)x, (double)(GameConstants.Enemy.Y + 60) },
                    new List<object> { (double)x, 480.0 },
                    new List<object> { (double)GameConstants.Player.X, (double)(GameConstants.Player.Y - 40) },
                },
            };
        }

        private void MarkStageChanged()
        {
            stageDirty = true;
            previewStale = true;
        }

        // ------------------------------------------------------------ プレビュー（軌道表示・制御点ドラッグ）

        private StageValidationResult ValidateEntry(StageEntry s)
        {
            return StageValidator.Validate(s.Data, EnemyIds());
        }

        private void RebuildPreview(StageEntry s)
        {
            previewStale = false;
            previewSim = null;
            previewError = null;
            StageValidationResult v = ValidateEntry(s);
            if (!v.Ok)
            {
                previewError = v.ErrorSummary;
                return;
            }
            try
            {
                previewSim = new BattleSimulation(v.Stage, TestSeed, new BattleOptions { RecordEvents = false });
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                previewError = e.Message;
            }
        }

        private void DrawPreview(StageEntry s)
        {
            if (previewStale) RebuildPreview(s);
            float w = GameConstants.World.Width * PreviewScale;
            float h = GameConstants.World.Height * PreviewScale;
            EditorGUILayout.LabelField("プレビュー（実寸の " + Mathf.RoundToInt(PreviewScale * 100) + "%）", EditorStyles.miniBoldLabel);
            Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            Vector2 W2S(double x, double y) => new Vector2(r.x + (float)x * PreviewScale, r.y + (float)y * PreviewScale);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0.05f, 0.03f, 0.12f));
                Rect field = new Rect(W2S(StageValidator.FieldMinX, StageValidator.FieldMinY), new Vector2((StageValidator.FieldMaxX - StageValidator.FieldMinX) * PreviewScale, (StageValidator.FieldMaxY - StageValidator.FieldMinY) * PreviewScale));
                Handles.color = new Color(1, 1, 1, 0.15f);
                Handles.DrawSolidRectangleWithOutline(field, new Color(0, 0, 0, 0), new Color(1, 1, 1, 0.2f));
                Rect baseRect = new Rect(W2S(GameConstants.Base.X0, GameConstants.Base.Y0), new Vector2((GameConstants.Base.X1 - GameConstants.Base.X0) * PreviewScale, (GameConstants.Base.Y1 - GameConstants.Base.Y0) * PreviewScale));
                EditorGUI.DrawRect(baseRect, new Color(0.4f, 0.8f, 1f, 0.25f));
                Handles.color = new Color(1f, 0.5f, 0.7f, 0.8f);
                Handles.DrawWireDisc(W2S(GameConstants.Enemy.X, GameConstants.Enemy.Y), Vector3.forward, GameConstants.Enemy.Radius * PreviewScale);
                Handles.color = new Color(0.5f, 0.9f, 1f, 0.9f);
                Handles.DrawWireDisc(W2S(GameConstants.Player.X, GameConstants.Player.Y), Vector3.forward, GameConstants.Player.Radius * PreviewScale);

                int dragR = ContentJson.Int(s.Data, "dragRadius", 120);
                List<object> barrages = ContentJson.List(s.Data, "barrages");
                for (int bi = 0; bi < barrages.Count; bi++)
                {
                    Color c = FieldRenderer.BarrageColor(bi);
                    bool sel = bi == barrageSel;
                    if (previewSim != null && bi < previewSim.Barrages.Length)
                    {
                        TrajectoryPath p = previewSim.Barrages[bi].Path;
                        Vector3[] pts = new Vector3[p.Count];
                        for (int i = 0; i < p.Count; i++) pts[i] = W2S(p.Xs[i], p.Ys[i]);
                        Handles.color = new Color(c.r, c.g, c.b, sel ? 1f : 0.45f);
                        Handles.DrawAAPolyLine(sel ? 4f : 2.5f, pts);
                    }
                    if (!(barrages[bi] is Dictionary<string, object> bd)) continue;
                    List<object> cps = ContentJson.List(bd, "points");
                    for (int i = 0; i < cps.Count; i++)
                    {
                        if (!(cps[i] is List<object> cp) || cp.Count < 2) continue;
                        MiniJson.TryGetInt(cp[0], out int x);
                        MiniJson.TryGetInt(cp[1], out int y);
                        Vector2 sp = W2S(x, y);
                        if (sel)
                        {
                            Handles.color = new Color(c.r, c.g, c.b, 0.12f);
                            Handles.DrawWireDisc(sp, Vector3.forward, dragR * PreviewScale);
                        }
                        Handles.color = sel ? Color.white : new Color(c.r, c.g, c.b, 0.7f);
                        Handles.DrawSolidDisc(sp, Vector3.forward, sel ? 5f : 3f);
                    }
                }
                if (previewError != null) GUI.Label(new Rect(r.x + 6, r.yMax - 40, r.width - 12, 36), "⚠ " + previewError, EditorStyles.wordWrappedMiniLabel);
            }
            HandlePointDrag(s, r);
            EditorGUILayout.HelpBox("白い点＝選択中の弾幕の制御点（ドラッグで移動）。薄い円＝プレイヤーが動かせる範囲（ドラッグ半径）。下の水色＝拠点。", MessageType.None);
        }

        private void HandlePointDrag(StageEntry s, Rect r)
        {
            Event e = Event.current;
            int cid = GUIUtility.GetControlID(FocusType.Passive);
            List<object> barrages = ContentJson.List(s.Data, "barrages");
            switch (e.GetTypeForControl(cid))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !r.Contains(e.mousePosition) || barrageSel >= barrages.Count) break;
                    if (!(barrages[barrageSel] is Dictionary<string, object> bd)) break;
                    List<object> cps = ContentJson.List(bd, "points");
                    float best = PickPx * PickPx;
                    int hit = -1;
                    for (int i = 0; i < cps.Count; i++)
                    {
                        if (!(cps[i] is List<object> cp) || cp.Count < 2) continue;
                        MiniJson.TryGetInt(cp[0], out int x);
                        MiniJson.TryGetInt(cp[1], out int y);
                        Vector2 sp = new Vector2(r.x + x * PreviewScale, r.y + y * PreviewScale);
                        float d2 = (sp - e.mousePosition).sqrMagnitude;
                        if (d2 <= best)
                        {
                            best = d2;
                            hit = i;
                        }
                    }
                    if (hit >= 0)
                    {
                        dragBarrage = barrageSel;
                        dragPoint = hit;
                        GUIUtility.hotControl = cid;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != cid || dragBarrage < 0 || dragBarrage >= barrages.Count) break;
                    if (barrages[dragBarrage] is Dictionary<string, object> db)
                    {
                        List<object> pts = ContentJson.List(db, "points");
                        if (dragPoint >= 0 && dragPoint < pts.Count)
                        {
                            int nx = Mathf.Clamp(Mathf.RoundToInt((e.mousePosition.x - r.x) / PreviewScale), StageValidator.FieldMinX, StageValidator.FieldMaxX);
                            int ny = Mathf.Clamp(Mathf.RoundToInt((e.mousePosition.y - r.y) / PreviewScale), StageValidator.FieldMinY, StageValidator.FieldMaxY);
                            pts[dragPoint] = new List<object> { (double)nx, (double)ny };
                            MarkStageChanged();
                            GUI.FocusControl(null);
                        }
                    }
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != cid) break;
                    GUIUtility.hotControl = 0;
                    dragBarrage = -1;
                    dragPoint = -1;
                    e.Use();
                    break;
            }
        }

        // ------------------------------------------------------------ 会話イベント

        private void DrawDialogue(StageEntry s)
        {
            Header("戦闘前の会話");
            EditorGUILayout.HelpBox("ストーリーでステージを始める前に表示されます。表情（face）は CharacterPortrait の表情名（空なら talk）。差分が無い表情はデフォルト立ち絵になります。", MessageType.None);
            LangSelector();
            int remove = -1;
            int moveUp = -1;
            for (int i = 0; i < s.Dialogue.Count; i++)
            {
                if (!(s.Dialogue[i] is Dictionary<string, object> line)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField((i + 1) + ".", GUILayout.Width(24));
                        int sp = ContentJson.Str(line, "speaker") == "player" ? 0 : 1;
                        sp = EditorGUILayout.Popup(sp, new[] { "主人公", "対戦相手" }, GUILayout.Width(90));
                        line["speaker"] = sp == 0 ? "player" : "enemy";
                        EditorGUILayout.LabelField("表情", GUILayout.Width(32));
                        string face = EditorGUILayout.TextField(ContentJson.Str(line, "face"), GUILayout.Width(90));
                        if (string.IsNullOrWhiteSpace(face)) line.Remove("face");
                        else line["face"] = face.Trim();
                        GUILayout.FlexibleSpace();
                        if (i > 0 && GUILayout.Button("↑", GUILayout.Width(24))) moveUp = i;
                        if (GUILayout.Button("×", GUILayout.Width(24))) remove = i;
                    }
                    if (EditorGUI.EndChangeCheck()) stageDirty = true;
                    if (LocField("セリフ", line, "text", true, 120)) stageDirty = true;
                }
            }
            if (remove >= 0)
            {
                s.Dialogue.RemoveAt(remove);
                stageDirty = true;
            }
            if (moveUp > 0)
            {
                object tmp = s.Dialogue[moveUp - 1];
                s.Dialogue[moveUp - 1] = s.Dialogue[moveUp];
                s.Dialogue[moveUp] = tmp;
                stageDirty = true;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("主人公のセリフを追加")) AddLine(s, "player");
                if (GUILayout.Button("対戦相手のセリフを追加")) AddLine(s, "enemy");
            }
        }

        private void AddLine(StageEntry s, string speaker)
        {
            s.Dialogue.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["speaker"] = speaker, ["text"] = ContentJson.NewLoc(string.Empty) });
            stageDirty = true;
        }

        // ------------------------------------------------------------ 検証・テスト

        private StageData ValidateCurrent(bool report)
        {
            StageEntry s = stages[stageSel];
            StageValidationResult v = ValidateEntry(s);
            if (!v.Ok)
            {
                string unknownType = UnsavedBulletHint(s);
                SetReport("検証エラー:\n・" + string.Join("\n・", v.Errors) + unknownType, MessageType.Error);
                return null;
            }
            if (report) SetReport("問題ありません（弾幕 " + v.Stage.Barrages.Count + " / 合計 " + TotalBullets(v.Stage) + " 発）", MessageType.Info);
            return v.Stage;
        }

        private string UnsavedBulletHint(StageEntry s)
        {
            foreach (object b in ContentJson.List(s.Data, "barrages"))
            {
                if (b is Dictionary<string, object> bd && BulletTypeDef.Find(ContentJson.Str(bd, "type")) == null)
                {
                    return "\n（弾種「" + ContentJson.Str(bd, "type") + "」が未登録です。弾種タブで保存してください）";
                }
            }
            return string.Empty;
        }

        private static int TotalBullets(StageData st)
        {
            int n = 0;
            foreach (BarrageData b in st.Barrages) n += b.Count;
            return n;
        }

        private void SetReport(string text, MessageType type)
        {
            stageReport = text;
            stageReportType = type;
        }

        private void RunNoInputTest()
        {
            StageData st = ValidateCurrent(false);
            if (st == null) return;
            try
            {
                BattleResult r = BattleSimulation.RunWithCommands(st, TestSeed, new List<InputCommand>());
                bool ok = r.Outcome != BattleOutcome.Clear;
                SetReport((ok ? "OK: 何もしないと負けます" : "注意: 何もしなくてもクリアできてしまいます（弾幕を主人公・拠点へ向けてください）")
                    + "\n結果: " + r.Outcome + (r.Outcome != BattleOutcome.Clear ? "（" + r.LoseCause + "）" : string.Empty) + " / 実行 " + (r.ExecTicks / 60f).ToString("0.0") + " 秒",
                    ok ? MessageType.Info : MessageType.Warning);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                SetReport("テストに失敗しました: " + e.Message, MessageType.Error);
            }
        }

        private void RunBotTest()
        {
            StageData st = ValidateCurrent(false);
            if (st == null) return;
            try
            {
                BotPlanner planner = new BotPlanner(st, TestSeed);
                IEnumerator<BotProgress> it = planner.PlanSteps();
                bool cancelled = false;
                while (it.MoveNext())
                {
                    BotProgress p = it.Current;
                    if (EditorUtility.DisplayCancelableProgressBar("bot 攻略テスト", "探索中… 評価 " + p.Evaluations + (p.Clear ? "（クリア解あり）" : string.Empty), Mathf.Clamp01(p.Evaluations / (float)GameConstants.Bot.MaxEvaluations)))
                    {
                        cancelled = true;
                        break;
                    }
                }
                EditorUtility.ClearProgressBar();
                if (cancelled)
                {
                    SetReport("bot 攻略テストを中止しました。", MessageType.None);
                    return;
                }
                BotPlan plan = planner.Best;
                if (plan == null)
                {
                    SetReport("bot が解を見つけられませんでした（攻略不能の可能性）。", MessageType.Warning);
                    return;
                }
                List<InputCommand> cmds = BotPlanner.BuildCommands(plan, st);
                BattleResult r = BattleSimulation.RunWithCommands(st, TestSeed, cmds);
                bool clear = r.Outcome == BattleOutcome.Clear;
                StageScore sc = ScoreCalculator.ComputeStage(r);
                SetReport((clear ? "OK: bot がクリアしました（攻略可能）" : "注意: bot はクリアできませんでした。難しすぎる可能性があります")
                    + "\n結果: " + r.Outcome + " / 操作 " + r.Ops + " / 相殺 " + r.Cancels + " / スコア " + sc.Total + " / 評価回数 " + planner.Evaluations,
                    clear ? MessageType.Info : MessageType.Warning);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                SetReport("テストに失敗しました: " + e.Message, MessageType.Error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------ 追加・削除・保存

        private void AddStage(StageEntry source)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (StageEntry s in stages) ids.Add(ContentJson.Str(s.Data, "id"));
            int n = stages.Count + 1;
            string id;
            do
            {
                id = "story_" + n.ToString("00");
                n++;
            }
            while (ids.Contains(id));
            Dictionary<string, object> d;
            List<object> dlg;
            if (source != null && source.LoadError == null)
            {
                d = (Dictionary<string, object>)ContentJson.DeepCopy(source.Data);
                dlg = (List<object>)ContentJson.DeepCopy(source.Dialogue);
            }
            else
            {
                List<string> enemies = EnemyIds();
                d = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["formatVersion"] = (double)GameConstants.StageFormatVersion,
                    ["name"] = ContentJson.NewLoc("新しいステージ"),
                    ["summary"] = ContentJson.NewLoc(string.Empty),
                    ["enemyId"] = enemies.Count > 0 ? enemies[0] : string.Empty,
                    ["editTimeSec"] = 40.0,
                    ["dragRadius"] = 120.0,
                    ["barrages"] = new List<object> { NewBarrage(0), NewBarrage(1) },
                };
                dlg = new List<object>();
            }
            d["id"] = id;
            stages.Insert(stageSel + 1, new StageEntry { OriginalId = null, File = id + ".json", Data = d, Dialogue = dlg });
            stageSel++;
            barrageSel = 0;
            MarkStageChanged();
            GUI.FocusControl(null);
        }

        private void DeleteStage(int index)
        {
            StageEntry s = stages[index];
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "ステージ「" + ContentJson.Str(s.Data, "id") + "」を一覧から削除しますか？\n保存時にステージファイルも削除されます。プレイヤーの進行データ（クリア記録）はそのまま残ります。", "削除", "キャンセル")) return;
            if (s.OriginalId != null && !string.IsNullOrEmpty(s.File)) removedStageFiles.Add(s.File);
            stages.RemoveAt(index);
            stageSel = Mathf.Clamp(index - 1, 0, Mathf.Max(0, stages.Count - 1));
            MarkStageChanged();
        }

        private void MoveStage(int index, int dir)
        {
            int to = index + dir;
            if (to < 0 || to >= stages.Count) return;
            StageEntry tmp = stages[to];
            stages[to] = stages[index];
            stages[index] = tmp;
            stageSel = to;
            stageDirty = true;
        }

        private void SaveStages()
        {
            GUI.FocusControl(null);
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<string> enemyIds = EnemyIds();
            foreach (StageEntry s in stages)
            {
                string id = ContentJson.Str(s.Data, "id");
                if (s.LoadError != null)
                {
                    errors.Add(id + ": 読み込みエラーのため保存できません（削除するか修正してください）");
                    continue;
                }
                if (!StageIdPattern.IsMatch(id)) errors.Add("ID が不正です: '" + id + "'");
                if (!ids.Add(id)) errors.Add("ID が重複しています: " + id);
                StageValidationResult v = StageValidator.Validate(s.Data, enemyIds);
                if (!v.Ok) errors.Add(id + ": " + v.ErrorSummary + UnsavedBulletHint(s));
            }
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "保存できません:\n" + string.Join("\n", errors), "OK");
                return;
            }
            try
            {
                List<object> index = new List<object>();
                Dictionary<string, object> dlgStages = new Dictionary<string, object>(StringComparer.Ordinal);
                HashSet<string> keepFiles = new HashSet<string>(StringComparer.Ordinal);
                foreach (StageEntry s in stages)
                {
                    string id = ContentJson.Str(s.Data, "id");
                    // ID を変えたステージは新しいファイル名で保存し、古いファイルを片付ける
                    string file = id + ".json";
                    if (s.File != file && s.OriginalId != null) removedStageFiles.Add(s.File);
                    s.File = file;
                    keepFiles.Add(file);
                    ContentJson.Write(ContentJson.StagesDir + "/" + file, s.Data);
                    index.Add(new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id, ["file"] = file });
                    if (s.Dialogue.Count > 0) dlgStages[id] = s.Dialogue;
                }
                indexRoot["stages"] = index;
                ContentJson.Write(IndexPath, indexRoot);
                dialoguesRoot["stages"] = dlgStages;
                ContentJson.Write(DialoguesPath, dialoguesRoot);
                foreach (string f in removedStageFiles)
                {
                    if (keepFiles.Contains(f)) continue;
                    string path = ContentJson.StagesDir + "/" + f;
                    if (File.Exists(path) && !AssetDatabase.DeleteAsset(path)) Debug.LogWarning("[ContentEditor] could not delete " + path);
                }
                removedStageFiles.Clear();
                foreach (StageEntry s in stages) s.OriginalId = ContentJson.Str(s.Data, "id");
                stageDirty = false;
                ShowNotification(new GUIContent("ステージ " + stages.Count + " 件を保存しました"));
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError("[ContentEditor] " + e.Message);
                EditorUtility.DisplayDialog("DRAW&WITCHES", e.Message, "OK");
            }
        }
    }
}

/*
## このアセットの役割
Content Editor の「ステージ」タブです。公式ステージ（ストーリー / エンドレス共通）の追加・複製・削除・並べ替え、
対戦相手・編集時間・ドラッグ半径・弾幕（種類・弾数・間隔・遅延・制御点）・多言語テキスト・戦闘前会話を編集します。
プレビューで軌道を確認し、制御点をドラッグで動かせます。「無操作テスト」「bot 攻略テスト」で難易度の目安を確認できます。

## 他アセットとの依存関係
stages/index.json・stages/*.json・dialogues.json、StageValidator（ゲームと同じ検証）、BattleSimulation / BotPlanner（テスト）、
キャラクタータブ（対戦相手の候補）、弾種タブ（弾の種類の候補）。

## Inspector設定
なし。

## USERがUnity上で行う作業
編集して「保存」。保存時に全ステージをゲームと同じ規則で検証し、問題があれば保存しません。

## 注意点
ステージ ID は進行・自己ベスト・ランキングのキーです。公開後に変えると既存の記録と紐付かなくなります。
弾種タブで追加した弾種は、弾種タブで保存してからステージで使ってください。
公式ステージの内容を変えると、そのステージで記録した既存リプレイは検証に失敗する場合があります。
*/
