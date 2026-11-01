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
}
