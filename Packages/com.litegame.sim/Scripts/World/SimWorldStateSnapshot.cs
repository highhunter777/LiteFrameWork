namespace LiteSim
{
    /// <summary>
    /// 快照轻量摘要（§11-3 常量集中）：进网/进对比的**必须是摘要，不是全量 state**—— 服务器不能为"上一广播帧副本"再养一份 SimWorldState（快照环已占 16 份）。
    /// </summary>
    public struct EntitySnapshotEntry
    {
        public long Id;
        public SimVector3 Pos;
        public SimVector3 Vel;
        public float Yaw;
        public int Hp;
        public uint Flags;

        // ---- P0 公共战斗面 + 主动作摘要（SlotDelta 全字段对齐）----
        public int Shield;
        public int Kills;
        public int Deaths;
        public int SelectedWeapon;
        public int ActionId;
        public int ActionStartFrame;
        public ActionPhase ActionPhase;
        public byte FireStanceFrames;
        public byte CorpseFrames;
        public int SpawnPointIndex;
        public int RespawnFrame;
        public int InvulnerableUntilFrame;
        public int LifeStartFrame;

        /// <summary>与另一槽位逐字段位级相等（RngState/Frame 不在此——它们在 <see cref="SimWorldStateSnapshot"/> 头部）。</summary>
        public static bool BitEqual(in EntitySnapshotEntry a, in EntitySnapshotEntry b)
        {
            return a.Id == b.Id // lint-allow R3（64 位整型 Id 判等，非浮点精度比较）
                && BitUtil.Equal(a.Pos.X, b.Pos.X) && BitUtil.Equal(a.Pos.Y, b.Pos.Y) && BitUtil.Equal(a.Pos.Z, b.Pos.Z)
                && BitUtil.Equal(a.Vel.X, b.Vel.X) && BitUtil.Equal(a.Vel.Y, b.Vel.Y) && BitUtil.Equal(a.Vel.Z, b.Vel.Z)
                && BitUtil.Equal(a.Yaw, b.Yaw)
                && a.Hp == b.Hp && a.Flags == b.Flags // lint-allow R3（整型血量/标志位判等，非浮点精度比较）
                && a.Shield == b.Shield && a.Kills == b.Kills && a.Deaths == b.Deaths // lint-allow R3（整型判等，非浮点精度比较）
                && a.SelectedWeapon == b.SelectedWeapon // lint-allow R3（整型判等，非浮点精度比较）
                && a.ActionId == b.ActionId && a.ActionStartFrame == b.ActionStartFrame && a.ActionPhase == b.ActionPhase // lint-allow R3（整型/枚举判等，非浮点精度比较）
                && a.FireStanceFrames == b.FireStanceFrames // lint-allow R3（byte 开火窗判等，非浮点精度比较）
                && a.CorpseFrames == b.CorpseFrames // lint-allow R3（byte 尸体期判等，非浮点精度比较）
                && a.SpawnPointIndex == b.SpawnPointIndex && a.RespawnFrame == b.RespawnFrame // lint-allow R3（整数帧/索引）
                && a.InvulnerableUntilFrame == b.InvulnerableUntilFrame && a.LifeStartFrame == b.LifeStartFrame; // lint-allow R3（整数帧）
        }
    }

    /// <summary>
    /// 帧级快照摘要：帧号 + RngState + 活体位图 + 定长槽位表。
    /// </summary>
    public sealed class SimWorldStateSnapshot
    {
        public int Frame;
        public ulong RngState;
        public readonly uint[] AliveBitmap = new uint[(SimConfig.MaxEntities + 31) / 32];
        public readonly EntitySnapshotEntry[] Entities = new EntitySnapshotEntry[SimConfig.MaxEntities];
        public int AliveCount;

        /// <summary>只拷贝"结构"（帧号/随机数/活体位图/活体数），槽位逐字段拷贝走 <see cref="CaptureFull"/>。</summary>
        public void Capture(in SimWorldState s)
        {
            Frame = s.Frame;
            RngState = s.RngState;
            System.Array.Copy(s.AliveBitmap, AliveBitmap, AliveBitmap.Length);
            AliveCount = s.AliveCount();
        }

        /// <summary>整快照（结构 + 全部活体槽位）——初始金标与"活体数变化"时的重建用。</summary>
        public void CaptureFull(in SimWorldState s)
        {
            Capture(s);
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                Entities[i] = Capture(s, i);
            }
        }

        /// <summary>槽位是否与摘要逐字段位级一致（活体集合不同即视为不一致——上层按"活体数变化"整体重建）。</summary>
        public bool Matches(int slot, in SimWorldState s)
        {
            return EntitySnapshotEntry.BitEqual(Entities[slot], Capture(s, slot));
        }

        /// <summary>槽位 → 摘要项（含主动作摘要——需取 <see cref="SimWorldState.Actions"/>，
        /// 故入口吃整个 state）。CaptureFull 与 Matches 共用同一份字段搬运，防两处字段清单漂移。</summary>
        public static EntitySnapshotEntry Capture(in SimWorldState s, int slot)
        {
            ref EntitySlot e = ref s.Entities[slot];
            ref ActionRuntime a = ref s.Actions[slot * SimConfig.ActionSlotsPerEntity];   // 主动作槽
            EntitySnapshotEntry entry;
            entry.Id = e.Id;
            entry.Pos = e.Pos;
            entry.Vel = e.Vel;
            entry.Yaw = e.Yaw;
            entry.Hp = e.Hp;
            entry.Flags = e.Flags;
            entry.Shield = e.Shield;
            entry.Kills = e.Kills;
            entry.Deaths = e.Deaths;
            entry.SelectedWeapon = e.SelectedWeapon;
            entry.FireStanceFrames = e.FireStanceFrames;   // 开火驻留窗（差分基线与回放重建面）
            entry.CorpseFrames = e.CorpseFrames;           // 尸体期（死亡表现的权威载体窗——远端可见）
            entry.SpawnPointIndex = e.SpawnPointIndex;
            entry.RespawnFrame = e.RespawnFrame;
            entry.InvulnerableUntilFrame = e.InvulnerableUntilFrame;
            entry.LifeStartFrame = e.LifeStartFrame;
            entry.ActionId = a.ActionId;
            entry.ActionStartFrame = a.StartFrame;
            entry.ActionPhase = a.Phase;
            return entry;
        }
    }

    /// <summary>float 位型工具（位级比较与位型转换的唯一入口，防各处写法漂移；±0 位型不同即视为不同值）。</summary>
    public static class BitUtil
    {
        public static bool Equal(float a, float b) =>
            System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);

        /// <summary>位型视图（int32）——哈希折叠、差异报告等需要位面证据的场合取位的唯一入口。</summary>
        public static int Bits(float v) => System.BitConverter.SingleToInt32Bits(v);
    }
}
