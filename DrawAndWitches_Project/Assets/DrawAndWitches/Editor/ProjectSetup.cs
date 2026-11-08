using System;
using System.Collections.Generic;
using System.IO;
using DrawAndWitches.App;
using DrawAndWitches.Core;
using DrawAndWitches.Data;
using DrawAndWitches.Online;
using DrawAndWitches.Presentation;
using DrawAndWitches.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrawAndWitches.EditorTools
{
    /// <summary>
    /// メニュー「DRAW&WITCHES / Setup Project」。素材のインポート設定、GameAssets の自動登録、
    /// Boot シーンの生成、ビルド設定・Player 設定を一括で行う。何度実行しても同じ結果になる（冪等）。
    /// </summary>
    public static class ProjectSetup
    {
        private const string Root = "Assets/DrawAndWitches";
        private const string ArtRoot = Root + "/Art";
        private const string AudioRoot = Root + "/Audio";
        private const string GameAssetsPath = Root + "/GameAssets.asset";
        private const string ScenePath = Root + "/Scenes/Boot.unity";
        private const string ManifestResource = "DrawAndWitches/manifest";
        private const string SkinRoot = ArtRoot + "/UI";
        private static readonly string[] SkinIds = { "rounded", "rounded_outline", "circle", "soft_circle", "fade" };

        [MenuItem("DRAW&WITCHES/Setup Project", priority = 0)]
        public static void SetupFromMenu()
        {
            try
            {
                List<string> report = Run();
                EditorUtility.DisplayDialog("DRAW&WITCHES", "セットアップが完了しました。\n\n" + string.Join("\n", report) + "\n\n再生ボタンでゲームを開始できます。", "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("DRAW&WITCHES", "セットアップに失敗しました。\n" + e.Message + "\n\n詳細は Console を確認してください。", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("DRAW&WITCHES/Open Boot Scene", priority = 1)]
        public static void OpenBootScene()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "Boot シーンがありません。先に「Setup Project」を実行してください。", "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("DRAW&WITCHES/UI/Rebuild UI Templates (keep adjustments)", priority = 40)]
        public static void RebuildUiKeep()
        {
            RebuildUiFromMenu(true);
        }

        [MenuItem("DRAW&WITCHES/UI/Reset UI Templates (discard adjustments)", priority = 41)]
        public static void RebuildUiReset()
        {
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "UI テンプレートを作り直し、シーン上で行った UI 調整をすべて破棄しますか？", "破棄して作り直す", "キャンセル")) return;
            RebuildUiFromMenu(false);
        }

        /// <summary>Boot シーンを開いて UI テンプレートだけを作り直す（Content Editor からも呼ばれる）</summary>
        public static void RebuildUiFromMenu(bool keepAdjustments)
        {
            try
            {
                if (!File.Exists(ScenePath))
                {
                    EditorUtility.DisplayDialog("DRAW&WITCHES", "Boot シーンがありません。先に「Setup Project」を実行してください。", "OK");
                    return;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameApp app = FindInScene<GameApp>(scene);
                if (app == null) throw new InvalidOperationException("Boot シーンに GameApp がありません。Setup Project を実行してください。");
                EditorUtility.DisplayProgressBar("DRAW&WITCHES", "UI テンプレートを生成…", 0.5f);
                string msg = BakeUi(app, scene, keepAdjustments);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("シーンを保存できませんでした: " + ScenePath);
                EditorUtility.DisplayDialog("DRAW&WITCHES", msg, "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("DRAW&WITCHES", "UI の生成に失敗しました。\n" + e.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("DRAW&WITCHES/Open Save Folder", priority = 20)]
        public static void OpenSaveFolder()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Stages");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("DRAW&WITCHES/Delete Save Data (PlayerPrefs)", priority = 21)]
        public static void DeleteSaveData()
        {
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "このプロジェクトの保存データ（進行・設定・リプレイ・自作ステージ）をすべて削除しますか？", "削除", "キャンセル")) return;
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[Setup] PlayerPrefs cleared");
        }

        /// <summary>セットアップ本体（バッチモードからも呼べる: -executeMethod DrawAndWitches.EditorTools.ProjectSetup.RunBatch）</summary>
        public static List<string> Run()
        {
            List<string> report = new List<string>();
            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "画像のインポート設定…", 0.1f);
            int images = ConfigureTextures();
            report.Add("画像: " + images + " 件をスプライト設定");
            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "音声のインポート設定…", 0.3f);
            int sounds = ConfigureAudio();
            report.Add("音声: " + sounds + " 件を設定");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "GameAssets を作成…", 0.5f);
            GameAssets ga = BuildGameAssets(report);
            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "UI スキン画像を作成…", 0.55f);
            report.Add("UI スキン: " + EnsureSkins(ga) + " 件");
            report.Add("キャラクター表現スロット: " + EnsureCharacterVisuals(ga) + " 件");

            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "Boot シーンと UI を作成…", 0.7f);
            report.Add(BuildBootScene(ga));
            report.Add("シーン: " + ScenePath);

            EditorUtility.DisplayProgressBar("DRAW&WITCHES", "ビルド設定…", 0.9f);
            ConfigureBuild();
            report.Add("ビルド設定: Boot シーンを登録、1920×1080 / ウィンドウ可変");
            if (EnsureLegacyInputEnabled()) report.Add("入力方式: 「Both」に変更しました。Unity を再起動してください。");
            AssetDatabase.SaveAssets();
            return report;
        }

        public static void RunBatch()
        {
            try
            {
                foreach (string line in Run()) Debug.Log("[Setup] " + line);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ インポート設定

        private static int ConfigureTextures()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                bool background = path.Contains("/Backgrounds/");
                bool changed = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single || ti.mipmapEnabled
                    || !ti.alphaIsTransparency || ti.maxTextureSize != 2048 || ti.npotScale != TextureImporterNPOTScale.None;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = !background || ti.DoesSourceTextureHaveAlpha();
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                if (changed) ti.SaveAndReimport();
                n++;
            }
            return n;
        }

        private static int ConfigureAudio()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioImporter ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai == null) continue;
                bool bgm = path.Contains("/BGM/");
                AudioImporterSampleSettings ss = ai.defaultSampleSettings;
                ss.loadType = bgm ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                ss.compressionFormat = bgm ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
                ss.quality = bgm ? 0.7f : 1f;
                ai.defaultSampleSettings = ss;
                ai.forceToMono = !bgm;
                ai.loadInBackground = bgm;
                ai.SaveAndReimport();
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ GameAssets

        /// <summary>manifest.json のキー → Art 内の PNG（拡張子違いのファイル名で対応付け）</summary>
        private static GameAssets BuildGameAssets(List<string> report)
        {
            GameAssets ga = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, GameAssetsPath);
            }
            TextAsset manifest = Resources.Load<TextAsset>(ManifestResource);
            if (manifest == null) throw new InvalidOperationException("Resources/" + ManifestResource + ".json が見つかりません");
            if (!MiniJson.TryParse(manifest.text, out object root, out string err)) throw new InvalidOperationException("manifest.json の解析に失敗: " + err);
            Dictionary<string, object> images = MiniJson.AsObject(MiniJson.Get(MiniJson.AsObject(root), "images"));
            if (images == null) throw new InvalidOperationException("manifest.json に images がありません");

            Dictionary<string, Sprite> byName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null) byName[Path.GetFileNameWithoutExtension(path)] = s;
            }
            // 既存の手動差し替えは保持する（キーが一致し Sprite が設定済みなら上書きしない）
            Dictionary<string, Sprite> current = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (SpriteEntry e in ga.sprites)
            {
                if (e != null && !string.IsNullOrEmpty(e.key) && e.sprite != null) current[e.key] = e.sprite;
            }
            List<SpriteEntry> sprites = new List<SpriteEntry>();
            List<string> missing = new List<string>();
            foreach (KeyValuePair<string, object> kv in images)
            {
                string file = Path.GetFileNameWithoutExtension(kv.Value as string ?? string.Empty);
                if (current.TryGetValue(kv.Key, out Sprite keep))
                {
                    sprites.Add(new SpriteEntry { key = kv.Key, sprite = keep });
                }
                else if (byName.TryGetValue(file, out Sprite s))
                {
                    sprites.Add(new SpriteEntry { key = kv.Key, sprite = s });
                }
                else
                {
                    missing.Add(kv.Key);
                }
            }
            Dictionary<string, AudioClip> curAudio = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            foreach (AudioEntry e in ga.audio)
            {
                if (e != null && !string.IsNullOrEmpty(e.key) && e.clip != null) curAudio[e.key] = e.clip;
            }
            List<AudioEntry> audio = new List<AudioEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string key = Path.GetFileNameWithoutExtension(path);
                AudioClip c = curAudio.TryGetValue(key, out AudioClip keep) ? keep : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (c != null) audio.Add(new AudioEntry { key = key, clip = c });
            }
            audio.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            ga.sprites = sprites;
            ga.audio = audio;
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            report.Add("GameAssets: 画像 " + sprites.Count + " / 音声 " + audio.Count + " 件を登録");
            if (missing.Count > 0)
            {
                report.Add("未登録の画像キー: " + string.Join(", ", missing));
                Debug.LogWarning("[Setup] sprites not found for keys: " + string.Join(", ", missing));
            }
            if (ga.font == null) report.Add("フォント: 未設定（OS の日本語フォントを自動使用。同梱したい場合は GameAssets の Font に設定）");
            return ga;
        }

        // ------------------------------------------------------------------ シーン

        /// <summary>
        /// Boot シーンを用意して UI を焼き込む。既存シーンがあれば開いて再利用し（GameApp・Online の Inspector 設定を保持）、
        /// UI テンプレートは調整内容を引き継いで作り直す。
        /// </summary>
        private static string BuildBootScene(GameAssets ga)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) throw new OperationCanceledException("シーンの保存がキャンセルされました");
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (File.Exists(ScenePath))
            {
                Scene existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameApp found = FindInScene<GameApp>(existing);
                if (found != null)
                {
                    SetAssets(found, ga);
                    if (FindInScene<HierarchicalDataManager>(existing) == null) CreateOnline(string.Empty);
                    string r = BakeUi(found, existing, true);
                    if (!EditorSceneManager.SaveScene(existing, ScenePath)) throw new IOException("シーンを保存できませんでした: " + ScenePath);
                    return r;
                }
                Debug.LogWarning("[Setup] Boot シーンに GameApp が無いため作り直します");
            }
            string gasUrl = ReadExistingGasUrl();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cam = new GameObject("Main Camera");
            Camera c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color32(0x0b, 0x08, 0x20, 0xff);
            c.orthographic = true;
            c.cullingMask = 0; // UI は Screen Space Overlay のため描画対象なし
            cam.tag = "MainCamera";
            cam.AddComponent<AudioListener>();

            CreateOnline(gasUrl);

            GameObject app = new GameObject("GameApp");
            GameApp ga2 = app.AddComponent<GameApp>();
            SetAssets(ga2, ga);
            string result = BakeUi(ga2, scene, false);

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("シーンを保存できませんでした: " + ScenePath);
            return result;
        }

        private static void CreateOnline(string gasUrl)
        {
            GameObject online = new GameObject("Online");
            HierarchicalDataManager hdm = online.AddComponent<HierarchicalDataManager>();
            online.AddComponent<TweetManager>();
            if (string.IsNullOrEmpty(gasUrl)) return;
            SerializedObject so = new SerializedObject(hdm);
            SerializedProperty p = so.FindProperty("GAS_URL");
            if (p == null) return;
            p.stringValue = gasUrl;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetAssets(GameApp app, GameAssets ga)
        {
            SerializedObject aso = new SerializedObject(app);
            SerializedProperty ap = aso.FindProperty("assets");
            if (ap == null) throw new InvalidOperationException("GameApp.assets フィールドが見つかりません");
            ap.objectReferenceValue = ga;
            aso.ApplyModifiedPropertiesWithoutUndo();
        }

        public static T FindInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid()) return null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                T c = go.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        // ------------------------------------------------------------------ UI 焼き込み

        /// <summary>シーン上の UI 調整 1 要素分（引き継ぎ用）</summary>
        private sealed class Adjustment
        {
            public string Kind;
            public UiSnap Scene;
            public ulong Mask;
            public bool ForceHidden;
            public bool IgnoreOverrides;
        }

        /// <summary>GameApp の UI テンプレートを生成する。keepAdjustments なら以前の調整を同じ位置の要素へ引き継ぐ。</summary>
        public static string BakeUi(GameApp app, Scene scene, bool keepAdjustments)
        {
            Dictionary<string, Adjustment> carry = keepAdjustments && app.Templates != null ? CollectAdjustments(app.Templates) : new Dictionary<string, Adjustment>();
            int count = app.EditorBakeScene(FindInScene<HierarchicalDataManager>(scene));
            int applied = ApplyAdjustments(app.Templates, carry);
            EditorUtility.SetDirty(app);
            EditorSceneManager.MarkSceneDirty(scene);
            string msg = "UI テンプレート: " + count + " 要素を生成（Canvas/Templates）";
            if (carry.Count > 0) msg += "、調整の引き継ぎ " + applied + "/" + carry.Count + " 件";
            return msg;
        }

        private static string PathOf(Transform t, Transform root)
        {
            List<string> parts = new List<string>();
            while (t != null && t != root)
            {
                Transform parent = t.parent;
                int k = 0;
                if (parent != null)
                {
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        Transform c = parent.GetChild(i);
                        if (c == t) break;
                        if (c.name == t.name) k++;
                    }
                }
                parts.Add(t.name + "#" + k);
                t = parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Dictionary<string, Adjustment> CollectAdjustments(Transform templates)
        {
            Dictionary<string, Adjustment> map = new Dictionary<string, Adjustment>(StringComparer.Ordinal);
            foreach (UiBaked b in templates.GetComponentsInChildren<UiBaked>(true))
            {
                UiSnap now = UiSnap.Capture(b.gameObject);
                ulong mask = now.DiffMask(b.reference);
                if (mask == 0 && !b.forceHidden && !b.ignoreSceneOverrides) continue;
                map[PathOf(b.transform, templates)] = new Adjustment { Kind = b.kind, Scene = now, Mask = mask, ForceHidden = b.forceHidden, IgnoreOverrides = b.ignoreSceneOverrides };
            }
            return map;
        }

        private static int ApplyAdjustments(Transform templates, Dictionary<string, Adjustment> carry)
        {
            if (templates == null || carry.Count == 0) return 0;
            int n = 0;
            foreach (UiBaked b in templates.GetComponentsInChildren<UiBaked>(true))
            {
                if (!carry.TryGetValue(PathOf(b.transform, templates), out Adjustment a) || a.Kind != b.kind) continue;
                a.Scene.Apply(b.gameObject, a.Mask);
                b.forceHidden = a.ForceHidden;
                b.ignoreSceneOverrides = a.IgnoreOverrides;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ UI スキン・キャラクター表現

        /// <summary>UI スキン PNG を（無ければ）生成し、9 スライス設定で取り込んで GameAssets に登録する</summary>
        private static int EnsureSkins(GameAssets ga)
        {
            Directory.CreateDirectory(SkinRoot);
            int n = 0;
            foreach (string id in SkinIds)
            {
                string path = SkinRoot + "/dw_" + id + ".png";
                if (!File.Exists(path))
                {
                    Texture2D tex = UiKit.CreateSkinTexture(id);
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.mipmapEnabled = false;
                    ti.alphaIsTransparency = true;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    Vector4 border = UiKit.SkinBorder(id);
                    if (ti.spriteBorder == Vector4.zero && border != Vector4.zero) ti.spriteBorder = border;
                    ti.SaveAndReimport();
                }
                Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp == null) continue;
                n++;
                switch (id)
                {
                    case "rounded": if (ga.uiRounded == null) ga.uiRounded = sp; break;
                    case "rounded_outline": if (ga.uiRoundedOutline == null) ga.uiRoundedOutline = sp; break;
                    case "circle": if (ga.uiCircle == null) ga.uiCircle = sp; break;
                    case "soft_circle": if (ga.uiSoftCircle == null) ga.uiSoftCircle = sp; break;
                    default: if (ga.uiVerticalFade == null) ga.uiVerticalFade = sp; break;
                }
            }
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            return n;
        }

        /// <summary>全キャラクターの表現スロット（差分立ち絵・Animator 用、空のまま）を用意する。既存設定は保持。</summary>
        public static int EnsureCharacterVisuals(GameAssets ga)
        {
            GameDatabase db = GameDatabase.Load();
            List<CharacterProfile> all = new List<CharacterProfile> { db.Player };
            all.AddRange(db.Enemies);
            all.Add(db.EditorEnemy);
            if (ga.characters == null) ga.characters = new List<CharacterVisual>();
            foreach (CharacterProfile c in all)
            {
                if (ga.Visual(c.Id) == null) ga.characters.Add(new CharacterVisual { characterId = c.Id });
            }
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            return ga.characters.Count;
        }

        /// <summary>既存 Boot シーンに入力済みの GAS URL を引き継ぐ（再セットアップで消えないように）</summary>
        private static string ReadExistingGasUrl()
        {
            if (!File.Exists(ScenePath)) return string.Empty;
            try
            {
                foreach (string line in File.ReadAllLines(ScenePath))
                {
                    string t = line.Trim();
                    if (t.StartsWith("GAS_URL:", StringComparison.Ordinal)) return t.Substring("GAS_URL:".Length).Trim();
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Setup] cannot read existing scene: " + e.Message);
            }
            return string.Empty;
        }

        // ------------------------------------------------------------------ ビルド設定

        /// <summary>
        /// 本作は UnityEngine.Input（旧入力）と StandaloneInputModule を使う。Active Input Handling が
        /// 「Input System Package (New)」のみの場合は「Both」へ切り替える。変更したら true（再起動が必要）。
        /// </summary>
        private static bool EnsureLegacyInputEnabled()
        {
            UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (objs == null || objs.Length == 0)
            {
                Debug.LogWarning("[Setup] ProjectSettings.asset を読み込めません。Player 設定の Active Input Handling を確認してください。");
                return false;
            }
            SerializedObject so = new SerializedObject(objs[0]);
            SerializedProperty p = so.FindProperty("activeInputHandler");
            if (p == null || p.intValue != 1) return false;
            p.intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return true;
        }

        private static void ConfigureBuild()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = GameConstants.GameTitle;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
        }
    }
}

/*
## このアセットの役割
Unity エディターのメニュー「DRAW&WITCHES」です。Setup Project を実行すると、画像をスプライトとして設定し、
音声の読み込み方式（BGM はストリーミング、効果音はメモリ展開）を設定し、GameAssets（素材参照表）を作成・自動登録し、
Boot シーン（カメラ・GameApp・Online）を生成し、全画面の UI を Canvas/Templates 以下へ焼き込んでビルド設定に登録します。

## 他アセットとの依存関係
GameApp / GameAssets / HierarchicalDataManager / TweetManager / MiniJson / Resources/DrawAndWitches/manifest.json。
Editor フォルダ内にあるため、ビルドには含まれません。

## Inspector設定
なし（実行結果として GameAssets.asset と Scenes/Boot.unity が作られます）。

## USERがUnity上で行う作業
プロジェクトを開いたら、メニュー「DRAW&WITCHES > Setup Project」を1回実行してください。
画像や音声を差し替えた後に再実行しても、Inspector で手動差し替えした参照と、入力済みの GAS URL は保持されます。

## 注意点
実行時に未保存のシーンがあると保存確認が出ます。2 回目以降は既存の Boot シーンを開いて再利用し、UI テンプレートは「同じ位置の要素」へ調整（位置・サイズ・色・画像・フォント・レイアウト・Force Hidden）を引き継いで作り直します。
テンプレート内に手で追加したオブジェクトやコンポーネントは作り直しで消えるため、装飾を追加したい場合は Canvas 直下など Templates の外に置いてください。
メニュー「DRAW&WITCHES > UI」から、UI テンプレートだけの作り直し（調整を保持 / 破棄）もできます。
*/
