using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DRAW&WITCHES/UI Baked Element")]
    public class UiBaked : MonoBehaviour
    {
        [Tooltip("要素の種類（rect / img / text / slider / scroll）。コードの要求と一致した要素だけが再利用されます。変更しないでください。")]
        public string kind = "rect";

        [Tooltip("ON にすると、この要素は実行時に常に非表示になります（装飾を消したい場合など）。")]
        public bool forceHidden;

        [Tooltip("ON にすると、シーンで変更した見た目を適用せず、常にコードの値を使います。")]
        public bool ignoreSceneOverrides;

        [HideInInspector] public UiSnap reference;

        [NonSerialized] internal bool bound;
        [NonSerialized] internal Transform pristine;
        [NonSerialized] internal UiSnap scene;
        [NonSerialized] internal ulong mask;
    }
}
