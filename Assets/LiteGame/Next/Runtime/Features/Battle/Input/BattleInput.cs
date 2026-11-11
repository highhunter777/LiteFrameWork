using System;
using LiteSim;
using UnityEngine;

namespace LiteGameNext.Features.Battle.Input
{
    /// <summary>一帧输入意图（引擎无关）：移动/开火/瞄准点；输入采集与 Sim 帧解耦。</summary>
    public readonly struct BattleInputState
    {
        public BattleInputState(float moveX, float moveZ, bool fire, bool aim, Vector3 aimPoint)
        {
            MoveX = moveX;
            MoveZ = moveZ;
            Fire = fire;
            Aim = aim;
            AimPoint = aimPoint;
        }

        public float MoveX { get; }

        public float MoveZ { get; }

        public bool Fire { get; }

        public bool Aim { get; }

        public Vector3 AimPoint { get; }
    }

    /// <summary>输入端口：生产实现读 InputSystem，测试注入假件。</summary>
    public interface IBattleInput
    {
        BattleInputState Read();
    }

    /// <summary>输入意图 → SimInputFrame 的唯一转换（不重复拼位集）。</summary>
    public static class BattleInputFrame
    {
        public static SimInputFrame Build(BattleInputState state, long entityId)
        {
            uint buttons = 0;
            if (state.Fire) buttons |= SimInputFrame.ButtonFire;
            if (state.Aim) buttons |= SimInputFrame.ButtonAim;
            return new SimInputFrame
            {
                EntityId = entityId,
                MoveX = state.MoveX,
                MoveZ = state.MoveZ,
                AimPointX = state.AimPoint.x,
                AimPointY = state.AimPoint.y,
                AimPointZ = state.AimPoint.z,
                Buttons = buttons,
            };
        }
    }
}
