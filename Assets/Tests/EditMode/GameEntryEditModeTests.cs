using System;
using System.Reflection;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// GameEntry 静态纪律用例（《商业级通用客户端框架总设计》§6.1
    /// "使用 RuntimeInitializeOnLoadMethod(SubsystemRegistration) 清理静态兼容状态，支持关闭 Domain Reload 的编辑器场景"）：
    /// 重复引导守卫 s_booted 是 GameEntry 仅存的静态——Domain-Reload-Off 下跨 Play 残留，
    /// 不复位会把第二次 Play 的引导件当重复件静默销毁（引导死锁）。ResetForEditorReload 是唯一复位入口。
    /// </summary>
    public sealed class GameEntryEditModeTests : UnityTestBase
    {
        private static FieldInfo BootedField()
            => typeof(GameEntry).GetField("s_booted", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void 静态重置_s_booted跨Play残留被复位()
        {
            var field = BootedField();
            Assert.NotNull(field, "s_booted 字段改名——同步更新本用例与 ResetForEditorReload");

            field.SetValue(null, true);                          // 模拟 Domain-Reload-Off 下上一场 Play 的残留
            GameEntry.ResetForEditorReload();                    // SubsystemRegistration 钩子在每次 Play 前调用

            Assert.False((bool)field.GetValue(null), "残留守卫未复位——第二次 Play 引导件会被误杀");
        }

        [Test]
        public void 静态重置_未引导态调用无副作用()
        {
            GameEntry.ResetForEditorReload();                    // 任何时候调用都安全（幂等清理）
            Assert.False((bool)BootedField().GetValue(null));
        }

        [Test]
        public void 入口类型名可解析且实现IClientEntry()
        {
            // B1.5 守卫：AOT 壳按程序集限定名反射装载；名字漂移 = 冷启动即“入口类型无法解析”。
            Type type = Type.GetType(ClientEntryLoader.EntryTypeName, throwOnError: false);
            Assert.NotNull(type, "入口类型无法解析：" + ClientEntryLoader.EntryTypeName);
            Assert.IsTrue(typeof(IClientEntry).IsAssignableFrom(type), "入口类型未实现 IClientEntry：" + type.FullName);
        }

        [Test]
        public void 冷启动错误面_固定标记与根件幂等()
        {
            Assert.AreEqual("[BootstrapError]", BootstrapErrorPresenter.SmokeMarker);
            try
            {
                BootstrapErrorPresenter.Show("guard smoke", ErrorKind.Fatal);
                Assert.NotNull(GameObject.Find(BootstrapErrorPresenter.ErrorUIRootName),
                    "冷启动错误面未创建最小错误 UI");

                BootstrapErrorPresenter.Show("guard smoke again", ErrorKind.Fatal);   // 幂等：Error 可重入
                Assert.NotNull(GameObject.Find(BootstrapErrorPresenter.ErrorUIRootName));
            }
            finally
            {
                var root = GameObject.Find(BootstrapErrorPresenter.ErrorUIRootName);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
