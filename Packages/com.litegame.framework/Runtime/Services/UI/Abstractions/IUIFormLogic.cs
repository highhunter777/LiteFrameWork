using System;

namespace LiteFramework.UI
{
    /// <summary>
    /// 界面逻辑契约：C# 界面逻辑与 LuaBehaviourAdapter（§2.3）同面。
    /// </summary>
    public interface IUIFormLogic
    {
        void OnInit(UIForm form, IUIData data);  // 实例化后一次（首次加载；池化复用不重复调）
        void OnShow(IUIData data);               // 激活（Loading→Active）/恢复（Paused→Active）
        void OnUpdate(float deltaTime);          // 仅 Active 态（UIService.Tick 驱动）
        void OnPause();                          // 手动暂停
        void OnCover();                          // 被更高层级组全屏界面遮盖（批量语义）
        void OnReveal();                         // 遮盖解除
        void OnHide();                           // 关闭（进入 Closing→Recycled 前）

        /// <summary>
        /// 外壳要丢弃本逻辑实例时的收尾钩子（<see cref="UIForm.DropLogic"/>、逻辑替换、 打开失败回滚三处都走它）。
        /// </summary>
        void Release()
        {
            // 默认空实现：C# 界面逻辑通常不持有需归还的外部引用。
            // LuaBehaviourAdapter 覆写它来释放 Lua 表引用（防"拿已 Dispose 的 LuaFunction 打进死环境"）。
        }
    }
}
