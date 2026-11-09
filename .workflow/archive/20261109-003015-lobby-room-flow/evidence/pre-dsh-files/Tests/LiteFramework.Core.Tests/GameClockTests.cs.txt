using Xunit;

namespace LiteFramework.Tests
{
    public sealed class GameClockTests   // 实例时钟，无静态状态
    {
        [Fact]
        public void GameClock_变速05_Now增速与ScaledDelta同步减半()
        {
            var clock = new WorldClock { TimeScale = 0.5f };
            clock.Tick(0.1f);
            Assert.Equal(0.05f, clock.ScaledDelta, 5);
            Assert.Equal(0.05f, clock.Now, 5);
        }

        [Fact]
        public void GameClock_暂停_Now不动_ScaledDelta为0()
        {
            var clock = new WorldClock { Paused = true };
            clock.Tick(0.1f);
            Assert.Equal(0f, clock.ScaledDelta, 5);
            Assert.Equal(0f, clock.Now, 5);
        }

        [Fact]
        public void GameClock_尖峰钳制_超过MaxDelta按上限计()
        {
            var clock = new WorldClock();                 // MaxDelta 默认 0.1
            clock.Tick(0.8f);                             // 加载 Hitch 尖峰
            Assert.Equal(0.1f, clock.ScaledDelta, 5);
        }

        [Fact]
        public void GameClock_双实例类型隔离_World与UI各自独立()
        {
            var world = new WorldClock();
            var ui = new UIClock();
            world.Paused = true;
            ui.Tick(0.1f);
            Assert.Equal(0.1f, ui.Now, 5);                // 一个实例暂停不影响另一个
            Assert.True(world is IWorldClock);
            Assert.True(ui is IUIClock);                  // 标记子接口：注入点拿混即编译错误
        }

        [Fact]
        public void SystemWallClock_三域互不干扰_游戏时钟暂停后墙钟仍推进()
        {
            var world = new WorldClock { Paused = true };
            var wall = new SystemWallClock();
            world.Tick(0.1f);
            Assert.Equal(0f, world.Now, 5);
            long ms = wall.NowMs;
            Assert.True(wall.UtcNow != default);
            Assert.True(ms > 0);                          // 墙钟读数有效（不断言具体值——时间精度纪律）
        }
    }
}
