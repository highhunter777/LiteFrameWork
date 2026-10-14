using System;
using LiteSim;
using LiteView;
using TMPro;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 死亡/复活 HUD 驱动（阵亡批）：**中央复活倒计时** ＋ **阵亡归属** ＋ **保护期角标**。
    ///
    /// - **显隐/倒计时**读**本地预测态**：<see cref="SimView.IsDead"/> 的死亡锁存判据 ＋
    ///   `EntitySlot.RespawnFrame − Frame`（复活目标绝对帧——剩余帧转整秒，**秒粒度才写**）；
    /// - **归属**吃 `Death` 帧事件（<see cref="IHitFeedbackConsumer"/>）：仅 `role==Received`（我阵亡）
    ///   记击杀者（<see cref="HitFeedbackContext.OtherId"/>，0 = 自杀/环境无归属）；命中/开火不吃；
    ///   **名字为占位**（<see cref="PlayerIdentity"/> 身份序号——与排行榜**共用单源**，真名字归 G3）；
    /// - **保护期** = `Frame &lt; InvulnerableUntilFrame`（**端点不含**；仅**成功开火**取消、超时自然结束
    ///   ——口径由 `MatchRulesTests.AcceptedShotCancelsProtectionButBlockedWeaponDoesNot` 钉住）；
    /// - **写纪律**：全部**值变化才写**（两个根按开关、倒计时按整秒、标题按串变）；
    /// - 测试注：镜像 / 本地槽解析走构造注入（同面板与排行榜驱动）——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleRespawnHudDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>无归属（自杀/环境伤害）时的标题。</summary>
        public const string DeathTitle = "已阵亡";

        /// <summary>保护期角标文案（HUD 固定英文口径——同 <c>RELOADING</c>/<c>SETTINGS</c>）。</summary>
        public const string ProtectedLabel = "PROTECTED";

        /// <summary>60Hz 逻辑帧 → 秒（倒计时换算；不引浮点取整误差）。</summary>
        private const int TicksPerSecond = 60;

        private readonly SimWorldState _state;
        private readonly Func<int> _localSlot;
        private readonly Func<bool> _isLocalDead;
        private readonly RectTransform _respawnRoot;
        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _countdown;
        private readonly RectTransform _protectionRoot;

        private bool _lastDead;
        private int _lastSeconds = int.MinValue;
        private string _lastTitle;
        private bool _lastProtected;
        private long _killerId;
        private bool _disposed;

        /// <param name="state">逻辑镜像（`RespawnFrame`/`InvulnerableUntilFrame`/`Frame` 读面）。</param>
        /// <param name="localSlot">本地槽位（-1 = 未对齐/解析失败）——注入缝（EditMode 免构造 SimView）。</param>
        /// <param name="isLocalDead">本地是否阵亡（预测态锁存判据）。</param>
        /// <param name="protectionRoot">保护期角标根（null = 无角标，静默跳过）。</param>
        public BattleRespawnHudDriver(SimWorldState state, Func<int> localSlot, Func<bool> isLocalDead,
            RectTransform respawnRoot, TextMeshProUGUI title, TextMeshProUGUI countdown,
            RectTransform protectionRoot = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _localSlot = localSlot ?? throw new ArgumentNullException(nameof(localSlot));
            _isLocalDead = isLocalDead ?? throw new ArgumentNullException(nameof(isLocalDead));
            _respawnRoot = respawnRoot ?? throw new ArgumentNullException(nameof(respawnRoot));
            _title = title ?? throw new ArgumentNullException(nameof(title));
            _countdown = countdown ?? throw new ArgumentNullException(nameof(countdown));
            _protectionRoot = protectionRoot;               // null = 无保护角标（静默降级）
            _respawnRoot.gameObject.SetActive(false);
        }

        /// <summary>每渲染帧（视图状态的消费者——同面板/排行榜/准心同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            int slot = _localSlot();
            if (slot < 0)
            {
                HideAll();                                  // 未对齐：如实收起，不猜
                return;
            }

            bool dead = _isLocalDead();
            if (dead != _lastDead)
            {
                _lastDead = dead;
                _respawnRoot.gameObject.SetActive(dead);
            }

            if (dead)
            {
                UpdateTitle();
                UpdateCountdown(slot);
            }
            else
            {
                _killerId = 0L;                             // 复活即清归属（下次阵亡重新记）
                _lastTitle = null;
                _lastSeconds = int.MinValue;                // 下次阵亡 0 秒也要写
            }

            UpdateProtection(slot);
        }

        /// <summary>阵亡归属（只吃 Death × 我阵亡）——命中/开火等其余事件不处理。</summary>
        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed || ctx.Kind != FrameEventKind.Death) return;
            if (ctx.LocalRole != HitLocalRole.Received) return;   // 我阵亡；否则（Caused=我击杀）归后续"击杀提示"批
            _killerId = ctx.OtherId;
        }

        private void UpdateTitle()
        {
            string killer = _killerId != 0L ? PlayerIdentity.PlaceholderName(_state, _killerId) : string.Empty;
            string text = string.IsNullOrEmpty(killer) ? DeathTitle : "被 " + killer + " 击杀";
            if (text == _lastTitle) return;
            _lastTitle = text;
            _title.text = text;
        }

        private void UpdateCountdown(int slot)
        {
            int remaining = _state.Entities[slot].RespawnFrame - _state.Frame;
            int seconds = remaining <= 0 ? 0 : (remaining + TicksPerSecond - 1) / TicksPerSecond;
            if (seconds == _lastSeconds) return;            // 整秒未变不写（逐帧递减不逐帧写）
            _lastSeconds = seconds;
            _countdown.text = seconds.ToString();
        }

        private void UpdateProtection(int slot)
        {
            if (_protectionRoot == null) return;
            bool on = _state.Frame < _state.Entities[slot].InvulnerableUntilFrame;   // 端点不含
            if (on == _lastProtected) return;
            _lastProtected = on;
            _protectionRoot.gameObject.SetActive(on);
        }

        private void HideAll()
        {
            if (_respawnRoot.gameObject.activeSelf) _respawnRoot.gameObject.SetActive(false);
            if (_protectionRoot != null && _protectionRoot.gameObject.activeSelf) _protectionRoot.gameObject.SetActive(false);
            _lastDead = false;
            _lastProtected = false;
            _lastSeconds = int.MinValue;
            _lastTitle = null;
            _killerId = 0L;
        }

        /// <summary>离场收尾（HUD 实例随视图域销毁——本件只把面板/角标归位，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            HideAll();
        }
    }
}
