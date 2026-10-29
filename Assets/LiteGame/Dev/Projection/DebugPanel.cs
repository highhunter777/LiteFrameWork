#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using LiteFramework;
using LiteFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiteGame
{
    /// <summary>
    /// 常用调试面板（F2，UI Toolkit）：控制台之外的轻量投影——本地测试模式进入/退出/传送 +
    /// <see cref="TestModeOptions"/> 的 Live 开关。动作与读写全部经 <see cref="DebugKernel"/>，
    /// 不直调业务服务、不另拼读数；release 随三宏剥离。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugPanel : MonoBehaviour
    {
        private const string InputBlockerName = "debug.panel";

        private readonly List<TestModeOptions.Info> _liveOptions = new List<TestModeOptions.Info>(8);

        private DebugKernel _kernel;
        private IDisposable _inputLease;
        private DebugPanelUiToolkitHost _ui;
        private IReadOnlyList<DebugTunable> _runtimeTunables;
        private bool _tunablesBound;
        private bool _open;
        private bool _uiFailed;
        private float _nextBind;
        private float _nextRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindAnyObjectByType<DebugPanel>() != null) return;
            var go = new GameObject("[DebugPanel]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<DebugPanel>();
        }

        private void Awake()
        {
            Log.Info("开发面板已装配（F2 开关）", LogTag.UI);
        }

        private void Update()
        {
            if (DevHotkey.Pressed(DevKey.F2))
            {
                if (_open) Close();
                else Open();
                return;
            }

            if (!_open) return;

            if (_kernel == null && Time.unscaledTime >= _nextBind)
            {
                _nextBind = Time.unscaledTime + 0.5f;
                BindKernel();
                EnsureInputBlocker();
            }
            EnsureUi();
            if (_kernel != null && !_tunablesBound) RebuildTunables();

            if (_kernel != null && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        private void Open()
        {
            _open = true;
            BindKernel();
            EnsureInputBlocker();
            EnsureUi();
            if (_kernel != null) RebuildTunables();
            _ui?.SetVisible(true);
            _nextRefresh = 0f;
            Refresh();
        }

        private void Close()
        {
            _open = false;
            _inputLease?.Dispose();
            _inputLease = null;
            _ui?.SetVisible(false);
        }

        private void OnDestroy()
        {
            _inputLease?.Dispose();
            _inputLease = null;
            _ui?.Dispose();
            _ui = null;
            _runtimeTunables = null;
        }

        private void BindKernel()
        {
            if (_kernel != null) return;
            _kernel = DevRuntime.Kernel;   // 开发装配装饰期登记（不再经 GameEntry 的调试口）
            if (_kernel == null) return;
            _nextRefresh = 0f;
            if (_liveOptions.Count == 0)
            {
                foreach (TestModeOptions.Info option in _kernel.Options)
                    if (option.Group == TestModeOptions.Group.Live) _liveOptions.Add(option);
                // 早于内核装配建过 UI（空开关表）时重建一次，补上 Live 开关按钮
                if (_ui != null)
                {
                    _ui.Dispose();
                    _ui = null;
                    _tunablesBound = false;
                }
            }
        }

        private void EnsureInputBlocker()
        {
            if (!_open || _kernel == null || _inputLease != null) return;
            try { _inputLease = _kernel.BlockInput(InputBlockerName, "开发面板输入"); }
            catch (Exception ex) { SetStatus("输入拦截注册失败：" + ex.Message, true); }
        }

        private void EnsureUi()
        {
            if (_ui != null || _uiFailed) return;
            try
            {
                _ui = new DebugPanelUiToolkitHost(transform, _liveOptions, OnToggle, OnEnter, OnExit, OnTeleport,
                    null, null, null, OnForceDisconnect, OnForceReconnect, OnToggleSuppressReconnect);
                _ui.SetVisible(_open);
                _tunablesBound = false;
            }
            catch (Exception ex)
            {
                _uiFailed = true;
                Log.Error("开发面板 UI Toolkit 初始化失败：" + ex.Message, LogTag.UI);
            }
        }

        private void Refresh()
        {
            if (_ui == null) return;
            if (_kernel == null)
            {
                _ui.SetStatus("调试内核未装配（非开发包或装配失败）", true);
                return;
            }

            _ui.SetStatus("本地测试模式：" + (_kernel.IsTestModeActive ? "开" : "关")
                + " · 即时开关走本进程");
            UpdateNetStatus();
            for (int i = 0; i < _liveOptions.Count; i++)
            {
                TestModeOptions.Info option = _liveOptions[i];
                _ui.SetOption(option.Id, _kernel.GetOption(option.Id));
            }
            if (_runtimeTunables != null)
            {
                for (int i = 0; i < _runtimeTunables.Count; i++)
                {
                    DebugTunable tunable = _runtimeTunables[i];
                    try { _ui.SetTunableValue(tunable.Id, tunable.Get()); }
                    catch (Exception ex) { Log.Warning("调参读取失败 " + tunable.Id + "：" + ex.Message, LogTag.UI); }
                }
            }
        }

        private void RebuildTunables()
        {
            if (_ui == null || _kernel == null) return;
            _runtimeTunables = _kernel.RuntimeTunables;
            _ui.SetTunables(_runtimeTunables, OnTunableSet, OnTunableReset);
            _tunablesBound = true;
        }

        private void OnTunableSet(DebugTunable tunable, float value)
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            if (!_kernel.TrySetTunable(tunable.Id, value, out string error))
            {
                SetStatus(error, true);
                return;
            }
            Refresh();
        }

        private void OnTunableReset(DebugTunable tunable)
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            if (!_kernel.ResetTunable(tunable.Id, out string error))
            {
                SetStatus(error, true);
                return;
            }
            Refresh();
        }

        private void OnToggle(TestModeOptions.Id id)
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            bool next = !_kernel.GetOption(id);
            if (!_kernel.TrySetOption(id.ToString(), next, out string error))
            {
                SetStatus(error, true);
                return;
            }
            Refresh();
        }

        private void OnEnter()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            _kernel.RequestEnterTestMode();
            SetStatus("已请求进入本地测试模式");
        }

        private void OnExit()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            _kernel.RequestExitTestMode();
            SetStatus("已请求退出本地测试模式");
        }

        private void OnTeleport()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            _kernel.RequestTeleport();
            SetStatus("已请求传送到准心");
        }

        private void OnForceDisconnect()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            bool ok = _kernel.TryForceDisconnect(out string error);
            SetStatus(ok ? "已断线（等待自动重连）" : "拒绝：" + error, !ok);
            Refresh();
        }

        private void OnForceReconnect()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            bool ok = _kernel.TryBeginReconnect(out string error);
            SetStatus(ok ? "已发起重连" : "拒绝：" + error, !ok);
            Refresh();
        }

        private void OnToggleSuppressReconnect()
        {
            if (_kernel == null) { SetStatus("调试内核未装配", true); return; }
            bool next = !(_kernel.Network != null && _kernel.Network.SuppressAutoReconnect);
            bool ok = _kernel.TrySetSuppressAutoReconnect(next, out string error);
            SetStatus(ok ? (next ? "已抑制自动重连（断线后停住）" : "已恢复自动重连") : "拒绝：" + error, !ok);
            Refresh();
        }

        /// <summary>联机读数行（DebugKernel.Network 单源；无对局/未装配各自给空态文案）。</summary>
        private void UpdateNetStatus()
        {
            if (_ui == null) return;
            if (_kernel == null) { _ui.SetNetStatus("网络调试未装配", true); return; }
            NetworkDebug network = _kernel.Network;
            if (network == null) { _ui.SetNetStatus("网络调试未装配", true); return; }
            if (!network.HasBattle) { _ui.SetNetStatus("无对局（未进房）"); return; }

            var sb = new StringBuilder(160);
            network.AppendLines(sb);
            _ui.SetNetStatus(sb.ToString().Replace('\n', ' '));
            _ui.SetSuppressReconnect(network.SuppressAutoReconnect);
        }

        private void SetStatus(string text) => SetStatus(text, false);

        private void SetStatus(string text, bool error)
        {
            if (_ui != null) _ui.SetStatus(text, error);
            else if (error) Log.Warning(text, LogTag.UI);
        }
    }
}
#endif
