using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>测试模式配置（测试面板编辑；GM 面板/测试面板进入测试模式时快照进 TestModeRuntime）。
    /// 资产随库（可审查、队友共享）；运行时（含开发包）不读资产——F10 未配置时套默认口径。</summary>
    public sealed class TestModeSettings : ScriptableObject
    {
        [Tooltip("全房免死：跨死线 Hp 保底 1、不写 Kill/Death——目标不消失，命中/受击反馈保留")]
        public bool NoDeath = true;

        [Tooltip("世界时钟缩放（由场景 DebugTuner 落钟）"), Range(0f, 2f)]
        public float TimeScale = 1f;

        [Tooltip("世界时钟暂停")]
        public bool Paused;

        [Tooltip("定点传送：T 键 / GM 面板按钮 → 传送到当前准心点")]
        public bool TeleportEnabled = true;

        [Tooltip("bot 冻结：权威侧每帧把补位 bot 位置回写到进房时快照（站桩加强，防推挤类漂移）")]
        public bool BotFrozen;

        [Tooltip("补位 bot 数量（总席位 = 1 + 本值）"), Min(0)]
        public int BotCount = 1;

        public const string AssetPath = "Assets/LiteGame/Editor/TestModeSettings.asset";
    }
}