#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LiteClient
{
    /// <summary>DevHUD 用到的功能键（不暴露 KeyCode，便于输入后端切换）。</summary>
    public enum DevKey
    {
        F1,
        F2,
        F3,
        F4,
        F5,
        F6,
        F7,
        Backspace,
        Enter,
        Escape,
    }

    /// <summary>
    /// 开发工具热键/文本输入后端：Input System 可用时走 `Keyboard.current`，否则旧 `Input`。
    /// 放在平台适配层（`LiteClient.Platform.Unity`）是因为 App 层禁直接 import InputSystem（R12）；
    /// 本类由三宏剥离，release 不编译。切 New only 时不会回退旧 Input（`Input.GetKeyDown` 会抛）。
    /// </summary>
    public static class DevHotkey
    {
        private static readonly StringBuilder Typed = new StringBuilder(64);
        private static bool _textActive;

        public static bool Pressed(DevKey key)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard[ToInputKey(key)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(ToKeyCode(key));
#endif
        }

        /// <summary>过滤框模式：开启时接收字符（Input System 走 onTextInput；旧 Input 走 inputString）。</summary>
        public static void SetTextInputActive(bool active)
        {
            if (_textActive == active) return;
            _textActive = active;
            Typed.Clear();
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (active) keyboard.onTextInput += OnTextInput;
            else keyboard.onTextInput -= OnTextInput;
#endif
        }

        /// <summary>取走本帧输入的可见字符（不含退格/回车等控制键）。</summary>
        public static string TakeTypedText()
        {
            if (!_textActive) return string.Empty;
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
            if (_textActive) Typed.Append(c);
        }

        private static Key ToInputKey(DevKey key)
        {
            switch (key)
            {
                case DevKey.F1: return Key.F1;
                case DevKey.F2: return Key.F2;
                case DevKey.F3: return Key.F3;
                case DevKey.F4: return Key.F4;
                case DevKey.F5: return Key.F5;
                case DevKey.F6: return Key.F6;
                case DevKey.F7: return Key.F7;
                case DevKey.Backspace: return Key.Backspace;
                case DevKey.Enter: return Key.Enter;
                case DevKey.Escape: return Key.Escape;
                default: return Key.None;
            }
        }
#else
        private static KeyCode ToKeyCode(DevKey key)
        {
            switch (key)
            {
                case DevKey.F1: return KeyCode.F1;
                case DevKey.F2: return KeyCode.F2;
                case DevKey.F3: return KeyCode.F3;
                case DevKey.F4: return KeyCode.F4;
                case DevKey.F5: return KeyCode.F5;
                case DevKey.F6: return KeyCode.F6;
                case DevKey.F7: return KeyCode.F7;
                case DevKey.Backspace: return KeyCode.Backspace;
                case DevKey.Enter: return KeyCode.Return;
                case DevKey.Escape: return KeyCode.Escape;
                default: return KeyCode.None;
            }
        }
#endif
    }
}
#endif
