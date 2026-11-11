using System;
using LiteFramework;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using LiteGame.Adapters.Dev.Input;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// DevHotkey 文本收集契约：控制字符（退格 '\b'/回车 '\n'）不得进文本缓冲—— 真实 Windows 键路里退格伴随 WM_CHAR('\b') 文本事件。
    /// </summary>
    public sealed class DevHotkeyEditModeTests
    {
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(DevHotkey.SetTextInputActive(_owner, true), "文本输入被其他 owner 占用");
        }

        [TearDown]
        public void TearDown()
        {
            DevHotkey.SetTextInputActive(_owner, false);
        }

        [Test]
        public void 文本收集_控制字符不进缓冲()
        {
            Keyboard kb = Keyboard.current;
            Assume.That(kb, Is.Not.Null, "本机无键盘设备（无头环境跳过）");

            InputSystem.QueueTextEvent(kb, 'a');
            InputSystem.QueueTextEvent(kb, '\b');
            InputSystem.QueueTextEvent(kb, '\n');
            InputSystem.Update();

            Assert.AreEqual("a", DevHotkey.TakeTypedText(_owner));
        }

        [Test]
        public void 退格键边沿_经状态事件可见()
        {
            // wasPressedThisFrame 依赖动态帧循环：编辑器在后台时不推进该标志， InputSystem.Update() 也不刷新——后台跑必红，属环境非缺陷（语义由前台/PlayMode 验）。
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                Assert.Ignore("编辑器在后台：wasPressedThisFrame 无动态帧循环");

            Keyboard kb = Keyboard.current;
            Assume.That(kb, Is.Not.Null, "本机无键盘设备（无头环境跳过）");

            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Backspace));
            InputSystem.Update();
            Assert.IsTrue(DevHotkey.Pressed(DevKey.Backspace), "按下帧应见边沿");

            InputSystem.QueueStateEvent(kb, new KeyboardState());
            InputSystem.Update();
            Assert.IsFalse(DevHotkey.Pressed(DevKey.Backspace), "释放后不应再见边沿");
        }
    }
}
