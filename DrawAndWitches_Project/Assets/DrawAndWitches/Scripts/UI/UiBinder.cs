using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.UI
{
    /// <summary>
    /// UI 1要素の見た目（配置・色・画像・文字・レイアウト）のスナップショット。
    /// 焼き込み時の値（reference）とシーン上の値を比べ、ユーザーが変更した項目だけをビットで表す。
    /// </summary>
    [Serializable]
    public struct UiSnap
    {
        public const ulong AnchorMin = 1UL << 0;
        public const ulong AnchorMax = 1UL << 1;
        public const ulong Pivot = 1UL << 2;
        public const ulong Position = 1UL << 3;
        public const ulong Size = 1UL << 4;
        public const ulong Scale = 1UL << 5;
        public const ulong Rotation = 1UL << 6;
        public const ulong Color = 1UL << 7;
        public const ulong Raycast = 1UL << 8;
        public const ulong SpriteBit = 1UL << 9;
        public const ulong ImageType = 1UL << 10;
        public const ulong PreserveAspect = 1UL << 11;
        public const ulong FontBit = 1UL << 12;
        public const ulong FontSize = 1UL << 13;
        public const ulong FontStyleBit = 1UL << 14;
        public const ulong Alignment = 1UL << 15;
        public const ulong LineSpacing = 1UL << 16;
        public const ulong BestFit = 1UL << 17;
        public const ulong Overflow = 1UL << 18;
        public const ulong LeMin = 1UL << 19;
        public const ulong LePreferred = 1UL << 20;
        public const ulong LeFlexible = 1UL << 21;
        public const ulong LgSpacing = 1UL << 22;
        public const ulong LgPadding = 1UL << 23;
        public const ulong LgAlignment = 1UL << 24;
        public const ulong GridCell = 1UL << 25;
        public const ulong GridSpacing = 1UL << 26;
        public const ulong ShadowColor = 1UL << 27;
        public const ulong ShadowDistance = 1UL << 28;

        public bool valid;
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
        public Vector3 localScale, localEuler;

        public bool hasGraphic;
        public UnityEngine.Color color;
        public bool raycastTarget;

        public bool hasImage;
        public Sprite sprite;
        public Image.Type imageType;
        public bool preserveAspect;

        public bool hasText;
        public Font font;
        public int fontSize;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public float lineSpacing;
        public bool bestFit;
        public int bestFitMin, bestFitMax;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;

        public bool hasLayoutElement;
        public float minWidth, minHeight, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight;

        public bool hasLayoutGroup;
        public float lgSpacing;
        public int padLeft, padRight, padTop, padBottom;
        public TextAnchor lgAlignment;

        public bool hasGrid;
        public Vector2 gridCell, gridSpacing;

        public bool hasShadow;
        public UnityEngine.Color shadowColor;
        public Vector2 shadowDistance;

        public static UiSnap Capture(GameObject go)
        {
            UiSnap s = new UiSnap();
            if (go == null) return s;
            RectTransform rt = go.transform as RectTransform;
            if (rt == null) return s;
            s.valid = true;
            s.anchorMin = rt.anchorMin;
            s.anchorMax = rt.anchorMax;
            s.pivot = rt.pivot;
            s.anchoredPosition = rt.anchoredPosition;
            s.sizeDelta = rt.sizeDelta;
            s.localScale = rt.localScale;
            s.localEuler = rt.localEulerAngles;

            Graphic g = go.GetComponent<Graphic>();
            if (g != null)
            {
                s.hasGraphic = true;
                s.color = g.color;
                s.raycastTarget = g.raycastTarget;
            }
            Image img = go.GetComponent<Image>();
            if (img != null)
            {
                s.hasImage = true;
                s.sprite = img.sprite;
                s.imageType = img.type;
                s.preserveAspect = img.preserveAspect;
            }
            Text t = go.GetComponent<Text>();
            if (t != null)
            {
                s.hasText = true;
                s.font = t.font;
                s.fontSize = t.fontSize;
                s.fontStyle = t.fontStyle;
                s.alignment = t.alignment;
                s.lineSpacing = t.lineSpacing;
                s.bestFit = t.resizeTextForBestFit;
                s.bestFitMin = t.resizeTextMinSize;
                s.bestFitMax = t.resizeTextMaxSize;
                s.horizontalOverflow = t.horizontalOverflow;
                s.verticalOverflow = t.verticalOverflow;
            }
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                s.hasLayoutElement = true;
                s.minWidth = le.minWidth;
                s.minHeight = le.minHeight;
                s.preferredWidth = le.preferredWidth;
                s.preferredHeight = le.preferredHeight;
                s.flexibleWidth = le.flexibleWidth;
                s.flexibleHeight = le.flexibleHeight;
            }
            HorizontalOrVerticalLayoutGroup lg = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (lg != null)
            {
                s.hasLayoutGroup = true;
                s.lgSpacing = lg.spacing;
                s.padLeft = lg.padding.left;
                s.padRight = lg.padding.right;
                s.padTop = lg.padding.top;
                s.padBottom = lg.padding.bottom;
                s.lgAlignment = lg.childAlignment;
            }
            GridLayoutGroup grid = go.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                s.hasGrid = true;
                s.gridCell = grid.cellSize;
                s.gridSpacing = grid.spacing;
            }
            Shadow sh = go.GetComponent<Shadow>();
            if (sh != null)
            {
                s.hasShadow = true;
                s.shadowColor = sh.effectColor;
                s.shadowDistance = sh.effectDistance;
            }
            return s;
        }

        private static bool Eq(float a, float b) => Mathf.Abs(a - b) <= 0.0005f;
        private static bool Eq(Vector2 a, Vector2 b) => Eq(a.x, b.x) && Eq(a.y, b.y);
        private static bool Eq(Vector3 a, Vector3 b) => Eq(a.x, b.x) && Eq(a.y, b.y) && Eq(a.z, b.z);
        private static bool Eq(UnityEngine.Color a, UnityEngine.Color b) => Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;

        /// <summary>this（シーン値）と reference（焼き込み時の値）で異なる項目のビット</summary>
        public ulong DiffMask(UiSnap reference)
        {
            if (!valid || !reference.valid) return 0;
            ulong m = 0;
            if (!Eq(anchorMin, reference.anchorMin)) m |= AnchorMin;
            if (!Eq(anchorMax, reference.anchorMax)) m |= AnchorMax;
            if (!Eq(pivot, reference.pivot)) m |= Pivot;
            if (!Eq(anchoredPosition, reference.anchoredPosition)) m |= Position;
            if (!Eq(sizeDelta, reference.sizeDelta)) m |= Size;
            if (!Eq(localScale, reference.localScale)) m |= Scale;
            if (!Eq(localEuler, reference.localEuler)) m |= Rotation;
            if (hasGraphic && reference.hasGraphic)
            {
                if (!Eq(color, reference.color)) m |= Color;
                if (raycastTarget != reference.raycastTarget) m |= Raycast;
            }
            if (hasImage && reference.hasImage)
            {
                if (sprite != reference.sprite) m |= SpriteBit;
                if (imageType != reference.imageType) m |= ImageType;
                if (preserveAspect != reference.preserveAspect) m |= PreserveAspect;
            }
            if (hasText && reference.hasText)
            {
                if (font != reference.font) m |= FontBit;
                if (fontSize != reference.fontSize) m |= FontSize;
                if (fontStyle != reference.fontStyle) m |= FontStyleBit;
                if (alignment != reference.alignment) m |= Alignment;
                if (!Eq(lineSpacing, reference.lineSpacing)) m |= LineSpacing;
                if (bestFit != reference.bestFit || bestFitMin != reference.bestFitMin || bestFitMax != reference.bestFitMax) m |= BestFit;
                if (horizontalOverflow != reference.horizontalOverflow || verticalOverflow != reference.verticalOverflow) m |= Overflow;
            }
            if (hasLayoutElement && reference.hasLayoutElement)
            {
                if (!Eq(minWidth, reference.minWidth) || !Eq(minHeight, reference.minHeight)) m |= LeMin;
                if (!Eq(preferredWidth, reference.preferredWidth) || !Eq(preferredHeight, reference.preferredHeight)) m |= LePreferred;
                if (!Eq(flexibleWidth, reference.flexibleWidth) || !Eq(flexibleHeight, reference.flexibleHeight)) m |= LeFlexible;
            }
            if (hasLayoutGroup && reference.hasLayoutGroup)
            {
                if (!Eq(lgSpacing, reference.lgSpacing)) m |= LgSpacing;
                if (padLeft != reference.padLeft || padRight != reference.padRight || padTop != reference.padTop || padBottom != reference.padBottom) m |= LgPadding;
                if (lgAlignment != reference.lgAlignment) m |= LgAlignment;
            }
            if (hasGrid && reference.hasGrid)
            {
                if (!Eq(gridCell, reference.gridCell)) m |= GridCell;
                if (!Eq(gridSpacing, reference.gridSpacing)) m |= GridSpacing;
            }
            if (hasShadow && reference.hasShadow)
            {
                if (!Eq(shadowColor, reference.shadowColor)) m |= ShadowColor;
                if (!Eq(shadowDistance, reference.shadowDistance)) m |= ShadowDistance;
            }
            return m;
        }

        /// <summary>mask に含まれる項目だけを go に書き戻す</summary>
        public void Apply(GameObject go, ulong m)
        {
            if (go == null || m == 0 || !valid) return;
            RectTransform rt = go.transform as RectTransform;
            if (rt == null) return;
            if ((m & AnchorMin) != 0) rt.anchorMin = anchorMin;
            if ((m & AnchorMax) != 0) rt.anchorMax = anchorMax;
            if ((m & Pivot) != 0) rt.pivot = pivot;
            if ((m & Size) != 0) rt.sizeDelta = sizeDelta;
            if ((m & Position) != 0) rt.anchoredPosition = anchoredPosition;
            if ((m & Scale) != 0) rt.localScale = localScale;
            if ((m & Rotation) != 0) rt.localEulerAngles = localEuler;
            Graphic g = go.GetComponent<Graphic>();
            if (g != null && hasGraphic)
            {
                if ((m & Color) != 0) g.color = color;
                if ((m & Raycast) != 0) g.raycastTarget = raycastTarget;
            }
            Image img = go.GetComponent<Image>();
            if (img != null && hasImage)
            {
                if ((m & SpriteBit) != 0) img.sprite = sprite;
                if ((m & ImageType) != 0) img.type = imageType;
                if ((m & PreserveAspect) != 0) img.preserveAspect = preserveAspect;
            }
            Text t = go.GetComponent<Text>();
            if (t != null && hasText)
            {
                if ((m & FontBit) != 0 && font != null) t.font = font;
                if ((m & FontSize) != 0) t.fontSize = fontSize;
                if ((m & FontStyleBit) != 0) t.fontStyle = fontStyle;
                if ((m & Alignment) != 0) t.alignment = alignment;
                if ((m & LineSpacing) != 0) t.lineSpacing = lineSpacing;
                if ((m & BestFit) != 0)
                {
                    t.resizeTextForBestFit = bestFit;
                    t.resizeTextMinSize = bestFitMin;
                    t.resizeTextMaxSize = bestFitMax;
                }
                if ((m & Overflow) != 0)
                {
                    t.horizontalOverflow = horizontalOverflow;
                    t.verticalOverflow = verticalOverflow;
                }
            }
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null && hasLayoutElement)
            {
                if ((m & LeMin) != 0)
                {
                    le.minWidth = minWidth;
                    le.minHeight = minHeight;
                }
                if ((m & LePreferred) != 0)
                {
                    le.preferredWidth = preferredWidth;
                    le.preferredHeight = preferredHeight;
                }
                if ((m & LeFlexible) != 0)
                {
                    le.flexibleWidth = flexibleWidth;
                    le.flexibleHeight = flexibleHeight;
                }
            }
            HorizontalOrVerticalLayoutGroup lg = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (lg != null && hasLayoutGroup)
            {
                if ((m & LgSpacing) != 0) lg.spacing = lgSpacing;
                if ((m & LgPadding) != 0) lg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
                if ((m & LgAlignment) != 0) lg.childAlignment = lgAlignment;
            }
            GridLayoutGroup grid = go.GetComponent<GridLayoutGroup>();
            if (grid != null && hasGrid)
            {
                if ((m & GridCell) != 0) grid.cellSize = gridCell;
                if ((m & GridSpacing) != 0) grid.spacing = gridSpacing;
            }
            Shadow sh = go.GetComponent<Shadow>();
            if (sh != null && hasShadow)
            {
                if ((m & ShadowColor) != 0) sh.effectColor = shadowColor;
                if ((m & ShadowDistance) != 0) sh.effectDistance = shadowDistance;
            }
        }
    }

    /// <summary>
    /// Setup Project でシーンへ焼き込まれた UI 要素の印。
    /// 実行時は同じ名前・種類の要素がコードから要求されたときに再利用され、シーンで変更した見た目が優先されます。
    /// </summary>
    //[DisallowMultipleComponent]
    //[AddComponentMenu("DRAW&WITCHES/UI Baked Element")]
    //public sealed class UiBaked : MonoBehaviour
    //{
    //    [Tooltip("要素の種類（rect / img / text / slider / scroll）。コードの要求と一致した要素だけが再利用されます。変更しないでください。")]
    //    public string kind = "rect";

    //    [Tooltip("ON にすると、この要素は実行時に常に非表示になります（装飾を消したい場合など）。")]
    //    public bool forceHidden;

    //    [Tooltip("ON にすると、シーンで変更した見た目を適用せず、常にコードの値を使います。")]
    //    public bool ignoreSceneOverrides;

    //    [HideInInspector] public UiSnap reference;

    //    [NonSerialized] internal bool bound;
    //    [NonSerialized] internal Transform pristine;
    //    [NonSerialized] internal UiSnap scene;
    //    [NonSerialized] internal ulong mask;
    //}

    /// <summary>
    /// 焼き込み UI と実行時コードの橋渡し。UiKit.Rect から呼ばれる。
    /// 1. 親の子に未使用の同名・同種の焼き込み要素があれば再利用
    /// 2. 無ければ親の「原本（テンプレート）」の子を複製
    /// 3. どちらも無ければ null（呼び出し側が新規作成）
    /// Flush で「シーン上で変更された項目」だけをシーン値に戻し、使われなかった焼き込み要素は非表示にする。
    /// </summary>
    public static class UiBinder
    {
        /// <summary>true の間は新規作成した要素に UiBaked を付ける（Setup Project の焼き込み中）</summary>
        public static bool BakeMode;

        private static readonly Dictionary<Transform, Transform> layerPristine = new Dictionary<Transform, Transform>();
        private static readonly List<UiBaked> pending = new List<UiBaked>();

        /// <summary>実行時の親（画面レイヤーなど）に対応するテンプレート親を登録する</summary>
        public static void SetPristine(Transform live, Transform pristine)
        {
            if (live == null) return;
            if (pristine == null) layerPristine.Remove(live);
            else layerPristine[live] = pristine;
        }

        public static void ClearPristine()
        {
            layerPristine.Clear();
            pending.Clear();
        }

        private static Transform PristineOf(Transform parent, out bool isLayer)
        {
            isLayer = false;
            if (parent == null) return null;
            if (layerPristine.TryGetValue(parent, out Transform p) && p != null)
            {
                isLayer = true;
                return p;
            }
            UiBaked b = parent.GetComponent<UiBaked>();
            return b != null ? b.pristine : null;
        }

        private static bool Matches(Transform t, string name, string kind, bool requireUnbound)
        {
            if (t == null || t.name != name) return false;
            UiBaked b = t.GetComponent<UiBaked>();
            if (b == null || b.kind != kind) return false;
            return !requireUnbound || !b.bound;
        }

        /// <summary>再利用または複製した要素を返す。該当なしは null。</summary>
        public static RectTransform Acquire(Transform parent, string name, string kind)
        {
            if (BakeMode || parent == null || !Application.isPlaying) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (Matches(c, name, kind, true))
                {
                    UiBaked b = c.GetComponent<UiBaked>();
                    Bind(b, false);
                    return c as RectTransform;
                }
            }
            Transform pristineParent = PristineOf(parent, out bool isLayer);
            if (pristineParent == null) return null;
            // 同名の兄弟が既に k 個使われていれば、テンプレート側も k 番目の同名要素を使う（無ければ最初のもの）
            int already = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (Matches(parent.GetChild(i), name, kind, false)) already++;
            }
            Transform chosen = null;
            int seen = 0;
            for (int i = 0; i < pristineParent.childCount; i++)
            {
                Transform src = pristineParent.GetChild(i);
                if (!Matches(src, name, kind, false)) continue;
                if (chosen == null) chosen = src;
                if (seen == already)
                {
                    chosen = src;
                    break;
                }
                seen++;
            }
            if (chosen == null) return null;
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(chosen.gameObject, parent, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UiBinder] template clone failed (" + name + "): " + e.Message);
                return null;
            }
            clone.name = name;
            MapPristine(clone.transform, chosen);
            Bind(clone.GetComponent<UiBaked>(), isLayer);
            return clone.transform as RectTransform;
        }

        private static void MapPristine(Transform clone, Transform src)
        {
            UiBaked b = clone.GetComponent<UiBaked>();
            if (b != null)
            {
                b.pristine = src;
                b.bound = false;
            }
            int n = Mathf.Min(clone.childCount, src.childCount);
            for (int i = 0; i < n; i++) MapPristine(clone.GetChild(i), src.GetChild(i));
        }

        private static void Bind(UiBaked b, bool forceActive)
        {
            if (b == null) return;
            b.bound = true;
            b.scene = UiSnap.Capture(b.gameObject);
            b.mask = b.ignoreSceneOverrides ? 0 : b.scene.DiffMask(b.reference);
            bool active = forceActive || !b.forceHidden;
            if (b.gameObject.activeSelf != active) b.gameObject.SetActive(active);
            pending.Add(b);
        }

        /// <summary>
        /// 直近に再利用した要素へシーン上の変更を適用し、使われなかった焼き込み子要素を隠す。
        /// コードが値を書き終えた後（画面構築の直後・毎フレーム LateUpdate）に呼ぶ。
        /// </summary>
        public static void Flush()
        {
            if (pending.Count == 0) return;
            UiBaked[] list = pending.ToArray();
            pending.Clear();
            foreach (UiBaked b in list)
            {
                if (b == null) continue;
                try
                {
                    if (b.mask != 0) b.scene.Apply(b.gameObject, b.mask);
                    Transform t = b.transform;
                    for (int i = 0; i < t.childCount; i++)
                    {
                        Transform c = t.GetChild(i);
                        UiBaked cb = c.GetComponent<UiBaked>();
                        if (cb != null && !cb.bound && c.gameObject.activeSelf) c.gameObject.SetActive(false);
                    }
                    if (b.forceHidden && b.gameObject.activeSelf) b.gameObject.SetActive(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[UiBinder] flush failed (" + b.name + "): " + e.Message);
                }
            }
        }

        /// <summary>焼き込み完了時：全要素の現在値を reference として記録する</summary>
        public static int RecordReferences(Transform root)
        {
            if (root == null) return 0;
            UiBaked[] all = root.GetComponentsInChildren<UiBaked>(true);
            foreach (UiBaked b in all) b.reference = UiSnap.Capture(b.gameObject);
            return all.Length;
        }
    }

    public static class ComponentExtensions
    {
        /// <summary>既にあれば取得、無ければ追加（焼き込み要素の再利用で同じ部品を二重に付けないため）</summary>
        public static T GetOrAdd<T>(this GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}

/*
## このアセットの役割
Setup Project が焼き込んだ UI（Canvas/Templates 以下）を実行時に再利用する仕組みです。コードは従来どおり UiKit で UI を「作る」だけで、同じ名前・種類の焼き込み要素があればそれが使われます。

## 他アセットとの依存関係
UiKit.Rect から呼ばれます。GameApp が画面・モーダル・通知レイヤーのテンプレートを SetPristine で登録し、LateUpdate で Flush します。

## Inspector設定
UiBaked: Force Hidden（常に非表示）、Ignore Scene Overrides（シーン変更を使わない）。Kind は変更しないでください。

## USERがUnity上で行う作業
Canvas/Templates/Screens の各画面を表示して、位置・サイズ・色・画像・フォント・レイアウト間隔などを調整してください。変更した項目だけが実行時に優先されます。

## 注意点
毎フレーム演出で書き換える値（タイマー色、揺れ、点滅など）はコード側の値が使われます。要素の名前を変えると再利用されず、コードが新規作成します。
*/
