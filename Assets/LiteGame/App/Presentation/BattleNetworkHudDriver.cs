using System;
using System.Globalization;
using LiteNet;
using TMPro;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 网络质量 HUD 驱动（ping/丢包批）：显示会话探针（<see cref="NetworkQuality"/>）推断的
    /// **RTT / 丢包千分比**——<c>48ms / 0.3%</c> 一行。
    ///
    /// - **数据源**：应用层探针（<see cref="RoomClient.Quality"/> 经会话透传；1Hz 回声配对）——
    ///   表现/诊断面，**不参与任何判定**；
    /// - **显隐**：<see cref="NetworkQuality.Connected"/> 且 <see cref="NetworkQuality.HasSample"/>
    ///   才显示（离线/无样本整组隐藏——不显示陈旧值）；值/千分位变化才写（脏位）；
    /// - 测试注：质量源走构造注入——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleNetworkHudDriver : IDisposable
    {
        private readonly Func<NetworkQuality> _quality;
        private readonly GameObject _root;
        private readonly TextMeshProUGUI _text;

        private bool _lastShow;
        private int _lastRtt = int.MinValue;
        private int _lastLoss = int.MinValue;
        private bool _disposed;

        public BattleNetworkHudDriver(Func<NetworkQuality> quality, GameObject root, TextMeshProUGUI text)
        {
            _quality = quality;                             // null = 无源（恒显离线口径）
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _root.SetActive(false);                         // 缺省隐藏（首帧按事实开关——避免加载闪一下）
        }

        /// <summary>每渲染帧（视图状态的消费者——同面板/排行榜/击杀提示同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            NetworkQuality q = _quality != null ? _quality() : NetworkQuality.None;
            bool show = q.Connected && q.HasSample;
            if (show != _lastShow)
            {
                _lastShow = show;
                _root.SetActive(show);
            }
            if (!show) return;

            if (q.RttMs == _lastRtt && q.LossPermille == _lastLoss) return;
            _lastRtt = q.RttMs;
            _lastLoss = q.LossPermille;
            // 千分比 → 百分比一位小数（Invariant：不受本机区域设置影响）
            _text.text = string.Format(CultureInfo.InvariantCulture, "{0}ms / {1:0.0}%", q.RttMs, q.LossPermille / 10f);
        }

        /// <summary>离场收尾（HUD 实例随视图域销毁——本件只把件归位，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _root.SetActive(false);
        }
    }
}
