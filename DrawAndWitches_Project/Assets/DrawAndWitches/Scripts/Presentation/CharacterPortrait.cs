using System;
using DrawAndWitches.Data;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.Presentation
{
    /// <summary>
    /// 立ち絵 1 枚の表示制御。GameAssets.characters の設定に従い、表情ごとの差分立ち絵と Animator を切り替える。
    /// 差分・Animator が未設定なら常にデフォルト立ち絵を表示する（従来と同じ見た目）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("DRAW&WITCHES/Character Portrait")]
    public sealed class CharacterPortrait : MonoBehaviour
    {
        public const string Default = "default";
        public const string Talk = "talk";
        public const string Damage = "damage";
        public const string Spell = "spell";
        public const string Win = "win";
        public const string Lose = "lose";

        /// <summary>ゲームが自動で使う表情名（エディター拡張の候補表示用）</summary>
        public static readonly string[] BuiltinExpressions = { Default, Talk, Damage, Spell, Win, Lose };

        private const string TalkingParam = "Talking";

        [SerializeField, Tooltip("表示中のキャラクター ID（実行時に設定）")] private string characterId;
        [SerializeField, Tooltip("表示中の表情（実行時に設定）")] private string expression = Default;

        private Image image;
        private Animator animator;
        private CharacterVisual visual;
        private Sprite fallback;
        private float revertAt = -1f;

        public string CharacterId => characterId;
        public string Expression => expression;

        /// <summary>キャラクターを割り当てる。profile が null なら何もしない。</summary>
        public void Setup(GameAssets assets, CharacterProfile profile, string initialExpression = Default)
        {
            image = GetComponent<Image>();
            if (assets == null || profile == null || image == null) return;
            characterId = profile.Id;
            visual = assets.Visual(profile.Id);
            fallback = visual != null && visual.portrait != null ? visual.portrait : assets.Sprite(profile.PortraitKey);
            animator = GetComponent<Animator>();
            if (visual != null && visual.animator != null)
            {
                if (animator == null) animator = gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = visual.animator;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.enabled = true;
            }
            else if (animator != null)
            {
                // シーン上で手動で付けた Animator はそのまま使う。コントローラが無ければ止めておく。
                animator.enabled = animator.runtimeAnimatorController != null;
            }
            SetExpression(string.IsNullOrEmpty(initialExpression) ? Default : initialExpression);
        }

        /// <summary>表情を変える。holdSeconds &gt; 0 ならその秒数後に default へ戻る。</summary>
        public void SetExpression(string name, float holdSeconds = 0f)
        {
            if (image == null) image = GetComponent<Image>();
            if (image == null) return;
            expression = string.IsNullOrEmpty(name) ? Default : name;
            Sprite s = visual != null ? visual.FindExpression(expression) : null;
            if (s == null && expression != Default && visual != null) s = visual.FindExpression(Default);
            if (s == null) s = fallback;
            if (s != null) image.sprite = s;
            revertAt = holdSeconds > 0f ? Time.unscaledTime + holdSeconds : -1f;
            DriveAnimator();
        }

        private void DriveAnimator()
        {
            if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return;
            try
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Trigger && p.name == expression) animator.SetTrigger(p.nameHash);
                    else if (p.type == AnimatorControllerParameterType.Bool && p.name == TalkingParam) animator.SetBool(p.nameHash, expression == Talk);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CharacterPortrait] animator update failed (" + characterId + "): " + e.Message);
            }
        }

        private void OnEnable()
        {
            // 非表示中に指定された表情を、表示された時点で Animator に反映する
            if (animator != null) DriveAnimator();
        }

        private void Update()
        {
            if (revertAt > 0f && Time.unscaledTime >= revertAt)
            {
                revertAt = -1f;
                SetExpression(Default);
            }
        }
    }
}

/*
## このアセットの役割
立ち絵の表情切替です。GameAssets の「キャラクター表現」に差分立ち絵や Animator Controller を登録すると、戦闘・会話・カットイン・リザルトで自動的に使われます。

## 他アセットとの依存関係
GameAssets.CharacterVisual / CharacterProfile。Common.Portrait や戦闘画面の顔アイコン生成時に自動で付きます。

## Inspector設定
なし（表示中のキャラクター ID と表情は確認用）。

## USERがUnity上で行う作業
任意: DRAW&WITCHES > Content Editor の「キャラクター」タブで差分立ち絵・Animator を設定。

## 注意点
表情名: default（通常）/ talk（会話で発言中）/ damage（被弾時 0.6 秒）/ spell（カットイン）/ win（リザルトで勝った側）/ lose（自由に使える予備。会話の face などで指定）。
差分が無い表情は default の差分 → デフォルト立ち絵の順で代替します。Animator には同名の Trigger と bool「Talking」があれば反映します。
Animator のクリップで Image.sprite を動かす場合、差分立ち絵より Animator の値が優先されます。
*/
