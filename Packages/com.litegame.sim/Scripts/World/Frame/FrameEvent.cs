namespace LiteSim
{
    /// <summary>
    /// 帧事件（§3.7 帧事件缓冲）：Sim → View 的离散告知（命中/暴击/死亡/开火——状态差分推不出来的）。
    /// </summary>
    public struct FrameEvent
    {
        public FrameEventKind Kind;

        /// <summary>主体 Id（开火者/命中目标/死者；ObstacleHit 同开火者）。</summary>
        public long EntityId;

        /// <summary>对象 Id（Hit=射手；Death=击杀者；Fire/ObstacleHit 无对象为 0）。</summary>
        public long OtherId;

        /// <summary>数值（伤害量等，按 Kind 释义）。</summary>
        public int Value;

        /// <summary>事件位置（Fire=射线原点；Hit=命中点；Death=死亡位置；ObstacleHit=障碍命中点）。</summary>
        public SimVector3 Pos;
    }
}
