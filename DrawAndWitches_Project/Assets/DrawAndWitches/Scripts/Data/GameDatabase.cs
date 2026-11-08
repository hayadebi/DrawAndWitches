using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;
using UnityEngine;

namespace DrawAndWitches.Data
{
    /// <summary>キャラクター1人分（characters.json の1要素）</summary>
    public sealed class CharacterProfile
    {
        public string Id;
        public int Rank;
        public Color Color = Color.white;
        public string PortraitKey;
        public string BackgroundKey;
        public string BgmKey;
        public LocalizedText Name = LocalizedText.Empty;
        public LocalizedText Title = LocalizedText.Empty;
        public LocalizedText Intro = LocalizedText.Empty;
        public LocalizedText Spell = LocalizedText.Empty;
        public LocalizedText SpellDetail = LocalizedText.Empty;
    }

    public sealed class DialogueLine
    {
        /// <summary>true = 主人公、false = 敵</summary>
        public bool IsPlayer;
        public LocalizedText Text;
        /// <summary>発言者の表情（任意。未指定なら talk）。CharacterPortrait の表情名。</summary>
        public string Face;
    }

    /// <summary>
    /// Resources/DrawAndWitches 以下の JSON（Web版 data/ と同一ファイル）を読み込んで検証する。
    /// 致命的な欠落（キャラクター・公式ステージ・日本語テキスト）は例外で起動を止め、画面にエラーを表示する。
    /// </summary>
    public sealed class GameDatabase
    {
        public const string ResourceRoot = "DrawAndWitches/";
        private const int DialogueMaxLength = 300;
        private static readonly Regex StageFilePattern = new Regex("^[A-Za-z0-9_-]+\\.json$", RegexOptions.CultureInvariant);

        public CharacterProfile Player { get; private set; }
        public readonly List<CharacterProfile> Enemies = new List<CharacterProfile>();
        public CharacterProfile EditorEnemy { get; private set; }
        public readonly List<StageData> OfficialStages = new List<StageData>();
        /// <summary>読込時の警告（不正なステージ・会話のスキップなど）。起動は継続する。</summary>
        public readonly List<string> Warnings = new List<string>();

        private readonly Dictionary<string, CharacterProfile> byId = new Dictionary<string, CharacterProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DialogueLine>> dialogues = new Dictionary<string, List<DialogueLine>>(StringComparer.Ordinal);

        public HashSet<string> EnemyIds => new HashSet<string>(byId.Keys, StringComparer.Ordinal);

        public static string LoadText(string path)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceRoot + path);
            if (asset == null)
            {
                throw new InvalidOperationException("Resources/" + ResourceRoot + path + ".json が見つかりません（Assets/DrawAndWitches/Resources に配置してください）");
            }
            return asset.text;
        }

        private static Dictionary<string, object> LoadObject(string path)
        {
            if (!MiniJson.TryParse(LoadText(path), out object raw, out string error))
            {
                throw new InvalidOperationException(path + ".json: JSON parse error " + error);
            }
            Dictionary<string, object> obj = MiniJson.AsObject(raw);
            if (obj == null || MiniJson.GetInt(obj, "formatVersion", -1) != 1)
            {
                throw new InvalidOperationException(path + ".json: invalid format (formatVersion 1 の JSON オブジェクトが必要です)");
            }
            return obj;
        }

        public static LocalizedText ParseText(object raw)
        {
            if (raw is string s) return new LocalizedText(s);
            Dictionary<string, object> o = MiniJson.AsObject(raw);
            if (o == null) return LocalizedText.Empty;
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> kv in o)
            {
                if (kv.Value is string v) map[kv.Key] = v;
            }
            return new LocalizedText(map);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            return hex != null && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }

        private static CharacterProfile ParseCharacter(object raw, string label)
        {
            Dictionary<string, object> o = MiniJson.AsObject(raw);
            string id = MiniJson.GetString(o, "id", null);
            if (o == null || string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException("characters.json: " + label + " has no id");
            }
            return new CharacterProfile
            {
                Id = id,
                Rank = MiniJson.GetInt(o, "rank", 0),
                Color = ParseColor(MiniJson.GetString(o, "color", null), Color.white),
                PortraitKey = MiniJson.GetString(o, "portrait", string.Empty),
                BackgroundKey = MiniJson.GetString(o, "background", string.Empty),
                BgmKey = MiniJson.GetString(o, "bgm", "bgm_battle"),
                Name = ParseText(MiniJson.Get(o, "name")),
                Title = ParseText(MiniJson.Get(o, "title")),
                Intro = ParseText(MiniJson.Get(o, "intro")),
                Spell = ParseText(MiniJson.Get(o, "spell")),
                SpellDetail = ParseText(MiniJson.Get(o, "spellDetail")),
            };
        }

        /// <summary>全データを読み込む。失敗時は InvalidOperationException。</summary>
        public static GameDatabase Load()
        {
            GameDatabase db = new GameDatabase();
            db.LoadBullets();
            Dictionary<string, object> chars = LoadObject("characters");
            List<object> enemies = MiniJson.AsArray(MiniJson.Get(chars, "enemies"));
            if (enemies == null || MiniJson.Get(chars, "player") == null || MiniJson.Get(chars, "editorEnemy") == null)
            {
                throw new InvalidOperationException("characters.json: player / enemies / editorEnemy が必要です");
            }
            db.Player = ParseCharacter(MiniJson.Get(chars, "player"), "player");
            for (int i = 0; i < enemies.Count; i++) db.Enemies.Add(ParseCharacter(enemies[i], "enemies[" + i + "]"));
            db.EditorEnemy = ParseCharacter(MiniJson.Get(chars, "editorEnemy"), "editorEnemy");
            List<CharacterProfile> all = new List<CharacterProfile>(db.Enemies) { db.EditorEnemy };
            foreach (CharacterProfile c in all)
            {
                if (db.byId.ContainsKey(c.Id)) throw new InvalidOperationException("characters.json: duplicated id " + c.Id);
                db.byId[c.Id] = c;
            }

            Dictionary<string, object> index = LoadObject("stages/index");
            List<object> entries = MiniJson.AsArray(MiniJson.Get(index, "stages"));
            if (entries == null) throw new InvalidOperationException("stages/index.json: stages 配列が必要です");
            HashSet<string> enemyIds = db.EnemyIds;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object e in entries)
            {
                string file = MiniJson.GetString(MiniJson.AsObject(e), "file", null);
                if (file == null || !StageFilePattern.IsMatch(file))
                {
                    db.Warnings.Add("stages/index.json: invalid entry");
                    continue;
                }
                string name = file.Substring(0, file.Length - ".json".Length);
                StageValidationResult v;
                try
                {
                    v = StageValidator.ParseJson(LoadText("stages/" + name), enemyIds);
                }
                catch (InvalidOperationException ex)
                {
                    db.Warnings.Add(ex.Message);
                    continue;
                }
                if (!v.Ok)
                {
                    db.Warnings.Add(file + ": " + v.ErrorSummary);
                    continue;
                }
                if (!seen.Add(v.Stage.Id))
                {
                    db.Warnings.Add("duplicated stage id " + v.Stage.Id);
                    continue;
                }
                db.OfficialStages.Add(v.Stage);
            }
            if (db.OfficialStages.Count == 0) throw new InvalidOperationException("公式ステージが1つも読み込めませんでした");

            db.LoadDialogues();
            foreach (string w in db.Warnings) Debug.LogWarning("[GameDatabase] " + w);
            return db;
        }

        /// <summary>
        /// 弾種定義（Resources/DrawAndWitches/bullets.json、任意）。無い場合は Web版と同じ標準 3 種。
        /// 不正な定義は警告して標準定義のまま起動する。
        /// </summary>
        private void LoadBullets()
        {
            if (Resources.Load<TextAsset>(ResourceRoot + "bullets") == null)
            {
                BulletTypeDef.ResetToBuiltin();
                return;
            }
            try
            {
                Dictionary<string, object> root = LoadObject("bullets");
                List<object> types = MiniJson.AsArray(MiniJson.Get(root, "types"));
                if (types == null || types.Count == 0) throw new InvalidOperationException("bullets.json: types 配列が必要です");
                List<BulletTypeDef> defs = new List<BulletTypeDef>();
                foreach (object t in types) defs.Add(BulletTypeDef.FromJson(MiniJson.AsObject(t)));
                BulletTypeDef.Register(defs);
            }
            catch (InvalidOperationException e)
            {
                Warnings.Add(e.Message);
                BulletTypeDef.ResetToBuiltin();
            }
        }

        private void LoadDialogues()
        {
            Dictionary<string, object> root;
            try
            {
                root = LoadObject("dialogues");
            }
            catch (InvalidOperationException e)
            {
                // 会話はなくても遊べるため警告のみ
                Warnings.Add(e.Message);
                return;
            }
            Dictionary<string, object> stages = MiniJson.AsObject(MiniJson.Get(root, "stages"));
            if (stages == null)
            {
                Warnings.Add("dialogues.json: stages が必要です");
                return;
            }
            foreach (KeyValuePair<string, object> kv in stages)
            {
                List<object> lines = MiniJson.AsArray(kv.Value);
                if (lines == null) continue;
                List<DialogueLine> valid = new List<DialogueLine>();
                foreach (object l in lines)
                {
                    Dictionary<string, object> lo = MiniJson.AsObject(l);
                    string speaker = MiniJson.GetString(lo, "speaker", null);
                    Dictionary<string, object> text = MiniJson.AsObject(MiniJson.Get(lo, "text"));
                    if ((speaker != "player" && speaker != "enemy") || text == null) continue;
                    bool ok = true;
                    foreach (string lang in LocalizedText.Languages)
                    {
                        string s = MiniJson.GetString(text, lang, null);
                        if (string.IsNullOrEmpty(s) || s.Length > DialogueMaxLength) ok = false;
                    }
                    string face = MiniJson.GetString(lo, "face", null);
                    if (face != null && (face.Length > 32 || face.Trim().Length == 0)) face = null;
                    if (ok) valid.Add(new DialogueLine { IsPlayer = speaker == "player", Text = ParseText(text), Face = face });
                }
                if (valid.Count != lines.Count) Warnings.Add("dialogues " + kv.Key + ": " + (lines.Count - valid.Count) + " invalid line(s) skipped");
                if (valid.Count > 0) dialogues[kv.Key] = valid;
            }
        }

        public CharacterProfile GetCharacter(string id)
        {
            return id != null && byId.TryGetValue(id, out CharacterProfile c) ? c : null;
        }

        /// <summary>敵情報（未登録 ID は練習人形で代替し、表示が崩れないようにする）</summary>
        public CharacterProfile EnemyOf(StageData stage)
        {
            return GetCharacter(stage?.EnemyId) ?? EditorEnemy;
        }

        public List<DialogueLine> DialogueOf(string stageId)
        {
            return stageId != null && dialogues.TryGetValue(stageId, out List<DialogueLine> l) ? l : null;
        }

        public StageData GetOfficial(string id)
        {
            return OfficialStages.Find(s => s.Id == id);
        }

        public int IndexOfOfficial(string id)
        {
            return OfficialStages.FindIndex(s => s.Id == id);
        }

        public List<string> OfficialIds()
        {
            return OfficialStages.ConvertAll(s => s.Id);
        }
    }

    /// <summary>UI 文言の多言語テーブル（Resources/DrawAndWitches/i18n/*.json、Web版と同一ファイル）</summary>
    public sealed class LocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Regex ParamPattern = new Regex("\\{(\\w+)\\}", RegexOptions.CultureInvariant);

        public string Language { get; private set; } = "ja";

        public event Action LanguageChanged;

        public void LoadAll()
        {
            foreach (string lang in LocalizedText.Languages)
            {
                Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    if (MiniJson.TryParse(GameDatabase.LoadText("i18n/" + lang), out object raw, out string error) && MiniJson.AsObject(raw) != null)
                    {
                        foreach (KeyValuePair<string, object> kv in MiniJson.AsObject(raw))
                        {
                            if (kv.Value is string s) table[kv.Key] = s;
                        }
                    }
                    else
                    {
                        Debug.LogError("[Localization] i18n/" + lang + ".json parse failed: " + error);
                    }
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogError("[Localization] " + e.Message);
                }
                tables[lang] = table;
            }
            if (tables["ja"].Count == 0)
            {
                throw new InvalidOperationException("Localization: 日本語テーブル (i18n/ja.json) が必要です");
            }
        }

        public void SetLanguage(string lang)
        {
            string next = Array.IndexOf(LocalizedText.Languages, lang) >= 0 ? lang : "ja";
            if (next == Language) return;
            Language = next;
            Action handler = LanguageChanged;
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Debug.LogError("[Localization] listener threw: " + e);
            }
        }

        /// <summary>キーの文言。未定義なら日本語、それもなければキー自体を返す。{name} 形式の置換に対応。</summary>
        public string T(string key, params object[] keyValuePairs)
        {
            string s = null;
            if (!tables.TryGetValue(Language, out Dictionary<string, string> t) || !t.TryGetValue(key, out s))
            {
                if (missing.Add(Language + ":" + key)) Debug.LogWarning("[Localization] missing key \"" + key + "\" for " + Language);
                if (!tables.TryGetValue("ja", out Dictionary<string, string> ja) || !ja.TryGetValue(key, out s)) return key;
            }
            if (keyValuePairs == null || keyValuePairs.Length < 2) return s;
            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
            {
                p[Convert.ToString(keyValuePairs[i], CultureInfo.InvariantCulture)] = Convert.ToString(keyValuePairs[i + 1], CultureInfo.InvariantCulture);
            }
            return ParamPattern.Replace(s, m => p.TryGetValue(m.Groups[1].Value, out string v) ? v : m.Value);
        }

        public string Resolve(LocalizedText text)
        {
            return text == null ? string.Empty : text.Resolve(Language);
        }
    }
}

/*
## このアセットの役割
GameDatabase: キャラクター・公式ステージ・会話の読込と検証。LocalizationService: UI 文言の4言語切替（日本語・英語・中国語簡体字・韓国語）。

## 他アセットとの依存関係
Resources/DrawAndWitches 以下の JSON（Web版 data/ と同一内容）、StageValidator、MiniJson。GameApp が起動時に生成します。

## Inspector設定
なし。

## USERがUnity上で行う作業
公式ステージを追加する場合は Resources/DrawAndWitches/stages に JSON を置き、index.json に {"id":..., "file":...} を追記します。

## 注意点
ステージ JSON の拡張子は .json のまま Resources に置いてください（TextAsset として読み込まれます）。
*/
