using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LiteGameNext.Features.Battle.Input
{
    /// <summary>InputSystem 采集：WASD/左右键；无设备（测试/批处理）返回零输入。</summary>
    public sealed class UnityBattleInput : IBattleInput
    {
        private readonly Func<Vector3> _position;

        public UnityBattleInput(Func<Vector3> position = null)
        {
            _position = position ?? (() => Vector3.zero);
        }

        public BattleInputState Read()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null) return default;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            Vector2 move = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
            bool fire = mouse != null && mouse.leftButton.isPressed;
            bool aim = mouse != null && mouse.rightButton.isPressed;
            Vector3 aimPoint = _position() + new Vector3(0f, 1.2f, 10f);
            return new BattleInputState(move.x, move.y, fire, aim, aimPoint);
        }
    }
}
