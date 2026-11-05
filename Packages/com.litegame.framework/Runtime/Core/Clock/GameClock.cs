using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>
    /// 时钟唯一实现(表现层,变步长——确定性归 Sim 的 FrameDriver,见回写 5)。
    /// 纪律:单一缩放源——GameEntry 喂 unscaledDeltaTime,变速只发生在这里;
    /// Unity Time.timeScale 保持 1 永不触碰(双缩放 = 0.2×0.2 = 0.04 事故)。
    /// 暂停/变速的全部下游效果经 ScaledDelta 免费传导(调度器/时间轴/FSM)。
    /// </summary>
    public class GameClock : IGameClock
    {
        private readonly string _id;
        public GameClock(string id) => _id = id;

        public float TimeScale { get; set; } = 1f;
        public bool Paused { get; set; }
        public float Now { get; private set; }           // 本域时间累计
        public float ScaledDelta { get; private set; }   // 本帧增量:Paused 时为 0

        /// <summary>单帧增量上限:加载/Hitch 的 deltaTime 尖峰被钳制——防定时器集体到期、动画跳变。</summary>
        public float MaxDelta { get; set; } = 0.1f;

        public void Tick(float realDelta)
        {
            if (realDelta > MaxDelta) realDelta = MaxDelta;        // 尖峰钳制
            ScaledDelta = Paused ? 0f : realDelta * TimeScale;
            Now += ScaledDelta;
        }

        public string StatsName => $"Clock.{_id}";
        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["Now"] = Now.ToString("0.0");
            into["TimeScale"] = TimeScale.ToString("0.##");
            into["Paused"] = Paused ? "是" : "否";
            into["本帧增量"] = ScaledDelta.ToString("0.000");
        }
    }
    /// <summary>两个空壳子类:实例在类型上各自满足标记接口,注册即发现各自入列。</summary>
    public sealed class WorldClock : GameClock, IWorldClock { public WorldClock() : base("World") { } }
    public sealed class UIClock : GameClock, IUIClock { public UIClock() : base("UI") { } }
}
