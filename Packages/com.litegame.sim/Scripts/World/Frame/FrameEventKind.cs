namespace LiteSim
{
    /// <summary>帧事件类型（§3.7）。判定链产出 命中/暴击/死亡/开火/着墙/复活；Explosion 为后续里程碑预留。</summary>
    public enum FrameEventKind : byte
    {
        Fire = 0,
        Hit = 1,
        Death = 2,
        Crit = 3,
        Explosion = 4,
        Respawn = 5,
        ObstacleHit = 6,
    }
}
