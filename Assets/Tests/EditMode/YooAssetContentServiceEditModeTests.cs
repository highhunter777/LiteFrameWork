using System;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// YooAssetContentService 用例（C1-⑧ 批次B）。
    /// **EditMode 边界（实测）**：YooAssets.Initialize 内部 DontDestroyOnLoad 仅 Play mode 合法——
    /// 编辑器脚本/测试无法初始化 YooAsset（run_script 探针 2026-09-24 实证 InvalidOperationException）。
    /// 因此本文件只覆盖不依赖 YooAsset 初始化的纯逻辑面（未初始化显性失败/关闭幂等）；
    /// 真资源租约/共享加载/引用归零的端到端验证走 **Player 冒烟**（Preload 经 IContentService 初始化
    /// → [Asset] ready 标记）与后续 L2P lane（接入后补真资源用例）。协调器全语义已在 L1 覆盖
    /// （SharedLoadCoordinatorTests 10 例）。
    /// </summary>
    public sealed class YooAssetContentServiceEditModeTests : UnityTestBase
    {
        [Test]
        public void 未初始化获取_显性失败含location()
        {
            var content = new YooAssetContentService();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                content.AcquireAsync<GameObject>("Assets/UI/Widgets/any.prefab").GetAwaiter().GetResult());
            StringAssert.Contains("Assets/UI/Widgets/any.prefab", ex.Message);   // 契约：失败含 location
        }

        [Test]
        public void 空location_拒绝()
        {
            var content = new YooAssetContentService();
            Assert.Throws<ArgumentNullException>(() =>
                content.AcquireAsync<GameObject>(null).GetAwaiter().GetResult());
        }

        [Test]
        public void 关闭_未初始化调用幂等不抛()
        {
            var content = new YooAssetContentService();
            Assert.DoesNotThrow(() => content.ShutdownAsync().GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => content.ShutdownAsync().GetAwaiter().GetResult());   // 幂等
            Assert.AreEqual(0, content.LiveEntries);
        }
    }
}
