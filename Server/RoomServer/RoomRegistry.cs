using System;
using System.Collections.Generic;

namespace RoomServer
{
    /// <summary>
    /// 房间表（《商业级通用服务端框架总设计》§6"一个 roomId 只能映射一个独立 RoomActor"的宿主侧持有件
    /// ——自 <see cref="ServerHost"/> 外提，边界见《服务端宿主结构拆分专项设计》§2）：
    /// 字典（roomId → 实例）＋插入序 List（Pump/Ops 遍历序）双结构，容量判定与查询、增删均带
    /// **身份校验**（同房号重建后的新实例不得被旧实例的清理路径误删——ReferenceEquals 判据）。
    ///
    /// **职责边界**：本类只持"表"——房间的**构造**（模板/多图/数值/Worker 驱动绑定）与**销毁清理**
    /// （会话路由复位、邮箱 Complete）留在宿主：宿主清完场经 <see cref="Remove"/> 落表，
    /// 构造好经 <see cref="Add"/> 入表。
    ///
    /// **线程契约**：变更只发生在宿主主线程（权威循环/传输事件——同拆分前纪律）；心跳线程等
    /// 低频读者经 <see cref="SnapshotArray"/>（扩容竞态以 catch 兜底：本跳放弃、下跳再报——
    /// 上报面本就是"近似快照"非权威）。Pump 的在册遍历用 <see cref="Order"/>（宿主线程同栈
    /// 读改，遍历中增删的下标补偿归调用方——与拆分前语义一致）。
    /// </summary>
    internal sealed class RoomRegistry
    {
        private readonly Dictionary<string, RoomInstance> _byId =
            new Dictionary<string, RoomInstance>(StringComparer.Ordinal);

        private readonly List<RoomInstance> _order = new List<RoomInstance>();

        private readonly int _capacity;

        /// <param name="capacity">房间容量上限（达上限拒绝新建——来源 RoomServerConfig.MaxRooms，
        /// 装载期定值；嵌入式单房形态为 1）。</param>
        public RoomRegistry(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 1;
        }

        /// <summary>当前在册房间数。</summary>
        public int Count
        {
            get { return _order.Count; }
        }

        /// <summary>是否已达容量上限（新建房的拒绝判据）。</summary>
        public bool AtCapacity
        {
            get { return _order.Count >= _capacity; }
        }

        /// <summary>插入序首房（在册为空 = null）——宿主"首房间兼容投影"与输出应用定位序的读口。</summary>
        public RoomInstance First
        {
            get { return _order.Count > 0 ? _order[0] : null; }
        }

        /// <summary>插入序视图（宿主线程遍历用；跨线程读走 <see cref="SnapshotArray"/>）。</summary>
        public IReadOnlyList<RoomInstance> Order
        {
            get { return _order; }
        }

        /// <summary>按房间号查在册实例（不存在 = false；不创建——建房要容量校验与配置模板，归宿主）。</summary>
        public bool TryGetInstance(string roomId, out RoomInstance instance)
        {
            return _byId.TryGetValue(roomId, out instance);
        }

        /// <summary>入表（字典按 roomId 索引赋值＋插入序追加——调用方保证构造已完成）。</summary>
        public void Add(RoomInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _byId[instance.RoomId] = instance;
            _order.Add(instance);
        }

        /// <summary>该实例是否仍在册且**就是**当前登记实例（同房号可能已重建——身份不符即否）。</summary>
        public bool IsRegistered(RoomInstance instance)
        {
            return instance != null
                && _byId.TryGetValue(instance.RoomId, out RoomInstance registered)
                && ReferenceEquals(registered, instance);
        }

        /// <summary>出表（身份校验：非当前登记实例不动表——防旧实例清理路径误删重建后的新房间）。</summary>
        public bool Remove(RoomInstance instance)
        {
            if (!IsRegistered(instance)) return false;
            _byId.Remove(instance.RoomId);
            _order.Remove(instance);
            return true;
        }

        /// <summary>在册快照（跨线程读面）：List 扩容与并发变更的撕裂以 catch 兜底——
        /// 本跳返回空（下跳再报），不炸读者线程。</summary>
        public RoomInstance[] SnapshotArray()
        {
            try
            {
                return _order.ToArray();
            }
            catch
            {
                return Array.Empty<RoomInstance>();
            }
        }
    }
}
