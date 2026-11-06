using System;
using System.Text;
using UnityEngine.InputSystem;

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

    /// <summary>键盘状态/文本输入的平台能力：Input System 单路（工程 activeInputHandler 含 New，包引用为硬依赖）。</summary>
    public static class PlatformKeyInput
    {
        private static readonly StringBuilder Typed = new StringBuilder(64);
        private static object _textOwner;

        public static bool Pressed(PlatformKey key)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard[ToInputKey(key)].wasPressedThisFrame;
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
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null) keyboard.onTextInput += OnTextInput;
                return true;
            }

            if (!ReferenceEquals(_textOwner, owner)) return false;
            _textOwner = null;
            Typed.Clear();
            Keyboard releaseKeyboard = Keyboard.current;
            if (releaseKeyboard != null) releaseKeyboard.onTextInput -= OnTextInput;
            return true;
        }

        /// <summary>取走本帧输入的可见字符（仅当前 owner；不含退格/回车等控制键）。</summary>
        public static string TakeTypedText(object owner)
        {
            if (!ReferenceEquals(_textOwner, owner)) return string.Empty;
            string text = Typed.ToString();
            Typed.Clear();
            return text;
        }

        // 只收可见字符：退格在 Windows 键路里伴随 '\b' 文本事件，收进会与 Backspace 删除相互抵消。
        private static void OnTextInput(char c)
        {
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
    }
}
