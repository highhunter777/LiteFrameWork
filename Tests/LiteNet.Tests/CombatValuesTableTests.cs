using System;
using System.IO;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 限速数值表化守卫：tb_combatnum 两列 <c>aim_move_speed</c> / <c>fire_stance_frames</c>
    /// 落表后，把"表 ↔ 兜底默认 ↔ 服务端装载链"
    /// 三面钉在一起——形状与 <see cref="WeaponTableTests"/> 同源（技术债 #1 的实例纪律）。
    ///
    /// 钉的判据：
    /// ① <b>表 = 兜底默认</b>——<see cref="CombatValues.Default"/> 是表未装载时的灰盒兜底，
    /// 漂移即本类红（否则没装载时跑的测试全用兜底值，漂移不可见）。
    /// ② <b>表 = 服务端装载链产物</b>——同一次装载的数值行。
    /// ③ <b>窗长 byte 边界</b>——窗长落进 <c>EntitySlot.FireStanceFrames</c>（<c>public byte</c>，
    /// 且进公共快照/双口径 checksum/proto），越界会在强转处静默截断；装载闸门
    /// （<c>SimConfigMapper.Validate</c>）已 fail-fast，此处钉住默认面不越界。
    /// </summary>
    public sealed class CombatValuesTableTests
    {
        private const string TableRelativePath = "Assets/GameData/Config/Combat/tbcombatnum.bytes";

        [Fact]
        public void 表文件存在且默认行与兜底默认一致()
        {
            string path = Path.Combine(RepoRoot(), TableRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"缺玩法数值表产物：{TableRelativePath}（跑 Luban/gen.bat Pass 1——数值缺失等于两端分叉）");

            var table = new cfg.Tbcombatnum(new Luban.ByteBuf(File.ReadAllBytes(path)));
            cfg.combatnum row = table.GetOrDefault(1);
            Assert.NotNull(row);

            CombatValues def = CombatValues.Default;
            Assert.Equal(def.MoveSpeed, row.MoveSpeed);
            Assert.Equal(def.HitscanRange, row.HitscanRange);
            Assert.Equal(def.BaseDamage, row.BaseDamage);
            Assert.Equal(def.DamageSpread, row.DamageSpread);
            // 本批新增两列：兜底默认必须与表值同钉
            Assert.Equal(def.AimMoveSpeed, row.AimMoveSpeed, 6);
            Assert.Equal(def.FireStanceFrames, row.FireStanceFrames);
        }

        [Fact]
        public void 服务端装载链产出实例_表值在册()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            ServerTableLoad load = ServerTableLoader.LoadTableBytes(dir);

            CombatValues values = load.Combat.ToValues();
            Assert.Equal(load.Combat.MoveSpeed, values.MoveSpeed);
            Assert.Equal(load.Combat.AimMoveSpeed, values.AimMoveSpeed, 6);
            Assert.Equal(load.Combat.FireStanceFrames, values.FireStanceFrames);
        }

        [Fact]
        public void 窗长落byte_默认面不越界()
        {
            // 越界会在 (byte) 强转处静默截断：255→255、256→0（=窗尽即退根），后者极难排查
            int frames = CombatValues.Default.FireStanceFrames;
            Assert.InRange(frames, 1, byte.MaxValue);
            Assert.Equal(frames, (byte)frames);
        }

        [Fact]
        public void 限速档独立于基础移速_不再派生()
        {
            // 语义钉子：表化后 AimMoveSpeed 直接落表、**不是** MoveSpeed × 0.5 的派生，两列互不联动；
            // 此用例锁住"改 MoveSpeed 不带动限速档"这条口径，防止后人对着旧式推演把派生关系改回去。
            CombatValues v = new CombatValues(7.5f, -20f, 100f, 25, 1, 100, 2.5f, 60);
            Assert.Equal(7.5f, v.MoveSpeed);
            Assert.Equal(2.5f, v.AimMoveSpeed, 6);
            Assert.NotEqual(v.MoveSpeed * 0.5f, v.AimMoveSpeed, 6);
        }

        [Fact]
        public void 瞄准限速档命中视图Walk档上界()
        {
            // 默认表值 2.5 恰为 LocomotionBlendMath.WalkFullMps——瞄准移动只需一套 AimWalk 片段。
            // 该常量在 View 层（Unity 程序集），此处只钉数值契约，视图侧一致性由装配期校验负责。
            Assert.Equal(2.5f, CombatValues.Default.AimMoveSpeed, 6);
        }

        private static string RepoRoot()
        {
            for (var c = new DirectoryInfo(AppContext.BaseDirectory); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }
}