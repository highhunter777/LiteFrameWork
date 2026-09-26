using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程（U1-④：真实主页面接入——《UI框架总设计》U1 退出条件"实际资源包 Player 可打开/关闭"的验收锚点）：
    /// 进入 Main 即打开 UIMain（真 prefab + 真 Lua + 内容租约链路端到端），失败进确定错误态。
    /// 打开走装配根注入的导航能力（U2 首版单写者导航的真实消费者——产品入口必须走导航，不直调 ShowAsync）。
    ///
    /// **玩法场景加载（2026-09-26）**：本阶段负责把启动场景（Test.unity，只含引导件 + 主相机）
    /// 切到**玩法场景**（训练场）——场景里有虚拟相机与场地，是对局相机的来源。
    /// 位置选在这里而不是更早：场景经 **YooAsset 内容包**加载，而资源包在 `ProcedurePatch` 才初始化
    /// （§7.1 Patch 先于 Preload），故不可能更早；又必须在进对局前，所以是 Main 进入时。
    /// 加载成功后**回填输入服务的瞄准相机**——主相机随场景切换换了对象，原引用已随旧场景销毁
    /// （设备源另有失效回落，这里是显式的正常路径，不靠回落兜底）。
    /// 后续流程树（主菜单 → 选模式 → 进场景 → 战斗 → 结算，M5+/C2）在此扩展；
    /// Match/Battle/Result 预留见 <see cref="ProcedureId"/>。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>主页面 formId（tbuiform id=1 = UIMain——真配置投影，与 UI-U0 真 Lua 用例同一行）。</summary>
        private const int MainFormId = 1;

        /// <summary>Player 冒烟标记（scripts/player-smoke.ps1 标记集——真资源包页面打开的健康证据）。</summary>
        private const string SmokeMarker = "[UI] main open";

        /// <summary>玩法场景（内容包 location = 资源完整路径；训练场含场地 + 虚拟相机）。</summary>
        private const string BattleScene = "Assets/Scenes/训练场.unity";

        /// <summary>打开主页面（§5.1 逻辑边界：流程层不认识 UI 运行时，只持一个"打开第 N 号界面"的能力）。
        /// 导航的**单写者**语义（U2）不变——由装配根注入的 <see cref="UINavigationController"/> 保持唯一。</summary>
        private readonly Func<int, CancellationToken, UniTask> _open;

        /// <summary>场景切换（可空 = 不切场景，纯 UI 形态/测试装配）。</summary>
        private readonly ISceneService _scenes;

        public ProcedureMain(Func<int, CancellationToken, UniTask> open, ISceneService scenes = null,
            CancellationToken rootToken = default)
            : base(rootToken)
        {
            _open = open ?? throw new ArgumentNullException(nameof(open));
            _scenes = scenes;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // C2 测试入口（批①）：F9 = 进入测试对局（隔离测试发行者身份，正式登录页归 G3）。
            // 主菜单 → 进对局的正式入口（页面按钮/Matchmaking）归 U2/G3——此处只留热键，不进正式 UI。
            // 门禁与 SimSandbox 同口径：开发/编辑器可用，正式包无该入口（不塞进 Release 玩家）。
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
                m.Request(ProcedureId.Match);
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                await _open(MainFormId, ct);                   // 单写者导航（真 prefab + 真 Lua + 内容租约——U1 全链）
                UnityEngine.Debug.Log($"{SmokeMarker} form={MainFormId} lease-held");
                await EnterBattleSceneAsync(ct);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }

        /// <summary>
        /// 切到玩法场景（Single 模式：启动场景被替换）。**幂等**——已在目标场景不重复加载。
        /// 未注入场景服务（纯 UI 装配/测试）= 跳过，本阶段仍只做 UI 那件事。
        ///
        /// **不做"回填瞄准相机"这件事**：设备源在采样时发现缓存的相机已随旧场景销毁（Unity 伪 null）
        /// 会**自动回落到当前 `Camera.main`**——那是更强的保证（谁在什么时候切场景都对），
        /// 而"记得在切场景后回填"的约定迟早会漏（本批之前正是那个形态）。
        /// </summary>
        private async UniTask EnterBattleSceneAsync(CancellationToken ct)
        {
            if (_scenes == null) return;
            if (_scenes.IsLoaded(BattleScene)) return;         // 幂等：重进 Main 不重复加载

            await _scenes.LoadSingleAsync(BattleScene, ct);
            UnityEngine.Debug.Log($"[Main] 玩法场景已加载:{BattleScene} scene={_scenes.SingleSceneName}");
        }
    }
}
