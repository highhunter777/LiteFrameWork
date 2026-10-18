#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 开发页（dev-only 覆盖件，随 `DevMenuUI.prefab` 由 <see cref="DebugTuner"/> 在编辑器形态开出）：
    /// 常态化开发入口——**大厅页/任意页面在场时都可点**（画布排序在工作台工具条之上）：
    /// 「开发」钮展开面板 → ①测试模式：进训练场（本地服 Room-Test）②UI 工作台：开工作台（置
    /// <see cref="DebugTuner.WorkbenchMode"/>，由开关统一加载工作台场景）③关闭。
    /// 取代已删除的 F9/F10 热键（《UI工作台设计》§5 入口矩阵）。整件随三宏剥离。
    ///
    /// **联机 GM 不开在此处**：云端 GM（免死/无限弹药下发）住在编辑器 GM 面板的「测试模式」页
    /// （《GM与调试工具分层设计》§4 投影面）——本件只做"进什么形态"的入口，避免两处入口分叉。
    /// </summary>
    public sealed class DevMenuPanel : MonoBehaviour
    {
        [SerializeField] private Button _btnToggle;      // 左上「开发」钮
        [SerializeField] private GameObject _panel;      // 展开的面板容器
        [SerializeField] private Button _btnTestMode;    // 测试模式（训练场）
        [SerializeField] private Button _btnWorkbench;   // UI 工作台
        [SerializeField] private Button _btnMenu;        // 设置菜单（工作台落点的退出入口；与 ESC 同一装订）
        [SerializeField] private Button _btnDirectMatch; // 直连对局（原 F9：真 KCP/本机服 Room-A）
        [SerializeField] private Button _btnClose;
        [SerializeField] private TMP_Text _stateText;

        private int _frame;
        private string _lastOp = "—";

        private void Awake()
        {
            _btnToggle.onClick.AddListener(() => _panel.SetActive(!_panel.activeSelf));
            _btnTestMode.onClick.AddListener(OnTestMode);
            _btnWorkbench.onClick.AddListener(OnWorkbench);
            _btnMenu.onClick.AddListener(OnMenu);
            _btnDirectMatch.onClick.AddListener(OnDirectMatch);
            _btnClose.onClick.AddListener(() => _panel.SetActive(false));
            if (_panel != null) _panel.SetActive(false);
        }

        private void Update()
        {
            if ((_frame++ & 31) != 0 || _stateText == null) return;
            var tuner = FindAnyObjectByType<DebugTuner>();
            string mode = tuner == null ? "无开关" : tuner.DirectToTraining ? "直入训练场" : tuner.WorkbenchMode ? "直入工作台" : "常态";
            _stateText.text = $"形态:{mode}  测试模式:{(TestModeRuntime.Active ? "开" : "关")}  最近:{_lastOp}";
        }

        /// <summary>测试模式：快照场景开关 → 请求进房（与 GM 面板/直入同一条读即清请求链）。</summary>
        private void OnTestMode()
        {
            DebugTuner.ApplySnapshotOrDefaults();
            TestModeRuntime.RequestEnter();
            _lastOp = "已请求测试模式（训练场）";
            _panel.SetActive(false);
        }

        /// <summary>直连对局（原 F9 能力）：非测试房——走真 KCP 到本机 RoomServer（联测路径 B）。</summary>
        private void OnDirectMatch()
        {
            TestModeRuntime.RequestDirect();
            _lastOp = "已请求直连对局（Room-A/真 KCP）";
            _panel.SetActive(false);
        }

        /// <summary>设置菜单：请求开对局菜单（工作台落点的退出入口）——由工作台驱动统一开（与 ESC 同一条路）。</summary>
        private void OnMenu()
        {
            DebugTuner.RequestWorkbenchMenu();
            _lastOp = "已请求设置菜单（在工作台内生效）";
            _panel.SetActive(false);
        }

        /// <summary>UI 工作台：置开关——工作台场景的加性加载归 <see cref="DebugTuner"/> 单点负责。</summary>
        private void OnWorkbench()
        {
            var tuner = FindAnyObjectByType<DebugTuner>();
            if (tuner == null) { _lastOp = "场景无 DebugTuner——开关落不了位"; return; }
            tuner.WorkbenchMode = true;
            _lastOp = "已置工作台模式（工作台场景加载中）";
            _panel.SetActive(false);
        }
    }
}
#endif
