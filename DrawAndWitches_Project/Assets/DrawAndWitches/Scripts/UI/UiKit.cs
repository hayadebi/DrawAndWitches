using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrawAndWitches.UI
{
    public enum ButtonVariant
    {
        Normal,
        Primary,
        Danger,
        Ghost,
        Gold,
    }

    /// <summary>
    /// uGUI の部品をコードで生成するユーティリティ。プレハブを使わないため、
    /// USER が Unity 上で UI を組み立てる作業は不要（セットアップメニューで Boot シーンを作るだけ）。
    /// 角丸・円・グラデーションのスプライトは起動時に生成する。
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color32(0x1d, 0x14, 0x3d, 0xe6);
        public static readonly Color PanelBorder = new Color32(0x8f, 0x76, 0xd8, 0xff);
        public static readonly Color TextColor = new Color32(0xf3, 0xee, 0xff, 0xff);
        public static readonly Color SubTextColor = new Color32(0xb9, 0xae, 0xdc, 0xff);
        public static readonly Color Gold = new Color32(0xff, 0xd1, 0x5c, 0xff);
        public static readonly Color Pink = new Color32(0xff, 0x6f, 0xd8, 0xff);
        public static readonly Color Danger = new Color32(0xff, 0x5c, 0x7a, 0xff);
        public static readonly Color Success = new Color32(0x9f, 0xe8, 0xc6, 0xff);

        private const int RoundSize = 64;
        private const int RoundRadius = 18;
        private const int CircleSize = 128;

        public static Font Font { get; private set; }
        public static Sprite Rounded { get; private set; }
        public static Sprite RoundedOutline { get; private set; }
        public static Sprite Circle { get; private set; }
        public static Sprite SoftCircle { get; private set; }
        public static Sprite VerticalFade { get; private set; }

        /// <summary>クリック時に鳴らす効果音（AudioManager が設定）</summary>
        public static Action ClickSound;
        public static Action BackSound;

        /// <summary>フォント未設定時に使う OS フォント（日本語・中国語・韓国語の表示に対応する候補を優先順に並べる）</summary>
        private static readonly string[] OsFontCandidates =
        {
            "Yu Gothic UI", "Meiryo UI", "Meiryo", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Noto Sans JP",
            "Microsoft YaHei UI", "PingFang SC", "Noto Sans CJK SC", "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR",
            "Segoe UI", "Helvetica Neue", "Arial",
        };

        /// <summary>インストール済みの候補だけで動的フォントを作る（候補が無ければ null）</summary>
        private static Font CreateOsFont()
        {
            try
            {
                HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
                List<string> names = new List<string>();
                foreach (string n in OsFontCandidates)
                {
                    if (installed.Contains(n)) names.Add(n);
                }
                if (names.Count == 0) return null;
                return Font.CreateDynamicFontFromOSFont(names.ToArray(), 32);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UiKit] OS font unavailable: " + e.Message);
                return null;
            }
        }

        public static void Init(Font font)
        {
            Font = font;
            if (Font == null)
            {
                Font = CreateOsFont();
            }
            if (Font == null)
            {
                Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (Font == null)
            {
                throw new InvalidOperationException("UiKit: フォントがありません。GameAssets の Font に日本語対応フォント（例: Noto Sans JP）を設定してください。");
            }
            Rounded = MakeRounded(false);
            RoundedOutline = MakeRounded(true);
            Circle = MakeCircle(false);
            SoftCircle = MakeCircle(true);
            VerticalFade = MakeFade();
        }

        /// <summary>
        /// スキン画像を差し替える（GameAssets の UI スキン。Setup Project が PNG を生成して設定）。
        /// null の項目は自動生成のまま。焼き込み UI はこの画像アセットを参照するため、シーン保存後も表示が保たれる。
        /// </summary>
        public static void SetSkin(Sprite rounded, Sprite roundedOutline, Sprite circle, Sprite softCircle, Sprite verticalFade)
        {
            if (rounded != null) Rounded = rounded;
            if (roundedOutline != null) RoundedOutline = roundedOutline;
            if (circle != null) Circle = circle;
            if (softCircle != null) SoftCircle = softCircle;
            if (verticalFade != null) VerticalFade = verticalFade;
        }

        /// <summary>スキン画像の生成（エディター拡張が PNG として書き出す用）。id: rounded / rounded_outline / circle / soft_circle / fade</summary>
        public static Texture2D CreateSkinTexture(string id)
        {
            switch (id)
            {
                case "rounded": return MakeRounded(false).texture;
                case "rounded_outline": return MakeRounded(true).texture;
                case "circle": return MakeCircle(false).texture;
                case "soft_circle": return MakeCircle(true).texture;
                case "fade": return MakeFade().texture;
                default: throw new ArgumentException("unknown skin id: " + id);
            }
        }

        /// <summary>スキン画像の9スライス境界（px）。rounded 系のみ。</summary>
        public static Vector4 SkinBorder(string id)
        {
            return id == "rounded" || id == "rounded_outline" ? new Vector4(RoundRadius, RoundRadius, RoundRadius, RoundRadius) : Vector4.zero;
        }

        /// <summary>再生中・エディター双方で安全に破棄する</summary>
        public static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private static Sprite MakeRounded(bool outline)
        {
            Texture2D tex = new Texture2D(RoundSize, RoundSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = outline ? "dw_rounded_outline" : "dw_rounded" };
            Color32[] px = new Color32[RoundSize * RoundSize];
            const float Border = 2.5f;
            for (int y = 0; y < RoundSize; y++)
            {
                for (int x = 0; x < RoundSize; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, RoundRadius, RoundSize - RoundRadius);
                    float cy = Mathf.Clamp(y + 0.5f, RoundRadius, RoundSize - RoundRadius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(RoundRadius - d + 0.5f);
                    if (outline)
                    {
                        a *= Mathf.Clamp01(d - (RoundRadius - Border) + 0.5f);
                    }
                    px[y * RoundSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, RoundSize, RoundSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(RoundRadius, RoundRadius, RoundRadius, RoundRadius));
        }

        private static Sprite MakeCircle(bool soft)
        {
            Texture2D tex = new Texture2D(CircleSize, CircleSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = soft ? "dw_soft_circle" : "dw_circle" };
            Color32[] px = new Color32[CircleSize * CircleSize];
            float r = CircleSize * 0.5f;
            for (int y = 0; y < CircleSize; y++)
            {
                for (int x = 0; x < CircleSize; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = soft ? Mathf.Pow(Mathf.Clamp01(1f - d / r), 2f) : Mathf.Clamp01(r - d);
                    px[y * CircleSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, CircleSize, CircleSize), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeFade()
        {
            const int H = 64;
            Texture2D tex = new Texture2D(1, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "dw_fade" };
            for (int y = 0; y < H; y++)
            {
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, (float)y / (H - 1)));
            }
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f));
        }

        // -------------------------------------------------------------- レイアウト

        /// <summary>
        /// UI 要素を用意する。焼き込み済みの同名・同種の要素があれば再利用し（UiBinder）、無ければ新規作成する。
        /// kind: rect / img / text / slider / scroll
        /// </summary>
        public static RectTransform Rect(Transform parent, string name, string kind = "rect")
        {
            RectTransform reused = UiBinder.Acquire(parent, name, kind);
            if (reused != null) return reused;
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (UiBinder.BakeMode) go.AddComponent<UiBaked>().kind = kind;
            return rt;
        }

        /// <summary>同名要素の再利用で別種のレイアウトが残っていた場合に取り除く（同一オブジェクトに縦横は共存できない）</summary>
        private static void RemoveConflicting<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>アンカー位置（0〜1）を基準に、中心座標とサイズで配置する</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            RectTransform rt = Rect(parent, name, "img");
            Image img = rt.gameObject.GetOrAdd<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return img;
        }

        public static Image Panel(Transform parent, string name, bool border = true)
        {
            Image bg = Image(parent, name, Rounded, PanelColor, true);
            if (border)
            {
                Image line = Image(bg.transform, "Border", RoundedOutline, new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.55f));
                Stretch(line.rectTransform);
            }
            return bg;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect(parent, "Label", "text");
            Text t = rt.gameObject.GetOrAdd<Text>();
            t.font = Font;
            t.text = text ?? string.Empty;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        public static Shadow AddShadow(Graphic g, Color color, Vector2 distance)
        {
            Shadow s = g.gameObject.GetOrAdd<Shadow>();
            s.effectColor = color;
            s.effectDistance = distance;
            return s;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, ButtonVariant variant = ButtonVariant.Normal, int fontSize = 26)
        {
            Color baseColor;
            Color textColor = TextColor;
            switch (variant)
            {
                case ButtonVariant.Primary:
                    baseColor = new Color32(0xc0, 0x4c, 0xd8, 0xff);
                    break;
                case ButtonVariant.Danger:
                    baseColor = new Color32(0x8c, 0x23, 0x45, 0xff);
                    break;
                case ButtonVariant.Gold:
                    baseColor = new Color32(0xd9, 0xa4, 0x2e, 0xff);
                    textColor = new Color32(0x2a, 0x17, 0x08, 0xff);
                    break;
                case ButtonVariant.Ghost:
                    baseColor = new Color32(0x2a, 0x1f, 0x52, 0xb0);
                    textColor = SubTextColor;
                    break;
                default:
                    baseColor = new Color32(0x3a, 0x2a, 0x72, 0xff);
                    break;
            }
            Image bg = Image(parent, "Button", Rounded, baseColor, true);
            Image line = Image(bg.transform, "Border", RoundedOutline, new Color(1f, 1f, 1f, variant == ButtonVariant.Primary ? 0.45f : 0.18f));
            Stretch(line.rectTransform);
            Button b = bg.gameObject.GetOrAdd<Button>();
            b.targetGraphic = bg;
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.2f, 1.35f, 1f);
            cb.pressedColor = new Color(0.8f, 0.75f, 0.9f, 1f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.6f, 0.6f);
            cb.colorMultiplier = 1.2f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            Navigation nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            Text t = Label(bg.transform, text, fontSize, textColor);
            Stretch(t.rectTransform, 12, 12, 4, 4);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(10, fontSize / 2);
            t.resizeTextMaxSize = fontSize;
            if (onClick != null)
            {
                b.onClick.AddListener(() =>
                {
                    Action s = variant == ButtonVariant.Ghost ? BackSound : ClickSound;
                    s?.Invoke();
                });
                b.onClick.AddListener(onClick);
            }
            return b;
        }

        public static Text ButtonLabel(Button b)
        {
            return b.GetComponentInChildren<Text>(true);
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChange)
        {
            RectTransform root = Rect(parent, "Slider", "slider");
            Slider s = root.gameObject.GetOrAdd<Slider>();
            Image bg = Image(root, "Track", Rounded, new Color32(0x2a, 0x1f, 0x52, 0xff), true);
            Stretch(bg.rectTransform, 0, 0, 14, 14);
            RectTransform fillArea = Stretch(Rect(root, "FillArea"), 0, 0, 14, 14);
            Image fill = Image(fillArea, "Fill", Rounded, Pink);
            fill.rectTransform.sizeDelta = Vector2.zero;
            RectTransform handleArea = Stretch(Rect(root, "HandleArea"), 12, 12, 0, 0);
            Image handle = Image(handleArea, "Handle", Circle, Color.white, true);
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = true;
            s.value = value;
            Navigation nav = s.navigation;
            nav.mode = Navigation.Mode.None;
            s.navigation = nav;
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static InputField Input(Transform parent, string value, string placeholder, int maxChars, UnityAction<string> onEnd)
        {
            Image bg = Image(parent, "Input", Rounded, new Color32(0x12, 0x0c, 0x2a, 0xff), true);
            Image line = Image(bg.transform, "Border", RoundedOutline, new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.7f));
            Stretch(line.rectTransform);
            InputField f = bg.gameObject.GetOrAdd<InputField>();
            Text text = Label(bg.transform, string.Empty, 24, TextColor, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 16, 16, 4, 4);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text ph = Label(bg.transform, placeholder, 24, new Color(SubTextColor.r, SubTextColor.g, SubTextColor.b, 0.6f), TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 16, 16, 4, 4);
            f.textComponent = text;
            f.placeholder = ph;
            f.characterLimit = maxChars;
            f.lineType = InputField.LineType.SingleLine;
            f.text = value ?? string.Empty;
            Navigation nav = f.navigation;
            nav.mode = Navigation.Mode.None;
            f.navigation = nav;
            if (onEnd != null) f.onEndEdit.AddListener(onEnd);
            return f;
        }

        /// <summary>縦スクロールリスト。戻り値は要素を追加する Content。</summary>
        public static RectTransform ScrollList(Transform parent, out ScrollRect scroll, float spacing = 12f, int padding = 8)
        {
            RectTransform root = Rect(parent, "Scroll", "scroll");
            Image hit = root.gameObject.GetOrAdd<Image>();
            hit.color = new Color(0, 0, 0, 0);
            scroll = root.gameObject.GetOrAdd<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            RectTransform viewport = Stretch(Rect(root, "Viewport"));
            viewport.gameObject.GetOrAdd<RectMask2D>();
            RectTransform content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.GetOrAdd<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.gameObject.GetOrAdd<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        /// <summary>レイアウトグループ内で固定の高さを持たせる</summary>
        public static LayoutElement Height(Component c, float height)
        {
            LayoutElement le = c.gameObject.GetOrAdd<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return le;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing, TextAnchor align = TextAnchor.MiddleCenter)
        {
            RemoveConflicting<VerticalLayoutGroup>(rt.gameObject);
            HorizontalLayoutGroup h = rt.gameObject.GetOrAdd<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return h;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, TextAnchor align = TextAnchor.UpperCenter)
        {
            RemoveConflicting<HorizontalLayoutGroup>(rt.gameObject);
            VerticalLayoutGroup v = rt.gameObject.GetOrAdd<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        /// <summary>全面を覆って下のクリックを遮る半透明の幕</summary>
        public static Image Blocker(Transform parent, float alpha)
        {
            Image img = Image(parent, "Blocker", null, new Color(0.03f, 0.01f, 0.08f, alpha), true);
            Stretch(img.rectTransform);
            return img;
        }

        /// <summary>イベントシステムがなければ作る（Boot シーン以外から起動した場合の保険）</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(es);
        }

        public static string FormatNumber(long n)
        {
            return n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

/*
## このアセットの役割
uGUI 部品（パネル・ラベル・ボタン・スライダー・入力欄・スクロールリスト）をコードで生成し、配色と角丸・円スプライトを一元管理します。

## 他アセットとの依存関係
UnityEngine.UI（uGUI パッケージ。Unity 6 の新規プロジェクトに標準で含まれます）。全画面スクリプトから使用されます。

## Inspector設定
なし。フォントは GameAssets の Font 欄から渡されます。

## USERがUnity上で行う作業
日本語・中国語・韓国語を確実に表示するため、GameAssets に CJK 対応フォント（Noto Sans CJK など）を設定してください。未設定でも OS のフォールバックフォントで表示されますが、環境により文字化けすることがあります。

## 注意点
uGUI の Text（レガシー）を使っています。TextMeshPro に置き換える場合は Label() と Button() の中身を差し替えるだけで全画面に反映されます。
*/
