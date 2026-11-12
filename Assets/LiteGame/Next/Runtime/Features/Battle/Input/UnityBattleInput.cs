using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LiteGameNext.Features.Battle.Input
{
    /// <summary>InputSystem 采集：WASD/左右键；无设备（测试/批处理）返回零输入。</summary>
    public sealed class UnityBattleInput : IBattleInput
    {
        private readonly Func<Vector3> _position;
        private readonly Func<Vector3> _aim;
        private readonly Action<float, float> _look;
        private const float LookSensitivity = 0.12f;

        public UnityBattleInput(Func<Vector3> position = null, Func<Vector3> aim = null,
            Action<float, float> look = null)
        {
            _position = position ?? (() => Vector3.zero);
            _aim = aim;
            _look = look;
        }

        public BattleInputState Read()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null) return default;

            if (_look != null && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0f)
                    _look(delta.x * LookSensitivity, delta.y * LookSensitivity);
            }

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            Vector2 move = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
            bool fire = mouse != null && mouse.leftButton.isPressed;
            bool aim = mouse != null && mouse.rightButton.isPressed;
            Vector3 aimPoint = _aim != null ? _aim() : _position() + new Vector3(0f, 1.2f, 10f);
            return new BattleInputState(move.x, move.y, fire, aim, aimPoint);
        }
    }
}
