using System;
using Cysharp.Threading.Tasks;

namespace LiteFramework.UI
{

    /// <summary>
    /// 确认框路由器：**先建口、后绑定实现**的可变槽（<see cref="IConfirmDialog"/> 的唯一产品实现）。
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
