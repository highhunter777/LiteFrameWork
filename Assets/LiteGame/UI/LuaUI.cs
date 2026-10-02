using System;

namespace LiteGame
{
    /// <summary>
    /// 界面数据契约：ShowAsync / OnInit / OnShow 的传参统一收口到此接口——
    /// 生命周期不裸传 object（编译期类型安全；调用方定义数据记录实现本接口即可）。
    /// </summary>
    public interface IUIData
    {
    }

    /// <summary>
    /// 界面逻辑契约：C# 界面逻辑与 LuaBehaviourAdapter（§2.3）同面。
    ///
    /// **文件名与命名空间的分离是有意的**（§5.1 逻辑边界）：本文件叫 `LuaUI.cs`
    /// 是因为它的主要消费者是 Lua 界面逻辑，但它**零 xLua 依赖**——纯契约。
    /// xLua 实现在 `LuaBridge/LuaBehaviourAdapter.cs`（`LuaUIData` 同处），
    /// 依赖方向为 **LuaBridge → UI 契约**，UI 侧不认识脚本运行时。
    /// 这与 `ILuaRegistry`（住核心层、无 xLua）同一纪律：**Lua 契约不带具体运行时**。
    /// 数据传参一律 <see cref="IUIData"/>（禁 object）；调用一律经 <see cref="UIForm"/> 的 SafeCall 防护——
    /// 单个回调抛异常 = 该界面降级，不炸壳（错误语义同事件桥，§3.4 降级细则）。
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
        /// 外壳要丢弃本逻辑实例时的收尾钩子（<see cref="UIForm.DropLogic"/>、逻辑替换、
        /// 打开失败回滚三处都走它）。
        ///
        /// **为什么需要它**（《客户端总设计》§5.1"先形成逻辑边界"）：外壳不直接对适配器做类型判断
        /// （那会让**通用 UI 运行时必须认识脚本适配器**，等于把通用层焊死在 xLua 上，asmdef 拆不开）。
        /// 由逻辑**自己**申明"我持有需要归还的东西"，外壳只管调这一个方法，
        /// 于是 UI 运行时只依赖 <see cref="IUIFormLogic"/> 这一张契约。
        ///
        /// **实现要求**：**必须幂等**——同一实例可能被多次丢弃（关→回滚→再关）。
        /// 默认实现为空（无外部持有的纯 C# 逻辑无需实现）。
        /// </summary>
        void Release()
        {
            // 默认空实现：C# 界面逻辑通常不持有需归还的外部引用。
            // LuaBehaviourAdapter 覆写它来释放 Lua 表引用（防"拿已 Dispose 的 LuaFunction 打进死环境"）。
        }
    }

    /// <summary>空逻辑（无 C# 逻辑、Lua 逻辑未接前的占位——§2.3 前壳可独立运行）。</summary>
    public sealed class NullUIFormLogic : IUIFormLogic
    {
        public static readonly NullUIFormLogic Instance = new NullUIFormLogic();

        private NullUIFormLogic() { }

        public void OnInit(UIForm form, IUIData data) { }
        public void OnShow(IUIData data) { }
        public void OnUpdate(float deltaTime) { }
        public void OnPause() { }
        public void OnCover() { }
        public void OnReveal() { }
        public void OnHide() { }
    }
}
