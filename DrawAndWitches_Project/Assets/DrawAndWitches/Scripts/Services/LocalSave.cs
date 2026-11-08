using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DrawAndWitches.Core;
using UnityEngine;

namespace DrawAndWitches.Services
{
    /// <summary>保存エラーの種類</summary>
    public enum StorageErrorKind
    {
        Read,
        Corrupt,
        Write,
    }

    /// <summary>保存エラー通知の内容</summary>
    public struct StorageError
    {
        public StorageErrorKind Kind;
        public string Key;
        public string Detail;
    }

    /// <summary>
    /// PlayerPrefs を使ったローカル保存（Web版 localSave.js の localStorage を PlayerPrefs に置き換えたもの）。
    /// 値は Web版と同じスキーマの JSON 文字列で保存し、読み込み時に必ず検証する。破損時は既定値へ戻して通知する。
    /// </summary>
    public static class LocalSave
    {
        /// <summary>このゲームが使う全キー（PlayerPrefs はキー列挙ができないため明示管理）</summary>
        public static readonly string[] AllKeys =
        {
            GameConstants.Storage.Settings,
            GameConstants.Storage.Progress,
            GameConstants.Storage.Tutorial,
            GameConstants.Storage.Replays,
            GameConstants.Storage.MyStages,
            GameConstants.Storage.CacheRanking,
            GameConstants.Storage.CacheShared,
            GameConstants.Storage.PlayerId,
        };

        /// <summary>保存エラー発生時に通知（UI がトースト表示する）。登録したら必ず解除すること。</summary>
        public static event Action<StorageError> ErrorOccurred;

        private static void Report(StorageErrorKind kind, string key, string detail)
        {
            Action<StorageError> handler = ErrorOccurred;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(new StorageError { Kind = kind, Key = key, Detail = detail });
            }
            catch (Exception e)
            {
                Debug.LogError("[LocalSave] storage error listener threw: " + e);
            }
        }

        /// <summary>
        /// JSON を読み込んで検証する。未保存なら makeDefault()、解析失敗・検証失敗（sanitize が null）なら通知して makeDefault()。
        /// </summary>
        public static T LoadJson<T>(string key, Func<T> makeDefault, Func<object, T> sanitize) where T : class
        {
            if (makeDefault == null || sanitize == null)
            {
                throw new ArgumentNullException(makeDefault == null ? nameof(makeDefault) : nameof(sanitize));
            }
            string text;
            try
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    return makeDefault();
                }
                text = PlayerPrefs.GetString(key, null);
            }
            catch (Exception e)
            {
                // PlayerPrefs のネイティブ層エラー（レジストリ・plist の読込失敗など）
                Debug.LogError("[LocalSave] " + key + ": read failed (" + e.Message + "). Defaults are used for this session.");
                Report(StorageErrorKind.Read, key, e.Message);
                return makeDefault();
            }
            if (string.IsNullOrEmpty(text))
            {
                return makeDefault();
            }
            if (!MiniJson.TryParse(text, out object raw, out string parseError))
            {
                Debug.LogWarning("[LocalSave] " + key + ": parse failed (" + parseError + "), reset to defaults");
                Report(StorageErrorKind.Corrupt, key, parseError);
                return makeDefault();
            }
            T value;
            try
            {
                value = sanitize(raw);
            }
            catch (InvalidCastException e)
            {
                Debug.LogWarning("[LocalSave] " + key + ": unexpected value type (" + e.Message + "), reset to defaults");
                Report(StorageErrorKind.Corrupt, key, e.Message);
                return makeDefault();
            }
            if (value == null)
            {
                Debug.LogWarning("[LocalSave] " + key + ": validation failed, reset to defaults");
                Report(StorageErrorKind.Corrupt, key, "validation failed");
                return makeDefault();
            }
            return value;
        }

        /// <summary>JSON 化して保存し、PlayerPrefs.Save() でディスクへ書き出す。書き込み後に読み戻して検証する。</summary>
        public static bool SaveJson(string key, object jsonValue)
        {
            string text;
            try
            {
                text = MiniJson.Serialize(jsonValue);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError("[LocalSave] " + key + ": serialize failed (" + e.Message + ")");
                Report(StorageErrorKind.Write, key, "serialize failed: " + e.Message);
                return false;
            }
            return SaveString(key, text);
        }

        public static bool SaveString(string key, string text)
        {
            try
            {
                PlayerPrefs.SetString(key, text);
                PlayerPrefs.Save();
                string back = PlayerPrefs.GetString(key, null);
                if (!string.Equals(back, text, StringComparison.Ordinal))
                {
                    Debug.LogError("[LocalSave] " + key + ": verify mismatch after save (storage may be full)");
                    Report(StorageErrorKind.Write, key, "verify mismatch");
                    return false;
                }
                return true;
            }
            catch (PlayerPrefsException e)
            {
                // WebGL などで容量上限を超えた場合
                Debug.LogError("[LocalSave] " + key + ": save failed (" + e.Message + "). The game continues with in-memory data.");
                Report(StorageErrorKind.Write, key, "quota exceeded");
                return false;
            }
        }

        public static string LoadString(string key)
        {
            try
            {
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, null) : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[LocalSave] " + key + ": read failed (" + e.Message + ")");
                Report(StorageErrorKind.Read, key, e.Message);
                return null;
            }
        }

        public static bool Remove(string key)
        {
            try
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[LocalSave] " + key + ": delete failed (" + e.Message + ")");
                Report(StorageErrorKind.Write, key, e.Message);
                return false;
            }
        }

        /// <summary>このゲームの全保存データを削除する（設定画面の「データ初期化」）</summary>
        public static bool ClearAllGameData()
        {
            bool ok = true;
            for (int i = 0; i < AllKeys.Length; i++)
            {
                ok &= Remove(AllKeys[i]);
            }
            return ok;
        }

        // -------------------------------------------------------------- 検証ヘルパー

        private static readonly Regex IdPattern = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

        public static bool IsValidId(string id)
        {
            return id != null && IdPattern.IsMatch(id);
        }

        /// <summary>{id: true} 形式の辞書を検証して読み込む</summary>
        public static Dictionary<string, bool> ReadBoolMap(object raw)
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (raw is Dictionary<string, object> obj)
            {
                foreach (KeyValuePair<string, object> kv in obj)
                {
                    if (IsValidId(kv.Key) && kv.Value is bool b && b)
                    {
                        result[kv.Key] = true;
                    }
                }
            }
            return result;
        }

        /// <summary>{id: 非負整数} 形式の辞書を検証して読み込む</summary>
        public static Dictionary<string, int> ReadIntMap(object raw)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (raw is Dictionary<string, object> obj)
            {
                foreach (KeyValuePair<string, object> kv in obj)
                {
                    if (IsValidId(kv.Key) && MiniJson.TryGetInt(kv.Value, out int v) && v >= 0)
                    {
                        result[kv.Key] = v;
                    }
                }
            }
            return result;
        }

        public static JsonObject WriteBoolMap(Dictionary<string, bool> map)
        {
            JsonObject o = new JsonObject();
            List<string> keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                if (map[keys[i]]) o.Add(keys[i], true);
            }
            return o;
        }

        public static JsonObject WriteIntMap(Dictionary<string, int> map)
        {
            JsonObject o = new JsonObject();
            List<string> keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                o.Add(keys[i], map[keys[i]]);
            }
            return o;
        }
    }

    /// <summary>ID・時刻の生成</summary>
    public static class IdGenerator
    {
        private const int Base36 = 36;
        private const string Digits36 = "0123456789abcdefghijklmnopqrstuvwxyz";
        private const int RandomWords = 4;
        private const int MaxIdBodyLength = 32;
        private static readonly Regex PlayerIdPattern = new Regex("^p_[A-Za-z0-9]{8,40}$", RegexOptions.CultureInvariant);
        public static readonly Regex CreationIdPattern = new Regex("^c_[A-Za-z0-9]{8,40}$", RegexOptions.CultureInvariant);

        public static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string ToBase36(uint value)
        {
            if (value == 0u) return "0";
            char[] buf = new char[sizeof(uint) * 2];
            int pos = buf.Length;
            while (value > 0u)
            {
                buf[--pos] = Digits36[(int)(value % Base36)];
                value /= Base36;
            }
            return new string(buf, pos, buf.Length - pos);
        }

        private static string RandomBody(int words)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < words; i++)
            {
                sb.Append(ToBase36(DeterministicRng.CreateSeed()));
            }
            string s = sb.ToString();
            return s.Length > MaxIdBodyLength ? s.Substring(0, MaxIdBodyLength) : s;
        }

        /// <summary>自作ステージの作成ID（Web版 newCreationId と同形式 c_xxxxxxxx）</summary>
        public static string NewCreationId()
        {
            return "c_" + RandomBody(RandomWords);
        }

        /// <summary>ランキング用プレイヤーID。初回生成後は PlayerPrefs に保存して再利用する。</summary>
        public static string GetOrCreatePlayerId()
        {
            string existing = LocalSave.LoadString(GameConstants.Storage.PlayerId);
            if (existing != null && PlayerIdPattern.IsMatch(existing))
            {
                return existing;
            }
            const int PlayerIdWords = 3;
            string id = "p_" + RandomBody(PlayerIdWords);
            if (!LocalSave.SaveString(GameConstants.Storage.PlayerId, id))
            {
                Debug.LogWarning("[Online] player id could not be persisted; a session-only id is used");
            }
            return id;
        }
    }
}

/*
## このアセットの役割
PlayerPrefs への JSON 保存・読込・検証・破損時の既定値復帰（LocalSave）と、プレイヤーID・作成IDの生成（IdGenerator）を担当します。

## 他アセットとの依存関係
MiniJson / GameConstants に依存。SettingsService / ProgressService / ReplayStore / MyStageService / OnlineCacheService から使用されます。保存仕様は docs/UNITY_PHASE2.md の「PlayerPrefs保存仕様」に記載しています。

## Inspector設定
なし。

## USERがUnity上で行う作業
なし。保存データを消したい場合は、ゲーム内の設定画面「データ初期化」またはメニュー Edit > Clear All PlayerPrefs を使います。

## 注意点
PlayerPrefs はキー列挙ができないため、新しいキーを追加したら AllKeys にも追加してください（初期化対象から漏れます）。
*/
