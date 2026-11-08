using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DrawAndWitches.Presentation;
using UnityEditor;
using UnityEngine;

namespace DrawAndWitches.EditorTools
{
    public sealed partial class ContentEditorWindow
    {
        private const string CharactersPath = ContentJson.ResDir + "/characters.json";
        private static readonly Regex CharIdPattern = new Regex("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant);
        private static readonly Regex AssetKeyPattern = new Regex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant);

        /// <summary>編集中のキャラクター 1 人分（元 JSON を直接編集し、未知のキーも保持）</summary>
        private sealed class CharEntry
        {
            public string Role; // player / enemy / editor
            public string OriginalId;
            public Dictionary<string, object> Data;
        }

        private Dictionary<string, object> charRoot;
        private List<CharEntry> chars = new List<CharEntry>();
        private int charSel;
        private bool charDirty;
        private Vector2 charScroll;
        private string newExpression = "talk";

        private void LoadCharacters()
        {
            charRoot = ContentJson.ReadObject(CharactersPath);
            chars = new List<CharEntry>();
            Dictionary<string, object> player = charRoot.TryGetValue("player", out object p) ? p as Dictionary<string, object> : null;
            if (player == null) throw new InvalidOperationException("characters.json に player がありません");
            chars.Add(new CharEntry { Role = "player", OriginalId = ContentJson.Str(player, "id"), Data = player });
            foreach (object e in ContentJson.List(charRoot, "enemies"))
            {
                if (e is Dictionary<string, object> d) chars.Add(new CharEntry { Role = "enemy", OriginalId = ContentJson.Str(d, "id"), Data = d });
            }
            if (charRoot.TryGetValue("editorEnemy", out object ed) && ed is Dictionary<string, object> edd)
            {
                chars.Add(new CharEntry { Role = "editor", OriginalId = ContentJson.Str(edd, "id"), Data = edd });
            }
            charSel = Mathf.Clamp(charSel, 0, chars.Count - 1);
            charDirty = false;
        }

        private List<string> EnemyIds()
        {
            List<string> ids = new List<string>();
            foreach (CharEntry c in chars)
            {
                if (c.Role != "player") ids.Add(ContentJson.Str(c.Data, "id"));
            }
            return ids;
        }

        private void DrawCharactersTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    List<string> labels = new List<string>();
                    foreach (CharEntry c in chars)
                    {
                        string role = c.Role == "player" ? "[主人公] " : c.Role == "editor" ? "[エディター] " : "[" + ContentJson.Int(c.Data, "rank", 0) + "] ";
                        labels.Add(role + ContentJson.LocJa(c.Data, "name") + " (" + ContentJson.Str(c.Data, "id") + ")");
                    }
                    charSel = DrawList(labels, charSel, 230);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("敵を追加")) AddEnemy(null);
                        GUI.enabled = chars[charSel].Role == "enemy";
                        if (GUILayout.Button("複製")) AddEnemy(chars[charSel]);
                        if (GUILayout.Button("削除")) DeleteEnemy(charSel);
                        GUI.enabled = true;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = chars[charSel].Role == "enemy";
                        if (GUILayout.Button("↑")) MoveEnemy(charSel, -1);
                        if (GUILayout.Button("↓")) MoveEnemy(charSel, 1);
                        GUI.enabled = true;
                    }
                    EditorGUILayout.Space(8);
                    GUI.backgroundColor = charDirty ? new Color(1f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(charDirty ? "保存（未保存の変更あり）" : "保存", GUILayout.Height(32))) SaveCharacters();
                    GUI.backgroundColor = Color.white;
                }
                using (EditorGUILayout.ScrollViewScope sv = new EditorGUILayout.ScrollViewScope(charScroll))
                {
                    charScroll = sv.scrollPosition;
                    DrawCharacterDetail(chars[charSel]);
                }
            }
        }

        private void DrawCharacterDetail(CharEntry c)
        {
            Dictionary<string, object> d = c.Data;
            EditorGUI.BeginChangeCheck();
            Header("基本");
            string id = EditorGUILayout.DelayedTextField(new GUIContent("ID", "英小文字で始まる英小文字・数字・_（ステージの enemyId から参照されます）"), ContentJson.Str(d, "id"));
            if (id != ContentJson.Str(d, "id"))
            {
                if (!CharIdPattern.IsMatch(id)) ShowNotification(new GUIContent("ID は英小文字で始まる英小文字・数字・_ の 1〜32 文字です"));
                else d["id"] = id;
            }
            if (c.Role == "enemy")
            {
                d["rank"] = (double)EditorGUILayout.IntSlider(new GUIContent("序列", "1 が最強。図鑑の並び順にも使われます"), ContentJson.Int(d, "rank", 10), 1, 99);
            }
            Color col = ContentJson.FromHex(ContentJson.Str(d, "color"), Color.white);
            Color ncol = EditorGUILayout.ColorField("テーマ色", col);
            if (ncol != col) d["color"] = ContentJson.ToHex(ncol);
            if (EditorGUI.EndChangeCheck()) charDirty = true;

            Header("テキスト");
            LangSelector();
            bool t = LocField("名前", d, "name", false, 24);
            t |= LocField("肩書き", d, "title", false, 32);
            t |= LocField("紹介文", d, "intro", true, 200);
            if (c.Role != "player")
            {
                t |= LocField("スペル名", d, "spell", false, 40);
                t |= LocField("スペル解説", d, "spellDetail", true, 200);
            }
            if (t) charDirty = true;

            Header("画像・音声（GameAssets に登録）");
            KeyedSprite("立ち絵キー", d, "portrait", "char_");
            KeyedSprite("背景キー", d, "background", "bg_");
            if (c.Role != "player") KeyedClip("戦闘 BGM キー", d, "bgm", "bgm_battle_");

            DrawVisuals(ContentJson.Str(d, "id"), ContentJson.Str(d, "portrait"));
        }

        /// <summary>キー（文字列）と、そのキーに割り当てる Sprite</summary>
        private void KeyedSprite(string label, Dictionary<string, object> d, string field, string prefix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string key = EditorGUILayout.DelayedTextField(label, ContentJson.Str(d, field));
                if (key != ContentJson.Str(d, field))
                {
                    if (!AssetKeyPattern.IsMatch(key)) ShowNotification(new GUIContent("キーは英小文字・数字・_ です（例: " + prefix + "xxx）"));
                    else
                    {
                        d[field] = key;
                        charDirty = true;
                    }
                }
                if (assets != null)
                {
                    string k = ContentJson.Str(d, field);
                    Sprite cur = assets.PeekSprite(k);
                    Sprite next = (Sprite)EditorGUILayout.ObjectField(cur, typeof(Sprite), false, GUILayout.Width(200));
                    if (next != cur && !string.IsNullOrEmpty(k))
                    {
                        Undo.RecordObject(assets, "Assign Sprite");
                        assets.SetSprite(k, next);
                        MarkAssetsDirty();
                    }
                }
            }
        }

        private void KeyedClip(string label, Dictionary<string, object> d, string field, string prefix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string key = EditorGUILayout.DelayedTextField(label, ContentJson.Str(d, field));
                if (key != ContentJson.Str(d, field))
                {
                    if (key.Length > 0 && !AssetKeyPattern.IsMatch(key)) ShowNotification(new GUIContent("キーは英小文字・数字・_ です（例: " + prefix + "xxx）"));
                    else
                    {
                        if (key.Length == 0) d.Remove(field);
                        else d[field] = key;
                        charDirty = true;
                    }
                }
                if (assets != null)
                {
                    string k = ContentJson.Str(d, field);
                    AudioClip cur = assets.PeekClip(k);
                    AudioClip next = (AudioClip)EditorGUILayout.ObjectField(cur, typeof(AudioClip), false, GUILayout.Width(200));
                    if (next != cur && !string.IsNullOrEmpty(k))
                    {
                        Undo.RecordObject(assets, "Assign Clip");
                        assets.SetClip(k, next);
                        MarkAssetsDirty();
                    }
                }
            }
            if (string.IsNullOrEmpty(ContentJson.Str(d, field))) EditorGUILayout.HelpBox("BGM 未指定の場合は手続き生成の汎用戦闘曲が流れます。", MessageType.None);
        }

        /// <summary>差分立ち絵・アニメーター（GameAssets.characters）。即時に GameAssets へ保存される。</summary>
        private void DrawVisuals(string charId, string portraitKey)
        {
            Header("差分立ち絵・アニメーター（任意）");
            if (assets == null) return;
            CharacterVisual v = assets.Visual(charId);
            if (v == null)
            {
                EditorGUILayout.HelpBox("このキャラクターの表現スロットがありません（未設定ならデフォルト立ち絵を表示します）。", MessageType.None);
                if (GUILayout.Button("表現スロットを作成"))
                {
                    Undo.RecordObject(assets, "Add Character Visual");
                    assets.characters.Add(new CharacterVisual { characterId = charId });
                    MarkAssetsDirty();
                }
                return;
            }
            EditorGUILayout.HelpBox("差分やアニメーターを設定しない場合は、これまで通りデフォルト立ち絵が表示されます。\n" +
                "ゲームが自動で使う表情: default（通常）/ talk（会話で発言中）/ damage（被弾）/ spell（カットイン）/ win（リザルト）。会話データの face で任意の名前も使えます。\n" +
                "Animator には表情名と同名の Trigger、および bool「Talking」があれば自動で操作します。", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    Sprite portrait = (Sprite)EditorGUILayout.ObjectField(new GUIContent("デフォルト立ち絵の上書き", "未設定なら立ち絵キーの画像"), v.portrait, typeof(Sprite), false);
                    RuntimeAnimatorController anim = (RuntimeAnimatorController)EditorGUILayout.ObjectField("Animator Controller", v.animator, typeof(RuntimeAnimatorController), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(assets, "Edit Character Visual");
                        v.portrait = portrait;
                        v.animator = anim;
                        MarkAssetsDirty();
                    }
                    if (v.animator == null && GUILayout.Button("表情 Trigger 入りの Animator Controller を作成"))
                    {
                        CreateAnimatorFor(v);
                    }
                }
                DrawSpritePreview(v.portrait != null ? v.portrait : assets.PeekSprite(portraitKey), 120);
            }
            EditorGUILayout.LabelField("表情差分", EditorStyles.miniBoldLabel);
            int remove = -1;
            for (int i = 0; i < v.expressions.Count; i++)
            {
                ExpressionEntry e = v.expressions[i];
                if (e == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField(e.expression, GUILayout.Width(140));
                    Sprite sp = (Sprite)EditorGUILayout.ObjectField(e.sprite, typeof(Sprite), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(assets, "Edit Expression");
                        e.expression = string.IsNullOrWhiteSpace(name) ? e.expression : name.Trim();
                        e.sprite = sp;
                        MarkAssetsDirty();
                    }
                    if (GUILayout.Button("×", GUILayout.Width(24))) remove = i;
                }
            }
            if (remove >= 0)
            {
                Undo.RecordObject(assets, "Remove Expression");
                v.expressions.RemoveAt(remove);
                MarkAssetsDirty();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                int pi = Array.IndexOf(CharacterPortrait.BuiltinExpressions, newExpression);
                int npi = EditorGUILayout.Popup(pi < 0 ? 0 : pi, CharacterPortrait.BuiltinExpressions, GUILayout.Width(100));
                if (npi != pi) newExpression = CharacterPortrait.BuiltinExpressions[npi];
                newExpression = EditorGUILayout.TextField(newExpression, GUILayout.Width(120));
                if (GUILayout.Button("表情を追加", GUILayout.Width(100)))
                {
                    string n = (newExpression ?? string.Empty).Trim();
                    if (n.Length == 0 || n.Length > 32) ShowNotification(new GUIContent("表情名は 1〜32 文字です"));
                    else if (v.expressions.Exists(x => x != null && x.expression == n)) ShowNotification(new GUIContent("同じ表情名があります: " + n));
                    else
                    {
                        Undo.RecordObject(assets, "Add Expression");
                        v.expressions.Add(new ExpressionEntry { expression = n });
                        MarkAssetsDirty();
                    }
                }
            }
        }

        /// <summary>表情名 Trigger と Talking を持つ Animator Controller を作る（状態・クリップはユーザーが追加）</summary>
        private void CreateAnimatorFor(CharacterVisual v)
        {
            const string Dir = "Assets/DrawAndWitches/Art/Animators";
            try
            {
                Directory.CreateDirectory(Dir);
                string path = AssetDatabase.GenerateUniqueAssetPath(Dir + "/" + v.characterId + "_portrait.controller");
                UnityEditor.Animations.AnimatorController ac = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
                foreach (string ex in CharacterPortrait.BuiltinExpressions) ac.AddParameter(ex, AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Talking", AnimatorControllerParameterType.Bool);
                AssetDatabase.SaveAssets();
                Undo.RecordObject(assets, "Assign Animator");
                v.animator = ac;
                MarkAssetsDirty();
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(ac);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("DRAW&WITCHES", "Animator Controller の作成に失敗しました。\n" + e.Message, "OK");
            }
        }

        private void AddEnemy(CharEntry source)
        {
            HashSet<string> ids = new HashSet<string>(EnemyIds(), StringComparer.Ordinal);
            ids.Add(ContentJson.Str(chars[0].Data, "id"));
            int n = 1;
            string id;
            do
            {
                id = "witch_" + n++;
            }
            while (ids.Contains(id));
            Dictionary<string, object> d;
            if (source != null)
            {
                d = (Dictionary<string, object>)ContentJson.DeepCopy(source.Data);
                d["id"] = id;
            }
            else
            {
                d = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = id,
                    ["rank"] = 11.0,
                    ["color"] = "#c8a0ff",
                    ["portrait"] = "char_" + id,
                    ["background"] = "bg_title",
                    ["name"] = ContentJson.NewLoc("新しい魔女"),
                    ["title"] = ContentJson.NewLoc(""),
                    ["intro"] = ContentJson.NewLoc(""),
                    ["spell"] = ContentJson.NewLoc(""),
                    ["spellDetail"] = ContentJson.NewLoc(""),
                };
            }
            int insertAt = chars.FindLastIndex(x => x.Role == "enemy") + 1;
            if (insertAt <= 0) insertAt = 1;
            chars.Insert(insertAt, new CharEntry { Role = "enemy", OriginalId = null, Data = d });
            charSel = insertAt;
            charDirty = true;
            GUI.FocusControl(null);
        }

        private void DeleteEnemy(int index)
        {
            CharEntry c = chars[index];
            string id = ContentJson.Str(c.Data, "id");
            List<string> used = StagesUsingEnemy(c.OriginalId ?? id);
            if (used.Count > 0)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "このキャラクターは次のステージで使われているため削除できません:\n" + string.Join(", ", used), "OK");
                return;
            }
            if (chars.FindAll(x => x.Role == "enemy").Count <= 1)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "敵キャラクターは 1 人以上必要です。", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "「" + ContentJson.LocJa(c.Data, "name") + "」を削除しますか？（保存するまでファイルは変わりません）", "削除", "キャンセル")) return;
            chars.RemoveAt(index);
            charSel = Mathf.Clamp(index - 1, 0, chars.Count - 1);
            charDirty = true;
        }

        private void MoveEnemy(int index, int dir)
        {
            int to = index + dir;
            if (to < 0 || to >= chars.Count || chars[to].Role != "enemy") return;
            CharEntry tmp = chars[to];
            chars[to] = chars[index];
            chars[index] = tmp;
            charSel = to;
            charDirty = true;
        }

        /// <summary>検証して characters.json を保存。ID 変更は参照ステージと表現スロットへ反映する。</summary>
        private void SaveCharacters()
        {
            GUI.FocusControl(null);
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharEntry c in chars)
            {
                string id = ContentJson.Str(c.Data, "id");
                if (!CharIdPattern.IsMatch(id)) errors.Add("ID が不正です: '" + id + "'");
                if (!ids.Add(id)) errors.Add("ID が重複しています: " + id);
                if (string.IsNullOrWhiteSpace(ContentJson.LocJa(c.Data, "name"))) errors.Add(id + ": 名前（日本語）が空です");
                if (!AssetKeyPattern.IsMatch(ContentJson.Str(c.Data, "portrait"))) errors.Add(id + ": 立ち絵キーが不正です");
                if (!AssetKeyPattern.IsMatch(ContentJson.Str(c.Data, "background"))) errors.Add(id + ": 背景キーが不正です");
            }
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "保存できません:\n" + string.Join("\n", errors), "OK");
                return;
            }
            // ID 変更：ステージの enemyId と表現スロットを追従させる
            Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CharEntry c in chars)
            {
                string id = ContentJson.Str(c.Data, "id");
                if (c.OriginalId != null && c.OriginalId != id) renames[c.OriginalId] = id;
            }
            try
            {
                List<object> enemies = new List<object>();
                foreach (CharEntry c in chars)
                {
                    if (c.Role == "player") charRoot["player"] = c.Data;
                    else if (c.Role == "editor") charRoot["editorEnemy"] = c.Data;
                    else enemies.Add(c.Data);
                }
                charRoot["enemies"] = enemies;
                ContentJson.Write(CharactersPath, charRoot);
                if (renames.Count > 0)
                {
                    int changed = ApplyEnemyRenames(renames);
                    if (assets != null)
                    {
                        Undo.RecordObject(assets, "Rename Character");
                        foreach (CharacterVisual v in assets.characters)
                        {
                            if (v != null && v.characterId != null && renames.TryGetValue(v.characterId, out string nid)) v.characterId = nid;
                        }
                        MarkAssetsDirty();
                    }
                    Debug.Log("[ContentEditor] renamed characters; stages updated: " + changed);
                }
                if (assets != null)
                {
                    ProjectSetup.EnsureCharacterVisuals(assets);
                }
                AssetDatabase.SaveAssets();
                foreach (CharEntry c in chars) c.OriginalId = ContentJson.Str(c.Data, "id");
                charDirty = false;
                ShowNotification(new GUIContent("characters.json を保存しました"));
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
Content Editor の「キャラクター」タブです。主人公・敵魔女・エディター用の敵の追加 / 複製 / 削除 / 並べ替え、
名前などの多言語テキスト、テーマ色、立ち絵・背景・BGM の割り当て、差分立ち絵と Animator の設定を行います。

## 他アセットとの依存関係
characters.json、GameAssets（画像・音声・キャラクター表現）、ステージタブ（ID 変更時の enemyId 追従・使用中チェック）。

## Inspector設定
なし。

## USERがUnity上で行う作業
新しい魔女を追加したら、立ち絵キー（例: char_witch_1）に画像を割り当て、ステージタブでその魔女のステージを作ってください。

## 注意点
テキストと ID は「保存」で characters.json に書き込まれます。画像・音声・差分立ち絵・Animator の割り当ては GameAssets に即時反映されます（Undo 可）。
使用中のステージがあるキャラクターは削除できません。ID を変えると参照しているステージの enemyId も自動で書き換わります。
*/
