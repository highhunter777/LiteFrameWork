using UnityEngine;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// **瞄准相机注入口**（可选能力，设备源按需实现）：把对局渲染相机交给需要相机做瞄准解算的设备源。
    ///
    /// 与 <see cref="IAimWorldSink"/> 同一范式（见 <see cref="IIntentSource"/>）：核心接口不为
    /// "某个设备源需要相机"而全体扩面，触屏/手柄源维持既有行为；消费方按能力 `is` 注入，
    /// 未实现即跳过（对局前的主菜单形态、测试替身都走这条退化路径）。
    /// </summary>
    public interface IAimCameraSink
    {
        /// <summary>注入渲染相机；null = 不做相机相关换算。</summary>
        void SetAimCamera(Camera camera);
    }

    /// <summary>
    /// **自由视角设备读数**（可选能力）：像素级视角增量与肩位切换沿。
    ///
    /// 只有真正的自由视角设备（鼠标/触屏拖拽）才需要它；手柄固定视角形态不实现本接口，
    /// 表现层据此得到 null 增量源（= 无自由视角形态），而不是去猜具体设备实现类型。
    /// </summary>
    public interface ILookDevice
    {
        /// <summary>本帧视角增量（像素；纯视图侧读数，不进意图三件、不上行）。</summary>
        Vector2 LookDelta { get; }

        /// <summary>肩位切换沿（本帧按下）。</summary>
        bool ShoulderSwapPressed { get; }
    }
}
