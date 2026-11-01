using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>SafeArea 策略（刘海屏红线）：解析避让矩形。默认 = Screen.safeArea 原样
    /// （桌面/编辑器 = 全屏；异形屏 = 真实安全区；横竖屏 / 分屏自动跟随）。</summary>
    public interface ISafeAreaStrategy
    {
        Rect Resolve();
    }
}
