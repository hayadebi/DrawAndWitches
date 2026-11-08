using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;
using UnityEngine;

namespace DrawAndWitches.Services
{
    public enum FigurebookState
    {
        Locked,
        Seen,
        Full,
    }

    /// <summary>ストーリー進行・ベストスコア・図鑑・ランキング送信済み・エンドレス記録・チュートリアル既読（Web版 progress.js と同スキーマ）</summary>
    public sealed class ProgressService
    {
        private const int Version = 1;

        private sealed class ProgressData
        {
            public Dictionary<string, bool> Cleared = new Dictionary<string, bool>(StringComparer.Ordinal);
            public Dictionary<string, int> Best = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, bool> Encountered = new Dictionary<string, bool>(StringComparer.Ordinal);
            public Dictionary<string, bool> Defeated = new Dictionary<string, bool>(StringComparer.Ordinal);
            public Dictionary<string, int> RankingSubmitted = new Dictionary<string, int>(StringComparer.Ordinal);
            public int EndlessBest;
            public int EndlessBestCleared;
        }

        private sealed class TutorialData
        {
            public bool Seen;
        }

        private readonly ProgressData data;
        private readonly TutorialData tutorial;

        public ProgressService()
        {
            data = LocalSave.LoadJson(GameConstants.Storage.Progress, () => new ProgressData(), SanitizeProgress);
            tutorial = LocalSave.LoadJson(GameConstants.Storage.Tutorial, () => new TutorialData(), raw =>
            {
                Dictionary<string, object> o = MiniJson.AsObject(raw);
                if (o == null || MiniJson.GetInt(o, "version", -1) != Version || !(MiniJson.Get(o, "seen") is bool seen)) return null;
                return new TutorialData { Seen = seen };
            });
        }

        private static ProgressData SanitizeProgress(object rawValue)
        {
            Dictionary<string, object> raw = MiniJson.AsObject(rawValue);
            if (raw == null || MiniJson.GetInt(raw, "version", -1) != Version) return null;
            ProgressData p = new ProgressData
            {
                Cleared = LocalSave.ReadBoolMap(MiniJson.Get(raw, "cleared")),
                Best = LocalSave.ReadIntMap(MiniJson.Get(raw, "best")),
                Encountered = LocalSave.ReadBoolMap(MiniJson.Get(raw, "encountered")),
                Defeated = LocalSave.ReadBoolMap(MiniJson.Get(raw, "defeated")),
                RankingSubmitted = LocalSave.ReadIntMap(MiniJson.Get(raw, "rankingSubmitted")),
            };
            int eb = MiniJson.GetInt(raw, "endlessBest", 0);
            int ec = MiniJson.GetInt(raw, "endlessBestCleared", 0);
            p.EndlessBest = eb >= 0 ? eb : 0;
            p.EndlessBestCleared = ec >= 0 ? ec : 0;
            return p;
        }

        private bool Save()
        {
            JsonObject o = new JsonObject()
                .Add("version", Version)
                .Add("cleared", LocalSave.WriteBoolMap(data.Cleared))
                .Add("best", LocalSave.WriteIntMap(data.Best))
                .Add("encountered", LocalSave.WriteBoolMap(data.Encountered))
                .Add("defeated", LocalSave.WriteBoolMap(data.Defeated))
                .Add("rankingSubmitted", LocalSave.WriteIntMap(data.RankingSubmitted))
                .Add("endlessBest", data.EndlessBest)
                .Add("endlessBestCleared", data.EndlessBestCleared);
            return LocalSave.SaveJson(GameConstants.Storage.Progress, o);
        }

        public bool IsCleared(string stageId)
        {
            return stageId != null && data.Cleared.ContainsKey(stageId);
        }

        /// <summary>直前のステージをクリアしていれば解放（最初のステージは常に解放）</summary>
        public bool IsUnlocked(IList<string> stageIds, int index)
        {
            if (index <= 0) return true;
            if (stageIds == null || index >= stageIds.Count) return false;
            return IsCleared(stageIds[index - 1]);
        }

        public int BestOf(string stageId)
        {
            return stageId != null && data.Best.TryGetValue(stageId, out int v) ? v : 0;
        }

        /// <returns>ベスト更新なら true</returns>
        public bool RecordStageResult(string stageId, string enemyId, BattleOutcome outcome, int score, bool trackBest)
        {
            bool newBest = false;
            if (LocalSave.IsValidId(enemyId)) data.Encountered[enemyId] = true;
            if (outcome == BattleOutcome.Clear)
            {
                if (trackBest && LocalSave.IsValidId(stageId))
                {
                    data.Cleared[stageId] = true;
                    if (score > BestOf(stageId))
                    {
                        data.Best[stageId] = score;
                        newBest = true;
                    }
                }
                if (LocalSave.IsValidId(enemyId)) data.Defeated[enemyId] = true;
            }
            Save();
            return newBest;
        }

        public void MarkEncountered(string enemyId)
        {
            if (!LocalSave.IsValidId(enemyId) || data.Encountered.ContainsKey(enemyId)) return;
            data.Encountered[enemyId] = true;
            Save();
        }

        public FigurebookState FigurebookStateOf(string enemyId)
        {
            if (enemyId == null) return FigurebookState.Locked;
            if (data.Defeated.ContainsKey(enemyId)) return FigurebookState.Full;
            if (data.Encountered.ContainsKey(enemyId)) return FigurebookState.Seen;
            return FigurebookState.Locked;
        }

        public int EndlessBest => data.EndlessBest;
        public int EndlessBestCleared => data.EndlessBestCleared;

        public bool RecordEndless(int total, int cleared)
        {
            bool newBest = false;
            if (total > data.EndlessBest)
            {
                data.EndlessBest = total;
                data.EndlessBestCleared = cleared;
                newBest = true;
            }
            Save();
            return newBest;
        }

        /// <returns>送信済みスコア。未送信なら -1</returns>
        public int RankingSubmittedScore(string key)
        {
            return key != null && data.RankingSubmitted.TryGetValue(key, out int v) ? v : -1;
        }

        public void MarkRankingSubmitted(string key, int score)
        {
            if (!LocalSave.IsValidId(key) || score < 0) return;
            data.RankingSubmitted[key] = score;
            Save();
        }

        public bool IsTutorialSeen => tutorial.Seen;

        public bool SetTutorialSeen(bool seen)
        {
            tutorial.Seen = seen;
            return LocalSave.SaveJson(GameConstants.Storage.Tutorial, new JsonObject().Add("version", Version).Add("seen", seen));
        }
    }

    /// <summary>保存されたリプレイ1件</summary>
    public sealed class ReplayEntry
    {
        public string Id;
        public string Data;
        public long At;
        public string Title;
        public string Outcome;
        public int Score;
    }

    /// <summary>直近のリプレイ1件と bot 記録（最大20件）の保存（Web版 replayStore.js と同スキーマ）</summary>
    public sealed class ReplayStore
    {
        private const int Version = 1;
        private const int TitleMax = 80;
        private ReplayEntry last;
        private readonly List<ReplayEntry> bots = new List<ReplayEntry>();

        public ReplayStore()
        {
            ReplayStore loaded = LocalSave.LoadJson(GameConstants.Storage.Replays, () => null as ReplayStore, Sanitize);
            if (loaded != null)
            {
                last = loaded.last;
                bots.AddRange(loaded.bots);
            }
        }

        private ReplayStore(bool empty)
        {
        }

        public ReplayEntry Last => last;
        public IReadOnlyList<ReplayEntry> Bots => bots;

        private static ReplayEntry SanitizeEntry(object rawValue)
        {
            Dictionary<string, object> e = MiniJson.AsObject(rawValue);
            if (e == null) return null;
            string id = MiniJson.GetString(e, "id", null);
            string dataText = MiniJson.GetString(e, "data", null);
            if (!LocalSave.IsValidId(id)) return null;
            if (dataText == null || !dataText.StartsWith(GameConstants.Replay.Prefix, StringComparison.Ordinal) || dataText.Length > GameConstants.Replay.MaxStringLength) return null;
            if (!MiniJson.TryGetLong(MiniJson.Get(e, "at"), out long at) || at < 0) return null;
            string title = MiniJson.GetString(e, "title", string.Empty);
            if (title.Length > TitleMax) title = title.Substring(0, TitleMax);
            int score = MiniJson.GetInt(e, "score", 0);
            return new ReplayEntry
            {
                Id = id,
                Data = dataText,
                At = at,
                Title = title,
                Outcome = MiniJson.GetString(e, "outcome", null) == "clear" ? "clear" : "lose",
                Score = score >= 0 ? score : 0,
            };
        }

        private static ReplayStore Sanitize(object rawValue)
        {
            Dictionary<string, object> raw = MiniJson.AsObject(rawValue);
            if (raw == null || MiniJson.GetInt(raw, "version", -1) != Version) return null;
            ReplayStore s = new ReplayStore(true);
            object rawLast = MiniJson.Get(raw, "last");
            s.last = rawLast == null ? null : SanitizeEntry(rawLast);
            List<object> rawBots = MiniJson.AsArray(MiniJson.Get(raw, "bots"));
            if (rawBots != null)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < rawBots.Count && s.bots.Count < GameConstants.Replay.BotRecordsMax; i++)
                {
                    ReplayEntry e = SanitizeEntry(rawBots[i]);
                    if (e != null && seen.Add(e.Id)) s.bots.Add(e);
                }
            }
            return s;
        }

        private static JsonObject EntryJson(ReplayEntry e)
        {
            return new JsonObject().Add("id", e.Id).Add("data", e.Data).Add("at", e.At).Add("title", e.Title).Add("outcome", e.Outcome).Add("score", e.Score);
        }

        private bool Save()
        {
            List<object> list = new List<object>(bots.Count);
            for (int i = 0; i < bots.Count; i++) list.Add(EntryJson(bots[i]));
            return LocalSave.SaveJson(GameConstants.Storage.Replays, new JsonObject().Add("version", Version).Add("last", last == null ? null : EntryJson(last)).Add("bots", list));
        }

        private static ReplayEntry Normalize(ReplayEntry entry)
        {
            if (entry == null) return null;
            return SanitizeEntry(MiniJson.TryParse(MiniJson.Serialize(EntryJson(entry)), out object raw, out string _) ? raw : null);
        }

        public bool SetLast(ReplayEntry entry)
        {
            ReplayEntry e = Normalize(entry);
            if (e == null) return false;
            last = e;
            return Save();
        }

        public bool AddBot(ReplayEntry entry)
        {
            ReplayEntry e = Normalize(entry);
            if (e == null) return false;
            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i].Id == e.Id) return true; // 重複登録防止
            }
            bots.Insert(0, e);
            if (bots.Count > GameConstants.Replay.BotRecordsMax) bots.RemoveRange(GameConstants.Replay.BotRecordsMax, bots.Count - GameConstants.Replay.BotRecordsMax);
            return Save();
        }

        public bool RemoveBot(string id)
        {
            int removed = bots.RemoveAll(b => b.Id == id);
            return removed > 0 && Save();
        }
    }

    /// <summary>自作ステージ1件</summary>
    public sealed class MyStageItem
    {
        public string CreationId;
        public StageData Stage;
        public string ClearedHash;
        public bool Registered;
        public long UpdatedAt;
    }

    public enum MyStageError
    {
        None,
        Invalid,
        Locked,
        Full,
        SaveFailed,
    }

    /// <summary>自作ステージの保存（最大50件）・テストプレイ済み判定・ステージファイル入出力</summary>
    public sealed class MyStageService
    {
        private const int Version = 1;
        private static readonly Regex HashPattern = new Regex("^[0-9a-f]{8}$", RegexOptions.CultureInvariant);
        private const string StageFileExtension = ".dwstage.json";
        private const int MaxFileBaseLength = 48;
        private readonly ICollection<string> enemyIds;
        private readonly List<MyStageItem> items = new List<MyStageItem>();

        public MyStageService(ICollection<string> enemyIds)
        {
            this.enemyIds = enemyIds;
            List<MyStageItem> loaded = LocalSave.LoadJson(GameConstants.Storage.MyStages, () => new List<MyStageItem>(), Sanitize);
            items.AddRange(loaded);
        }

        private List<MyStageItem> Sanitize(object rawValue)
        {
            Dictionary<string, object> raw = MiniJson.AsObject(rawValue);
            List<object> rawItems = MiniJson.AsArray(MiniJson.Get(raw, "items"));
            if (raw == null || MiniJson.GetInt(raw, "version", -1) != Version || rawItems == null) return null;
            List<MyStageItem> result = new List<MyStageItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rawItems.Count && result.Count < GameConstants.Storage.MaxMyStages; i++)
            {
                Dictionary<string, object> it = MiniJson.AsObject(rawItems[i]);
                string cid = MiniJson.GetString(it, "creationId", null);
                if (it == null || cid == null || !IdGenerator.CreationIdPattern.IsMatch(cid) || seen.Contains(cid)) continue;
                StageValidationResult v = StageValidator.Validate(MiniJson.Get(it, "stage"), enemyIds);
                if (!v.Ok)
                {
                    Debug.LogWarning("[MyStages] dropped invalid stage " + cid + ": " + v.ErrorSummary);
                    continue;
                }
                seen.Add(cid);
                string ch = MiniJson.GetString(it, "clearedHash", null);
                result.Add(new MyStageItem
                {
                    CreationId = cid,
                    Stage = v.Stage,
                    ClearedHash = ch != null && HashPattern.IsMatch(ch) ? ch : null,
                    Registered = MiniJson.GetBool(it, "registered", false),
                    UpdatedAt = MiniJson.GetLong(it, "updatedAt", 0),
                });
            }
            return result;
        }

        private bool Save()
        {
            List<object> list = new List<object>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                MyStageItem it = items[i];
                list.Add(new JsonObject()
                    .Add("creationId", it.CreationId)
                    .Add("stage", it.Stage.ToJson())
                    .Add("clearedHash", it.ClearedHash)
                    .Add("registered", it.Registered)
                    .Add("updatedAt", it.UpdatedAt));
            }
            return LocalSave.SaveJson(GameConstants.Storage.MyStages, new JsonObject().Add("version", Version).Add("items", list));
        }

        public int Count => items.Count;

        /// <summary>更新日時の新しい順</summary>
        public List<MyStageItem> List()
        {
            List<MyStageItem> copy = new List<MyStageItem>(items);
            copy.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            return copy;
        }

        public MyStageItem Get(string creationId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].CreationId == creationId) return items[i];
            }
            return null;
        }

        /// <summary>検証してから追加・更新する。登録済み（共有済み）ステージは編集不可。</summary>
        public MyStageError Upsert(string creationId, StageData stage, out string detail)
        {
            detail = string.Empty;
            if (stage == null || !IdGenerator.CreationIdPattern.IsMatch(creationId ?? string.Empty))
            {
                return MyStageError.Invalid;
            }
            StageValidationResult v = StageValidator.Validate(RoundTrip(stage), enemyIds);
            if (!v.Ok)
            {
                detail = v.ErrorSummary;
                return MyStageError.Invalid;
            }
            MyStageItem existing = Get(creationId);
            if (existing != null && existing.Registered) return MyStageError.Locked;
            if (existing == null && items.Count >= GameConstants.Storage.MaxMyStages) return MyStageError.Full;
            long now = IdGenerator.NowMs();
            if (existing != null)
            {
                existing.Stage = v.Stage;
                existing.UpdatedAt = now;
                if (existing.ClearedHash != null && existing.ClearedHash != v.Stage.ContentHash()) existing.ClearedHash = null;
            }
            else
            {
                items.Add(new MyStageItem { CreationId = creationId, Stage = v.Stage, UpdatedAt = now });
            }
            return Save() ? MyStageError.None : MyStageError.SaveFailed;
        }

        /// <summary>StageData を JSON 経由で検証器へ渡す（UI で組み立てたデータも同じ規則で検証する）</summary>
        private static object RoundTrip(StageData stage)
        {
            return MiniJson.TryParse(MiniJson.Serialize(stage.ToJson()), out object raw, out string _) ? raw : null;
        }

        public bool MarkCleared(string creationId, StageData stage)
        {
            MyStageItem it = Get(creationId);
            if (it == null || stage == null) return false;
            string h = stage.ContentHash();
            if (it.Stage.ContentHash() != h) return false;
            it.ClearedHash = h;
            return Save();
        }

        public bool IsTestCleared(string creationId)
        {
            MyStageItem it = Get(creationId);
            return it != null && it.ClearedHash != null && it.ClearedHash == it.Stage.ContentHash();
        }

        public bool MarkRegistered(string creationId)
        {
            MyStageItem it = Get(creationId);
            if (it == null) return false;
            it.Registered = true;
            return Save();
        }

        public bool Remove(string creationId)
        {
            int removed = items.RemoveAll(i => i.CreationId == creationId);
            return removed > 0 && Save();
        }

        /// <returns>複製した作成ID。上限・未存在・保存失敗時は null</returns>
        public string Duplicate(string creationId)
        {
            MyStageItem it = Get(creationId);
            if (it == null || items.Count >= GameConstants.Storage.MaxMyStages) return null;
            string id = IdGenerator.NewCreationId();
            StageData copy = it.Stage.Clone();
            copy.Id = id;
            items.Add(new MyStageItem { CreationId = id, Stage = copy, UpdatedAt = IdGenerator.NowMs() });
            return Save() ? id : null;
        }

        /// <summary>ステージファイルの保存先（persistentDataPath/Stages）</summary>
        public static string StageFolder => Path.Combine(Application.persistentDataPath, "Stages");

        /// <summary>ステージを JSON ファイルへ書き出す（Web版のダウンロードと同じ内容・同じ拡張子）</summary>
        public static bool ExportStageFile(StageData stage, string fileBaseName, out string path, out string error)
        {
            path = null;
            error = null;
            if (stage == null)
            {
                error = "no stage";
                return false;
            }
            string safe = Regex.Replace(fileBaseName ?? "stage", "[^A-Za-z0-9_-]", "_");
            if (safe.Length > MaxFileBaseLength) safe = safe.Substring(0, MaxFileBaseLength);
            if (safe.Length == 0) safe = "stage";
            try
            {
                Directory.CreateDirectory(StageFolder);
                path = Path.Combine(StageFolder, safe + StageFileExtension);
                File.WriteAllText(path, MiniJson.Serialize(stage.ToJson()), new System.Text.UTF8Encoding(false));
                return true;
            }
            catch (IOException e)
            {
                error = e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                error = e.Message;
            }
            Debug.LogError("[MyStages] export failed: " + error + " (folder: " + StageFolder + ")");
            return false;
        }

        /// <summary>保存フォルダ内のステージファイル一覧（新しい順）</summary>
        public static List<string> ListStageFiles()
        {
            List<string> files = new List<string>();
            try
            {
                if (!Directory.Exists(StageFolder)) return files;
                files.AddRange(Directory.GetFiles(StageFolder, "*" + StageFileExtension));
                files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            }
            catch (IOException e)
            {
                Debug.LogError("[MyStages] cannot list stage files: " + e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError("[MyStages] cannot list stage files: " + e.Message);
            }
            return files;
        }

        /// <summary>ステージファイルを読み込んで検証する（サイズ上限込み）</summary>
        public StageValidationResult ImportStageFile(string path)
        {
            StageValidationResult fail = new StageValidationResult();
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    fail.Errors.Add("no file");
                    return fail;
                }
                if (info.Length > GameConstants.StageLimits.MaxJsonBytes)
                {
                    fail.Errors.Add("file too large");
                    return fail;
                }
                return StageValidator.ParseJson(File.ReadAllText(path), enemyIds);
            }
            catch (IOException e)
            {
                fail.Errors.Add("read failed: " + e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                fail.Errors.Add("read failed: " + e.Message);
            }
            return fail;
        }

        public StageValidationResult ImportStageText(string text)
        {
            return StageValidator.ParseJson(text, enemyIds);
        }
    }
}

/*
## このアセットの役割
ProgressService（進行・ベスト・図鑑・ランキング送信済み・エンドレス・チュートリアル既読）、ReplayStore（直近リプレイと bot 記録）、MyStageService（自作ステージ・テストプレイ済み判定・ファイル入出力）を PlayerPrefs に保存します。

## 他アセットとの依存関係
LocalSave / IdGenerator / StageValidator / StageData に依存。GameApp が生成し、各画面（Story / Battle / Result / Records / Editor / Dex）から使用されます。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。ステージファイルは Application.persistentDataPath/Stages に出力されます（Windows: %USERPROFILE%/AppData/LocalLow/&lt;会社名&gt;/&lt;製品名&gt;/Stages）。

## 注意点
保存内容は Web版 localStorage と同じ JSON スキーマです。Web版のデータを移行する手順は docs/UNITY_PHASE2.md「Web版データの移行」を参照してください。
*/
