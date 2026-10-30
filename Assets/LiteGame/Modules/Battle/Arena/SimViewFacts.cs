using System;
using LiteSim;

namespace LiteGame.View
{
    /// <summary>
    /// 表现层事实读口：本地取预测态、远端取渲染帧快照；槽位越界/首快照未到/槽位不在场一律 false。
    /// 只读 Sim，不写任何判定状态。
    /// </summary>
    internal sealed class SimViewFacts
    {
        private readonly SimWorldState _sim;
        private readonly SnapshotInterpolationBuffer _snapshots;
        private readonly Func<long> _localEntityId;
        private readonly Func<float> _snapDistance;

        public SimViewFacts(SimWorldState sim, SnapshotInterpolationBuffer snapshots,
            Func<long> localEntityId, Func<float> snapDistance)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _snapDistance = snapDistance ?? throw new ArgumentNullException(nameof(snapDistance));
        }

        public bool IsLocalSlot(int slotIndex)
            => _localEntityId() != 0 && _sim.TryResolve(_localEntityId(), out int localSlot) && localSlot == slotIndex;

        public bool IsAiming(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (IsLocal(slotIndex))
                return (_sim.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            return (snapshot.Entities[slotIndex].Flags & EntityFlags.Aiming) != 0u;
        }

        public bool IsFireStance(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (IsLocal(slotIndex))
                return _sim.Entities[slotIndex].FireStanceFrames > 0;

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            return snapshot.Entities[slotIndex].FireStanceFrames > 0;
        }

        public bool IsDead(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (IsLocal(slotIndex))
                return _sim.Entities[slotIndex].Hp <= 0;

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            return snapshot.Entities[slotIndex].Hp <= 0;
        }

        public bool IsProtected(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (IsLocal(slotIndex))
                return _sim.Frame < _sim.Entities[slotIndex].InvulnerableUntilFrame;

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            return snapshot.Frame < snapshot.Entities[slotIndex].InvulnerableUntilFrame;
        }

        public bool TryGetLifeStartFrame(int slotIndex, out int lifeStartFrame)
        {
            lifeStartFrame = 0;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            if (IsLocal(slotIndex))
            {
                lifeStartFrame = _sim.Entities[slotIndex].LifeStartFrame;
                return true;
            }

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            lifeStartFrame = snapshot.Entities[slotIndex].LifeStartFrame;
            return true;
        }

        public bool TryGetRenderPosition(int slotIndex, out SimVector3 position)
            => _snapshots.TryGetRenderPosition(_sim, slotIndex, _snapDistance(), out position);

        public bool IsReloading(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;

            if (IsLocal(slotIndex))
            {
                int sel = _sim.Entities[slotIndex].SelectedWeapon;
                if (sel < 0 || sel >= SimConfig.WeaponSlotsPerEntity) return false;
                return _sim.Weapons[slotIndex * SimConfig.WeaponSlotsPerEntity + sel].State
                    == WeaponSlotState.Reloading;   // lint-allow R3（枚举判等，非浮点精度比较）
            }

            if (!_snapshots.TryGetRenderSnapshot(out SimWorldStateSnapshot snapshot)) return false;
            if (!AliveIn(snapshot, slotIndex)) return false;
            return snapshot.Entities[slotIndex].ActionId == ActionIds.Reload;   // lint-allow R3（整型判等）
        }

        public bool TryGetSpreadDegrees(int slotIndex, out float degrees)
        {
            degrees = 0f;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (!WeaponSystem.IsEquipped(_sim, slotIndex, WeaponConfig.Loaded, out WeaponDef def, out int _))
                return false;
            degrees = def.Spread;
            return true;
        }

        public bool TryGetWeaponDefId(int slotIndex, out int weaponDefId)
        {
            weaponDefId = -1;
            if (slotIndex < 0 || slotIndex >= SimConfig.MaxEntities) return false;
            if (!WeaponSystem.IsEquipped(_sim, slotIndex, WeaponConfig.Loaded, out WeaponDef def, out int _))
                return false;
            weaponDefId = def.Id;
            return true;
        }

        private bool IsLocal(int slotIndex)
            => _localEntityId() != 0 && _sim.TryResolve(_localEntityId(), out int localSlot) && localSlot == slotIndex;

        private static bool AliveIn(SimWorldStateSnapshot snapshot, int slotIndex)
            => (snapshot.AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) != 0u;
    }
}
