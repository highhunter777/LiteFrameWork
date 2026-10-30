using System.IO;
using LiteSim;

namespace LiteGame
{
    /// <summary>表到 Sim 数值的共用投影。服务端源链接同一文件，避免维护第二份映射。</summary>
    public static class SimConfigMapper
    {
        public const int DefaultRowId = 1;

        /// <summary>投影前检查必要定义，缺行或非法数值时拒绝发布。</summary>
        public static string Validate(cfg.Tbcombatnum combat, cfg.Tbmovementconfig movement,
            cfg.Tbentityconfig entity)
        {
            if (combat == null || combat.GetOrDefault(DefaultRowId) == null)
                return "tbcombatnum 缺 id=1 行（玩法数值）";
            cfg.combatnum row = combat.GetOrDefault(DefaultRowId);
            if (float.IsNaN(row.MoveSpeed) || float.IsInfinity(row.MoveSpeed) || row.MoveSpeed <= 0f)
                return "tbcombatnum id=1 move_speed 必须为正有限数值";
            if (float.IsNaN(row.AimMoveSpeed) || float.IsInfinity(row.AimMoveSpeed) || row.AimMoveSpeed < 0f)
                return "tbcombatnum id=1 aim_move_speed 必须为非负有限数值";
            // 窗长落进 EntitySlot.FireStanceFrames（public byte，且进公共快照/双口径 checksum/proto）——
            // 越界会在强转处静默截断，故在装载闸门 fail-fast，不让错表起得来。
            if (row.FireStanceFrames < 1 || row.FireStanceFrames > byte.MaxValue)
                return "tbcombatnum id=1 fire_stance_frames 必须落在 1..255（落 EntitySlot.FireStanceFrames 的 byte）";
            cfg.movementconfig move = movement?.GetOrDefault(DefaultRowId);
            if (move == null)
                return "tbmovementconfig 缺 id=1 行（重力唯一表源）";
            if (float.IsNaN(move.Gravity) || float.IsInfinity(move.Gravity))
                return "tbmovementconfig id=1 gravity 必须为有限数值";
            cfg.entityconfig definition = entity?.GetOrDefault(DefaultRowId);
            if (definition == null)
                return "tbentityconfig 缺 id=1 行（默认角色定义）";
            if (definition.InitialHp <= 0)
                return "tbentityconfig id=1 initial_hp 必须为正整数";
            return null;
        }

        /// <summary>三张表组合为固定对局快照。Gravity 与 EntityHp 是投影值，没有独立表列。</summary>
        public static CombatValues BuildCombatValues(cfg.Tbcombatnum combat, cfg.Tbmovementconfig movement,
            cfg.Tbentityconfig entity)
        {
            string error = Validate(combat, movement, entity);
            if (error != null) throw new InvalidDataException(error);
            cfg.combatnum row = combat.Get(DefaultRowId);
            return new CombatValues(row.MoveSpeed, movement.Get(DefaultRowId).Gravity, row.HitscanRange,
                row.BaseDamage, row.DamageSpread, entity.Get(DefaultRowId).InitialHp,
                row.AimMoveSpeed, row.FireStanceFrames);
        }
    }
}
