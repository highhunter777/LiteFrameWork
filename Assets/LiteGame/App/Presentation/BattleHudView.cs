using UnityEngine;
using TMPro;

namespace LiteGame
{
    /// <summary>
    /// 战斗 HUD 引用件（《战斗HUD层级设计》§3——序列化引用即契约）：挂在 <c>Hud_Battle.prefab</code> 根上，
    /// 持驱动读写的 8 个暴露节点——运行时零 Find（六条场景路径常量随 prefab 化退役）。
    /// HUD 属对局表现域（专项设计 §2.2）：实例由 <c>ProcedureBattle</c> 经内容服务租约创建/销毁。
    /// </summary>
    public sealed class BattleHudView : MonoBehaviour
    {
        /// <summary>HUD prefab 内容包路径（收集组 UIHud——AddressByFileName/assetPath 双可寻址）。</summary>
        public const string PrefabLocation = "Assets/ShareResource/UI/Hud/Hud_Battle.prefab";

        [Header("准心（BattleCrosshairDriver）")]
        [SerializeField] private RectTransform _crosshairRoot;
        [SerializeField] private GameObject _hipForm;
        [SerializeField] private GameObject _adsForm;

        [Header("面板（BattleHudDriver）")]
        [SerializeField] private CanvasGroup _panelGroup;
        [SerializeField] private UnityEngine.UI.Slider _hpSlider;
        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private TextMeshProUGUI _ammoText;
        [SerializeField] private GameObject _reloadLabel;

        public RectTransform CrosshairRoot => _crosshairRoot;
        public GameObject HipForm => _hipForm;
        public GameObject AdsForm => _adsForm;
        public CanvasGroup PanelGroup => _panelGroup;
        public UnityEngine.UI.Slider HpSlider => _hpSlider;
        public TextMeshProUGUI HpText => _hpText;
        public TextMeshProUGUI AmmoText => _ammoText;
        public GameObject ReloadLabel => _reloadLabel;
    }
}
