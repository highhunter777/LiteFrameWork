// 本文件由 HeadHitLineTuner 生成（菜单 LiteGame/调整爆头带 → 导出，或测试面板导出按钮）——勿手改。
// 语义：双柱判定几何——身体柱 [0, HeadHitLine) × HitscanRadius ＋ 爆头柱 [HeadHitLine, HitscanHeight] × Radius
//       （竖直堆叠；下沿 = HitscanHeight × Ratio，比例单源随烘焙身高自动缩放——《固定斜视角射击方案专项设计》§5）。
// 消费：CombatConfig.HeadHitLine（双柱分界） / HeadshotRadius（爆头柱径——命中几何的一部分，非事后闸）。
// 来源：视觉调带裁决值（编辑器 Scene 视图拖带 / 测试模式实机滑杆，二者共用同一导出口径）。
namespace LiteSim
{
    /// <summary>爆头区常量（视觉裁决值；重调 = HeadHitLineTuner 拖带后导出）。</summary>
    public static class HeadBake
    {
        public const float Ratio = 0.805f;

        /// <summary>爆头柱半径（m）：双柱几何的上段柱径——命中爆头柱即 Crit（SimRaycast 求交单源）。</summary>
        public const float Radius = 0.15f;
    }
}
