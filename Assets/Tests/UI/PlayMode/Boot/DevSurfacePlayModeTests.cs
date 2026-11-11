using System.Collections;
using LiteFramework;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Dev;
using LiteGame.Adapters.Dev.Projection;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// **6b 装配搬迁的 PlayMode 冒烟**（M3-6b 收尾补测）：验证开发面整件迁入 `LiteGame.Dev` 后， 真实 Play 会话里仍然"绑得上、建得出、按得动"。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class DevSurfacePlayModeTests
    {
        [UnityTest]
        public IEnumerator 开发装配在位_会话与诊断端口是开发版()
        {
            yield return null;   // 等 BeforeSceneLoad 绑定与 AfterSceneLoad 自创建落地

            Assert.IsNotNull(DevHooks.Current, "DevHooks 未绑定——Dev 程序集引导钩子没跑");
            IDevSession session = DevHooks.Current.CreateSession();
            Assert.IsNotNull(session, "开发会话工厂返回 null");
            Assert.IsTrue(session.IsDevelopmentBuild, "Play 会话里应拿到开发版会话");
            Assert.IsNotNull(DevHooks.Diagnostics, "对局诊断端口未提供（Dev 装配未就位）");
        }

        [UnityTest]
        public IEnumerator 开发面自创建_HUD与控制台与面板都在场()
        {
            yield return null;

            Assert.IsNotNull(Object.FindAnyObjectByType<DevHud>(), "DevHUD 未自创建");
            Assert.IsNotNull(GameObject.Find("[DevHUD]"), "[DevHUD] 宿主缺失");
            Assert.IsNotNull(GameObject.Find("[DebugConsole]"), "[DebugConsole] 宿主缺失");
            Assert.IsNotNull(GameObject.Find("[DebugPanel]"), "[DebugPanel] 宿主缺失");
        }

        [UnityTest]
        public IEnumerator F1_真实按键_开发HUD开关往返()
        {
            yield return null;
            DevHud hud = Object.FindAnyObjectByType<DevHud>();
            Assert.IsNotNull(hud, "DevHUD 未自创建——热键链无可验对象");

            bool before = hud.Visible;
            try
            {
                yield return PressKey(Key.F1);
                Assert.AreEqual(!before, hud.Visible, "F1 应翻转 HUD 可见性（真实键盘事件路径）");

                yield return PressKey(Key.F1);
                Assert.AreEqual(before, hud.Visible, "再按 F1 应还原");
            }
            finally
            {
                if (hud != null) hud.Visible = before;
            }
        }

        /// <summary>真键盘事件：按下 → 等一帧（InputSystem 动态更新 + 脚本 Update 同帧可见边沿）→ 松开。</summary>
        private static IEnumerator PressKey(Key key)
        {
            Keyboard keyboard = Keyboard.current;
            Assume.That(keyboard, Is.Not.Null, "本机无键盘设备（无头环境跳过）");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
    }
}
