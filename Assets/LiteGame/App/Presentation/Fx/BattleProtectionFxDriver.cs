using System;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 出生保护壳驱动（FX 域；**金色**半透明包裹壳，`fx_spawn_protection`）：保护期内把壳件**跟随对应
    /// 角色视图根**显隐——保护态 = `Frame &lt; InvulnerableUntilFrame`（**端点不含**；**仅成功开火**取消、
    /// 超时自然结束——口径同 HUD 面，单源）。
    ///
    /// - **按槽位一壳**：本地与远端玩家都显示（保护态读口由调用方注入——本地预测态/远端权威快照）；
    /// - 壳件**挂 `_viewRoot`**（**不挂角色视图**——角色视图会回池复用，父子绑定会把壳留给别的实体）；
    /// - 位姿每帧跟随（**仅保护期内写**）；视图缺失 → 诚实不显示（不造第二视觉源）；
    /// - 缺件（壳 prefab 未取）→ 驱动不构造（取件处已警告降级）；壳按需惰性实例化。
    /// - 测试注：保护态/视图查询/壳工厂走构造注入——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleProtectionFxDriver : IDisposable
    {
        /// <summary>壳件包围体相对角色根（脚底）的中心偏移与尺寸：prefab 里的球体以脚底为原点，
        /// 直接用会有一半埋在地里且盖不住头部——运行时按角色躯干中心与身高撑开。</summary>
        public static readonly Vector3 ShellCenterOffset = new Vector3(0f, 0.9f, 0f);
        private static readonly Vector3 ShellScale = new Vector3(1.4f, 1.8f, 1.4f);

        private readonly Func<int, bool> _isProtected;
        private readonly Func<int, GameObject> _viewOf;
        private readonly Func<int, GameObject> _shellForSlot;
        private readonly GameObject[] _shells = new GameObject[SimConfig.MaxEntities];

        private bool _disposed;

        /// <param name="isProtected">槽位 → 是否保护期（本地预测态/远端权威快照由调用方注入）。</param>
        /// <param name="viewOf">槽位 → 角色视图（跟随目标；返回 null = 无视图，本帧不显示）。</param>
        /// <param name="shellForSlot">槽位 → 壳实例（惰性创建；返回 null = 缺件降级）。</param>
        public BattleProtectionFxDriver(Func<int, bool> isProtected,
            Func<int, GameObject> viewOf, Func<int, GameObject> shellForSlot)
        {
            _isProtected = isProtected ?? throw new ArgumentNullException(nameof(isProtected));
            _viewOf = viewOf ?? throw new ArgumentNullException(nameof(viewOf));
            _shellForSlot = shellForSlot ?? throw new ArgumentNullException(nameof(shellForSlot));
        }

        /// <summary>当前点亮的壳数（诊断/测试面）。</summary>
        public int ActiveShellCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _shells.Length; i++)
                    if (_shells[i] != null && _shells[i].activeSelf) count++;
                return count;
            }
        }

        /// <summary>槽位壳当前是否点亮（诊断/测试面）。</summary>
        public bool IsShellActive(int slot)
            => slot >= 0 && slot < _shells.Length && _shells[slot] != null && _shells[slot].activeSelf;

        /// <summary>每渲染帧（视图消费者——同面板/排行榜/死亡 HUD 同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            for (int slot = 0; slot < _shells.Length; slot++)
            {
                bool on = _isProtected(slot);
                GameObject shell = _shells[slot];
                if (!on)
                {
                    if (shell != null && shell.activeSelf) shell.SetActive(false);
                    continue;
                }

                GameObject view = _viewOf(slot);
                if (view == null)
                {
                    // 无视图（灰盒/未建/AOI 缺省）：不造第二视觉源
                    if (shell != null && shell.activeSelf) shell.SetActive(false);
                    continue;
                }

                if (shell == null)
                {
                    shell = _shellForSlot(slot);
                    if (shell == null) continue;
                    shell.transform.localScale = ShellScale;
                    _shells[slot] = shell;
                }
                if (!shell.activeSelf) shell.SetActive(true);
                shell.transform.position = view.transform.position + ShellCenterOffset;
                shell.transform.rotation = Quaternion.identity;   // 壳不随角色转头（保持包裹观感）
            }
        }

        /// <summary>离场收尾（壳实例随 `_viewRoot` 销毁——本件只收起，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _shells.Length; i++)
                if (_shells[i] != null) _shells[i].SetActive(false);
        }
    }
}
