using System;
using System.Collections.Generic;
using DrawAndWitches.App;
using DrawAndWitches.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrawAndWitches.EditorTools
{
    /// <summary>
    /// メニュー「DRAW&WITCHES / Content Editor」。キャラクター・弾種・ステージ（会話含む）の追加と調整、
    /// およびシーン上の UI テンプレートの表示切替・再生成を行う。
    /// データは Resources/DrawAndWitches 以下の JSON に保存され、ゲーム（Web版と同形式）がそのまま読み込む。
    /// </summary>
    public sealed partial class ContentEditorWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "キャラクター", "弾種", "ステージ", "UI" };
        private const string GameAssetsPath = "Assets/DrawAndWitches/GameAssets.asset";

        [SerializeField] private int tab;
        private Vector2 listScroll;
        private Vector2 bodyScroll;
        private int langTab;
        private GameAssets assets;
        private string loadError;

        [MenuItem("DRAW&WITCHES/Content Editor", priority = 2)]
        public static void Open()
        {
            ContentEditorWindow w = GetWindow<ContentEditorWindow>("DW Content Editor");
            w.minSize = new Vector2(900, 560);
            w.Show();
        }

        private void OnEnable()
        {
            ReloadAll();
        }

        private void ReloadAll()
        {
            loadError = null;
            assets = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
            try
            {
                LoadCharacters();
                LoadBullets();
                LoadStages();
            }
            catch (InvalidOperationException e)
            {
                loadError = e.Message;
                Debug.LogError("[ContentEditor] " + e.Message);
            }
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("再生中は編集できません。再生を停止してください。", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int nt = GUILayout.Toolbar(tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(420));
                if (nt != tab)
                {
                    GUI.FocusControl(null);
                    tab = nt;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("ファイルから再読込", EditorStyles.toolbarButton))
                {
                    if (!AnyDirty() || EditorUtility.DisplayDialog("DRAW&WITCHES", "保存していない変更を破棄して読み込み直しますか？", "破棄して再読込", "キャンセル"))
                    {
                        GUI.FocusControl(null);
                        ReloadAll();
                    }
                }
            }
            if (loadError != null)
            {
                EditorGUILayout.HelpBox("読み込みエラー: " + loadError, MessageType.Error);
                return;
            }
            if (assets == null && tab != 3)
            {
                EditorGUILayout.HelpBox("GameAssets.asset がありません。先に DRAW&WITCHES > Setup Project を実行してください（画像・音声の割り当てに必要です）。", MessageType.Warning);
            }
            try
            {
                switch (tab)
                {
                    case 0: DrawCharactersTab(); break;
                    case 1: DrawBulletsTab(); break;
                    case 2: DrawStagesTab(); break;
                    default: DrawUiTab(); break;
                }
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorGUILayout.HelpBox("表示中にエラーが発生しました: " + e.Message, MessageType.Error);
            }
        }

        private bool AnyDirty()
        {
            return charDirty || bulletDirty || stageDirty;
        }

        private void OnDestroy()
        {
            if (AnyDirty() && EditorUtility.DisplayDialog("DRAW&WITCHES", "Content Editor に保存していない変更があります。保存しますか？", "保存", "破棄"))
            {
                if (charDirty) SaveCharacters();
                if (bulletDirty) SaveBullets();
                if (stageDirty) SaveStages();
            }
        }

        // ------------------------------------------------------------ 共通部品

        /// <summary>左の一覧（選択・ボタン類は呼び出し側）。戻り値は新しい選択番号。</summary>
        private int DrawList(IList<string> labels, int selected, float width)
        {
            using (EditorGUILayout.ScrollViewScope sv = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(width)))
            {
                listScroll = sv.scrollPosition;
                for (int i = 0; i < labels.Count; i++)
                {
                    GUIStyle st = i == selected ? EditorStyles.helpBox : EditorStyles.label;
                    if (GUILayout.Button(labels[i], st, GUILayout.Height(22)))
                    {
                        GUI.FocusControl(null);
                        selected = i;
                    }
                }
            }
            return selected;
        }

        /// <summary>多言語テキスト（4 言語タブ）。変更があれば true。</summary>
        private bool LocField(string label, Dictionary<string, object> owner, string key, bool multiline, int maxChars = 0)
        {
            Dictionary<string, object> d = ContentJson.Loc(owner, key);
            string lang = ContentJson.Languages[langTab];
            string cur = d.TryGetValue(lang, out object v) && v is string s ? s : string.Empty;
            EditorGUILayout.LabelField(label + "（" + ContentJson.LanguageLabels[langTab] + "）", EditorStyles.miniBoldLabel);
            string next = multiline ? EditorGUILayout.TextArea(cur, EditorStyles.textArea, GUILayout.MinHeight(44)) : EditorGUILayout.TextField(cur);
            if (maxChars > 0 && next.Length > maxChars)
            {
                EditorGUILayout.HelpBox(maxChars + " 文字までです（現在 " + next.Length + "）", MessageType.Warning);
            }
            if (next == cur) return false;
            d[lang] = next;
            return true;
        }

        private void LangSelector()
        {
            langTab = GUILayout.Toolbar(langTab, ContentJson.LanguageLabels, GUILayout.Width(360));
        }

        private static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        private static void DrawSpritePreview(Sprite s, float size)
        {
            Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            EditorGUI.DrawRect(r, new Color(0.08f, 0.06f, 0.16f));
            if (s == null || s.texture == null)
            {
                GUI.Label(r, "（画像なし）", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            Rect tr = s.textureRect;
            Texture2D tex = s.texture;
            Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            Rect fit = aspect >= 1f ? new Rect(r.x, r.y + (r.height - r.width / aspect) * 0.5f, r.width, r.width / aspect)
                                    : new Rect(r.x + (r.width - r.height * aspect) * 0.5f, r.y, r.height * aspect, r.height);
            GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
        }

        private void MarkAssetsDirty()
        {
            if (assets == null) return;
            EditorUtility.SetDirty(assets);
        }

        // ------------------------------------------------------------ UI タブ

        private Vector2 uiScroll;

        private void DrawUiTab()
        {
            EditorGUILayout.HelpBox(
                "UI は Boot シーンの GameApp/DW_Canvas/Templates 以下に、画面ごとのテンプレートとして生成されています。\n" +
                "表示したい画面を選んでシーンビュー / ゲームビューで位置・サイズ・色・画像・フォント・レイアウトを調整し、シーンを保存してください。\n" +
                "実行時はテンプレートが自動で非表示になり、調整した値がそのまま画面に反映されます。文言は言語設定に合わせて実行時に入ります。\n" +
                "要素の UiBaked コンポーネントで「Force Hidden」（実行時に非表示）や「Ignore Scene Overrides」（調整を無視）を指定できます。",
                MessageType.Info);
            Scene scene = SceneManager.GetActiveScene();
            GameApp app = ProjectSetup.FindInScene<GameApp>(scene);
            if (app == null || app.Templates == null)
            {
                EditorGUILayout.HelpBox("開いているシーンに UI テンプレートがありません。", MessageType.Warning);
                if (GUILayout.Button("Boot シーンを開く", GUILayout.Height(28))) ProjectSetup.OpenBootScene();
                if (GUILayout.Button("Setup Project を実行", GUILayout.Height(28))) EditorApplication.delayCall += ProjectSetup.SetupFromMenu;
                return;
            }
            Transform templates = app.Templates;
            Transform screens = templates.Find("Screens");
            Transform modals = templates.Find("Modals");
            Transform toasts = templates.Find("Toasts");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("すべて隠す（実行時と同じ状態）", GUILayout.Height(26)))
                {
                    SetTemplateView(templates, null);
                }
                if (GUILayout.Button("Templates を選択", GUILayout.Height(26)))
                {
                    Selection.activeTransform = templates;
                    EditorGUIUtility.PingObject(templates);
                }
            }
            Header("画面テンプレート");
            using (EditorGUILayout.ScrollViewScope sv = new EditorGUILayout.ScrollViewScope(uiScroll))
            {
                uiScroll = sv.scrollPosition;
                List<Transform> items = new List<Transform>();
                if (screens != null) for (int i = 0; i < screens.childCount; i++) items.Add(screens.GetChild(i));
                if (modals != null) items.Add(modals);
                if (toasts != null) items.Add(toasts);
                foreach (Transform t in items)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool visible = templates.gameObject.activeSelf && t.gameObject.activeInHierarchy;
                        EditorGUILayout.LabelField((visible ? "● " : "○ ") + DisplayName(t), GUILayout.Width(300));
                        if (GUILayout.Button("表示して選択", GUILayout.Width(110)))
                        {
                            SetTemplateView(templates, t);
                            Selection.activeTransform = t;
                            EditorGUIUtility.PingObject(t);
                            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                        }
                        if (t.parent == screens)
                        {
                            Transform overlay = t.Find("Overlay");
                            if (overlay != null && GUILayout.Button("演出レイヤーを表示", GUILayout.Width(130)))
                            {
                                SetTemplateView(templates, t);
                                for (int i = 0; i < overlay.childCount; i++) overlay.GetChild(i).gameObject.SetActive(true);
                                Selection.activeTransform = overlay;
                                MarkSceneDirty(scene);
                            }
                        }
                    }
                }
            }
            Header("再生成");
            EditorGUILayout.HelpBox("画面のコードを変更した場合や、要素が増減した場合は再生成してください。「調整を保持」は同じ位置の要素に調整値を引き継ぎます（手で追加したオブジェクトは消えます）。", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("UI を再生成（調整を保持）", GUILayout.Height(28))) EditorApplication.delayCall += ProjectSetup.RebuildUiKeep;
                if (GUILayout.Button("UI をリセット（調整を破棄）", GUILayout.Height(28))) EditorApplication.delayCall += ProjectSetup.RebuildUiReset;
            }
        }

        private static string DisplayName(Transform t)
        {
            switch (t.name)
            {
                case "TitleScreen": return "タイトル (TitleScreen)";
                case "HomeScreen": return "ホーム (HomeScreen)";
                case "StoryScreen": return "ストーリー (StoryScreen)";
                case "EndlessScreen": return "エンドレス (EndlessScreen)";
                case "FigurebookScreen": return "図鑑 (FigurebookScreen)";
                case "SettingsScreen": return "設定 (SettingsScreen)";
                case "RecordsScreen": return "記録 (RecordsScreen)";
                case "EditorMenuScreen": return "エディター入口 (EditorMenuScreen)";
                case "MyStagesScreen": return "自作ステージ (MyStagesScreen)";
                case "StageEditorScreen": return "ステージ作成 (StageEditorScreen)";
                case "SharedStagesScreen": return "共有ステージ (SharedStagesScreen)";
                case "BattleScreen": return "戦闘 (BattleScreen)";
                case "ResultScreen": return "リザルト (ResultScreen)";
                case "Modals": return "モーダル（確認ダイアログ）";
                case "Toasts": return "トースト通知";
                default: return t.name;
            }
        }

        /// <summary>指定テンプレートだけを表示（null なら Templates ごと隠す）</summary>
        private static void SetTemplateView(Transform templates, Transform show)
        {
            Undo.RegisterFullObjectHierarchyUndo(templates.gameObject, "Show UI Template");
            if (show == null)
            {
                templates.gameObject.SetActive(false);
            }
            else
            {
                templates.gameObject.SetActive(true);
                for (int g = 0; g < templates.childCount; g++)
                {
                    Transform group = templates.GetChild(g);
                    bool groupHasTarget = show == group || show.IsChildOf(group);
                    group.gameObject.SetActive(groupHasTarget);
                    if (group.name == "Screens")
                    {
                        for (int i = 0; i < group.childCount; i++) group.GetChild(i).gameObject.SetActive(group.GetChild(i) == show);
                    }
                }
            }
            MarkSceneDirty(templates.gameObject.scene);
        }

        private static void MarkSceneDirty(Scene scene)
        {
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}

/*
## このアセットの役割
メニュー「DRAW&WITCHES > Content Editor」のウィンドウ本体です。タブで「キャラクター」「弾種」「ステージ」「UI」を切り替えます。

## 他アセットとの依存関係
ContentJson（JSON 入出力）、ContentEditorWindow.Characters / Bullets / Stages（各タブ）、ProjectSetup（UI 再生成）、GameAssets。

## Inspector設定
なし。

## USERがUnity上で行う作業
各タブで編集し「保存」を押してください。保存すると Resources/DrawAndWitches の JSON が更新され、次回の再生から反映されます。

## 注意点
再生中は編集できません。保存していない変更がある状態でウィンドウを閉じると保存確認が出ます。
*/
