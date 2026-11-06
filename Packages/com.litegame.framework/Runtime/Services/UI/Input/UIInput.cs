using LiteFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// UI 输入适配（R12 边界判例同 <c>UI/Anim</c> 的 DOTween——**UI 专属适配随 UI 服务装配归 UI 框架侧**， InputSystem 直读收敛在本目录这一个文件。
    /// </summary>
    public static class UIInput
    {
        /// <summary>幂等保障：已存在（含场景自制）即用；EditMode 返回 false（不建真模块——用例自建替身）。</summary>
        public static bool EnsureEventSystem()
        {
            if (EventSystem.current != null) return true;
            if (!Application.isPlaying) return false;

            var go = new GameObject("[UIEventSystem]");
            Object.DontDestroyOnLoad(go);                       // 与 [UIRoot] 同寿（UI 服务装配不随场景销毁）
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            Log.Info("UI 输入底座就绪：[UIEventSystem] + InputSystemUIInputModule 默认动作集", LogTag.UI);
            return true;
        }

        /// <summary>平台返回键本帧是否按下（无键盘环境恒 false——安全跳过）。</summary>
        public static bool BackPressedThisFrame()
            => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }
}
