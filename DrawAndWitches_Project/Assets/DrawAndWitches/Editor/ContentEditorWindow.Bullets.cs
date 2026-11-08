using System;
using System.Collections.Generic;
using DrawAndWitches.Logic;
using UnityEditor;
using UnityEngine;

namespace DrawAndWitches.EditorTools
{
    public sealed partial class ContentEditorWindow
    {
        private const string BulletsPath = ContentJson.ResDir + "/bullets.json";

        private sealed class BulletEntry
        {
            public string OriginalId;
            public Dictionary<string, object> Data;
        }

        private List<BulletEntry> bullets = new List<BulletEntry>();
        private int bulletSel;
        private bool bulletDirty;
        private bool bulletsCustom;
        private Vector2 bulletScroll;

        private void LoadBullets()
        {
            bullets = new List<BulletEntry>();
            bulletsCustom = System.IO.File.Exists(BulletsPath);
            if (bulletsCustom)
            {
                Dictionary<string, object> root = ContentJson.ReadObject(BulletsPath);
                foreach (object o in ContentJson.List(root, "types"))
                {
                    if (o is Dictionary<string, object> d) bullets.Add(new BulletEntry { OriginalId = ContentJson.Str(d, "id"), Data = d });
                }
            }
            if (bullets.Count == 0)
            {
                BulletTypeDef[] builtin = { BulletTypeDef.Normal, BulletTypeDef.Straight, BulletTypeDef.Giant };
                string[][] names =
                {
                    new[] { "通常弾", "Normal", "普通弹", "일반탄" },
                    new[] { "直進弾", "Straight", "直进弹", "직진탄" },
                    new[] { "巨弾", "Giant", "巨弹", "거대탄" },
                };
                for (int i = 0; i < builtin.Length; i++)
                {
                    Dictionary<string, object> nm = new Dictionary<string, object>(StringComparer.Ordinal);
                    for (int l = 0; l < ContentJson.Languages.Length; l++) nm[ContentJson.Languages[l]] = names[i][l];
                    bullets.Add(new BulletEntry
                    {
                        OriginalId = builtin[i].Id,
                        Data = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["id"] = builtin[i].Id,
                            ["name"] = nm,
                            ["speed"] = builtin[i].Speed,
                            ["radius"] = (double)builtin[i].Radius,
                            ["damage"] = (double)builtin[i].Damage,
                            ["smooth"] = builtin[i].Smooth,
                        },
                    });
                }
            }
            bulletSel = Mathf.Clamp(bulletSel, 0, bullets.Count - 1);
            bulletDirty = false;
            if (bulletsCustom)
            {
                // エディター上の検証（ステージタブ）でも bullets.json の弾種を使えるように登録する
                try
                {
                    List<BulletTypeDef> defs = new List<BulletTypeDef>();
                    foreach (BulletEntry b in bullets) defs.Add(ToDef(b.Data));
                    BulletTypeDef.Register(defs);
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogWarning("[ContentEditor] bullets.json is invalid: " + e.Message);
                    BulletTypeDef.ResetToBuiltin();
                }
            }
            else
            {
                BulletTypeDef.ResetToBuiltin();
            }
        }

        private static BulletTypeDef ToDef(Dictionary<string, object> d)
        {
            return BulletTypeDef.FromJson(d);
        }

        private void DrawBulletsTab()
        {
            EditorGUILayout.HelpBox(
                (bulletsCustom ? "bullets.json の弾種定義を使用中です。" : "現在は標準の 3 種（Web版と同値）です。保存すると bullets.json が作られ、以後はその定義を使います。") +
                "\n標準 3 種（normal / straight / giant）の数値を変えると Web版とのリプレイ互換・既存リプレイの検証結果が変わります。新しい弾種は「追加」で作るのがおすすめです。",
                bulletsCustom ? MessageType.Info : MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    List<string> labels = new List<string>();
                    foreach (BulletEntry b in bullets) labels.Add(ContentJson.LocJa(b.Data, "name") + " (" + ContentJson.Str(b.Data, "id") + ")");
                    bulletSel = DrawList(labels, bulletSel, 230);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = bullets.Count < BulletTypeDef.MaxTypes;
                        if (GUILayout.Button("追加")) AddBullet(null);
                        if (GUILayout.Button("複製")) AddBullet(bullets[bulletSel]);
                        GUI.enabled = bullets.Count > 1;
                        if (GUILayout.Button("削除")) DeleteBullet(bulletSel);
                        GUI.enabled = true;
                    }
                    EditorGUILayout.Space(8);
                    GUI.backgroundColor = bulletDirty ? new Color(1f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(bulletDirty ? "保存（未保存の変更あり）" : "保存", GUILayout.Height(32))) SaveBullets();
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = bulletsCustom;
                    if (GUILayout.Button("標準の 3 種に戻す")) ResetBullets();
                    GUI.enabled = true;
                }
                using (EditorGUILayout.ScrollViewScope sv = new EditorGUILayout.ScrollViewScope(bulletScroll))
                {
                    bulletScroll = sv.scrollPosition;
                    DrawBulletDetail(bullets[bulletSel]);
                }
            }
        }

        private void DrawBulletDetail(BulletEntry b)
        {
            Dictionary<string, object> d = b.Data;
            EditorGUI.BeginChangeCheck();
            Header("基本");
            string id = EditorGUILayout.DelayedTextField(new GUIContent("ID", "ステージの type から参照されます"), ContentJson.Str(d, "id"));
            if (id != ContentJson.Str(d, "id")) d["id"] = id.Trim();
            d["speed"] = Math.Round(EditorGUILayout.Slider(new GUIContent("速度（px/tick）", "1 tick = 1/60 秒"), (float)ContentJson.Num(d, "speed", 3), (float)BulletTypeDef.MinSpeed, (float)BulletTypeDef.MaxSpeed), 2);
            d["radius"] = (double)EditorGUILayout.IntSlider("半径（px）", ContentJson.Int(d, "radius", 10), BulletTypeDef.MinRadius, BulletTypeDef.MaxRadius);
            d["damage"] = (double)EditorGUILayout.IntSlider(new GUIContent("ダメージ", "主人公・拠点に当たったときの減少量"), ContentJson.Int(d, "damage", 1), 1, BulletTypeDef.MaxDamage);
            d["smooth"] = EditorGUILayout.Toggle(new GUIContent("曲線補間", "オン: 制御点を滑らかに結ぶ / オフ: 折れ線"), ContentJson.Bool(d, "smooth", true));
            bool hasColor = !string.IsNullOrEmpty(ContentJson.Str(d, "color"));
            bool useColor = EditorGUILayout.Toggle(new GUIContent("固定色を使う", "オフなら弾幕ごとの色"), hasColor);
            if (useColor)
            {
                Color c = ContentJson.FromHex(ContentJson.Str(d, "color"), new Color(1f, 0.7f, 0.9f));
                d["color"] = ContentJson.ToHex(EditorGUILayout.ColorField("弾の色", c));
            }
            else if (hasColor)
            {
                d.Remove("color");
            }
            if (EditorGUI.EndChangeCheck()) bulletDirty = true;

            Header("表示名");
            LangSelector();
            if (LocField("名前", d, "name", false, 16)) bulletDirty = true;

            Header("確認");
            BulletTypeDef def = ToDef(d);
            string err = BulletTypeDef.Validate(def);
            if (err != null) EditorGUILayout.HelpBox(err, MessageType.Error);
            int minInterval = MinIntervalFor(def);
            EditorGUILayout.LabelField("最小発射間隔（自動）", minInterval + " tick（" + (minInterval / 60f).ToString("0.00") + " 秒）— 弾同士が重ならない間隔");
            List<string> used = StagesUsingBullet(b.OriginalId ?? def.Id);
            EditorGUILayout.LabelField("使用中のステージ", used.Count == 0 ? "なし" : string.Join(", ", used));
            Rect r = GUILayoutUtility.GetRect(240, 90, GUILayout.Width(240));
            EditorGUI.DrawRect(r, new Color(0.06f, 0.04f, 0.14f));
            Color bc = ContentJson.FromHex(ContentJson.Str(d, "color"), new Color(1f, 0.55f, 0.8f));
            Handles.color = new Color(bc.r, bc.g, bc.b, 0.35f);
            Handles.DrawSolidDisc(r.center, Vector3.forward, def.Radius * 1.6f);
            Handles.color = bc;
            Handles.DrawSolidDisc(r.center, Vector3.forward, def.Radius);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(r.center, Vector3.forward, def.Radius * 0.55f);
        }

        /// <summary>StageValidator.MinIntervalTicksFor と同じ式（登録前の定義でも計算できるようにする）</summary>
        private static int MinIntervalFor(BulletTypeDef def)
        {
            if (def == null || def.Speed <= 0) return Core.GameConstants.StageLimits.MinIntervalTicks;
            int need = (int)Math.Ceiling((def.Radius * 2 + Core.GameConstants.StageLimits.SpawnGapPadding) / def.Speed);
            return Math.Max(Core.GameConstants.StageLimits.MinIntervalTicks, need);
        }

        private void AddBullet(BulletEntry source)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BulletEntry b in bullets) ids.Add(ContentJson.Str(b.Data, "id"));
            int n = 1;
            string id;
            do
            {
                id = "custom_" + n++;
            }
            while (ids.Contains(id));
            Dictionary<string, object> d = source != null
                ? (Dictionary<string, object>)ContentJson.DeepCopy(source.Data)
                : new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["name"] = ContentJson.NewLoc("新しい弾"),
                    ["speed"] = 3.5,
                    ["radius"] = 12.0,
                    ["damage"] = 1.0,
                    ["smooth"] = true,
                };
            d["id"] = id;
            bullets.Add(new BulletEntry { OriginalId = null, Data = d });
            bulletSel = bullets.Count - 1;
            bulletDirty = true;
            GUI.FocusControl(null);
        }

        private void DeleteBullet(int index)
        {
            BulletEntry b = bullets[index];
            List<string> used = StagesUsingBullet(b.OriginalId ?? ContentJson.Str(b.Data, "id"));
            if (used.Count > 0)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "この弾種は次のステージで使われているため削除できません:\n" + string.Join(", ", used), "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "弾種「" + ContentJson.Str(b.Data, "id") + "」を削除しますか？（保存するまでファイルは変わりません）", "削除", "キャンセル")) return;
            bullets.RemoveAt(index);
            bulletSel = Mathf.Clamp(index - 1, 0, bullets.Count - 1);
            bulletDirty = true;
        }

        private void SaveBullets()
        {
            GUI.FocusControl(null);
            List<BulletTypeDef> defs = new List<BulletTypeDef>();
            foreach (BulletEntry b in bullets) defs.Add(ToDef(b.Data));
            Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BulletEntry b in bullets)
            {
                string id = ContentJson.Str(b.Data, "id");
                if (b.OriginalId != null && b.OriginalId != id) renames[b.OriginalId] = id;
            }
            try
            {
                // 登録を試して検証（失敗時は登録内容は変わらない）
                BulletTypeDef.Register(defs);
                List<object> types = new List<object>();
                foreach (BulletEntry b in bullets) types.Add(b.Data);
                Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["formatVersion"] = 1.0,
                    ["types"] = types,
                };
                ContentJson.Write(BulletsPath, root);
                bulletsCustom = true;
                if (renames.Count > 0)
                {
                    int changed = ApplyBulletRenames(renames);
                    Debug.Log("[ContentEditor] renamed bullet types; stages updated: " + changed);
                }
                foreach (BulletEntry b in bullets) b.OriginalId = ContentJson.Str(b.Data, "id");
                bulletDirty = false;
                ShowNotification(new GUIContent("bullets.json を保存しました"));
            }
            catch (InvalidOperationException e)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "保存できません:\n" + e.Message, "OK");
            }
        }

        private void ResetBullets()
        {
            List<string> custom = new List<string>();
            foreach (BulletEntry b in bullets)
            {
                string id = ContentJson.Str(b.Data, "id");
                if (id != "normal" && id != "straight" && id != "giant" && StagesUsingBullet(id).Count > 0) custom.Add(id);
            }
            if (custom.Count > 0)
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "追加した弾種がステージで使われているため戻せません: " + string.Join(", ", custom), "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("DRAW&WITCHES", "bullets.json を削除して標準の 3 種に戻しますか？", "戻す", "キャンセル")) return;
            if (!AssetDatabase.DeleteAsset(BulletsPath))
            {
                EditorUtility.DisplayDialog("DRAW&WITCHES", "bullets.json を削除できませんでした。", "OK");
                return;
            }
            BulletTypeDef.ResetToBuiltin();
            LoadBullets();
            ShowNotification(new GUIContent("標準の弾種に戻しました"));
        }
    }
}

/*
## このアセットの役割
Content Editor の「弾種」タブです。弾の速度・半径・ダメージ・軌道の補間・色・表示名を調整し、新しい弾種を追加できます。

## 他アセットとの依存関係
Resources/DrawAndWitches/bullets.json（GameDatabase が起動時に読み込み BulletTypeDef に登録）、ステージタブ（使用中チェック・ID 変更の追従）。

## Inspector設定
なし。

## USERがUnity上で行う作業
「追加」で弾種を作り「保存」。ステージタブの弾幕の種類に表示されます。

## 注意点
bullets.json が無い場合は Web版と同じ標準 3 種で動きます。標準 3 種の数値を変えると Web版とのリプレイ互換が無くなります。
不正な bullets.json は起動時に警告を出して標準定義で動作します。
*/
