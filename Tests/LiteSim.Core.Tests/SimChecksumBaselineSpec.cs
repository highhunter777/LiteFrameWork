using System;
using System.Text;

namespace LiteSim.Tests
{
    /// <summary>
    /// Sim checksum 基线规格（《M8 实施指导》§2.6/§3 校验基线组）——**唯一的真值源**。
    /// .NET 侧记录基线文件（Baselines/SimChecksum.txt）；Unity 侧（沙盒/M11）用同一场景
    /// 跑同一套运算逐位对账（v3 下为预测质量保障，降级不阻断）。
    /// 场景固定：2 玩家 + 5 靶标，随机脚本输入（外部 SimRng，不碰世界 RngState），3000 逻辑帧。
    /// </summary>
    public static class SimChecksumBaselineSpec
    {
        public const int FrameCount = 3000;
        public const int PlayerCount = 2;
        public const int TargetCount = 5;

        private const ulong WorldSeed = 0x5EEDBEEF12345678UL;
        private const ulong InputSeed = 0x0F0F0F0F0F0F0F0FUL;

        public const string Header =
            "# Sim checksum 基线 — 3000 帧确定性场景（.NET 侧记录）\n" +
            "# 布局：每行一帧的 SimChecksum.ComputeChecksum 值（uint 十进制，不变文化）。\n" +
            "# 场景：2 玩家 + 5 靶标；输入脚本由外部 SimRng 生成；世界 RngState 仅被 ShootingSystem 消费。\n";

        public static SimMapData BuildMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);
            map.SpawnPoints[1] = new SimVector3(10f, 0f, 10f);
            map.SpawnPointCount = 2;
            return map;
        }

        /// <summary>构造初始世界：玩家占槽 0/1，靶标占槽 2..6（分配顺序固定 → Id 确定）。</summary>
        public static SimWorldState BuildWorld()
        {
            var s = new SimWorldState { RngState = WorldSeed };

            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int _);
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(10f, 0f, 10f), Yaw = SimTrig.Pi }, out int _);

            for (int i = 0; i < TargetCount; i++)
            {
                float z = -10f + 5f * i;
                s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(20f, 0f, z) }, out int _);
            }
            return s;
        }

        /// <summary>玩家实体 Id（槽 0/1，version=1 → Id 确定）。</summary>
        public static long[] PlayerIds(SimWorldState s)
        {
            return new[] { s.Entities[0].Id, s.Entities[1].Id };
        }

        /// <summary>跑完 <paramref name="frames"/> 帧，返回逐帧 checksum 序列（§9 验收①②的运行体）。</summary>
        public static uint[] RunChecksumSequence(int frames)
        {
            RunResult r = Run(frames, -1);
            return r.Sequence;
        }

        /// <summary>跑完 <paramref name="frames"/> 帧；<paramref name="snapshotFrame"/> ≥ 0 时在中途做 CopyTo 快照。</summary>
        public static RunResult Run(int frames, int snapshotFrame)
        {
            SimMapData map = BuildMap();
            SimWorldState s = BuildWorld();
            long[] players = PlayerIds(s);

            var inputRng = new SimRng(InputSeed);
            var inputs = new SimInputFrame[PlayerCount];
            var seq = new uint[frames];

            SimWorldState snapshot = null;
            for (int f = 0; f < frames; f++)
            {
                MakeInputs(ref inputRng, players, inputs);
                SimStep.Step(s, map, inputs);
                seq[f] = SimChecksum.ComputeChecksum(s);

                if (f == snapshotFrame)
                {
                    snapshot = new SimWorldState();
                    s.CopyTo(snapshot);
                }
            }
            return new RunResult { Sequence = seq, Final = s, Snapshot = snapshot };
        }

        public static string BuildText()
        {
            uint[] seq = RunChecksumSequence(FrameCount);
            var sb = new StringBuilder();
            sb.Append(Header);
            for (int i = 0; i < seq.Length; i++) sb.Append(seq[i].ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        /// <summary>脚本输入：外部 SimRng 生成（与世界 RngState 无关——玩家行为不属于逻辑状态）。
        /// 注意：SimRng 是可变 struct，必须以 ref 传入推进调用方状态——按值传参会把输入流冻结在首帧。</summary>
        private static void MakeInputs(ref SimRng rng, long[] players, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].EntityId = players[i];
                inputs[i].MoveX = rng.NextFloat01() * 2f - 1f;
                inputs[i].MoveZ = rng.NextFloat01() * 2f - 1f;
                inputs[i].AimX = 1f - 2f * rng.NextFloat01();
                inputs[i].AimZ = 1f - 2f * rng.NextFloat01();
                inputs[i].Buttons = (rng.NextUInt32() & 3u) == 0u ? SimInputFrame.ButtonFire : 0u;
            }
        }

        /// <summary>一次运行的全部产物。</summary>
        public sealed class RunResult
        {
            public uint[] Sequence;
            public SimWorldState Final;
            public SimWorldState Snapshot;
        }
    }
}
