using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Content;
using UnityEngine;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>页面工厂：按 PageSpec 从内容服务装载 prefab，取 PageBase 后交给 UiRuntime。</summary>
    public static class EditorPageFactory
    {
        /// <summary>装载并实例化页面；缺资源或根组件不是 PageBase 直接抛（fail-fast，不造替身）。</summary>
        public static async UniTask<PageBase> CreateAsync(PageSpec spec, CancellationToken cancellationToken,
            NextContentService content)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (content == null) throw new ArgumentNullException(nameof(content));
            GameObject instance = await content.InstantiateAsync(spec.Address, cancellationToken);
            PageBase page = instance.GetComponent<PageBase>();
            if (page == null)
            {
                content.Release(instance);
                throw new InvalidOperationException("页面 prefab 根缺少 PageBase 组件：" + spec.Address);
            }
            return page;
        }
    }
}
