using System;

namespace LiteSim
{
    /// <summary>
    /// 公共战斗事件的**两端通用记录**（《状态同步专项设计》§5.2 CombatEvent 面）：
    /// 判定链在权威帧产出的离散事实（命中/暴击/死亡/开火/着墙/复活/爆炸），
    /// 经房间内核窗口 → wire → 客户端公共事件面，全程**同一形状**。
    ///
    /// **为什么在 LiteSim**（而不是 proto / RoomServer）：
    /// - 词汇单源：<see cref="FrameEventKind"/> 已定义全词汇，本记录就是它的**可跨帧携带**版本
    ///   （<see cref="FrameEvent"/> 只在逻辑帧内活，帧末即清）；
    /// - 两端共享：LiteNet（协议映射）、RoomServer.Runtime（窗口只持纯数据，R11 纯度）、
    ///   LiteClient（公共事件面）都引用 <c>LiteSim.Core</c>——放进任何一端都会造成反向依赖；
    /// - 泛化点：**新增一种事件 = 加一个 <see cref="FrameEventKind"/> 值 + 一个产出点**，
    ///   窗口/wire/客户端分发零改动（字段按 kind 释义复用，不新增协议字段）。
    ///
    /// 字段语义与 <see cref="FrameEvent"/> 逐项同义（只是把"帧内瞬态"变成"可携带记录"）：
    /// <see cref="A"/> = 主体（开火者/命中目标/死者），<see cref="B"/> = 对象（命中=射手/死亡=击杀者），
    /// <see cref="Value"/> 按 Kind 释义（伤害量等），<see cref="Pos"/> = 事件位置。
    /// </summary>
    public readonly struct CombatEventRecord
    {
        /// <summary>事件类型（词汇单源；wire 侧按同一枚举序列化）。</summary>
        public readonly FrameEventKind Kind;

        /// <summary>主体 Id（开火者/命中目标/死者；ObstacleHit 同开火者）。</summary>
        public readonly long A;

        /// <summary>对象 Id（Hit=射手；Death=击杀者；Fire/ObstacleHit 无对象为 0）。</summary>
        public readonly long B;

        /// <summary>权威帧号（两端口径一致的逻辑帧）。</summary>
        public readonly int Frame;

        /// <summary>数值（伤害量等，按 Kind 释义；Fire/Death 为 0）。</summary>
        public readonly int Value;

        /// <summary>事件位置（Fire=射线原点；Hit=命中点；Death=死亡位置；ObstacleHit=障碍命中点）。</summary>
        public readonly SimVector3 Pos;

        public CombatEventRecord(FrameEventKind kind, long a, long b, int frame, int value, in SimVector3 pos)
        {
            Kind = kind;
            A = a;
            B = b;
            Frame = frame;
            Value = value;
            Pos = pos;
        }

        /// <summary>从帧事件构造（<paramref name="frame"/> = 该事件所在的权威帧）。
        /// 这是 **FrameEvent → 可携带记录** 的唯一映射（Sim 侧产出点只调它，不手抄字段）。</summary>
        public static CombatEventRecord From(in FrameEvent e, int frame)
            => new CombatEventRecord(e.Kind, e.EntityId, e.OtherId, frame, e.Value, in e.Pos);

        /// <summary>还原为帧事件（客户端表现层的统一入口——公共事件面交回既有
        /// <c>HitFeedbackDispatcher</c>/动画接缝时不引入第二套形状）。</summary>
        public FrameEvent ToFrameEvent()
            => new FrameEvent { Kind = Kind, EntityId = A, OtherId = B, Value = Value, Pos = Pos };

        /// <summary>
        /// 去重键（客户端公共事件面用）：**同 kind + 同双方 + 同帧** = 同一条事件。
        /// 服务端已在窗口内按它聚合（霰弹多弹丸合并为一条），客户端据此保证一份事件只触发一次。
        /// </summary>
        public bool SameKey(in CombatEventRecord other)
            => Kind == other.Kind && A == other.A && B == other.B && Frame == other.Frame; // lint-allow R3（整型键判等）
    }

    /// <summary>
    /// 战斗事件窗口的**只读端口**（《状态同步专项设计》§5.2 CombatEvent 面）。
    ///
    /// **为什么在 LiteSim**：这是"房间内核 ↔ 协议层"之间唯一需要共享的形状。
    /// 房间内核（`RoomServer.Runtime`）只引用 <c>LiteSim.Core</c>（R11 纯度：禁 proto），
    /// 协议层（`LiteNet`）也只引用 <c>LiteSim.Core</c>——把端口放在任何一端都会逼出一条反向引用。
    /// 放在词汇单源旁边，两端各自实现/消费，谁也不认识谁的类型。
    ///
    /// 实现方：<c>RoomServer.Runtime.CombatEventWindow</c>；消费方：<c>LiteNet.Protocol.CombatEventCodec</c>。
    /// </summary>
    public interface ICombatEventSource
    {
        /// <summary>当前条数（下标域 = <c>[0, Count)</c>，0 = 最旧）。</summary>
        int Count { get; }

        /// <summary>按下标取（0 = 最旧 → 最新）。越界由实现方抛。</summary>
        CombatEventRecord Get(int index);
    }
}
