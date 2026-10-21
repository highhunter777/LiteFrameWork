using UnityEngine;

namespace LiteClient
{
    /// <summary>
    /// 指针锁定管理（第三人称自由视角的输入形态件）：对局内锁定并隐藏系统指针——鼠标增量
    /// 全部喂给相机视角（<see cref="ICameraService.AddLookDelta"/>），"屏幕光标位"语义整体退役；
    /// 上下文门被拦（模态 UI）/死亡/退场时解锁恢复。**唯一光标写者**——<c>Cursor.lockState</c> 与
    /// <c>Cursor.visible</c> 全仓只经本件写（准心驱动的光标显隐钩子收编进本件后消灭第二写者）。
    /// 纯平台件：不进意图三件、不进协议。
    /// </summary>
    public sealed class PointerLockService
    {
        /// <summary>当前锁定态（锁定 = 指针锁 + 隐藏；未锁 = 自由 + 可见）。</summary>
        public bool IsLocked { get; private set; }

        /// <summary>进入对局控制：锁定并隐藏系统指针（准心成为唯一瞄准标示）。
        /// 幂等——状态不变不重复写平台（帧级调用零负担）。</summary>
        public void Lock()
        {
            if (IsLocked) return;
            IsLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>离开对局控制/被门拦截/死亡：解锁并恢复指针可见（模态 UI 要可点）。幂等同上。</summary>
        public void Unlock()
        {
            if (!IsLocked) return;
            IsLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
