#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using UnityEngine;

namespace LiteGame
{
    /// <summary>开发启动配置：**场景级启动开关与本地测试模式初值**（Inspector 编辑，场景序列化，进 Play 前定好；
    /// Play 中改 Inspector 值同样生效）。只发布开关的静态镜像，不持有服务引用。
    /// 时钟/事件旋钮见 <see cref="DebugRuntimeTuner"/>；运行期开发入口见 <see cref="DebugConsole"/>（F12）。
    /// 挂 GameEntry 同 GameObject，随 DontDestroyOnLoad 常驻。release 构建零残留（条件编译）。</summary>
    public sealed class DevStartupConfig : MonoBehaviour
    {
        [Header("开发落点（直入）")]
        [Tooltip("直入训练场：启动即进本地测试模式对局（本地服 Room-Test），不加载登录页；退出按 UI（ESC→设置页→退出）回大厅页。")]
        public bool DirectToTraining;

        [Header("本地测试模式启动配置（进房时快照进运行时；局内可调项在 GM 面板）")]
        [Tooltip("全房免死：跨死线 Hp 保底 1、不写 Kill/Death——目标不消失，命中/受击反馈保留")]
        public bool NoDeath = true;
        [Tooltip("无限子弹：进房初值；对局中可在 GM 面板现场开合")]
        public bool InfiniteAmmo;
        [Tooltip("瞄准激光：进房初值；对局中可在 GM 面板现场开合")]
        public bool LaserSight = true;
        [Tooltip("伤害数字：进房初值；对局中可在 GM 面板现场开合")]
        public bool DamageNumbers = true;
        [Tooltip("爆头自动验证：替换输入源自动打爆头（进房初值；对局中可在 GM 面板现场开合）")]
        public bool AutoHeadshot;
        [Tooltip("爆头区域可视化：进房初值；对局中可在 GM 面板现场开合")]
        public bool DrawHeadshotDebug;
        [Tooltip("定点传送：T 键 / GM 面板按钮 → 传送到当前准心点")]
        public bool TeleportEnabled = true;
        [Tooltip("bot 冻结：权威侧每帧把补位 bot 位置回写到进房时快照（站桩加强，防推挤类漂移）")]
        public bool BotFrozen;
        [Tooltip("补位 bot 数量（总席位 = 1 + 本值）"), Min(0)]
        public int BotCount = 1;

        /// <summary>直入训练场开关的全局读点（流程层不认识本组件）。</summary>
        public static bool DirectToTrainingEnabled;

        /// <summary>把**本组件的启动配置**快照进运行时并置位本地测试模式（ProcedureMain 消费进房请求时单点调用）；
        /// 场景未挂本组件时套默认口径（<see cref="TestModeRuntime.ApplyDefaults"/>）。
        /// 逐项经 <see cref="TestModeOptions.Set"/> 落值（副作用收口在该表）。</summary>
        public static void ApplySnapshotOrDefaults()
        {
            var config = UnityEngine.Object.FindAnyObjectByType<DevStartupConfig>(FindObjectsInactive.Include);
            if (config == null)
            {
                TestModeRuntime.ApplyDefaults();
            }
            else
            {
                TestModeOptions.Set(TestModeOptions.Id.NoDeath, config.NoDeath);
                TestModeOptions.Set(TestModeOptions.Id.InfiniteAmmo, config.InfiniteAmmo);
                TestModeOptions.Set(TestModeOptions.Id.LaserSight, config.LaserSight);
                TestModeOptions.Set(TestModeOptions.Id.DamageNumbers, config.DamageNumbers);
                TestModeOptions.Set(TestModeOptions.Id.AutoHeadshot, config.AutoHeadshot);
                TestModeOptions.Set(TestModeOptions.Id.DrawHeadshotDebug, config.DrawHeadshotDebug);
                TestModeOptions.Set(TestModeOptions.Id.TeleportEnabled, config.TeleportEnabled);
                TestModeOptions.Set(TestModeOptions.Id.BotFrozen, config.BotFrozen);
                TestModeRuntime.BotCount = config.BotCount;               // 数量项不在开关表（int）
            }
            TestModeRuntime.Active = true;
        }

        private void Awake() => PublishSwitches();

        private void Update() => PublishSwitches();

        private void PublishSwitches()
        {
            DirectToTrainingEnabled = DirectToTraining;
        }
    }
}
#endif
