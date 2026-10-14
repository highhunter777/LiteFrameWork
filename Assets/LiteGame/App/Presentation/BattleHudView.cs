using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LiteGame.UI;

namespace LiteGame
{
    /// <summary>
    /// 战斗 HUD 引用件（《战斗HUD层级设计》§3——序列化引用即契约）：挂在 <c>Hud_Battle.prefab</code> 根上，
    /// 持驱动读写的暴露节点——运行时零 Find（六条场景路径常量随 prefab 化退役）。
    /// HUD 属对局表现域（专项设计 §2.2）：实例由 <c>ProcedureBattle</c> 经内容服务租约创建/销毁。
    /// 血条走 <see cref="HpBar"/> 控件 API（模板 <c>Tmp_HpBar</c> 实例）——单点组件引用，避免嵌套深层字段 override。
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
        [SerializeField] private HpBar _hpBar;
        [SerializeField] private TextMeshProUGUI _ammoText;
        [SerializeField] private GameObject _reloadLabel;

        [Header("菜单（对局退场链——Cot_Settings 角组，子 Canvas 单独开射线）")]
        [SerializeField] private Button _settingsButton;

        [Header("排行榜（排行榜批；FFA 比分＝击杀——8 格预置，禁运行时建视觉元素）")]
        [SerializeField] private RectTransform _rankboardRoot;
        [SerializeField] private TextMeshProUGUI _teamScoreText;                    // 团队比分预留（Team 模式；FFA 隐藏）
        [SerializeField] private TextMeshProUGUI[] _rankNames = new TextMeshProUGUI[8];
        [SerializeField] private TextMeshProUGUI[] _rankKills = new TextMeshProUGUI[8];
        [SerializeField] private GameObject[] _rankHighlights = new GameObject[8];

        public RectTransform CrosshairRoot => _crosshairRoot;
        public GameObject HipForm => _hipForm;
        public GameObject AdsForm => _adsForm;
        public CanvasGroup PanelGroup => _panelGroup;
        public HpBar HpBar => _hpBar;
        public TextMeshProUGUI AmmoText => _ammoText;
        public GameObject ReloadLabel => _reloadLabel;
        public Button SettingsButton => _settingsButton;
        public RectTransform RankboardRoot => _rankboardRoot;
        public TextMeshProUGUI TeamScoreText => _teamScoreText;
        public TextMeshProUGUI[] RankNames => _rankNames;
        public TextMeshProUGUI[] RankKills => _rankKills;
        public GameObject[] RankHighlights => _rankHighlights;

        /// <summary>排行榜格是否齐备（名字/击杀/高亮件各 <see cref="BattleRankboardDriver.CellCount"/> 件）
        /// ——装配期判据，缺件走静默降级（不把"旧 prefab"当对局失败）。</summary>
        public bool HasRankboard =>
            _rankNames != null && _rankNames.Length == BattleRankboardDriver.CellCount
            && _rankKills != null && _rankKills.Length == BattleRankboardDriver.CellCount
            && _rankHighlights != null && _rankHighlights.Length == BattleRankboardDriver.CellCount;
    }
}
