using System;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LiteFramework
{
    /// <summary>平台层功能键名（不暴露 InputSystem/KeyCode 类型，便于后端切换与上层复用）。</summary>
    public enum PlatformKey
    {
        F1, F2, F3, F4, F5, F6, F7, F8, F9,
        Backspace,
        Enter,
        Escape,
    }

    /// <summary>
    /// 键盘状态/文本输入的平台能力（**无开发宏**）：Input System 可用时走 <c>Keyboard.current</c>，
    /// 否则旧 <c>Input</c>。放在平台适配层是因为 R12 禁 App/Dev 直接 import InputSystem；
    /// 消费方（DevHUD/开发控制台）在 `LiteGame.Dev` 内，发布构建不编译消费者 = 本类无产品调用点。
    /// 切 New only 时不会回退旧 Input（<c>Input.GetKeyDown</c> 会抛）。
    /// </summary>
    public static class PlatformKeyInput
    {
        private static readonly StringBuilder Typed = new StringBuilder(64);
        private static object _textOwner;

        public static bool Pressed(PlatformKey key)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard[ToInputKey(key)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(ToKeyCode(key));
#endif
        }

        /// <summary>文本输入所有权：同一时刻只允许一个调试面收字符（HUD 过滤 / 控制台互斥）。
        /// 返回 false = 另一个 owner 正占用（调用方自行提示/重试），不抢占、不静默吞字。</summary>
        public static bool SetTextInputActive(object owner, bool active)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (active)
            {
                if (ReferenceEquals(_textOwner, owner)) return true;
                if (_textOwner != null) return false;
                _textOwner = owner;
                Typed.Clear();
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null) keyboard.onTextInput += OnTextInput;
#endif
                return true;
            }

            if (!ReferenceEquals(_textOwner, owner)) return false;
            _textOwner = null;
            Typed.Clear();
#if ENABLE_INPUT_SYSTEM
            Keyboard releaseKeyboard = Keyboard.current;
            if (releaseKeyboard != null) releaseKeyboard.onTextInput -= OnTextInput;
#endif
            return true;
        }

        /// <summary>取走本帧输入的可见字符（仅当前 owner；不含退格/回车等控制键）。</summary>
        public static string TakeTypedText(object owner)
        {
            if (!ReferenceEquals(_textOwner, owner)) return string.Empty;
#if ENABLE_INPUT_SYSTEM
            string text = Typed.ToString();
            Typed.Clear();
            return text;
#else
            string raw = Input.inputString;
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= ' ' && c != '\b') sb.Append(c);
            }
            return sb.ToString();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static void OnTextInput(char c)
        {
            // 只收可见字符：真实 Windows 键路里退格伴随 '\b' 文本事件（WM_CHAR），收进去会与
            // Backspace 键删除相互抵消（表现为"删不掉字符"）——与旧 Input 路径的 c >= ' ' 同口径
            if (_textOwner != null && c >= ' ') Typed.Append(c);
        }

        private static Key ToInputKey(PlatformKey key)
        {
            switch (key)
            {
                case PlatformKey.F1: return Key.F1;
                case PlatformKey.F2: return Key.F2;
                case PlatformKey.F3: return Key.F3;
                case PlatformKey.F4: return Key.F4;
                case PlatformKey.F5: return Key.F5;
                case PlatformKey.F6: return Key.F6;
                case PlatformKey.F7: return Key.F7;
                case PlatformKey.F8: return Key.F8;
                case PlatformKey.F9: return Key.F9;
                case PlatformKey.Backspace: return Key.Backspace;
                case PlatformKey.Enter: return Key.Enter;
                case PlatformKey.Escape: return Key.Escape;
                default: return Key.None;
            }
        }
#else
        private static KeyCode ToKeyCode(PlatformKey key)
        {
            switch (key)
            {
                case PlatformKey.F1: return KeyCode.F1;
                case PlatformKey.F2: return KeyCode.F2;
                case PlatformKey.F3: return KeyCode.F3;
                case PlatformKey.F4: return KeyCode.F4;
                case PlatformKey.F5: return KeyCode.F5;
                case PlatformKey.F6: return KeyCode.F6;
                case PlatformKey.F7: return KeyCode.F7;
                case PlatformKey.F8: return KeyCode.F8;
                case PlatformKey.F9: return KeyCode.F9;
                case PlatformKey.Backspace: return KeyCode.Backspace;
                case PlatformKey.Enter: return KeyCode.Return;
                case PlatformKey.Escape: return KeyCode.Escape;
                default: return KeyCode.None;
            }
        }
#endif
    }
}
