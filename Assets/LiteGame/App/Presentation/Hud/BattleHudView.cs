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
        [SerializeField] private TextMeshProUGUI _reserveAmmoText;   // 剩余备弹（`Txt_ReserveAmmo`——「当前 / 备弹」读出右半边）
        [SerializeField] private GameObject _slashSeparator;         // 「/」分隔符（`Txt_Slash`——与备弹同显隐）
        [SerializeField] private GameObject _reloadLabel;

        [Header("菜单（对局退场链——Cot_Settings 角组，子 Canvas 单独开射线）")]
        [SerializeField] private Button _settingsButton;

        [Header("排行榜（排行榜批；FFA 比分＝击杀——8 格预置，禁运行时建视觉元素）")]
        [SerializeField] private RectTransform _rankboardRoot;
        [SerializeField] private TextMeshProUGUI _teamScoreText;                    // 团队比分预留（Team 模式；FFA 隐藏）
        [SerializeField] private GameObject[] _rankCells = new GameObject[8];       // 格根＝单元（隐藏关根；格内子件保持激活）
        [SerializeField] private TextMeshProUGUI[] _rankNames = new TextMeshProUGUI[8];
        [SerializeField] private TextMeshProUGUI[] _rankKills = new TextMeshProUGUI[8];
        [SerializeField] private GameObject[] _rankHighlights = new GameObject[8];

        [Header("死亡/复活（阵亡批：中央倒计时 + 保护期角标）")]
        [SerializeField] private RectTransform _respawnRoot;
        [SerializeField] private TextMeshProUGUI _respawnTitle;
        [SerializeField] private TextMeshProUGUI _respawnCountdown;
        [SerializeField] private RectTransform _protectionRoot;

        [Header("击杀提示（击杀提示批：全局最近击杀——多条堆叠、行件全预置）")]
        [SerializeField] private RectTransform _killFeedRoot;
        [SerializeField] private GameObject[] _killFeedRows = new GameObject[BattleKillFeedDriver.RowCount];
        [SerializeField] private TextMeshProUGUI[] _killFeedKillers = new TextMeshProUGUI[BattleKillFeedDriver.RowCount];
        [SerializeField] private Image[] _killFeedWeapons = new Image[BattleKillFeedDriver.RowCount];
        [SerializeField] private TextMeshProUGUI[] _killFeedVictims = new TextMeshProUGUI[BattleKillFeedDriver.RowCount];
        [SerializeField] private Sprite[] _weaponIcons = new Sprite[BattleKillFeedDriver.SlotCount];

        [Header("网络质量（ping/丢包批：RTT / 丢包千分比）")]
        [SerializeField] private RectTransform _netStatsRoot;
        [SerializeField] private TextMeshProUGUI _netStatsText;

        public RectTransform CrosshairRoot => _crosshairRoot;
        public GameObject HipForm => _hipForm;
        public GameObject AdsForm => _adsForm;
        public CanvasGroup PanelGroup => _panelGroup;
        public HpBar HpBar => _hpBar;
        public TextMeshProUGUI AmmoText => _ammoText;

        /// <summary>剩余备弹文本（本人私有面 <see cref="LiteSim.WeaponRuntime.ReserveAmmo"/>——换弹从备弹扣、
        /// 随消耗递减；未装备时由驱动收起，只留 <see cref="AmmoText"/> 的 "--"）。</summary>
        public TextMeshProUGUI ReserveAmmoText => _reserveAmmoText;

        /// <summary>「当前 / 备弹」分隔符——与备弹件**同显隐**（收起时读出只剩 "--"）。</summary>
        public GameObject SlashSeparator => _slashSeparator;

        public GameObject ReloadLabel => _reloadLabel;
        public Button SettingsButton => _settingsButton;
        public RectTransform RankboardRoot => _rankboardRoot;
        public TextMeshProUGUI TeamScoreText => _teamScoreText;
        /// <summary>格根（单元）：**隐藏/显示关这里**，格内子件保持激活（编辑器里可直接读/调）。</summary>
        public GameObject[] RankCells => _rankCells;
        public TextMeshProUGUI[] RankNames => _rankNames;
        public TextMeshProUGUI[] RankKills => _rankKills;
        public GameObject[] RankHighlights => _rankHighlights;
        public RectTransform RespawnRoot => _respawnRoot;
        public TextMeshProUGUI RespawnTitle => _respawnTitle;
        public TextMeshProUGUI RespawnCountdown => _respawnCountdown;
        public RectTransform ProtectionRoot => _protectionRoot;
        public RectTransform KillFeedRoot => _killFeedRoot;
        public GameObject[] KillFeedRows => _killFeedRows;
        public TextMeshProUGUI[] KillFeedKillers => _killFeedKillers;
        public Image[] KillFeedWeapons => _killFeedWeapons;
        public TextMeshProUGUI[] KillFeedVictims => _killFeedVictims;

        /// <summary>武器图标（下标 = 装备槽 <c>0..WeaponSlotsPerEntity-1</c>；来自收集目录复制件——
        /// 数据面单源 <c>tbweapon.icon</c>）。驱动按击杀帧武器槽取图。</summary>
        public Sprite[] WeaponIcons => _weaponIcons;

        public RectTransform NetStatsRoot => _netStatsRoot;
        public TextMeshProUGUI NetStatsText => _netStatsText;

        /// <summary>死亡/复活件是否齐备（中央面板 3 件；保护角标可缺——驱动内静默降级）。</summary>
        public bool HasRespawnHud =>
            _respawnRoot != null && _respawnTitle != null && _respawnCountdown != null;

        /// <summary>排行榜格是否齐备（格根/名字/击杀/高亮各 <see cref="BattleRankboardDriver.CellCount"/> 件）
        /// ——装配期判据，缺件走静默降级（不把"旧 prefab"当对局失败）。</summary>
        public bool HasRankboard =>
            _rankCells != null && _rankCells.Length == BattleRankboardDriver.CellCount
            && _rankNames != null && _rankNames.Length == BattleRankboardDriver.CellCount
            && _rankKills != null && _rankKills.Length == BattleRankboardDriver.CellCount
            && _rankHighlights != null && _rankHighlights.Length == BattleRankboardDriver.CellCount;

        /// <summary>击杀提示件是否齐备（根 + 行四组各 <see cref="BattleKillFeedDriver.RowCount"/> 件）
        /// ——装配期判据，缺件走静默降级（不把"旧 prefab"当对局失败）。</summary>
        public bool HasKillFeed =>
            _killFeedRoot != null
            && _killFeedRows != null && _killFeedRows.Length == BattleKillFeedDriver.RowCount
            && _killFeedKillers != null && _killFeedKillers.Length == BattleKillFeedDriver.RowCount
            && _killFeedWeapons != null && _killFeedWeapons.Length == BattleKillFeedDriver.RowCount
            && _killFeedVictims != null && _killFeedVictims.Length == BattleKillFeedDriver.RowCount;
    }
}
