using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DrawAndWitches.Online
{
    /// <summary>
    /// Googleスプレッドシート（GAS）と連携して階層型データを管理するマネージャー。
    /// 添付の HierarchicalDataManager.cs と同一の通信仕様・公開API:
    ///   POST フォーム（action=get/getAll/set/delete, parentKey=シート名, childKey=A列キー, data=B列JSON）
    ///   応答 {status,data,message}、getAll の data は {"keys":[...],"values":[...]}、タイムアウト30秒。
    /// 追加点: 失敗理由を LastError に保持し、エラーをログ出力する（元コードではコメントアウトされていた部分）。
    /// </summary>
    public class HierarchicalDataManager : MonoBehaviour
    {
        private string PlaceholderUrl = "YOUR_GAS_DEPLOYMENT_URL_HERE";
        private const int TimeoutSeconds = 30;

        private static HierarchicalDataManager _instance;

        public static HierarchicalDataManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("HierarchicalDataManager");
                    _instance = go.AddComponent<HierarchicalDataManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>GAS のデプロイURL（Inspector で設定。https:// で始まる「ウェブアプリ」URL）</summary>
        [SerializeField]
        [Tooltip("GAS のウェブアプリURL（https://script.google.com/macros/s/.../exec）。空ならオンライン機能は無効になり、ゲームはオフラインで動作します。")]
        private string GAS_URL = "";

        /// <summary>直近の失敗理由（notConfigured / timeout / network: ... / http NNN / parse / server: ...）。成功時 null。</summary>
        public string LastError { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>コードから URL を設定する（Inspector 設定より優先）</summary>
        public void SetGasUrl(string url)
        {
            GAS_URL = url == null ? string.Empty : url.Trim();
        }

        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrEmpty(GAS_URL) && GAS_URL != PlaceholderUrl && GAS_URL.StartsWith("https://", StringComparison.Ordinal);
            }
        }

        #region Public Methods

        /// <summary>指定した親要素（シート）内の子要素（行）のデータを取得します</summary>
        /// <returns>B列のJSON文字列。存在しない・失敗時は null</returns>
        public async Task<string> GetDataAsync(string parentKey, string childKey)
        {
            if (string.IsNullOrEmpty(parentKey) || string.IsNullOrEmpty(childKey))
            {
                LastError = "invalid arguments";
                Debug.LogError("HierarchicalDataManager: parentKey and childKey cannot be null or empty");
                return null;
            }
            WWWForm form = new WWWForm();
            form.AddField("action", "get");
            form.AddField("parentKey", parentKey);
            form.AddField("childKey", childKey);
            GASResponse response = await SendRequestAsync(form);
            if (response != null && response.status == "success")
            {
                return response.data;
            }
            return null;
        }

        /// <summary>指定した親要素（シート）のすべての子要素（全行）のデータを取得します</summary>
        /// <returns>キーと値の Dictionary。失敗時は null</returns>
        public async Task<Dictionary<string, string>> GetAllDataAsync(string parentKey)
        {
            if (string.IsNullOrEmpty(parentKey))
            {
                LastError = "invalid arguments";
                Debug.LogError("HierarchicalDataManager: parentKey cannot be null or empty");
                return null;
            }
            WWWForm form = new WWWForm();
            form.AddField("action", "getAll");
            form.AddField("parentKey", parentKey);
            GASResponse response = await SendRequestAsync(form);
            if (response != null && response.status == "success")
            {
                try
                {
                    SerializableDictionary dict = JsonUtility.FromJson<SerializableDictionary>(response.data);
                    if (dict == null)
                    {
                        throw new ArgumentException("data is empty");
                    }
                    return dict.ToDictionary();
                }
                catch (Exception ex)
                {
                    LastError = "parse";
                    Debug.LogError($"HierarchicalDataManager: Failed to parse response data - {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        /// <summary>指定した親要素（シート）に子要素のデータを追加または上書きします</summary>
        public async Task<bool> SetDataAsync(string parentKey, string childKey, string jsonData)
        {
            if (string.IsNullOrEmpty(parentKey) || string.IsNullOrEmpty(childKey))
            {
                LastError = "invalid arguments";
                Debug.LogError("HierarchicalDataManager: parentKey and childKey cannot be null or empty");
                return false;
            }
            if (string.IsNullOrEmpty(jsonData))
            {
                Debug.LogWarning("HierarchicalDataManager: jsonData is empty");
            }
            WWWForm form = new WWWForm();
            form.AddField("action", "set");
            form.AddField("parentKey", parentKey);
            form.AddField("childKey", childKey);
            form.AddField("data", jsonData ?? string.Empty);
            GASResponse response = await SendRequestAsync(form);
            return response != null && response.status == "success";
        }

        /// <summary>指定した親要素（シート）内の子要素（行）を削除します</summary>
        public async Task<bool> DeleteDataAsync(string parentKey, string childKey)
        {
            if (string.IsNullOrEmpty(parentKey) || string.IsNullOrEmpty(childKey))
            {
                LastError = "invalid arguments";
                Debug.LogError("HierarchicalDataManager: parentKey and childKey cannot be null or empty");
                return false;
            }
            WWWForm form = new WWWForm();
            form.AddField("action", "delete");
            form.AddField("parentKey", parentKey);
            form.AddField("childKey", childKey);
            GASResponse response = await SendRequestAsync(form);
            return response != null && response.status == "success";
        }

        #endregion

        #region Private Methods

        private async Task<GASResponse> SendRequestAsync(WWWForm form)
        {
            LastError = null;
            if (!IsConfigured)
            {
                LastError = "notConfigured";
                Debug.LogWarning("HierarchicalDataManager: GAS_URL is not set. Online features are disabled.");
                return null;
            }
            TaskCompletionSource<GASResponse> tcs = new TaskCompletionSource<GASResponse>();
            StartCoroutine(SendRequestCoroutine(form, tcs));
            return await tcs.Task;
        }

        private IEnumerator SendRequestCoroutine(WWWForm form, TaskCompletionSource<GASResponse> tcs)
        {
            using (UnityWebRequest request = UnityWebRequest.Post(GAS_URL, form))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    bool timedOut = request.error != null && request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                    LastError = request.result == UnityWebRequest.Result.ProtocolError ? $"http {request.responseCode}" : (timedOut ? "timeout" : $"network: {request.error}");
                    Debug.LogError($"HierarchicalDataManager: Request failed - {request.error} (Response Code: {request.responseCode})");
                    tcs.SetResult(null);
                }
                else
                {
                    try
                    {
                        string responseText = request.downloadHandler.text;
                        GASResponse response = JsonUtility.FromJson<GASResponse>(responseText);
                        if (response == null || string.IsNullOrEmpty(response.status))
                        {
                            throw new ArgumentException("unexpected response shape");
                        }
                        if (response.status != "success")
                        {
                            LastError = "server: " + (string.IsNullOrEmpty(response.message) ? "error" : response.message);
                            Debug.LogWarning($"HierarchicalDataManager: Server returned error - {response.message}");
                        }
                        tcs.SetResult(response);
                    }
                    catch (Exception ex)
                    {
                        LastError = "parse";
                        Debug.LogError($"HierarchicalDataManager: Failed to parse response - {ex.Message}");
                        tcs.SetResult(null);
                    }
                }
            }
        }

        #endregion

        #region Helper Classes

        /// <summary>GAS からのレスポンス構造</summary>
        [Serializable]
        private class GASResponse
        {
            public string status;
            public string data;
            public string message;
        }

        /// <summary>Dictionary のシリアライズ用ヘルパー</summary>
        [Serializable]
        private class SerializableDictionary
        {
            public List<string> keys = new List<string>();
            public List<string> values = new List<string>();

            public Dictionary<string, string> ToDictionary()
            {
                Dictionary<string, string> result = new Dictionary<string, string>();
                for (int i = 0; i < keys.Count && i < values.Count; i++)
                {
                    result[keys[i]] = values[i];
                }
                return result;
            }
        }

        #endregion
    }
}

/*
## このアセットの役割
HierarchicalDataManager: GAS（Googleスプレッドシート）との通信。添付コードと同一の API・フォーム項目・応答形式・タイムアウトで実装し、失敗理由の保持とログ出力を追加しています。
X シェアは同フォルダの TweetManager.cs が担当します。

## 他アセットとの依存関係
UnityEngine.Networking（標準）のみ。OnlineServices（ランキング・共有ステージ）と ResultScreen（シェアボタン）から使用されます。GAS 側は Web版と共通の gas/Code.gs を使います（同じスプレッドシートを Web版と Unity版で共有できます）。

## Inspector設定
Boot シーンの「Online」オブジェクトに両コンポーネントを付け、HierarchicalDataManager の GAS_URL に Web版 js/config/onlineConfig.js と同じURLを入力します（空ならオフライン動作）。

## USERがUnity上で行う作業
GAS_URL の入力のみ。手順は docs/ONLINE_SETUP.md。

## 注意点
元コードの「¥n」は Shift_JIS 表示上の円記号で、実体はバックスラッシュ（改行 \n）です。本ファイルは UTF-8 で保存しているため "\n" と表記しています。
*/
