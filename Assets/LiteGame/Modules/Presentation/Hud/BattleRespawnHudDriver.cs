using System;
using LiteFramework;
using LiteSim;
using LiteGame.View;
using TMPro;
using UnityEngine;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>
    /// 死亡/复活 HUD 驱动（阵亡批）：**中央复活倒计时** ＋ **阵亡归属** ＋ **保护期角标**。
    ///
    /// - **事实来源**：一律经 <see cref="IBattleReadModel.TryGetLocalRespawn"/>（M2 读模型收口）——
    ///   显隐/倒计时/保护期不再直读 <see cref="SimWorldState"/>；阵亡锁存判据由读模型实例注入
    ///   （表现侧预测态）。
    /// - **归属**吃 `Death` 帧事件（<see cref="IHitFeedbackConsumer"/>）：仅 `role==Received`（我阵亡）
    ///   记击杀者（<see cref="HitFeedbackContext.OtherId"/>，0 = 自杀/环境无归属）；命中/开火不吃；
    ///   **名字为占位**（<see cref="PlayerIdentity"/> 身份序号——与排行榜**共用单源**，真名字归 G3），
    ///   经读模型的 <see cref="IBattleReadModel.DisplayName"/> 取（驱动的唯一名字入口）。
    /// - **保护期** = 读模型 `Protected`（**端点不含**；仅**成功开火**取消、超时自然结束
    ///   ——口径由 `MatchRulesTests.AcceptedShotCancelsProtectionButBlockedWeaponDoesNot` 钉住）；
    /// - **写纪律**：全部**值变化才写**（两个根按开关、倒计时按整秒、标题按串变）；
    /// - 测试注：读模型注入（同面板与排行榜驱动）——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleRespawnHudDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>无归属（自杀/环境伤害）时的标题。</summary>
        public const string DeathTitle = "已阵亡";

        /// <summary>保护期角标文案（HUD 固定英文口径——同 `RELOADING`/`SETTINGS`）。</summary>
        public const string ProtectedLabel = "PROTECTED";

        /// <summary>60Hz 逻辑帧 → 秒（倒计时换算；不引浮点取整误差）。</summary>
        private const int TicksPerSecond = 60;

        private readonly IBattleReadModel _model;
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

        /// <param name="model">对局只读事实端口（本地阵亡/剩余帧/保护期/显示名）。</param>
        /// <param name="protectionRoot">保护期角标根（null = 无角标，静默跳过）。</param>
        public BattleRespawnHudDriver(IBattleReadModel model,
            RectTransform respawnRoot, TextMeshProUGUI title, TextMeshProUGUI countdown,
            RectTransform protectionRoot = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
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

            if (!_model.TryGetLocalRespawn(out BattleRespawnFacts facts))
            {
                HideAll();                                  // 未对齐：如实收起，不猜
                return;
            }

            bool dead = facts.Dead;
            if (dead != _lastDead)
            {
                _lastDead = dead;
                _respawnRoot.gameObject.SetActive(dead);
            }

            if (dead)
            {
                UpdateTitle();
                UpdateCountdown(facts.RemainingFrames);
            }
            else
            {
                _killerId = 0L;                             // 复活即清归属（下次阵亡重新记）
                _lastTitle = null;
                _lastSeconds = int.MinValue;                // 下次阵亡 0 秒也要写
            }

            UpdateProtection(facts.Protected);
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
            string killer = _killerId != 0L ? _model.DisplayName(_killerId) : string.Empty;
            string text = string.IsNullOrEmpty(killer) ? DeathTitle : "被 " + killer + " 击杀";
            if (text == _lastTitle) return;
            _lastTitle = text;
            _title.text = text;
        }

        private void UpdateCountdown(int remainingFrames)
        {
            int seconds = remainingFrames <= 0 ? 0 : (remainingFrames + TicksPerSecond - 1) / TicksPerSecond;
            if (seconds == _lastSeconds) return;            // 整秒未变不写（逐帧递减不逐帧写）
            _lastSeconds = seconds;
            _countdown.text = seconds.ToString();
        }

        private void UpdateProtection(bool on)
        {
            if (_protectionRoot == null) return;
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
