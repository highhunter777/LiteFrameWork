using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// HUD 装配器基类：只收“视图根绑定 + 驱动列表 + 同帧派发”三件事；
    /// 各 HUD 族的组装/接线/具体 Dispose 顺序留在派生装配器，基类不认任何具体驱动。
    /// </summary>
    public abstract class HudAssemblerBase<TView> where TView : class, IHudView
    {
        private readonly List<IHudDriver> _drivers = new List<IHudDriver>(8);

        /// <summary>当前 HUD 视图（未 Attach = null；各 Attach 子步骤按此判空降级）。</summary>
        protected TView View { get; private set; }

        /// <summary>绑定视图（各 HUD 族的 Attach 首步调用）。</summary>
        protected void Bind(TView view) => View = view;

        /// <summary>解绑视图（HUD 根件销毁后调用；不自动 Dispose 驱动）。</summary>
        protected void Unbind() => View = null;

        /// <summary>登记一个驱动（顺序即 Tick 顺序）。</summary>
        protected void Add(IHudDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            _drivers.Add(driver);
        }

        /// <summary>移除一个驱动（各 HUD 族 Dispose* 件时调用）。</summary>
        protected void Remove(IHudDriver driver) => _drivers.Remove(driver);

        /// <summary>每渲染帧按装配顺序派发（基类唯一驱动入口）。</summary>
        public void Tick(float deltaSeconds)
        {
            for (int i = 0; i < _drivers.Count; i++)
                _drivers[i].Tick(deltaSeconds);
        }
    }
}
