using System;
using System.Collections.Generic;
using DrawAndWitches.Core;
using DrawAndWitches.Data;
using DrawAndWitches.Logic;
using DrawAndWitches.Online;
using DrawAndWitches.Presentation;
using DrawAndWitches.Services;
using DrawAndWitches.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawAndWitches.App
{
    /// <summary>画面の基底クラス。Build で Root 以下に UI を作り、破棄時は Root ごと削除される。</summary>
    public abstract class ScreenBase
    {
        protected GameApp App { get; private set; }
        protected RectTransform Root { get; private set; }
        public bool Disposed { get; private set; }

        internal void Attach(GameApp app, RectTransform root)
        {
            App = app;
            Root = root;
        }

        public abstract void Build();

        /// <summary>毎フレーム（実時間 dt）</summary>
        public virtual void Tick(float dt)
        {
        }

        /// <summary>Esc キー（モーダルが開いていないとき）</summary>
        public virtual void OnEscape()
        {
            App.GoHome();
        }

        /// <summary>言語変更時。既定では画面の状態を保ったまま UI を作り直す。</summary>
        public virtual void Relocalize()
        {
            Refresh();
        }

        /// <summary>画面インスタンスの状態（選択中タブなど）を保ったまま UI を作り直す</summary>
        protected void Refresh()
        {
            if (Disposed || Root == null) return;
            App.CloseAllModals();
            for (int i = Root.childCount - 1; i >= 0; i--)
            {
                Transform c = Root.GetChild(i);
                c.SetParent(null, false);
                UiKit.DestroySafe(c.gameObject);
            }
            try
            {
                Build();
                UiBinder.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[Screen] refresh failed: " + e);
                App.Notice(App.T("notice.screenFailed"), NoticeKind.Error);
                App.GoHome();
            }
        }

        public virtual void OnSettingsChanged(GameSettings s)
        {
        }

        /// <summary>
        /// UI 焼き込み（Setup Project）時に Build の後で呼ばれる。実行中にだけ現れる UI（演出・オーバーレイ等）を
        /// 作っておくと、それもシーン上で調整できるテンプレートになる。
        /// </summary>
        public virtual void BakeExtras()
        {
        }

        /// <summary>Setup Project による UI 焼き込み中か（通信・保存・音などの副作用を避ける）</summary>
        protected bool Baking => App != null && App.IsBaking;

        internal void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try
            {
                OnDispose();
            }
            catch (Exception e)
            {
                Debug.LogError("[Screen] dispose failed: " + e);
            }
        }

        protected virtual void OnDispose()
        {
        }

        protected string T(string key, params object[] kv)
        {
            return App.Loc.T(key, kv);
        }

        protected string Txt(LocalizedText text)
        {
            return App.Loc.Resolve(text);
        }
    }

    public enum BattleKind
    {
        Story,
        Endless,
        Custom,
        Shared,
        Replay,
    }

    /// <summary>エンドレス1周分の状態</summary>
    public sealed class EndlessRun
    {
        public readonly DeterministicRng Rng = new DeterministicRng(DeterministicRng.CreateSeed());
        public int Cleared;
        public int Sum;
        public int StageNo;
        public int LastIndex = -1;
        public readonly List<(string id, int score, BattleOutcome outcome)> History = new List<(string, int, BattleOutcome)>();
    }

    /// <summary>戦闘の文脈（どこから来て、終わったらどこへ戻るか）</summary>
    public sealed class BattleContext
    {
        public BattleKind Kind;
        public string StageId;
        public StageData Stage;
        public string CreationId;
        public string Author;
        public EndlessRun Run;
        public ReplayData Replay;
        public bool DialogueSeen;

        public string ModeString
        {
            get
            {
                switch (Kind)
                {
                    case BattleKind.Story: return "story";
                    case BattleKind.Endless: return "endless";
                    case BattleKind.Shared: return "shared";
                    case BattleKind.Custom: return "custom";
                    default: return Replay != null ? Replay.Mode : "story";
                }
            }
        }
    }

    public enum NoticeKind
    {
        Info,
        Success,
        Warn,
        Error,
    }

    /// <summary>
    /// ゲーム全体の起点（Boot シーンに1つだけ置く）。サービス生成・Canvas 構築・画面遷移・通知・モーダルを担当する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameApp : MonoBehaviour
    {
        [Header("素材（DRAW&WITCHES > Setup Project で自動設定）")]
        [SerializeField] private GameAssets assets;

        [Header("オンライン")]
        [Tooltip("X シェアに添えるゲームURL（Web版 onlineConfig.js の GAME_URL と同じ値を推奨）")]
        [SerializeField] private string gameUrl = "";
        [Tooltip("X シェアのハッシュタグ（カンマ区切り、# は不要）")]
        [SerializeField] private string hashtags = "DRAWandWITCHES,弾幕パズル";

        [Header("シーン UI（Setup Project が生成・自動設定）")]
        [Tooltip("ゲームの Canvas（DW_Canvas）")]
        [SerializeField] private RectTransform canvasRoot;
        [Tooltip("表示中の画面が置かれるレイヤー")]
        [SerializeField] private RectTransform screenLayer;
        [Tooltip("モーダル（確認ダイアログ等）のレイヤー")]
        [SerializeField] private RectTransform modalLayer;
        [Tooltip("トースト通知のレイヤー")]
        [SerializeField] private RectTransform toastLayer;
        [Tooltip("焼き込み済み UI テンプレート（Screens / Modals / Toasts）。実行時は非表示になり、ここから複製して使う。")]
        [SerializeField] private RectTransform templates;
        [Header("管理")]
        [Tooltip("ステージバージョン")]
        [SerializeField] private int defaultVersion = 1;

        private const float ToastSeconds = 3.2f;
        private const int MaxToasts = 4;

        public static GameApp Instance { get; private set; }

        public GameAssets Assets => assets;
        public GameDatabase Db { get; private set; }
        public LocalizationService Loc { get; private set; }
        public SettingsService Settings { get; private set; }
        public ProgressService Progress { get; private set; }
        public ReplayStore Replays { get; private set; }
        public MyStageService MyStages { get; private set; }
        public OnlineCacheService OnlineCache { get; private set; }
        public LeaderboardService Leaderboard { get; private set; }
        public SharedStageService SharedStages { get; private set; }
        public HierarchicalDataManager Remote { get; private set; }
        public TweetManager Tweet { get; private set; }
        public AudioManager Audio { get; private set; }
        public string PlayerId { get; private set; }
        public string GameUrl => gameUrl;
        public string Hashtags => hashtags;

        private ScreenBase current;
        private Func<ScreenBase> currentFactory;
        private readonly List<GameObject> modals = new List<GameObject>();
        private readonly List<(GameObject go, float until)> toasts = new List<(GameObject, float)>();
        private bool booted;
        private bool navigating;

        public bool ModalOpen => modals.Count > 0;

        /// <summary>Setup Project の UI 焼き込み中</summary>
        public bool IsBaking { get; private set; }
        private bool bakeToasts;

        public RectTransform Templates => templates;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            try
            {
                Boot();
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] boot failed: " + e);
                ShowFatal(e.Message);
            }
        }

        private void Boot()
        {
            if (assets == null)
            {
                throw new InvalidOperationException("GameApp の Assets が未設定です。メニュー「DRAW&WITCHES > Setup Project」を実行してください。");
            }
            InitUiKit();
            UiKit.EnsureEventSystem();
            BuildCanvas();
            LocalSave.ErrorOccurred += OnStorageError;

            CreateDataServices();
            Remote = GetComponentInChildren<HierarchicalDataManager>() ?? HierarchicalDataManager.Instance;
            Tweet = TweetManager.instance != null ? TweetManager.instance : GetComponentInChildren<TweetManager>();
            if (Tweet == null) Tweet = gameObject.AddComponent<TweetManager>();
            CreateOnlineServices();

            Audio = gameObject.AddComponent<AudioManager>();
            Audio.Init(assets);
            UiKit.ClickSound = () => Audio.PlaySe("ui_click");
            UiKit.BackSound = () => Audio.PlaySe("ui_back");

            ApplySettings(Settings.Current, true);
            Settings.Changed += s => ApplySettings(s, false);
            booted = true;

            if (Db.Warnings.Count > 0)
            {
                int stageWarn = Db.Warnings.FindAll(w => w.Contains(".json") && !w.StartsWith("dialogues", StringComparison.Ordinal)).Count;
                if (stageWarn > 0) Notice(T("notice.stageLoadPartial", "n", stageWarn), NoticeKind.Warn);
            }
            Go(() => new TitleScreen());
        }

        private void InitUiKit()
        {
            UiKit.Init(assets.font);
            UiKit.SetSkin(assets.uiRounded, assets.uiRoundedOutline, assets.uiCircle, assets.uiSoftCircle, assets.uiVerticalFade);
        }

        private void CreateDataServices()
        {
            Loc = new LocalizationService();
            Loc.LoadAll();
            Db = GameDatabase.Load();
            Settings = new SettingsService();
            Progress = new ProgressService();
            Replays = new ReplayStore();
            MyStages = new MyStageService(new[] { GameConstants.Editor.EnemyId });
            PlayerId = IdGenerator.GetOrCreatePlayerId();
        }

        private void CreateOnlineServices()
        {
            OnlineCache = new OnlineCacheService();
            Leaderboard = new LeaderboardService(Remote, OnlineCache, PlayerId);
            SharedStages = new SharedStageService(Remote, OnlineCache);
        }

        private void ApplySettings(GameSettings s, bool initial)
        {
            Audio.ApplyVolumes(s);
            SettingsService.ApplyDisplay(s);
            string before = Loc.Language;
            Loc.SetLanguage(s.Language);
            if (!initial)
            {
                if (before != Loc.Language && current != null) current.Relocalize();
                current?.OnSettingsChanged(s);
            }
        }

        private void OnStorageError(StorageError e)
        {
            switch (e.Kind)
            {
                case StorageErrorKind.Corrupt: Notice(T("notice.saveCorrupt"), NoticeKind.Warn); break;
                case StorageErrorKind.Write: Notice(T("notice.saveFailed"), NoticeKind.Error); break;
                default: Notice(T("notice.saveUnavailable"), NoticeKind.Error); break;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                LocalSave.ErrorOccurred -= OnStorageError;
                current?.Dispose();
                Instance = null;
            }
        }

        private void BuildCanvas()
        {
            if (screenLayer != null && modalLayer != null && toastLayer != null)
            {
                // Setup Project で生成済みのシーン UI を使う
                if (templates != null)
                {
                    templates.gameObject.SetActive(false);
                    UiBinder.ClearPristine();
                    UiBinder.SetPristine(screenLayer, templates.Find("Screens"));
                    UiBinder.SetPristine(modalLayer, templates.Find("Modals"));
                    UiBinder.SetPristine(toastLayer, templates.Find("Toasts"));
                }
                ClearLayer(screenLayer);
                ClearLayer(modalLayer);
                ClearLayer(toastLayer);
                return;
            }
            Debug.LogWarning("[GameApp] シーン UI が未生成のため実行時に作成します（DRAW&WITCHES > Setup Project で生成すると Unity 上で調整できます）");
            CreateCanvasHierarchy();
        }

        /// <summary>レイヤーに残っている（編集用に置かれた）要素を取り除く</summary>
        private static void ClearLayer(RectTransform layer)
        {
            for (int i = layer.childCount - 1; i >= 0; i--) UiKit.DestroySafe(layer.GetChild(i).gameObject);
        }

        /// <summary>Canvas とレイヤーを作る（実行時の代替 / Setup Project の焼き込みで共用）</summary>
        private void CreateCanvasHierarchy()
        {
            GameObject cgo = new GameObject("DW_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            Canvas canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            RectTransform root = (RectTransform)cgo.transform;
            canvasRoot = root;
            Image back = UiKit.Image(root, "Backdrop", null, new Color32(0x0b, 0x07, 0x1c, 0xff));
            UiKit.Stretch(back.rectTransform);
            screenLayer = UiKit.Stretch(UiKit.Rect(root, "Screens"));
            modalLayer = UiKit.Stretch(UiKit.Rect(root, "Modals"));
            toastLayer = UiKit.Stretch(UiKit.Rect(root, "Toasts"));
            VerticalLayoutGroup v = toastLayer.gameObject.GetOrAdd<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperCenter;
            v.spacing = 10;
            v.padding = new RectOffset(0, 0, 28, 0);
            v.childControlHeight = true;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
        }

        /// <summary>起動に失敗したときの画面（原因を表示し、ログを確認できるようにする）</summary>
        private void ShowFatal(string message)
        {
            try
            {
                if (UiKit.Font == null) UiKit.Init(assets != null ? assets.font : null);
                UiKit.EnsureEventSystem();
                if (screenLayer == null) CreateCanvasHierarchy();
                Image p = UiKit.Panel(screenLayer, "Fatal");
                UiKit.Place(p.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 420));
                Text t = UiKit.Label(p.transform, "DRAW&WITCHES を起動できませんでした\n\n" + message, 28, UiKit.TextColor);
                UiKit.Stretch(t.rectTransform, 40, 40, 30, 30);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] fatal screen failed: " + e);
            }
        }

        // ------------------------------------------------------------ 画面遷移

        public string T(string key, params object[] kv)
        {
            return Loc != null ? Loc.T(key, kv) : key;
        }

        public void Go(Func<ScreenBase> factory)
        {
            if (!booted || navigating || factory == null) return;
            navigating = true;
            try
            {
                CloseAllModals();
                ScreenBase old = current;
                current = null;
                old?.Dispose();
                for (int i = screenLayer.childCount - 1; i >= 0; i--) Destroy(screenLayer.GetChild(i).gameObject);
                ScreenBase next = factory();
                RectTransform root = UiKit.Stretch(UiKit.Rect(screenLayer, next.GetType().Name));
                next.Attach(this, root);
                current = next;
                currentFactory = factory;
                next.Build();
                UiBinder.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] screen failed: " + e);
                Notice(T("notice.screenFailed"), NoticeKind.Error);
                navigating = false;
                if (!(current is HomeScreen)) Go(() => new HomeScreen());
                return;
            }
            navigating = false;
        }

        /// <summary>現在の画面を作り直す（言語変更など）</summary>
        public void Rebuild()
        {
            if (currentFactory != null) Go(currentFactory);
        }

        public void GoHome()
        {
            Go(() => new HomeScreen());
        }

        private void Update()
        {
            if (!booted) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                if (toasts[i].go == null || now >= toasts[i].until)
                {
                    if (toasts[i].go != null) Destroy(toasts[i].go);
                    toasts.RemoveAt(i);
                }
            }
            if (current == null || navigating) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && ModalOpen)
                {
                    CloseTopModal();
                }
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && !(current is BattleScreen))
                {
                    current.OnEscape();
                }
                if (current != null && !current.Disposed) current.Tick(dt);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] screen update failed: " + e);
                Notice(T("notice.unexpected"), NoticeKind.Error);
                GoHome();
            }
        }

        private void LateUpdate()
        {
            if (!booted) return;
            // 画面・モーダル・通知でこのフレームに再利用した焼き込み UI に、シーン上の調整値を反映する
            UiBinder.Flush();
        }

        private void OnApplicationQuit()
        {
            current?.Dispose();
        }

        // ------------------------------------------------------------ UI 焼き込み（Setup Project）

#if UNITY_EDITOR
        /// <summary>
        /// エディター専用：Canvas・レイヤー・全画面の UI テンプレートをシーン上に生成する。
        /// 実行時と同じ Build() を使うため、生成物は実行時の画面と同じ構成になる。
        /// 戻り値は生成した要素数。失敗時は例外（呼び出し側が表示）。
        /// </summary>
        public int EditorBakeScene(HierarchicalDataManager remote)
        {
            if (Application.isPlaying) throw new InvalidOperationException("再生中は実行できません");
            if (assets == null) throw new InvalidOperationException("GameAssets が未設定です");
            GameObject audioHost = null;
            int count = 0;
            if (canvasRoot != null) DestroyImmediate(canvasRoot.gameObject);
            canvasRoot = screenLayer = modalLayer = toastLayer = templates = null;
            IsBaking = true;
            try
            {
                UiBinder.BakeMode = false;
                InitUiKit();
                CreateCanvasHierarchy();

                // テンプレート置き場（実行時は非表示）
                templates = UiKit.Stretch(UiKit.Rect(canvasRoot, "Templates"));
                RectTransform screensT = UiKit.Stretch(UiKit.Rect(templates, "Screens"));
                RectTransform modalsT = UiKit.Stretch(UiKit.Rect(templates, "Modals"));
                RectTransform toastsT = UiKit.Stretch(UiKit.Rect(templates, "Toasts"));
                VerticalLayoutGroup tv = toastsT.gameObject.GetOrAdd<VerticalLayoutGroup>();
                tv.childAlignment = TextAnchor.UpperCenter;
                tv.padding = new RectOffset(0, 0, 28, 0);
                tv.childControlHeight = true;
                tv.childControlWidth = false;
                tv.childForceExpandHeight = false;
                tv.childForceExpandWidth = false;

                CreateDataServices();
                audioHost = new GameObject("DW_BakeTemp") { hideFlags = HideFlags.HideAndDontSave };
                Remote = remote != null ? remote : GetComponentInChildren<HierarchicalDataManager>(true);
                if (Remote == null) Remote = audioHost.AddComponent<HierarchicalDataManager>(); // 焼き込み中は通信しない（画面構築用の参照のみ）
                CreateOnlineServices();
                Audio = audioHost.AddComponent<AudioManager>();

                UiBinder.BakeMode = true;
                foreach (Func<ScreenBase> factory in BakeTargets())
                {
                    ScreenBase sc = null;
                    try
                    {
                        sc = factory();
                        RectTransform root = UiKit.Stretch(UiKit.Rect(screensT, sc.GetType().Name));
                        sc.Attach(this, root);
                        current = sc;
                        sc.Build();
                        sc.BakeExtras();
                        root.gameObject.SetActive(false);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[GameApp] bake failed for " + (sc != null ? sc.GetType().Name : "?") + ": " + e);
                    }
                    finally
                    {
                        sc?.Dispose();
                        current = null;
                    }
                }

                // モーダルとトーストの見本
                RectTransform liveModal = modalLayer;
                RectTransform liveToast = toastLayer;
                try
                {
                    modalLayer = modalsT;
                    Confirm(T("common.back") + "?", T("common.close"), null, ButtonVariant.Primary);
                    toastLayer = toastsT;
                    bakeToasts = true;
                    Notice(T("common.close"), NoticeKind.Success);
                }
                finally
                {
                    bakeToasts = false;
                    modalLayer = liveModal;
                    toastLayer = liveToast;
                    modals.Clear();
                }

                UiBinder.BakeMode = false;
                count = UiBinder.RecordReferences(templates);
                foreach (FieldGraphic fg in templates.GetComponentsInChildren<FieldGraphic>(true)) fg.Drawer = null;
                templates.gameObject.SetActive(false);
            }
            finally
            {
                UiBinder.BakeMode = false;
                IsBaking = false;
                if (audioHost != null) DestroyImmediate(audioHost);
                Audio = null;
                current = null;
                currentFactory = null;
            }
            return count;
        }

        /// <summary>焼き込み対象の画面（実行時に表示されうる全画面）</summary>
        private IEnumerable<Func<ScreenBase>> BakeTargets()
        {
            yield return () => new TitleScreen();
            yield return () => new HomeScreen();
            yield return () => new StoryScreen(null);
            yield return () => new EndlessScreen();
            yield return () => new FigurebookScreen();
            yield return () => new SettingsScreen(null);
            yield return () => new RecordsScreen();
            yield return () => new EditorMenuScreen();
            yield return () => new MyStagesScreen();
            yield return () => new StageEditorScreen(IdGenerator.NewCreationId(), null);
            yield return () => new SharedStagesScreen();
            string firstStory = Db.OfficialStages[0].Id;
            yield return () => new BattleScreen(new BattleContext { Kind = BattleKind.Story, StageId = firstStory }, BattlePlayer.Human);
            yield return () => new ResultScreen(ResultScreen.BakeSample(Db.OfficialStages[0]));
        }
#endif

        // ------------------------------------------------------------ 通知・モーダル

        public void Notice(string text, NoticeKind kind = NoticeKind.Info, float seconds = ToastSeconds)
        {
            if (toastLayer == null || string.IsNullOrEmpty(text)) return;
            if (IsBaking && !bakeToasts) return;
            while (toasts.Count >= MaxToasts)
            {
                if (toasts[0].go != null) Destroy(toasts[0].go);
                toasts.RemoveAt(0);
            }
            Color c = kind == NoticeKind.Error ? UiKit.Danger : kind == NoticeKind.Warn ? UiKit.Gold : kind == NoticeKind.Success ? UiKit.Success : UiKit.PanelBorder;
            Image bg = UiKit.Image(toastLayer, "Toast", UiKit.Rounded, new Color(0.08f, 0.05f, 0.18f, 0.96f));
            Image bar = UiKit.Image(bg.transform, "Bar", UiKit.Rounded, c);
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.offsetMin = new Vector2(6, 8);
            bar.rectTransform.offsetMax = new Vector2(14, -8);
            Text t = UiKit.Label(bg.transform, text, 24, UiKit.TextColor, TextAnchor.MiddleLeft);
            UiKit.Stretch(t.rectTransform, 30, 22, 10, 10);
            LayoutElement le = bg.gameObject.GetOrAdd<LayoutElement>();
            le.preferredWidth = 900;
            le.preferredHeight = Mathf.Max(64, t.preferredHeight + 28);
            if (IsBaking) return;
            toasts.Add((bg.gameObject, Time.unscaledTime + seconds));
        }

        public sealed class ModalButton
        {
            public string Label;
            public ButtonVariant Variant;
            /// <summary>true を返すとモーダルを閉じない</summary>
            public Func<bool> OnClick;
        }

        public static ModalButton Btn(string label, ButtonVariant variant, Action onClick)
        {
            return new ModalButton
            {
                Label = label,
                Variant = variant,
                OnClick = () =>
                {
                    onClick?.Invoke();
                    return false;
                },
            };
        }

        /// <summary>汎用モーダル。body で中身を作る。戻り値で閉じられる。</summary>
        public GameObject OpenModal(string title, Action<RectTransform> body, IList<ModalButton> buttons, float width = 900f, float height = 520f)
        {
            Image blocker = UiKit.Blocker(modalLayer, 0.7f);
            GameObject go = blocker.gameObject;
            Image panel = UiKit.Panel(blocker.transform, "Panel");
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            if (!string.IsNullOrEmpty(title))
            {
                Text tt = UiKit.Label(panel.transform, title, 34, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                tt.rectTransform.anchorMin = new Vector2(0, 1);
                tt.rectTransform.anchorMax = new Vector2(1, 1);
                tt.rectTransform.offsetMin = new Vector2(30, -80);
                tt.rectTransform.offsetMax = new Vector2(-30, -20);
            }
            RectTransform content = UiKit.Rect(panel.transform, "Body");
            UiKit.Stretch(content, 36, 36, string.IsNullOrEmpty(title) ? 30 : 90, buttons != null && buttons.Count > 0 ? 110 : 30);
            try
            {
                body?.Invoke(content);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] modal body failed: " + e);
            }
            if (buttons != null && buttons.Count > 0)
            {
                RectTransform row = UiKit.Rect(panel.transform, "Actions");
                row.anchorMin = new Vector2(0, 0);
                row.anchorMax = new Vector2(1, 0);
                row.offsetMin = new Vector2(36, 26);
                row.offsetMax = new Vector2(-36, 96);
                UiKit.Row(row, 18);
                foreach (ModalButton mb in buttons)
                {
                    ModalButton captured = mb;
                    UiKit.Button(row, mb.Label, () =>
                    {
                        bool keep = false;
                        try
                        {
                            keep = captured.OnClick != null && captured.OnClick();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("[GameApp] modal action failed: " + e);
                            Notice(T("notice.unexpected"), NoticeKind.Error);
                        }
                        if (!keep) CloseModal(go);
                    }, mb.Variant);
                }
            }
            if (!IsBaking) modals.Add(go);
            return go;
        }

        public void Confirm(string text, string yesLabel, Action onYes, ButtonVariant yesVariant = ButtonVariant.Danger)
        {
            OpenModal(null, body =>
            {
                Text t = UiKit.Label(body, text, 28, UiKit.TextColor);
                UiKit.Stretch(t.rectTransform);
            }, new[] { Btn(T("common.cancel"), ButtonVariant.Ghost, null), Btn(yesLabel, yesVariant, onYes) }, 820, 360);
        }

        /// <summary>長い文字列の表示（リプレイ文字列など）。選択してコピーできる。</summary>
        public void ShowText(string title, string text)
        {
            OpenModal(title, body =>
            {
                InputField f = UiKit.Input(body, text, string.Empty, 0, null);
                UiKit.Stretch((RectTransform)f.transform);
                f.lineType = InputField.LineType.MultiLineNewline;
                f.readOnly = true;
                f.textComponent.alignment = TextAnchor.UpperLeft;
                f.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
                f.textComponent.fontSize = 18;
            }, new[] { Btn(T("common.close"), ButtonVariant.Primary, null) }, 1100, 620);
        }

        /// <summary>テキスト入力モーダル（リプレイ読込など）</summary>
        public void PromptText(string title, string hint, Func<string, bool> onSubmit)
        {
            InputField field = null;
            OpenModal(title, body =>
            {
                Text h = UiKit.Label(body, hint, 22, UiKit.SubTextColor, TextAnchor.UpperLeft);
                h.rectTransform.anchorMin = new Vector2(0, 1);
                h.rectTransform.anchorMax = new Vector2(1, 1);
                h.rectTransform.offsetMin = new Vector2(0, -60);
                h.rectTransform.offsetMax = Vector2.zero;
                field = UiKit.Input(body, string.Empty, string.Empty, 0, null);
                UiKit.Stretch((RectTransform)field.transform, 0, 0, 70, 0);
                field.lineType = InputField.LineType.MultiLineNewline;
                field.textComponent.alignment = TextAnchor.UpperLeft;
                field.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
                field.textComponent.fontSize = 18;
            }, new[]
            {
                Btn(T("common.cancel"), ButtonVariant.Ghost, null),
                new ModalButton { Label = T("replay.play"), Variant = ButtonVariant.Primary, OnClick = () => !onSubmit(field != null ? field.text : string.Empty) },
            }, 1100, 620);
        }

        public void CloseModal(GameObject modal)
        {
            if (modal == null) return;
            modals.Remove(modal);
            Destroy(modal);
        }

        public void CloseTopModal()
        {
            if (modals.Count == 0) return;
            UiKit.BackSound?.Invoke();
            CloseModal(modals[modals.Count - 1]);
        }

        public void CloseAllModals()
        {
            foreach (GameObject m in modals)
            {
                if (m != null) Destroy(m);
            }
            modals.Clear();
        }

        // ------------------------------------------------------------ 共通ヘルパー

        public string PlayerName()
        {
            string n = Settings.Current.PlayerName;
            return string.IsNullOrEmpty(n) ? T("common.defaultPlayerName") : n;
        }

        public bool CopyToClipboard(string text)
        {
            try
            {
                GUIUtility.systemCopyBuffer = text ?? string.Empty;
                return GUIUtility.systemCopyBuffer == (text ?? string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameApp] clipboard failed: " + e.Message);
                return false;
            }
        }

        /// <summary>X シェア（TweetManager と同一仕様）</summary>
        public void Share(string message)
        {
            try
            {
                Tweet.OpenTweet(message, gameUrl, hashtags);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameApp] share failed: " + e);
                Notice(T("share.blocked"), NoticeKind.Warn);
            }
        }

        /// <summary>弾種の表示名（bullets.json の name → UI 文言 type.&lt;id&gt; → id の順）</summary>
        public string BulletTypeName(string id)
        {
            BulletTypeDef def = BulletTypeDef.Find(id);
            if (def != null && def.Name != null)
            {
                string n = Loc.Resolve(def.Name);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            string key = "type." + id;
            string t = T(key);
            return t == key ? id : t;
        }

        public string RankLabel(CharacterProfile c)
        {
            return c != null && c.Rank > 0 ? T("common.rankN", "n", c.Rank) : T("common.rankNone");
        }

        /// <summary>通信エラーコードを表示文言にする（Web版 onlineErrorText と同じ分類）</summary>
        public string OnlineErrorText(string code)
        {
            if (string.IsNullOrEmpty(code)) return T("online.errUnknown");
            if (code == "notConfigured") return T("online.unavailable");
            if (code == "invalid") return T("online.errInvalid");
            if (code == "tooLarge") return T("online.errTooLarge");
            if (code.StartsWith("timeout", StringComparison.Ordinal)) return T("online.errTimeout");
            if (code.StartsWith("network", StringComparison.Ordinal)) return T("online.errNetwork");
            if (code.StartsWith("http", StringComparison.Ordinal) || code.StartsWith("server", StringComparison.Ordinal) || code.StartsWith("parse", StringComparison.Ordinal)) return T("online.errServer");
            return T("online.errUnknown");
        }

        public string OnlineStatusText(OnlineStatus s)
        {
            switch (s)
            {
                case OnlineStatus.Online: return T("online.statusOnline");
                case OnlineStatus.Cache: return T("online.statusCache");
                case OnlineStatus.NotConfigured: return T("online.statusNotConfigured");
                default: return T("online.statusError");
            }
        }
    }

    /// <summary>画面共通の部品</summary>
    public static class Common
    {
        /// <summary>背景画像＋暗幕</summary>
        public static Image Background(GameApp app, RectTransform root, string key, float dim)
        {
            Image bg = UiKit.Image(root, "Background", app.Assets.Sprite(key), Color.white);
            UiKit.Stretch(bg.rectTransform);
            bg.preserveAspect = false;
            if (bg.sprite == null) bg.color = new Color32(0x16, 0x0e, 0x30, 0xff);
            else
            {
                AspectRatioFitter f = bg.gameObject.GetOrAdd<AspectRatioFitter>();
                f.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                f.aspectRatio = bg.sprite.rect.width / Mathf.Max(1f, bg.sprite.rect.height);
            }
            Image shade = UiKit.Image(root, "Shade", UiKit.VerticalFade, new Color(0.04f, 0.02f, 0.1f, dim));
            UiKit.Stretch(shade.rectTransform);
            shade.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
            Image flat = UiKit.Image(root, "Dim", null, new Color(0.04f, 0.02f, 0.1f, dim * 0.5f));
            UiKit.Stretch(flat.rectTransform);
            return bg;
        }

        /// <summary>上部の見出しと戻るボタン</summary>
        public static void Header(GameApp app, RectTransform root, string title, UnityAction onBack)
        {
            RectTransform bar = UiKit.Rect(root, "Header");
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.offsetMin = new Vector2(40, -120);
            bar.offsetMax = new Vector2(-40, -24);
            Text t = UiKit.Label(bar, title, 48, UiKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 260, 260, 0, 0);
            UiKit.AddShadow(t, new Color(0.8f, 0.3f, 0.9f, 0.6f), new Vector2(0, -3));
            if (onBack != null)
            {
                Button b = UiKit.Button(bar, "‹ " + app.T("common.back"), onBack, ButtonVariant.Ghost, 26);
                RectTransform br = (RectTransform)b.transform;
                br.anchorMin = new Vector2(0, 0.5f);
                br.anchorMax = new Vector2(0, 0.5f);
                br.pivot = new Vector2(0, 0.5f);
                br.anchoredPosition = Vector2.zero;
                br.sizeDelta = new Vector2(220, 70);
            }
        }

        /// <summary>キャラクターの立ち絵（差分立ち絵・Animator 対応。未設定ならデフォルト立ち絵）</summary>
        public static Image Portrait(GameApp app, Transform parent, CharacterProfile character, Vector2 anchor, Vector2 pos, float height, string expression = CharacterPortrait.Default)
        {
            CharacterVisual v = character != null ? app.Assets.Visual(character.Id) : null;
            Sprite s = v != null && v.portrait != null ? v.portrait : app.Assets.Sprite(character != null ? character.PortraitKey : null);
            Image img = UiKit.Image(parent, "Portrait", s, s != null ? Color.white : new Color(1, 1, 1, 0));
            float aspect = s != null ? s.rect.width / Mathf.Max(1f, s.rect.height) : 0.7f;
            UiKit.Place(img.rectTransform, anchor, pos, new Vector2(height * aspect, height));
            img.preserveAspect = true;
            AttachFace(app, img, character, expression);
            return img;
        }

        /// <summary>既存の Image に表情制御を付ける（顔アイコン等）。戻り値は null の場合あり。</summary>
        public static CharacterPortrait AttachFace(GameApp app, Image img, CharacterProfile character, string expression = CharacterPortrait.Default)
        {
            if (img == null || character == null) return null;
            try
            {
                CharacterPortrait cp = img.gameObject.GetOrAdd<CharacterPortrait>();
                cp.Setup(app.Assets, character, expression);
                return cp;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Portrait] setup failed (" + character.Id + "): " + e.Message);
                return null;
            }
        }

        /// <summary>表情変更（null 安全）</summary>
        public static void SetFace(Image img, string expression, float holdSeconds = 0f)
        {
            if (img == null) return;
            CharacterPortrait cp = img.GetComponent<CharacterPortrait>();
            if (cp != null) cp.SetExpression(expression, holdSeconds);
        }

        /// <summary>ランキング表示モーダル（6時間キャッシュ）</summary>
        public static async void OpenRanking(GameApp app, string key, string title)
        {
            Text status = null;
            RectTransform list = null;
            GameObject modal = app.OpenModal(title, body =>
            {
                status = UiKit.Label(body, app.T("online.loading"), 22, UiKit.SubTextColor);
                status.rectTransform.anchorMin = new Vector2(0, 1);
                status.rectTransform.anchorMax = new Vector2(1, 1);
                status.rectTransform.offsetMin = new Vector2(0, -40);
                status.rectTransform.offsetMax = Vector2.zero;
                RectTransform scrollHost = UiKit.Stretch(UiKit.Rect(body, "List"), 0, 0, 50, 0);
                list = UiKit.ScrollList(scrollHost, out ScrollRect _, 6);
                UiKit.Stretch((RectTransform)list.parent.parent);
            }, new[] { GameApp.Btn(app.T("common.close"), ButtonVariant.Primary, null) }, 1000, 760);
            RankingResult res;
            try
            {
                res = await app.Leaderboard.GetRankingAsync(key);
            }
            catch (Exception e)
            {
                Debug.LogError("[Ranking] fetch failed: " + e);
                res = new RankingResult { Status = OnlineStatus.Error };
            }
            if (modal == null || list == null) return; // 取得中に閉じられた
            status.text = app.OnlineStatusText(res.Status) + (res.Status == OnlineStatus.NotConfigured ? "\n" + app.T("online.unavailable") : string.Empty);
            if (res.Rows.Count == 0)
            {
                Text none = UiKit.Label(list, app.T("online.noRanking"), 26, UiKit.SubTextColor);
                UiKit.Height(none, 80);
                return;
            }
            int n = Math.Min(res.Rows.Count, GameConstants.Online.RankingShowMax);
            for (int i = 0; i < n; i++)
            {
                RankingRow r = res.Rows[i];
                bool me = r.Id == app.PlayerId;
                Image row = UiKit.Image(list, "Row", UiKit.Rounded, me ? new Color(0.75f, 0.3f, 0.85f, 0.45f) : new Color(1, 1, 1, i % 2 == 0 ? 0.06f : 0.02f));
                UiKit.Height(row, 58);
                Text rank = UiKit.Label(row.transform, "#" + (i + 1), 26, i < 3 ? UiKit.Gold : UiKit.SubTextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Stretch(rank.rectTransform, 18, 0, 0, 0).anchorMax = new Vector2(0.14f, 1);
                Text name = UiKit.Label(row.transform, r.Name, 26, UiKit.TextColor, TextAnchor.MiddleLeft);
                RectTransform nr = UiKit.Stretch(name.rectTransform);
                nr.anchorMin = new Vector2(0.14f, 0);
                nr.anchorMax = new Vector2(0.58f, 1);
                string detail = r.Cleared > 0 ? app.T("endless.clearedN", "n", r.Cleared) : app.T("battle.ops") + " " + r.Ops;
                Text d = UiKit.Label(row.transform, detail, 20, UiKit.SubTextColor, TextAnchor.MiddleLeft);
                RectTransform dr = UiKit.Stretch(d.rectTransform);
                dr.anchorMin = new Vector2(0.58f, 0);
                dr.anchorMax = new Vector2(0.78f, 1);
                Text sc = UiKit.Label(row.transform, UiKit.FormatNumber(r.Score), 28, UiKit.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
                RectTransform sr = UiKit.Stretch(sc.rectTransform, 0, 18, 0, 0);
                sr.anchorMin = new Vector2(0.78f, 0);
            }
        }
    }
}

/*
## このアセットの役割
ゲーム全体の起点です。サービス（設定・進行・リプレイ・自作ステージ・オンライン）の生成、Canvas の構築、画面遷移、トースト通知、モーダル、X シェア、クリップボード、通信エラー文言の変換を担当します。

## 他アセットとの依存関係
全サービスと全画面。Boot シーンの GameApp オブジェクトに付けます（セットアップメニューが自動で作成）。同じオブジェクトの子に HierarchicalDataManager / TweetManager を置きます。

## Inspector設定
Assets: GameAssets.asset（自動設定）。Game Url: X シェアに添える URL。Hashtags: カンマ区切りのハッシュタグ。

## USERがUnity上で行う作業
Game Url の入力（任意）。GAS URL は子オブジェクト Online の HierarchicalDataManager に入力します。

## 注意点
起動に失敗した場合は画面中央に原因を表示します（Console にも詳細が出ます）。Esc キーはモーダル → 画面の順に処理されます。
*/
