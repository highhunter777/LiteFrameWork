using LiteFramework;
using LiteSim;

namespace LiteGame
{
    /// <summary>传送命令（GM/dev 专用）：T 键与 GM 面板的"传送到准心"统一经命令中心派发——
    /// 排队命令在帧边界执行（OnGUI/输入回调不直改运行态），成功入撤销栈（撤销 = 回出发位）。
    /// <see cref="Origin"/> 由处理器在 Execute 时回填（出发位在派发时刻才确定——入队方不填）。</summary>
    public sealed class TeleportToAimCommand : ICommand
    {
        /// <summary>出发位（Execute 捕获；Undo 回传——撤销即"传回传送前所在地"）。</summary>
        public SimVector3 Origin;
    }
}
