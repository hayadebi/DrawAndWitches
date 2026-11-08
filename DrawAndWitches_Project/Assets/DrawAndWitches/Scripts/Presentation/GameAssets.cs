using System;
using System.Collections.Generic;
using DrawAndWitches.Services;
using UnityEngine;

namespace DrawAndWitches.Presentation
{
    [Serializable]
    public sealed class SpriteEntry
    {
        [Tooltip("characters.json / manifest.json で使うキー（例: char_crea, bg_candy）")]
        public string key;
        public Sprite sprite;
    }

    [Serializable]
    public sealed class AudioEntry
    {
        [Tooltip("BGM / 効果音のキー（例: bgm_menu, ui_click）")]
        public string key;
        public AudioClip clip;
    }

    [Serializable]
    public sealed class ExpressionEntry
    {
        [Tooltip("表情名。ゲームが使う名前: default / talk / damage / spell / win / lose（会話データの face で任意の名前も指定可）")]
        public string expression;
        [Tooltip("この表情の差分立ち絵。未設定ならデフォルト立ち絵を表示します。")]
        public Sprite sprite;
    }

    /// <summary>
    /// キャラクターの見た目設定（差分立ち絵・アニメーター）。characters.json の id と対応。
    /// どれも未設定ならデフォルト立ち絵（characters.json の portrait キー）を表示する。
    /// </summary>
    [Serializable]
    public sealed class CharacterVisual
    {
        [Tooltip("characters.json の id（例: crea, milfy）")]
        public string characterId;
        [Tooltip("デフォルト立ち絵の上書き（未設定なら portrait キーの画像）")]
        public Sprite portrait;
        [Tooltip("立ち絵に付ける Animator Controller（任意）。表情名と同名の Trigger、bool Talking があれば自動で操作します。")]
        public RuntimeAnimatorController animator;
        public List<ExpressionEntry> expressions = new List<ExpressionEntry>();

        public Sprite FindExpression(string expression)
        {
            if (expressions == null || string.IsNullOrEmpty(expression)) return null;
            foreach (ExpressionEntry e in expressions)
            {
                if (e != null && e.sprite != null && string.Equals(e.expression, expression, StringComparison.Ordinal)) return e.sprite;
            }
            return null;
        }
    }

    /// <summary>
    /// 画像・音声・フォントの参照表。メニュー「DRAW&WITCHES / Setup Project」で自動生成・自動登録される。
    /// 差し替えたい場合は Inspector で該当キーの Sprite / AudioClip を入れ替えるだけでよい。
    /// </summary>
    [CreateAssetMenu(fileName = "GameAssets", menuName = "DRAW&WITCHES/Game Assets")]
    public sealed class GameAssets : ScriptableObject
    {
        [Tooltip("UI フォント。日本語・中国語・韓国語を含むフォント（例: Noto Sans CJK）を推奨。未設定時は Unity 標準フォント。")]
        public Font font;
        public List<SpriteEntry> sprites = new List<SpriteEntry>();
        public List<AudioEntry> audio = new List<AudioEntry>();

        [Header("キャラクター表現（差分立ち絵・アニメーター。任意）")]
        public List<CharacterVisual> characters = new List<CharacterVisual>();

        [Header("UI スキン（Setup Project が生成。差し替え可）")]
        public Sprite uiRounded;
        public Sprite uiRoundedOutline;
        public Sprite uiCircle;
        public Sprite uiSoftCircle;
        public Sprite uiVerticalFade;

        /// <summary>キャラクターの見た目設定。未登録なら null。</summary>
        public CharacterVisual Visual(string characterId)
        {
            if (characters == null || string.IsNullOrEmpty(characterId)) return null;
            foreach (CharacterVisual v in characters)
            {
                if (v != null && string.Equals(v.characterId, characterId, StringComparison.Ordinal)) return v;
            }
            return null;
        }

        /// <summary>キーの画像を登録・差し替え（エディター拡張用）</summary>
        public void SetSprite(string key, Sprite sprite)
        {
            if (string.IsNullOrEmpty(key)) return;
            SpriteEntry e = sprites.Find(x => x != null && x.key == key);
            if (e == null) sprites.Add(new SpriteEntry { key = key, sprite = sprite });
            else e.sprite = sprite;
            spriteMap = null;
        }

        /// <summary>キーの音声を登録・差し替え（エディター拡張用）</summary>
        public void SetClip(string key, AudioClip clip)
        {
            if (string.IsNullOrEmpty(key)) return;
            AudioEntry e = audio.Find(x => x != null && x.key == key);
            if (e == null) audio.Add(new AudioEntry { key = key, clip = clip });
            else e.clip = clip;
            audioMap = null;
        }

        /// <summary>登録済みかどうか（警告を出さずに確認）</summary>
        public Sprite PeekSprite(string key)
        {
            if (spriteMap == null) BuildMaps();
            return key != null && spriteMap.TryGetValue(key, out Sprite s) ? s : null;
        }

        public AudioClip PeekClip(string key)
        {
            if (audioMap == null) BuildMaps();
            return key != null && audioMap.TryGetValue(key, out AudioClip c) ? c : null;
        }

        private Dictionary<string, Sprite> spriteMap;
        private Dictionary<string, AudioClip> audioMap;
        private readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

        private void BuildMaps()
        {
            spriteMap = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (SpriteEntry e in sprites)
            {
                if (e != null && !string.IsNullOrEmpty(e.key) && e.sprite != null) spriteMap[e.key] = e.sprite;
            }
            audioMap = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            foreach (AudioEntry e in audio)
            {
                if (e != null && !string.IsNullOrEmpty(e.key) && e.clip != null) audioMap[e.key] = e.clip;
            }
        }

        /// <summary>キーの画像。未登録なら null（呼び出し側で代替表示する）。</summary>
        public Sprite Sprite(string key)
        {
            if (spriteMap == null) BuildMaps();
            if (key != null && spriteMap.TryGetValue(key, out Sprite s)) return s;
            if (key != null && warned.Add("s:" + key)) Debug.LogWarning("[GameAssets] sprite '" + key + "' is not registered (run DRAW&WITCHES > Setup Project)");
            return null;
        }

        public AudioClip Clip(string key)
        {
            if (audioMap == null) BuildMaps();
            if (key != null && audioMap.TryGetValue(key, out AudioClip c)) return c;
            if (key != null && warned.Add("a:" + key)) Debug.LogWarning("[GameAssets] audio '" + key + "' is not registered (run DRAW&WITCHES > Setup Project)");
            return null;
        }

        private void OnValidate()
        {
            spriteMap = null;
            audioMap = null;
        }
    }

    /// <summary>
    /// BGM（クロスフェード）と効果音（同時発音数制限・連打抑制）の再生。音量は設定値（0〜100）を反映する。
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private const int SePoolSize = 12;
        private const float BgmFadeSeconds = 0.6f;
        private const float SeMinInterval = 0.035f;
        private const float VolumeScale = 0.01f;
        private const float ChainPitchStep = 0.06f;
        private const float ChainPitchMax = 1.8f;

        private GameAssets assets;
        private AudioSource bgmA;
        private AudioSource bgmB;
        private AudioSource[] sePool;
        private int seCursor;
        private readonly Dictionary<string, float> lastSeTime = new Dictionary<string, float>(StringComparer.Ordinal);
        private string currentBgm;
        private float master = 0.8f;
        private float bgmVol = 0.6f;
        private float seVol = 0.8f;
        private float fadeT = 1f;

        public void Init(GameAssets gameAssets)
        {
            assets = gameAssets;
            bgmA = gameObject.AddComponent<AudioSource>();
            bgmB = gameObject.AddComponent<AudioSource>();
            foreach (AudioSource s in new[] { bgmA, bgmB })
            {
                s.loop = true;
                s.playOnAwake = false;
                s.volume = 0f;
            }
            sePool = new AudioSource[SePoolSize];
            for (int i = 0; i < SePoolSize; i++)
            {
                sePool[i] = gameObject.AddComponent<AudioSource>();
                sePool[i].playOnAwake = false;
            }
        }

        public void ApplyVolumes(GameSettings s)
        {
            master = s.MasterVolume * VolumeScale;
            bgmVol = s.BgmVolume * VolumeScale;
            seVol = s.SeVolume * VolumeScale;
            if (fadeT >= 1f && bgmA != null) bgmA.volume = master * bgmVol;
        }

        public void PlayBgm(string key)
        {
            if (!Application.isPlaying) return; // UI 焼き込み（エディター）中は鳴らさない
            if (assets == null || key == currentBgm) return;
            AudioClip clip = assets.Clip(key) ?? assets.Clip("bgm_battle");
            currentBgm = key;
            AudioSource tmp = bgmA;
            bgmA = bgmB;
            bgmB = tmp;
            bgmA.clip = clip;
            bgmA.volume = 0f;
            if (clip != null) bgmA.Play();
            fadeT = 0f;
        }

        public void StopBgm()
        {
            currentBgm = null;
            AudioSource tmp = bgmA;
            bgmA = bgmB;
            bgmB = tmp;
            bgmA.Stop();
            bgmA.clip = null;
            fadeT = 0f;
        }

        /// <param name="chainLevel">連鎖の段数（音程を上げる）。0 なら通常。</param>
        public void PlaySe(string key, int chainLevel = 0)
        {
            if (!Application.isPlaying) return;
            if (assets == null || sePool == null) return;
            float now = Time.unscaledTime;
            if (lastSeTime.TryGetValue(key, out float last) && now - last < SeMinInterval) return;
            AudioClip clip = assets.Clip(key);
            if (clip == null) return;
            lastSeTime[key] = now;
            AudioSource src = sePool[seCursor];
            seCursor = (seCursor + 1) % sePool.Length;
            src.pitch = chainLevel > 0 ? Mathf.Min(ChainPitchMax, 1f + chainLevel * ChainPitchStep) : 1f;
            src.PlayOneShot(clip, master * seVol);
        }

        private void Update()
        {
            if (bgmA == null || fadeT >= 1f) return;
            fadeT = Mathf.Min(1f, fadeT + Time.unscaledDeltaTime / BgmFadeSeconds);
            float target = master * bgmVol;
            bgmA.volume = target * fadeT;
            bgmB.volume = target * (1f - fadeT);
            if (fadeT >= 1f)
            {
                bgmB.Stop();
                bgmB.clip = null;
            }
        }
    }
}

/*
## このアセットの役割
GameAssets: 画像・音声・フォントをキーで引く参照表（ScriptableObject）。AudioManager: BGM のクロスフェード再生と効果音のプール再生。

## 他アセットとの依存関係
GameSettings（音量）。GameApp が生成・初期化し、全画面が使用します。キー名は Web版 assets/manifest.json と同じです。

## Inspector設定
Assets/DrawAndWitches/GameAssets.asset（セットアップメニューで自動作成）。Font に CJK 対応フォントを設定してください。

## USERがUnity上で行う作業
画像や音を差し替える場合は、GameAssets の該当キーの欄へ新しい Sprite / AudioClip をドラッグします。

## 注意点
WAV は「Load Type: Decompress On Load（効果音）／Streaming（BGM）」がおすすめです（セットアップメニューが自動設定します）。
*/
