using UnityEngine;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// **瞄准相机注入口**（可选能力，设备源按需实现）：把对局渲染相机交给需要相机做瞄准解算的设备源。
    /// </summary>
    public interface IAimCameraSink
    {
        /// <summary>注入渲染相机；null = 不做相机相关换算。</summary>
        void SetAimCamera(Camera camera);
    }

    /// <summary>
    /// **自由视角设备读数**（可选能力）：像素级视角增量与肩位切换沿。
    /// </summary>
    public interface ILookDevice
    {
        /// <summary>本帧视角增量（像素；纯视图侧读数，不进意图三件、不上行）。</summary>
        Vector2 LookDelta { get; }

        /// <summary>肩位切换沿（本帧按下）。</summary>
        bool ShoulderSwapPressed { get; }

        /// <summary>自由观察按住（Alt；新 Input System 的 FreeLook action）。</summary>
        bool FreeLookHeld { get; }

        /// <summary>冻结/解冻瞄准点（自由观察期间由视角控制器控制；冻结期设备源写缓存点）。</summary>
        void SetAimFrozen(bool frozen);
    }
}
