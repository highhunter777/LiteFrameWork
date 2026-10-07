// 本文件由 HeadHitLineTuner 生成（菜单 LiteGame/调整爆头带 → 导出，或测试面板导出按钮）——勿手改。
// 语义：爆头带下沿 = HitscanHeight × Ratio（比例单源，随烘焙身高自动缩放——《固定斜视角射击方案专项设计》§5）。
// 消费：CombatConfig.HeadHitLine（爆头带下沿 [HeadHitLine, HitscanHeight] 的下界）。
// 来源：视觉调带裁决值（编辑器 Scene 视图拖带 / 测试模式实机滑杆，二者共用同一导出口径）。
namespace LiteSim
{
    /// <summary>爆头带比例常量（视觉裁决值；重调 = HeadHitLineTuner 拖带后导出）。</summary>
    public static class HeadBake
    {
        public const float Ratio = 0.775f;
    }
}
