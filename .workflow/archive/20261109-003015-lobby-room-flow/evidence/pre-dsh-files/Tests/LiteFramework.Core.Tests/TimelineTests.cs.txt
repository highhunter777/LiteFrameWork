using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>时间轴（M4 §2.7）：乱序注册按时间点执行 / Stop 中断 / Finished 语义 / 步骤抛隔离 / 暂停冻结。
    /// 步骤异常经 SafeCall.Invoke → Log.Error（全局静态，无线程锁）。</summary>
    [Collection("CoreStatic")]
    public sealed class TimelineTests
    {
        [Fact]
        public void Timeline_乱序注册_按时间点顺序执行()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var order = new System.Collections.Generic.List<string>();
            runner.CreateTimeline()
                .At(1.0f, () => order.Add("late"))
                .At(0.2f, () => order.Add("a"))
                .At(0.5f, () => order.Add("b"))
                .Start();
            for (int i = 0; i < 12; i++)
            {
                clock.Tick(0.1f);
                runner.Tick(0.1f);
            }
            Assert.Equal(new[] { "a", "b", "late" }, order);
        }

        [Fact]
        public void Timeline_跑完自摘除_Finished置位()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var tl = runner.CreateTimeline();
            tl.At(0.2f, () => { });
            tl.Start();
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.False(tl.Finished);
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.True(tl.Finished);
            clock.Tick(0.1f); runner.Tick(0.1f);           // 完成后继续 Tick 不复触
            Assert.True(tl.Finished);
        }

        [Fact]
        public void Timeline_Stop中断_后续动作不执行()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int second = 0;
            var tl = runner.CreateTimeline();
            ITimeline handle = tl;
            tl.At(0.1f, () => handle.Stop());
            tl.At(0.2f, () => second++);
            tl.Start();
            clock.Tick(0.1f); runner.Tick(0.1f);           // 第一步触发并 Stop
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.Equal(0, second);
            Assert.False(tl.Finished);                     // 中断≠完成
        }

        [Fact]
        public void Timeline_步骤抛异常_隔离后续步骤继续()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int fired = 0;
            runner.CreateTimeline()
                .At(0.1f, () => throw new InvalidOperationException("boom"))
                .At(0.2f, () => fired++)
                .Start();
            clock.Tick(0.1f); runner.Tick(0.1f);
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Timeline_暂停冻结()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int fired = 0;
            runner.CreateTimeline().At(0.2f, () => fired++).Start();
            clock.Paused = true;
            clock.Tick(1f); runner.Tick(1f);
            Assert.Equal(0, fired);
            clock.Paused = false;
            clock.Tick(0.3f); runner.Tick(0.3f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Timeline_重复Start与空轴抛()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var tl = runner.CreateTimeline();
            Assert.Throws<InvalidOperationException>(() => tl.Start());   // 空轴
            tl.At(0.1f, () => { });
            tl.Start();
            Assert.Throws<InvalidOperationException>(() => tl.Start());   // 重复
            Assert.Throws<InvalidOperationException>(() => tl.At(0.2f, () => { }));   // 启动后追加
        }

        [Fact]
        public void Timeline_Loop两轮_保留跨轮剩余时间()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int fired = 0;
            var tl = runner.CreateTimeline().At(0.1f, () => fired++).Loop(2);
            tl.Start();

            clock.Tick(0.25f);
            runner.Tick(0.25f);

            Assert.Equal(2, fired);
            Assert.True(tl.Finished);
        }

        [Fact]
        public void Timeline_每帧触发上限_下一帧补发()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock) { MaxStepsPerTick = 100 };
            int fired = 0;
            var tl = runner.CreateTimeline();
            for (int i = 0; i < 150; i++) tl.At(0.1f, () => fired++);
            tl.Start();

            clock.Tick(0.2f);
            runner.Tick(0.2f);
            Assert.Equal(100, fired);
            Assert.False(tl.Finished);

            clock.Tick(0.1f);
            runner.Tick(0.1f);
            Assert.Equal(150, fired);
            Assert.True(tl.Finished);
        }

        [Fact]
        public void Timeline_无限循环_受每帧上限保护()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            ITimelineRunner runner = new GameTimelineRunner(clock) { MaxStepsPerTick = 3 };
            int fired = 0;
            runner.CreateTimeline().At(0.1f, () => fired++).Loop(-1).Start();

            clock.Tick(1f);
            runner.Tick(1f);
            Assert.Equal(3, fired);

            clock.Tick(1f);
            runner.Tick(1f);
            Assert.Equal(6, fired);
        }

        [Fact]
        public void Timeline_Seek跳过之前动作_不重复触发()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var order = new System.Collections.Generic.List<string>();
            var tl = runner.CreateTimeline()
                .At(0.1f, () => order.Add("skipped"))
                .At(0.2f, () => order.Add("played"))
                .Seek(0.15f);
            tl.Start();

            clock.Tick(0.05f);
            runner.Tick(0.05f);

            Assert.Equal(new[] { "played" }, order);
        }

        [Fact]
        public void Timeline_Stop后Seek再Start_从新位置继续()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int first = 0, second = 0;
            var tl = runner.CreateTimeline()
                .At(0.1f, () => first++)
                .At(0.3f, () => second++);
            tl.Start();

            clock.Tick(0.15f);
            runner.Tick(0.15f);
            tl.Stop();
            tl.Seek(0.2f).Start();

            clock.Tick(0.1f);
            runner.Tick(0.1f);
            Assert.Equal(1, first);
            Assert.Equal(1, second);
        }
    }
}
