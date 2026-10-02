using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>
    /// 事件中心:跨语言事件桥的基座。注册进 DI,由 GameEntry 驱动 Tick。
    /// 接口只留业务契约——ITickable / IModuleStats 由实现类 EventCenter 自行声明(ISP):
    /// ① mock 不必实现 tick/stats;② 只有用 PublishQueued 才需要被驱动,纯派发实现可以不 Tick。
    /// 驱动方(GameEntry)持有具体类型并加入 ITickable 列表。
    /// </summary>
    public interface IEventCenter
    {
        /// <summary>订阅。返回注销委托,幂等可重复调用(返回注销委托而非裸注册)。</summary>
        Action Subscribe<T>(Action<T> handler) where T : class;

        /// <summary>立即派发(热路径,稳态零 GC)。发布即移交所有权,派发完成后池化事件由中心统一回收。</summary>
        void Publish<T>(T e) where T : class;

        /// <summary>入队,Tick 统一派发(低频便利)。class 约束下 object 装箱为零成本引用上转。</summary>
        void PublishQueued<T>(T e) where T : class;

        /// <summary>默认按构建分流：编辑器/开发构建 true（未订阅事件告警），release false；运行时可覆盖。</summary>
        bool StrictMode { get; set; }
    }
}
