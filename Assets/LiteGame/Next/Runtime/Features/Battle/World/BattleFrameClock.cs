using System;
using LiteSim;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>本地逻辑帧时钟：最小 RollbackSim（空世界）只提供与服务器同速的 60Hz 帧轴。</summary>
    public sealed class BattleFrameClock
    {
        private readonly RollbackSim _sim;

        public BattleFrameClock(ulong seed, int expectedPlayers)
        {
            var world = new SimWorldState { RngState = seed };
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            var template = new SimInputFrame[Math.Max(1, expectedPlayers)];
            for (int index = 0; index < template.Length; index++) template[index].EntityId = index + 1;
            _sim = new RollbackSim(world, map, template, CombatValues.Default, WeaponTable.Default,
                rules: null, resolveCombat: false);
        }

        public int Frame => _sim.State.Frame;

        public void Tick(float deltaSeconds) => _sim.Tick(Math.Min(deltaSeconds, 0.1f));
    }
}
