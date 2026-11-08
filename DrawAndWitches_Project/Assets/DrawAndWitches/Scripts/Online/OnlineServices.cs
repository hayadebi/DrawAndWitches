using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;
using DrawAndWitches.Services;
using UnityEngine;

namespace DrawAndWitches.Online
{
    public enum OnlineStatus
    {
        Online,
        Cache,
        NotConfigured,
        Error,
    }

    public sealed class RankingRow
    {
        public string Id;
        public string Name;
        public int Score;
        public int Ops;
        public int Cancels;
        public int Damage;
        public int Cleared;
        public long At;
    }

    public sealed class RankingResult
    {
        public List<RankingRow> Rows = new List<RankingRow>();
        public OnlineStatus Status;
        public long FetchedAt;
    }

    public sealed class SharedStageItem
    {
        public string Id;
        public StageData Stage;
        public string Author;
        public long At;
    }

    public sealed class SharedListResult
    {
        public List<SharedStageItem> Items = new List<SharedStageItem>();
        public OnlineStatus Status;
        public int Skipped;
        public long FetchedAt;
    }

    public struct OnlineSubmitResult
    {
        public bool Ok;
        /// <summary>notConfigured / invalid / tooLarge / GAS 通信エラー文字列</summary>
        public string Error;
    }

    /// <summary>ランキング・共有ステージの取得結果キャッシュ（Web版と同スキーマ、6時間クールダウン）</summary>
    public sealed class OnlineCacheService
    {
        private const int Version = 1;
        internal static readonly Regex KeyPattern = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
        internal const int MaxScore = 100000000;
        private const int MaxAux = 100000;

        private sealed class RankingEntry
        {
            public long FetchedAt;
            public List<RankingRow> Rows;
        }

        private sealed class RankingCache
        {
            public Dictionary<string, RankingEntry> Entries = new Dictionary<string, RankingEntry>(StringComparer.Ordinal);
        }

        private sealed class SharedCache
        {
            public long FetchedAt;
            public List<SharedStageItem> Items = new List<SharedStageItem>();
        }

        private readonly RankingCache ranking;
        private SharedCache shared;
        private readonly ICollection<string> editorEnemyIds = new[] { GameConstants.Editor.EnemyId };

        /// <summary>この起動中に共有一覧を一度取得したか（起動中の取得は1回のみ）</summary>
        public bool SharedFetchedThisLaunch;

        public OnlineCacheService()
        {
            ranking = LocalSave.LoadJson(GameConstants.Storage.CacheRanking, () => new RankingCache(), raw =>
            {
                Dictionary<string, object> o = MiniJson.AsObject(raw);
                Dictionary<string, object> entries = MiniJson.AsObject(MiniJson.Get(o, "entries"));
                if (o == null || MiniJson.GetInt(o, "version", -1) != Version || entries == null) return null;
                RankingCache c = new RankingCache();
                foreach (KeyValuePair<string, object> kv in entries)
                {
                    Dictionary<string, object> v = MiniJson.AsObject(kv.Value);
                    List<object> rows = MiniJson.AsArray(MiniJson.Get(v, "rows"));
                    if (!KeyPattern.IsMatch(kv.Key) || v == null || rows == null || !MiniJson.TryGetLong(MiniJson.Get(v, "fetchedAt"), out long at)) continue;
                    RankingEntry e = new RankingEntry { FetchedAt = at, Rows = new List<RankingRow>() };
                    foreach (object r in rows)
                    {
                        RankingRow row = SanitizeRow(MiniJson.AsObject(r), null);
                        if (row != null) e.Rows.Add(row);
                    }
                    c.Entries[kv.Key] = e;
                }
                return c;
            });
            shared = LocalSave.LoadJson(GameConstants.Storage.CacheShared, () => new SharedCache(), raw =>
            {
                Dictionary<string, object> o = MiniJson.AsObject(raw);
                List<object> items = MiniJson.AsArray(MiniJson.Get(o, "items"));
                if (o == null || MiniJson.GetInt(o, "version", -1) != Version || items == null || !MiniJson.TryGetLong(MiniJson.Get(o, "fetchedAt"), out long at)) return null;
                SharedCache c = new SharedCache { FetchedAt = at };
                foreach (object it in items)
                {
                    Dictionary<string, object> d = MiniJson.AsObject(it);
                    SharedStageItem item = SanitizeShared(MiniJson.GetString(d, "id", null), MiniJson.Get(d, "stage"), MiniJson.Get(d, "author") as string, MiniJson.GetLong(d, "at", 0));
                    if (item != null) c.Items.Add(item);
                }
                return c;
            });
        }

        public static bool CooldownPassed(long fetchedAt, long now)
        {
            if (fetchedAt <= 0) return true;
            if (fetchedAt > now) return true; // 端末時刻の巻き戻り
            return now - fetchedAt >= GameConstants.Online.CooldownMilliseconds;
        }

        private static int Aux(Dictionary<string, object> r, string key)
        {
            return MiniJson.TryGetInt(MiniJson.Get(r, key), out int v) && v >= 0 && v < MaxAux ? v : 0;
        }

        /// <summary>ランキング行の検証（idOverride 指定時は id をそれで置き換える）</summary>
        internal static RankingRow SanitizeRow(Dictionary<string, object> r, string idOverride)
        {
            if (r == null) return null;
            string id = idOverride ?? MiniJson.GetString(r, "id", null);
            if (id == null || !KeyPattern.IsMatch(id)) return null;
            if (!MiniJson.TryGetInt(MiniJson.Get(r, "score"), out int score) || score < 0 || score > MaxScore) return null;
            if (!MiniJson.TryGetLong(MiniJson.Get(r, "at"), out long at) || at < 0) return null;
            string name = SettingsService.SanitizePlayerName(MiniJson.Get(r, "name") as string);
            return new RankingRow
            {
                Id = id,
                Name = name.Length > 0 ? name : "???",
                Score = score,
                Ops = Aux(r, "ops"),
                Cancels = Aux(r, "cancels"),
                Damage = Aux(r, "damage"),
                Cleared = Aux(r, "cleared"),
                At = at,
            };
        }

        internal SharedStageItem SanitizeShared(string id, object rawStage, string author, long at)
        {
            if (id == null || !KeyPattern.IsMatch(id)) return null;
            StageValidationResult v = StageValidator.Validate(rawStage, editorEnemyIds);
            if (!v.Ok || v.Stage.EnemyId != GameConstants.Editor.EnemyId) return null;
            string a = SettingsService.SanitizePlayerName(author);
            return new SharedStageItem { Id = id, Stage = v.Stage, Author = a.Length > 0 ? a : "???", At = at };
        }

        public bool TryGetRanking(string key, out long fetchedAt, out List<RankingRow> rows)
        {
            if (ranking.Entries.TryGetValue(key, out RankingEntry e))
            {
                fetchedAt = e.FetchedAt;
                rows = e.Rows;
                return true;
            }
            fetchedAt = 0;
            rows = null;
            return false;
        }

        public void SetRanking(string key, List<RankingRow> rows, long fetchedAt)
        {
            ranking.Entries[key] = new RankingEntry { FetchedAt = fetchedAt, Rows = rows };
            JsonObject entries = new JsonObject();
            foreach (KeyValuePair<string, RankingEntry> kv in ranking.Entries)
            {
                List<object> list = new List<object>();
                foreach (RankingRow r in kv.Value.Rows) list.Add(RowJson(r, true));
                entries.Add(kv.Key, new JsonObject().Add("fetchedAt", kv.Value.FetchedAt).Add("rows", list));
            }
            LocalSave.SaveJson(GameConstants.Storage.CacheRanking, new JsonObject().Add("version", Version).Add("entries", entries));
        }

        internal static JsonObject RowJson(RankingRow r, bool withId)
        {
            JsonObject o = new JsonObject();
            if (withId) o.Add("id", r.Id);
            else o.Add("v", 1);
            return o.Add("name", r.Name).Add("score", r.Score).Add("ops", r.Ops).Add("cancels", r.Cancels).Add("damage", r.Damage).Add("cleared", r.Cleared).Add("at", r.At);
        }

        public long SharedFetchedAt => shared.FetchedAt;
        public List<SharedStageItem> SharedItems => shared.Items;

        public void SetShared(List<SharedStageItem> items, long fetchedAt)
        {
            shared = new SharedCache { FetchedAt = fetchedAt, Items = items };
            List<object> list = new List<object>();
            foreach (SharedStageItem it in items)
            {
                list.Add(new JsonObject().Add("id", it.Id).Add("stage", it.Stage.ToJson()).Add("author", it.Author).Add("at", it.At));
            }
            LocalSave.SaveJson(GameConstants.Storage.CacheShared, new JsonObject().Add("version", Version).Add("fetchedAt", fetchedAt).Add("items", list));
        }
    }

    /// <summary>ランキング（シート "Ranking_&lt;stageId&gt;" / "Ranking_endless"、childKey = プレイヤーID）</summary>
    public sealed class LeaderboardService
    {
        private readonly HierarchicalDataManager client;
        private readonly OnlineCacheService cache;
        private readonly string playerId;
        private readonly Dictionary<string, Task<RankingResult>> inflight = new Dictionary<string, Task<RankingResult>>(StringComparer.Ordinal);
        private const int MaxRowJson = 2048;

        public LeaderboardService(HierarchicalDataManager client, OnlineCacheService cache, string playerId)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.playerId = playerId;
        }

        public static string StageKey(string stageId)
        {
            return GameConstants.Online.RankingPrefix + stageId;
        }

        public static string EndlessKey => GameConstants.Online.RankingEndlessKey;

        internal static List<RankingRow> Sort(List<RankingRow> rows)
        {
            List<RankingRow> copy = new List<RankingRow>(rows);
            copy.Sort((a, b) =>
            {
                if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
                if (a.At != b.At) return a.At.CompareTo(b.At);
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return copy;
        }

        /// <summary>取得（6時間以内はキャッシュを返す。同じキーの同時要求は1回にまとめる）</summary>
        public Task<RankingResult> GetRankingAsync(string key)
        {
            if (key == null || !OnlineCacheService.KeyPattern.IsMatch(key))
            {
                return Task.FromResult(new RankingResult { Status = OnlineStatus.Error });
            }
            if (inflight.TryGetValue(key, out Task<RankingResult> running)) return running;
            Task<RankingResult> task = FetchAsync(key);
            inflight[key] = task;
            return task;
        }

        private async Task<RankingResult> FetchAsync(string key)
        {
            try
            {
                bool hasCache = cache.TryGetRanking(key, out long cachedAt, out List<RankingRow> cachedRows);
                long now = IdGenerator.NowMs();
                if (hasCache && !OnlineCacheService.CooldownPassed(cachedAt, now))
                {
                    return new RankingResult { Rows = Sort(cachedRows), Status = OnlineStatus.Cache, FetchedAt = cachedAt };
                }
                RankingResult fallback = new RankingResult { Rows = hasCache ? Sort(cachedRows) : new List<RankingRow>(), FetchedAt = hasCache ? cachedAt : 0 };
                if (!client.IsConfigured)
                {
                    fallback.Status = OnlineStatus.NotConfigured;
                    return fallback;
                }
                Dictionary<string, string> map = await client.GetAllDataAsync(key);
                if (map == null)
                {
                    fallback.Status = OnlineStatus.Error;
                    return fallback;
                }
                List<RankingRow> rows = new List<RankingRow>();
                foreach (KeyValuePair<string, string> kv in map)
                {
                    if (!OnlineCacheService.KeyPattern.IsMatch(kv.Key) || kv.Value == null || kv.Value.Length > MaxRowJson) continue;
                    if (!MiniJson.TryParse(kv.Value, out object raw, out string _)) continue;
                    RankingRow row = OnlineCacheService.SanitizeRow(MiniJson.AsObject(raw), kv.Key);
                    if (row != null) rows.Add(row);
                }
                List<RankingRow> sorted = Sort(rows);
                cache.SetRanking(key, sorted, now);
                return new RankingResult { Rows = sorted, Status = OnlineStatus.Online, FetchedAt = now };
            }
            finally
            {
                inflight.Remove(key);
            }
        }

        /// <summary>自分のスコアを送信（同じプレイヤーIDの行を上書き）。成功時はキャッシュにも反映する。</summary>
        public async Task<OnlineSubmitResult> SubmitAsync(string key, string playerName, int score, int ops, int cancels, int damage, int cleared)
        {
            if (!client.IsConfigured) return new OnlineSubmitResult { Error = "notConfigured" };
            if (key == null || !OnlineCacheService.KeyPattern.IsMatch(key)) return new OnlineSubmitResult { Error = "invalid" };
            Dictionary<string, object> draft = new Dictionary<string, object>
            {
                { "name", playerName }, { "score", (double)score }, { "ops", (double)ops }, { "cancels", (double)cancels },
                { "damage", (double)damage }, { "cleared", (double)cleared }, { "at", (double)IdGenerator.NowMs() },
            };
            RankingRow row = OnlineCacheService.SanitizeRow(draft, playerId);
            if (row == null) return new OnlineSubmitResult { Error = "invalid" };
            bool ok = await client.SetDataAsync(key, playerId, MiniJson.Serialize(OnlineCacheService.RowJson(row, false)));
            if (!ok) return new OnlineSubmitResult { Error = client.LastError ?? "error" };
            bool hasCache = cache.TryGetRanking(key, out long cachedAt, out List<RankingRow> cachedRows);
            List<RankingRow> rows = hasCache ? cachedRows.FindAll(r => r.Id != playerId) : new List<RankingRow>();
            rows.Add(row);
            cache.SetRanking(key, Sort(rows), hasCache ? cachedAt : IdGenerator.NowMs());
            return new OnlineSubmitResult { Ok = true };
        }
    }

    /// <summary>共有ステージ（シート "SharedStages"、childKey = 作成ID）</summary>
    public sealed class SharedStageService
    {
        private readonly HierarchicalDataManager client;
        private readonly OnlineCacheService cache;
        private Task<SharedListResult> inflight;
        private readonly ICollection<string> editorEnemyIds = new[] { GameConstants.Editor.EnemyId };
        private const int PayloadOverhead = 1024;

        public SharedStageService(HierarchicalDataManager client, OnlineCacheService cache)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>一覧取得（起動中1回のみ、かつ6時間クールダウン。それ以外はキャッシュ）</summary>
        public Task<SharedListResult> GetListAsync()
        {
            if (inflight != null) return inflight;
            inflight = FetchAsync();
            return inflight;
        }

        private async Task<SharedListResult> FetchAsync()
        {
            try
            {
                long now = IdGenerator.NowMs();
                SharedListResult cached = new SharedListResult { Items = cache.SharedItems, FetchedAt = cache.SharedFetchedAt, Status = OnlineStatus.Cache };
                if (cache.SharedFetchedThisLaunch || !OnlineCacheService.CooldownPassed(cache.SharedFetchedAt, now)) return cached;
                if (!client.IsConfigured)
                {
                    cached.Status = OnlineStatus.NotConfigured;
                    return cached;
                }
                cache.SharedFetchedThisLaunch = true;
                Dictionary<string, string> map = await client.GetAllDataAsync(GameConstants.Online.SharedStagesKey);
                if (map == null)
                {
                    cached.Status = OnlineStatus.Error;
                    return cached;
                }
                List<SharedStageItem> items = new List<SharedStageItem>();
                int skipped = 0;
                foreach (KeyValuePair<string, string> kv in map)
                {
                    if (items.Count >= GameConstants.Online.SharedListMax || !OnlineCacheService.KeyPattern.IsMatch(kv.Key) || kv.Value == null
                        || kv.Value.Length > GameConstants.StageLimits.MaxJsonBytes + PayloadOverhead)
                    {
                        skipped++;
                        continue;
                    }
                    Dictionary<string, object> obj = MiniJson.TryParse(kv.Value, out object raw, out string _) ? MiniJson.AsObject(raw) : null;
                    if (obj == null || MiniJson.GetInt(obj, "v", -1) != 1)
                    {
                        skipped++;
                        continue;
                    }
                    SharedStageItem item = cache.SanitizeShared(kv.Key, MiniJson.Get(obj, "stage"), MiniJson.Get(obj, "author") as string, MiniJson.GetLong(obj, "at", 0));
                    if (item != null) items.Add(item);
                    else skipped++;
                }
                items.Sort((a, b) => b.At.CompareTo(a.At));
                cache.SetShared(items, now);
                return new SharedListResult { Items = items, Status = OnlineStatus.Online, Skipped = skipped, FetchedAt = now };
            }
            finally
            {
                inflight = null;
            }
        }

        /// <summary>テストプレイ済みの自作ステージを登録する（エディター敵限定・32KB以内）</summary>
        public async Task<OnlineSubmitResult> RegisterAsync(string creationId, StageData stage, string author)
        {
            if (!client.IsConfigured) return new OnlineSubmitResult { Error = "notConfigured" };
            if (creationId == null || !OnlineCacheService.KeyPattern.IsMatch(creationId) || stage == null) return new OnlineSubmitResult { Error = "invalid" };
            object raw = MiniJson.TryParse(MiniJson.Serialize(stage.ToJson()), out object r, out string _) ? r : null;
            StageValidationResult v = StageValidator.Validate(raw, editorEnemyIds);
            if (!v.Ok) return new OnlineSubmitResult { Error = "invalid" };
            string payload = MiniJson.Serialize(new JsonObject().Add("v", 1).Add("stage", v.Stage.ToJson()).Add("author", SettingsService.SanitizePlayerName(author)).Add("at", IdGenerator.NowMs()));
            if (payload.Length > GameConstants.StageLimits.MaxJsonBytes) return new OnlineSubmitResult { Error = "tooLarge" };
            bool ok = await client.SetDataAsync(GameConstants.Online.SharedStagesKey, creationId, payload);
            if (!ok)
            {
                Debug.LogWarning("[SharedStages] register failed: " + client.LastError);
                return new OnlineSubmitResult { Error = client.LastError ?? "error" };
            }
            return new OnlineSubmitResult { Ok = true };
        }
    }
}

/*
## このアセットの役割
ランキングと共有ステージの取得・送信・キャッシュ（6時間クールダウン、共有一覧は起動中1回）を担当します。Web版 onlineServices.js と同じシート名・行形式・検証規則なので、Web版と同じスプレッドシートを共用できます。

## 他アセットとの依存関係
HierarchicalDataManager / LocalSave / SettingsService / StageValidator / MiniJson に依存。ResultScreen・RankingPanel・SharedStagesScreen から使用されます。

## Inspector設定
なし（GameApp が生成します）。

## USERがUnity上で行う作業
なし。

## 注意点
async メソッドはメインスレッド（Unity の SynchronizationContext）で再開します。画面遷移後に結果が返ることがあるため、受け取る側は自分が破棄済みでないか確認してから UI を更新してください。
*/
