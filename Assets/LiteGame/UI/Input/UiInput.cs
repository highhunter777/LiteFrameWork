using LiteFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace LiteGame
{
    /// <summary>
    /// UI 输入适配（R12 边界判例同 <c>UI/Anim</c> 的 DOTween——**UI 专属适配随 UI 壳归游戏侧**，
    /// InputSystem 直读收敛在本目录这一个文件；其余 UI 代码只经本类的窄口）：
    ///
    /// - <see cref="EnsureEventSystem"/>：输入底座幂等建位（§7 基础设施例外——EventSystem 与 UI 输入
    ///   模块是输入基础设施不是视觉结构，与 [UIRoot] 层级骨架同判据；本文件在
    ///   VisualConstructionScanner 例外表登记）。默认 UI 动作集（Point/Click/Move/Submit/Cancel/
    ///   ScrollWheel——键盘+手柄）由 <see cref="InputSystemUIInputModule.AssignDefaultActions"/> 生成
    ///   （§6.2 键盘/手柄 Submit/Cancel 统一处理的引擎半部）；
    /// - <see cref="BackPressedThisFrame"/>：平台返回键直读（桌面先行口径 = ESC；Android 返回键
    ///   随平台适配批接 Action）——消费面是 <see cref="UIFocusCoordinator"/> 的输入侧轮询。
    /// </summary>
    public static class UiInput
    {
        /// <summary>幂等保障：已存在（含场景自制）即用；EditMode 返回 false（不建真模块——用例自建替身）。</summary>
        public static bool EnsureEventSystem()
        {
            if (EventSystem.current != null) return true;
            if (!Application.isPlaying) return false;

            var go = new GameObject("[UIEventSystem]");
            Object.DontDestroyOnLoad(go);                       // 与 [UIRoot] 同寿（UI 壳不随场景销毁）
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            Log.Info("UI 输入底座就绪：[UIEventSystem] + InputSystemUIInputModule 默认动作集", "UI");
            return true;
        }

        /// <summary>平台返回键本帧是否按下（无键盘环境恒 false——安全跳过）。</summary>
        public static bool BackPressedThisFrame()
            => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }
}
