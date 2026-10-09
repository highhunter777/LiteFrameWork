using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 命中反馈上下文（分发器在静默门之后解析交付——消费者不自行解事件、不重复解析槽位）。
    /// 字段按 <see cref="FrameEvent"/> 释义：主体（EntityId＝Hit 命中目标/Death 死者/Fire 开火者）、
    /// 对象（OtherId＝Hit 射手/Death 击杀者/Fire 恒 0）。角色枚举 <see cref="HitLocalRole"/> 在独立
    /// 零引擎文件（L1 源链接前提）。
    /// </summary>
    public readonly struct HitFeedbackContext
    {
        /// <summary>事件类型（Fire/Hit/Death/Crit/Explosion）。</summary>
        public readonly FrameEventKind Kind;

        /// <summary>事件主体 Id（见类注释义）。</summary>
        public readonly long EntityId;

        /// <summary>事件对象 Id（见类注释义；Fire 无对象为 0）。</summary>
        public readonly long OtherId;

        /// <summary>主体槽位（SimView 槽位解析；实体不存在/未对齐 = -1）。</summary>
        public readonly int Slot;

        /// <summary>对象槽位（无对象或解析失败 = -1）。</summary>
        public readonly int OtherSlot;

        /// <summary>数值（Hit/Crit = 伤害量；其余按 Kind 释义）。</summary>
        public readonly int Value;

        /// <summary>事件位置（Sim 空间直映世界——与 SimView.LocalDisplayPosition 同式）。</summary>
        public readonly Vector3 WorldPos;

        /// <summary>本地角色（造成/承受/旁观）。</summary>
        public readonly HitLocalRole LocalRole;

        public HitFeedbackContext(FrameEventKind kind, long entityId, long otherId,
            int slot, int otherSlot, int value, Vector3 worldPos, HitLocalRole localRole)
        {
            Kind = kind;
            EntityId = entityId;
            OtherId = otherId;
            Slot = slot;
            OtherSlot = otherSlot;
            Value = value;
            WorldPos = worldPos;
            LocalRole = localRole;
        }
    }

    /// <summary>
    /// 命中反馈消费者（分发器注册面）：伤害数字为首个消费者；命中特效/音效/受击死亡动画随各自批注册。
    /// 消费时机＝逻辑帧边界、静默门之后（回滚重放/和解段已被门挡掉——一次性副作用不重播）。
    /// 实现者只读，不改 Sim。
    /// </summary>
    public interface IHitFeedbackConsumer
    {
        /// <summary>单个命中反馈事件（按注册序调用；未覆盖的事件类型不处理即可）。</summary>
        void OnHitFeedback(in HitFeedbackContext ctx);
    }
}
