using LiteClient;
using LiteSim;
using RoomServer.Runtime;

namespace LiteGame
{
    /// <summary>
    /// 本地服宿主钩子执行件（开发面）：bot 冻结/传送分发/无限生命/重置假人。
    /// 只服务进程内测试房，读取方是宿主循环而非确定性判定路径；写入的 Sim 字段照常进快照/校验。
    /// </summary>
    internal sealed class LocalTestOverrides
    {
        private const int MaxSeats = 16;

        private readonly RoomRuntime _runtime;
        private readonly ILocalTestHooks _hooks;
        private readonly int _botRefillHp;
        private readonly SimVector3[] _botHomePos = new SimVector3[MaxSeats];
        private readonly bool[] _botHomeCaptured = new bool[MaxSeats];

        public LocalTestOverrides(RoomRuntime runtime, int botRefillHp, ILocalTestHooks hooks)
        {
            _runtime = runtime;
            _botRefillHp = botRefillHp;
            _hooks = hooks;
        }

        public void Apply(bool joined, int playerId)
        {
            if (_hooks == null || !_hooks.Active || !joined) return;
            SimWorldState sim = _runtime.AuthSim;

            if (_hooks.BotFrozen)
            {
                for (int p = 0; p < _runtime.ExpectedPlayers && p < MaxSeats; p++)
                {
                    if (p == playerId) continue;
                    long id = _runtime.EntityIdOf(p);
                    if (id == 0 || !sim.TryResolve(id, out int slot)) continue;
                    if (_botHomeCaptured[p]) sim.Entities[slot].Pos = _botHomePos[p];
                    else { _botHomePos[p] = sim.Entities[slot].Pos; _botHomeCaptured[p] = true; }
                }
            }

            if (_hooks.TeleportDispatch)
            {
                _hooks.TeleportDispatch = false;
                long id = _runtime.EntityIdOf(playerId);
                if (id != 0 && sim.TryResolve(id, out int slot))
                    sim.Entities[slot].Pos = _hooks.TeleportTarget;
            }

            // bot 无限生命：补位席位每帧回满——单帧伤害恒小于满血 ⇒ 永不跨死线；已死（尸体期）跳过，
            // 让它走完复活链自然回满（直写 HP 复活会绕过 RespawnSystem 的“生命更替”语义）。
            if (_hooks.BotInfiniteHp)
            {
                for (int p = 0; p < _runtime.ExpectedPlayers; p++)
                {
                    if (p == playerId) continue;
                    long id = _runtime.EntityIdOf(p);
                    if (id == 0 || !sim.TryResolve(id, out int slot)) continue;
                    ref EntitySlot e = ref sim.Entities[slot];
                    if (e.Hp > 0) e.Hp = _botRefillHp;
                }
            }

            // 重置假人：补位席位满血回出生点、清死亡/尸体/复活账；LifeStartFrame 更新为当前帧，
            // 视图/动画侧按“新生命”复位死亡锁存。
            if (_hooks.BotResetRequested)
            {
                _hooks.BotResetRequested = false;
                for (int p = 0; p < _runtime.ExpectedPlayers; p++)
                {
                    if (p == playerId) continue;
                    long id = _runtime.EntityIdOf(p);
                    if (id == 0 || !sim.TryResolve(id, out int slot)) continue;
                    ResetBotSeat(sim, slot);
                }
            }
        }

        private void ResetBotSeat(SimWorldState sim, int slot)
        {
            ref EntitySlot e = ref sim.Entities[slot];
            long id = e.Id;
            int kills = e.Kills, deaths = e.Deaths, spawnIndex = e.SpawnPointIndex;
            SimMapData map = _runtime.Map;
            sim.ResetCombatRuntime(slot);
            e = new EntitySlot
            {
                Id = id,
                Hp = _botRefillHp,
                Kills = kills,
                Deaths = deaths,
                Flags = EntityFlags.Player,
                SpawnPointIndex = spawnIndex,
                Pos = map.SpawnPoints[spawnIndex],
                LifeStartFrame = sim.Frame,
            };
        }
    }
}
