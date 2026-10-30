using System;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
{
    /// <summary>页面确认缝端口（Lua <c>self.ui:ShowConfirm</c> 的服务端；UI 能力层只认这个口）。
    /// 实现由装配根在后段绑定（<see cref="ConfirmDialogRouter.Bind"/>）——UI 段因此不认识任何具体实现。</summary>
    public interface IConfirmDialog
    {
        /// <summary>是否已绑定实现（false = 按取消返回，见 <see cref="ConfirmDialogRouter"/>）。</summary>
        bool IsBound { get; }

        /// <summary>弹确认框并等待结果。true = 用户确认；false = 取消/被外部关闭/未绑定。</summary>
        UniTask<bool> ShowAsync(string title, string message, string okText, string cancelText);
    }

    /// <summary>
    /// 确认框路由器：**先建口、后绑定实现**的可变槽（<see cref="IConfirmDialog"/> 的唯一产品实现）。
    ///
    /// 为什么需要：UI 段在装配序里早于装配根（Container），而确认框的真实现
    /// （<c>FeedbackService.ConfirmAsync → DialogService</c>）由后段建。UI 段直接 <c>Require&lt;FeedbackService&gt;</c>
    /// 会给模块依赖声明留下"UiService → Container"的反向边（启动期查不出的隐性依赖）；
    /// 改为：UI 段**产出**本路由器，装配根在后段 <see cref="Bind"/> 真实现。
    ///
    /// **未绑定**：按取消返回 false，**只记一条 Warning**（不抛、不悬挂——Lua 回调照常收到 false；
    /// 装配缺口可见但不阻断起服）。
    /// </summary>
    public sealed class ConfirmDialogRouter : IConfirmDialog
    {
        private Func<string, string, string, string, UniTask<bool>> _show;
        private int _warnCount;

        /// <inheritdoc />
        public bool IsBound => _show != null;

        /// <summary>未绑定时记录的告警条数（诊断读口：验证"只记一条"）。</summary>
        public int WarningCount => _warnCount;

        /// <summary>绑定实现（装配期一次；重复绑定以最后一次为准——热重载/重建 env 同口径）。</summary>
        public void Bind(Func<string, string, string, string, UniTask<bool>> show)
            => _show = show ?? throw new ArgumentNullException(nameof(show));

        /// <summary>解除绑定（模块关闭：不留指向已拆服务的委托）。</summary>
        public void Unbind() => _show = null;

        /// <inheritdoc />
        public UniTask<bool> ShowAsync(string title, string message, string okText, string cancelText)
        {
            Func<string, string, string, string, UniTask<bool>> show = _show;
            if (show != null) return show(title, message, okText, cancelText);

            if (_warnCount == 0)
            {
                _warnCount = 1;
                Log.Warning("确认框未绑定实现——按取消返回 false（装配缺口：确认框实现由装配根后段 Bind）", LogTag.UI);
            }
            return UniTask.FromResult(false);
        }
    }
}
