using System;
using UnityEngine;

namespace DrawAndWitches.Online
{
    /// <summary>
    /// X（旧Twitter）シェア。添付の TweetManager.cs と同一仕様の GenerateTweetURL に加え、
    /// ブラウザで開く OpenTweet を提供する。
    /// </summary>
    public class TweetManager : MonoBehaviour
    {
        public static TweetManager instance = null;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>ツイート用のURLリンクを生成します（本文 + 改行URL + 改行#タグ…）</summary>
        public string GenerateTweetURL(string message, string url = "", string hashtag = "")
        {
            string fullText = message ?? string.Empty;
            if (!string.IsNullOrEmpty(url))
            {
                fullText += "\n" + url;
            }
            if (!string.IsNullOrEmpty(hashtag))
            {
                string[] tags = hashtag.Split(',');
                foreach (string tag in tags)
                {
                    string t = tag.Trim();
                    if (t.Length > 0)
                    {
                        fullText += "\n#" + t;
                    }
                }
            }
            string encodedText = Uri.EscapeDataString(fullText);
            return $"https://twitter.com/intent/tweet?text={encodedText}";
        }

        /// <summary>シェア画面を既定ブラウザで開く</summary>
        public void OpenTweet(string message, string url = "", string hashtag = "")
        {
            Application.OpenURL(GenerateTweetURL(message, url, hashtag));
        }
    }
}

/*
## このアセットの役割
X（旧Twitter）シェア。添付の TweetManager.cs と同一仕様のシェアURL生成（本文＋改行URL＋改行#タグ、Uri.EscapeDataString、twitter.com/intent/tweet）と、既定ブラウザで開く処理です。

## 他アセットとの依存関係
なし（UnityEngine のみ）。GameApp.Share → ResultScreen のシェアボタンから使用されます。

## Inspector設定
Boot シーンの「Online」オブジェクトに付いています（Setup Project で自動配置）。設定項目はありません。

## USERがUnity上で行う作業
なし。シェアに添えるゲームURLとハッシュタグは GameApp の Inspector（Game Url / Hashtags）で設定します。

## 注意点
元コードの「¥n」は Shift_JIS 表示上の円記号で、実体は改行（\n）です。シーン間で1つだけ保持されます（DontDestroyOnLoad）。
*/
