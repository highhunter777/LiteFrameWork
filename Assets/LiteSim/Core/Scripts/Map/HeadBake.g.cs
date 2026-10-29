// 本文件由 HeadHitLineTuner 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 直调）——勿手改。
// 语义：爆头带下沿 = HitscanHeight × Ratio（比例单源，随烘焙身高自动缩放——《固定斜视角射击方案专项设计》§5）；
//       爆头柱半径 = Radius（窄于命中柱 0.45——头部带是窄柱切片，非命中柱全径切片）。
// 消费：CombatConfig.HeadHitLine / HeadshotRadius（爆头判据竖直下沿 + 水平闸）。
// 来源：视觉调带裁决值（编辑器 Scene 视图拖带 / 测试模式实机滑杆，二者共用同一导出口径）。
namespace LiteSim
{
    /// <summary>爆头区常量（视觉裁决值；重调 = 烘焙工具箱爆头带页拖带后导出）。</summary>
    public static class HeadBake
    {
        public const float Ratio = 0.805f;

        /// <summary>爆头柱半径（m）：判定点水平距目标 ≤ 本值才计爆头（命中与否仍由命中柱承担）。</summary>
        public const float Radius = 0.15f;
    }
}
