using System;
using LiteClient;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 对局只读事实端口的实现（适配器）：把 <see cref="SimWorldState"/> 的槽位/武器数组翻译成
    /// <see cref="IBattleReadModel"/> 事实——本类是**唯一**把 Sim 布局暴露给表现层驱动的翻译点。
    ///
    /// 读源为委托：HUD 用本地**预测态**、排行榜用**权威镜像**（首快照前回落预测），两者的实例
    /// 各自持不同委托；容量的内部缓冲（规范序）每帧重建一次，玩家数上界 = MaxEntities。
    /// </summary>
    public sealed class BattleReadModel : IBattleReadModel
    {
        private readonly Func<SimWorldState> _stateOf;
        private readonly Func<long> _localEntityId;
        private readonly long[] _ids;
        private readonly int[] _kills;
        private int _count;

        public BattleReadModel(Func<SimWorldState> stateOf, Func<long> localEntityId)
        {
            _stateOf = stateOf ?? throw new ArgumentNullException(nameof(stateOf));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _ids = new long[SimConfig.MaxEntities];
            _kills = new int[SimConfig.MaxEntities];
        }

        public int Frame => _stateOf()?.Frame ?? 0;

        public int TeamMode => _stateOf()?.Match.Team ?? 0;

        public int TeamScore => _stateOf()?.Match.Score ?? 0;

        public string DisplayName(long entityId)
        {
            SimWorldState state = _stateOf();
            return state != null ? PlayerIdentity.PlaceholderName(state, entityId) : string.Empty;
        }

        public bool TryGetLocalPlayer(out BattleLocalPlayerFacts local)
        {
            local = default;
            SimWorldState state = _stateOf();
            if (state == null) return false;
            long id = _localEntityId();
            if (id == 0L || !state.TryResolve(id, out int slot)) return false;

            ref EntitySlot e = ref state.Entities[slot];
            int selected = e.SelectedWeapon;
            if (selected < 0 || selected >= SimConfig.WeaponSlotsPerEntity)
            {
                local = new BattleLocalPlayerFacts(e.Hp, -1, 0, 0, false);
                return true;
            }

            ref WeaponRuntime weapon = ref state.Weapons[slot * SimConfig.WeaponSlotsPerEntity + selected];
            local = new BattleLocalPlayerFacts(e.Hp, selected, weapon.MagAmmo, weapon.ReserveAmmo,
                weapon.State == WeaponSlotState.Reloading);
            return true;
        }

        public int PlayerCount
        {
            get
            {
                RebuildPlayers();
                return _count;
            }
        }

        public bool TryGetPlayerAt(int index, out BattlePlayerFacts player)
        {
            if ((uint)index >= (uint)_count)
            {
                player = default;
                return false;
            }
            player = new BattlePlayerFacts(_ids[index], _kills[index]);
            return true;
        }

        /// <summary>重建规范序（EntityId 升序；玩家数 ≤ MaxEntities，插入排序足够）。</summary>
        private void RebuildPlayers()
        {
            SimWorldState state = _stateOf();
            if (state == null)
            {
                _count = 0;
                return;
            }

            EntitySlot[] entities = state.Entities;
            int count = 0;
            for (int slot = 0; slot < entities.Length; slot++)
            {
                ref EntitySlot e = ref entities[slot];
                if (!PlayerIdentity.IsPlayer(in e)) continue;

                int j = count - 1;
                while (j >= 0 && _ids[j] > e.Id)
                {
                    _ids[j + 1] = _ids[j];
                    _kills[j + 1] = _kills[j];
                    j--;
                }
                _ids[j + 1] = e.Id;
                _kills[j + 1] = e.Kills;
                count++;
            }
            _count = count;
        }
    }
}
