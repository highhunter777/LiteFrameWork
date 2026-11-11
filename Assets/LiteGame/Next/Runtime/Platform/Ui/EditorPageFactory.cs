using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>编辑器页面工厂（P0）：按 PageSpec.Address 从 AssetDatabase 装载 prefab，实例化后保持非激活交给 UiRuntime。</summary>
    public static class EditorPageFactory
    {
        /// <summary>装载并实例化页面；缺资源或根组件不是 PageBase 直接抛（fail-fast，不造替身）。</summary>
        public static async UniTask<PageBase> CreateAsync(PageSpec spec, CancellationToken cancellationToken)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.CompletedTask;
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(spec.Address);
            if (prefab == null)
                throw new InvalidOperationException("页面 prefab 不存在：" + spec.Address);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + "(Page)";
            instance.SetActive(false);

            PageBase page = instance.GetComponent<PageBase>();
            if (page == null)
            {
                DestroyObject(instance);
                throw new InvalidOperationException("页面 prefab 根缺少 PageBase 组件：" + spec.Address);
            }

            return page;
#else
            throw new NotSupportedException("P0 非编辑器未接入内容装载器：" + spec.Address + "（P1 接 YooAsset）");
#endif
        }

#if UNITY_EDITOR
        private static void DestroyObject(GameObject instance)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }
#endif
    }
}
