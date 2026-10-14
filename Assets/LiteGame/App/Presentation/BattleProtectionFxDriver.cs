using System;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 出生保护壳驱动（阵亡批；**金色**半透明包裹壳，`fx_spawn_protection`）：保护期内把壳件**跟随本地
    /// 角色视图根**显隐——保护态 = `Frame &lt; InvulnerableUntilFrame`（**端点不含**；**仅成功开火**取消、
    /// 超时自然结束——口径同 HUD 面，单源）。
    ///
    /// - 壳件**挂 `_viewRoot`**（**不挂角色视图**——角色视图会回池复用，父子绑定会把壳留给别的实体）；
    /// - 位姿每帧跟随（**仅保护期内写**）；灰盒/角色视图缺失 → 诚实不显示（不造第二视觉源）；
    /// - 缺件（壳 prefab 未取）→ 驱动不构造（取件处已警告降级）。
    /// - 测试注：镜像/壳件走构造注入——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleProtectionFxDriver : IDisposable
    {
        private readonly SimWorldState _state;
        private readonly Func<int> _localSlot;
        private readonly Func<int, GameObject> _viewOf;
        private readonly GameObject _shell;

        private bool _lastActive;
        private bool _disposed;

        /// <param name="state">逻辑镜像（`Frame`/`InvulnerableUntilFrame` 读面）。</param>
        /// <param name="localSlot">本地槽位（-1 = 未对齐）——注入缝（EditMode 免构造 SimView）。</param>
        /// <param name="viewOf">槽位 → 角色视图（跟随目标；返回 null = 无视图，本帧不跟随）。</param>
        /// <param name="shell">壳实例（挂 `_viewRoot`）。</param>
        public BattleProtectionFxDriver(SimWorldState state, Func<int> localSlot,
            Func<int, GameObject> viewOf, GameObject shell)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _localSlot = localSlot ?? throw new ArgumentNullException(nameof(localSlot));
            _viewOf = viewOf ?? throw new ArgumentNullException(nameof(viewOf));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _shell.SetActive(false);
        }

        /// <summary>壳件当前是否点亮（诊断/测试面）。</summary>
        public bool ShellActive => _lastActive;

        /// <summary>每渲染帧（视图消费者——同面板/排行榜/死亡 HUD 同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            int slot = _localSlot();
            bool on = slot >= 0 && _state.Frame < _state.Entities[slot].InvulnerableUntilFrame;
            if (on != _lastActive)
            {
                _lastActive = on;
                _shell.SetActive(on);
            }
            if (on) FollowLocalView(slot);
        }

        private void FollowLocalView(int slot)
        {
            GameObject view = _viewOf(slot);
            if (view == null) return;                      // 灰盒/视图缺失：不猜位姿
            _shell.transform.position = view.transform.position;
            _shell.transform.rotation = Quaternion.identity;   // 壳不随角色转头（保持包裹观感）
        }

        /// <summary>离场收尾（壳实例随 `_viewRoot` 销毁——本件只收起，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lastActive = false;
            if (_shell != null) _shell.SetActive(false);
        }
    }
}
