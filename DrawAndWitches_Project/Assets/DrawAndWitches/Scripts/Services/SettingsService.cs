using System;
using System.Collections.Generic;
using System.Text;
using DrawAndWitches.Core;
using UnityEngine;

namespace DrawAndWitches.Services
{
    /// <summary>キー設定の対象操作（Web版 KEY_ACTIONS と同じ順）</summary>
    public enum KeyAction
    {
        Pause,
        Retry,
        FastForward,
        TogglePreview,
    }

    /// <summary>設定値（Web版 settings と同じスキーマで保存）</summary>
    public sealed class GameSettings
    {
        public const int Version = 1;
        public int MasterVolume = DefaultMaster;
        public int BgmVolume = DefaultBgm;
        public int SeVolume = DefaultSe;
        public string Language = "ja";
        public string Resolution = "auto";
        public string ScreenMode = "windowed";
        public int FpsLimit = DefaultFps;
        public Dictionary<KeyAction, string> Keys = new Dictionary<KeyAction, string>();
        public string PlayerName = string.Empty;
        public bool ShowPreview = true;

        public const int DefaultMaster = 80;
        public const int DefaultBgm = 60;
        public const int DefaultSe = 80;
        public const int DefaultFps = 60;

        public GameSettings Clone()
        {
            GameSettings s = (GameSettings)MemberwiseClone();
            s.Keys = new Dictionary<KeyAction, string>(Keys);
            return s;
        }

        public string KeyOf(KeyAction action)
        {
            return Keys.TryGetValue(action, out string code) ? code : SettingsService.DefaultKey(action);
        }
    }

    /// <summary>
    /// 設定の読み書きと適用（音量は AudioManager、解像度・画面モード・FPS は Screen / Application へ反映）。
    /// </summary>
    public sealed class SettingsService
    {
        public static readonly string[] Languages = { "ja", "en", "zh", "ko" };
        public static readonly string[] Resolutions = { "auto", "1280x720", "1600x900", "1920x1080", "2560x1440" };
        public static readonly string[] ScreenModes = { "windowed", "fullscreen" };
        /// <summary>0 = 無制限（ディスプレイ同期）</summary>
        public static readonly int[] FpsOptions = { 30, 60, 120, 0 };
        public static readonly KeyAction[] KeyActions = { KeyAction.Pause, KeyAction.Retry, KeyAction.FastForward, KeyAction.TogglePreview };
        private static readonly string[] KeyActionJsonNames = { "pause", "retry", "fastForward", "togglePreview" };
        private static readonly string[] DefaultKeys = { "Escape", "KeyR", "KeyF", "KeyG" };
        private static readonly HashSet<string> ReservedCodes = new HashSet<string>(StringComparer.Ordinal) { "MetaLeft", "MetaRight", "ContextMenu", "F5", "F11", "F12", "Tab" };
        private const int MaxVolume = 100;
        private const int MaxKeyCodeLength = 24;

        public GameSettings Current { get; private set; }

        /// <summary>設定変更通知。登録したら OnDestroy などで必ず解除すること。</summary>
        public event Action<GameSettings> Changed;

        public SettingsService()
        {
            Current = LocalSave.LoadJson(GameConstants.Storage.Settings, CreateDefault, Sanitize);
        }

        public static string DefaultKey(KeyAction action)
        {
            return DefaultKeys[(int)action];
        }

        public static GameSettings CreateDefault()
        {
            GameSettings s = new GameSettings();
            for (int i = 0; i < KeyActions.Length; i++) s.Keys[KeyActions[i]] = DefaultKeys[i];
            return s;
        }

        /// <summary>プレイヤー名の正規化（制御文字除去・前後空白除去・12文字まで）。Web版 sanitizePlayerName と同一。</summary>
        public static string SanitizePlayerName(string name)
        {
            if (name == null) return string.Empty;
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool control = c <= '\u001F' || (c >= '\u007F' && c <= '\u009F') || c == '\u2028' || c == '\u2029';
                if (!control) sb.Append(c);
            }
            string cleaned = sb.ToString().Trim();
            int count = 0;
            int end = 0;
            while (end < cleaned.Length && count < GameConstants.Online.PlayerNameMax)
            {
                end += char.IsHighSurrogate(cleaned[end]) && end + 1 < cleaned.Length && char.IsLowSurrogate(cleaned[end + 1]) ? 2 : 1;
                count++;
            }
            return cleaned.Substring(0, end);
        }

        public static bool IsValidKeyCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > MaxKeyCodeLength || ReservedCodes.Contains(code)) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            }
            return KeyCodeMap.TryToUnity(code, out KeyCode _);
        }

        private static int Volume(object v, int fallback)
        {
            return MiniJson.TryGetInt(v, out int n) && n >= 0 && n <= MaxVolume ? n : fallback;
        }

        private static string OneOf(object v, string[] options, string fallback)
        {
            return v is string s && Array.IndexOf(options, s) >= 0 ? s : fallback;
        }

        public static GameSettings Sanitize(object rawValue)
        {
            Dictionary<string, object> raw = MiniJson.AsObject(rawValue);
            if (raw == null || MiniJson.GetInt(raw, "version", -1) != GameSettings.Version) return null;
            GameSettings d = CreateDefault();
            GameSettings s = CreateDefault();
            s.MasterVolume = Volume(MiniJson.Get(raw, "masterVolume"), d.MasterVolume);
            s.BgmVolume = Volume(MiniJson.Get(raw, "bgmVolume"), d.BgmVolume);
            s.SeVolume = Volume(MiniJson.Get(raw, "seVolume"), d.SeVolume);
            s.Language = OneOf(MiniJson.Get(raw, "language"), Languages, d.Language);
            s.Resolution = OneOf(MiniJson.Get(raw, "resolution"), Resolutions, d.Resolution);
            s.ScreenMode = OneOf(MiniJson.Get(raw, "screenMode"), ScreenModes, d.ScreenMode);
            s.FpsLimit = MiniJson.TryGetInt(MiniJson.Get(raw, "fpsLimit"), out int fps) && Array.IndexOf(FpsOptions, fps) >= 0 ? fps : d.FpsLimit;
            s.PlayerName = SanitizePlayerName(MiniJson.Get(raw, "playerName") as string);
            s.ShowPreview = MiniJson.Get(raw, "showPreview") is bool b ? b : d.ShowPreview;
            Dictionary<string, object> keys = MiniJson.AsObject(MiniJson.Get(raw, "keys"));
            if (keys != null)
            {
                HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < KeyActions.Length; i++)
                {
                    string code = MiniJson.Get(keys, KeyActionJsonNames[i]) as string;
                    if (IsValidKeyCode(code) && used.Add(code)) s.Keys[KeyActions[i]] = code;
                }
                HashSet<string> distinct = new HashSet<string>(s.Keys.Values, StringComparer.Ordinal);
                if (distinct.Count != s.Keys.Count)
                {
                    for (int i = 0; i < KeyActions.Length; i++) s.Keys[KeyActions[i]] = DefaultKeys[i];
                }
            }
            return s;
        }

        public static JsonObject ToJson(GameSettings s)
        {
            JsonObject keys = new JsonObject();
            for (int i = 0; i < KeyActions.Length; i++) keys.Add(KeyActionJsonNames[i], s.KeyOf(KeyActions[i]));
            return new JsonObject()
                .Add("version", GameSettings.Version)
                .Add("masterVolume", s.MasterVolume)
                .Add("bgmVolume", s.BgmVolume)
                .Add("seVolume", s.SeVolume)
                .Add("language", s.Language)
                .Add("resolution", s.Resolution)
                .Add("screenMode", s.ScreenMode)
                .Add("fpsLimit", s.FpsLimit)
                .Add("keys", keys)
                .Add("playerName", s.PlayerName)
                .Add("showPreview", s.ShowPreview);
        }

        /// <summary>変更を検証して保存し、通知する。保存に失敗しても値はメモリ上で反映される（戻り値 false）。</summary>
        public bool Update(Action<GameSettings> mutate)
        {
            if (mutate == null) throw new ArgumentNullException(nameof(mutate));
            GameSettings draft = Current.Clone();
            mutate(draft);
            GameSettings next = Sanitize(MiniJson.TryParse(MiniJson.Serialize(ToJson(draft)), out object round, out string _) ? round : null);
            if (next == null) return false;
            Current = next;
            bool ok = LocalSave.SaveJson(GameConstants.Storage.Settings, ToJson(next));
            Action<GameSettings> handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(next);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Settings] change listener threw: " + e);
                }
            }
            return ok;
        }

        /// <summary>キーを割り当てる。他の操作と重複する場合は入れ替える。</summary>
        public bool BindKey(KeyAction action, string code)
        {
            if (!IsValidKeyCode(code)) return false;
            return Update(s =>
            {
                Dictionary<KeyAction, string> map = s.Keys;
                string previous = s.KeyOf(action);
                for (int i = 0; i < KeyActions.Length; i++)
                {
                    KeyAction other = KeyActions[i];
                    if (other != action && s.KeyOf(other) == code)
                    {
                        map[other] = previous;
                        break;
                    }
                }
                map[action] = code;
            });
        }

        public bool ResetKeys()
        {
            return Update(s =>
            {
                for (int i = 0; i < KeyActions.Length; i++) s.Keys[KeyActions[i]] = DefaultKeys[i];
            });
        }

        /// <summary>キーが押された瞬間か（設定されたキーを UnityEngine.KeyCode に変換して判定）</summary>
        public bool IsKeyDown(KeyAction action)
        {
            return KeyCodeMap.TryToUnity(Current.KeyOf(action), out KeyCode key) && UnityEngine.Input.GetKeyDown(key);
        }

        /// <summary>解像度・画面モード・FPS を反映する（エディター上では Screen.SetResolution は無視される）</summary>
        public static void ApplyDisplay(GameSettings s)
        {
            if (s.FpsLimit == 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = s.FpsLimit;
            }
            bool fullscreen = s.ScreenMode == "fullscreen";
            FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (s.Resolution == "auto")
            {
                if (Screen.fullScreenMode != mode)
                {
                    Resolution native = Screen.currentResolution;
                    Screen.SetResolution(fullscreen ? native.width : Screen.width, fullscreen ? native.height : Screen.height, mode);
                }
                return;
            }
            string[] parts = s.Resolution.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                Screen.SetResolution(w, h, mode);
            }
            else
            {
                Debug.LogError("[Settings] invalid resolution value '" + s.Resolution + "' (expected WIDTHxHEIGHT)");
            }
        }
    }

    /// <summary>
    /// Web版の KeyboardEvent.code 文字列（"KeyR" "Escape" など）と UnityEngine.KeyCode の相互変換。
    /// 保存形式を Web版と同じにするため、設定には code 文字列を保存する。
    /// </summary>
    public static class KeyCodeMap
    {
        private static readonly Dictionary<string, KeyCode> ToUnityMap = new Dictionary<string, KeyCode>(StringComparer.Ordinal);
        private static readonly Dictionary<KeyCode, string> ToWebMap = new Dictionary<KeyCode, string>();
        private const int FunctionKeyCount = 12;
        private const int DigitCount = 10;

        /// <summary>キー入力待ち（設定画面）で走査するキー一覧</summary>
        public static readonly List<KeyCode> Bindable = new List<KeyCode>();

        static KeyCodeMap()
        {
            for (char c = 'A'; c <= 'Z'; c++) Register("Key" + c, (KeyCode)Enum.Parse(typeof(KeyCode), c.ToString()));
            for (int d = 0; d < DigitCount; d++)
            {
                Register("Digit" + d, KeyCode.Alpha0 + d);
                Register("Numpad" + d, KeyCode.Keypad0 + d);
            }
            for (int f = 1; f <= FunctionKeyCount; f++) Register("F" + f, KeyCode.F1 + (f - 1));
            Register("Escape", KeyCode.Escape);
            Register("Space", KeyCode.Space);
            Register("Enter", KeyCode.Return);
            Register("Backspace", KeyCode.Backspace);
            Register("ArrowUp", KeyCode.UpArrow);
            Register("ArrowDown", KeyCode.DownArrow);
            Register("ArrowLeft", KeyCode.LeftArrow);
            Register("ArrowRight", KeyCode.RightArrow);
            Register("ShiftLeft", KeyCode.LeftShift);
            Register("ShiftRight", KeyCode.RightShift);
            Register("ControlLeft", KeyCode.LeftControl);
            Register("ControlRight", KeyCode.RightControl);
            Register("AltLeft", KeyCode.LeftAlt);
            Register("AltRight", KeyCode.RightAlt);
            Register("Insert", KeyCode.Insert);
            Register("Delete", KeyCode.Delete);
            Register("Home", KeyCode.Home);
            Register("End", KeyCode.End);
            Register("PageUp", KeyCode.PageUp);
            Register("PageDown", KeyCode.PageDown);
            Register("Minus", KeyCode.Minus);
            Register("Equal", KeyCode.Equals);
            Register("Comma", KeyCode.Comma);
            Register("Period", KeyCode.Period);
            Register("Slash", KeyCode.Slash);
            Register("Semicolon", KeyCode.Semicolon);
            Register("Quote", KeyCode.Quote);
            Register("BracketLeft", KeyCode.LeftBracket);
            Register("BracketRight", KeyCode.RightBracket);
            Register("Backslash", KeyCode.Backslash);
            Register("Backquote", KeyCode.BackQuote);
        }

        private static void Register(string web, KeyCode unity)
        {
            ToUnityMap[web] = unity;
            ToWebMap[unity] = web;
            Bindable.Add(unity);
        }

        public static bool TryToUnity(string webCode, out KeyCode key)
        {
            key = KeyCode.None;
            return webCode != null && ToUnityMap.TryGetValue(webCode, out key);
        }

        public static bool TryToWeb(KeyCode key, out string webCode)
        {
            return ToWebMap.TryGetValue(key, out webCode);
        }

        /// <summary>画面表示用ラベル（"KeyR" → "R"、"Digit1" → "1"）</summary>
        public static string Label(string webCode)
        {
            if (string.IsNullOrEmpty(webCode)) return "-";
            const string KeyPrefix = "Key";
            const string DigitPrefix = "Digit";
            if (webCode.StartsWith(KeyPrefix, StringComparison.Ordinal) && webCode.Length == KeyPrefix.Length + 1) return webCode.Substring(KeyPrefix.Length);
            if (webCode.StartsWith(DigitPrefix, StringComparison.Ordinal)) return webCode.Substring(DigitPrefix.Length);
            if (webCode == "Escape") return "Esc";
            return webCode;
        }
    }
}

/*
## このアセットの役割
設定（音量3種・言語・解像度・画面モード・FPS上限・キー割り当て・プレイヤー名・軌道プレビュー表示）の検証・保存・適用と、Web版キーコード⇔Unity KeyCode の変換を行います。

## 他アセットとの依存関係
LocalSave / MiniJson に依存。GameApp（起動時適用）、SettingsScreen（UI）、AudioManager（音量）、BattleScreen（キー入力）から使用されます。

## Inspector設定
なし。

## USERがUnity上で行う作業
Player Settings > Other Settings > Active Input Handling を「Both」または「Input Manager (Old)」にしてください（キー入力に UnityEngine.Input を使用します）。

## 注意点
解像度変更はビルドしたアプリでのみ有効です（エディターの Game ビューでは Screen.SetResolution が無視されます）。
*/
