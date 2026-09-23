using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 预载流程（M3 版）：资源初始化 → 配置加载 → **M3 锚点五步序**（手册步骤 4 / M3 指导 §2.4）——
    /// ① Lua 全量预载 → ② Init env + 执行 main.lua → ③ RegistryFiller 读三件套填充注册表 →
    /// ④ 报告整批统一判定（有失败即 Fail 阻断）→ ⑤ 放行进 Main。
    /// LuaComponent/RegistryFiller 由装配点构造注入（依赖不从 payload 取）。
    /// </summary>
    public sealed class ProcedurePreload : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IConfigService _config;
        private readonly LuaComponent _lua;
        private readonly RegistryFiller _filler;
        private readonly IEventCenter _events;

        public ProcedurePreload(IConfigService config, LuaComponent lua, RegistryFiller filler, IEventCenter events,
            CancellationToken rootToken = default)
            : base(rootToken)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _filler = filler ?? throw new ArgumentNullException(nameof(filler));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                UnityEngine.Debug.Log("[Preload] begin");        // C1-③ 临时诊断
                await AssetService.InitAsync(ct: ct);
                UnityEngine.Debug.Log("[Preload] asset ready");  // C1-③ 临时诊断
                await _config.LoadAsync(ct);

                // ---- M3 锚点：Lua 预载与注册表填充段（勿在此行上方插入消费逻辑）----
                // ① 全量预载：同步 loader 的咽喉（§4.2），env 依赖它，先建缓存再 Init
                var preloader = new LuaPreloader();
                await preloader.PreloadAllAsync(ct);
                _lua.Init(preloader, _events);                   // env + 桥绑定（服务桥 §2.5 / 事件桥 §2.6）
                _lua.DoMain();                                   // ② 执行 main.lua（require/定义，§4.4）
                _lua.TickEnabled = true;                         // tick 派发开（宿主心跳；main.lua 无定时器也无害）

                var report = _filler.FillAll(ct);                // ③ 读三件套逐行 require/校验 → Fill → 报告
                if (report.HasFailures)                          // ④ 整批统一判定（不遇错即停，一次拿全问题）
                {
                    foreach (var f in report.Failures)
                        Log.Error($"注册表填充失败 [{f.kind}] {f.key}:{f.reason}", "Lua");
                    var ex = new InvalidOperationException(
                        $"注册表填充失败 {report.Failed}/{report.Total}——阻止进 Main（§3.4 fail-fast，不做带病启动）");
                    Fail(m, ex, nameof(RunAsyncCore));
                    m.Request(ProcedureId.Error, new ProcedureArgs(ex));   // 失败原因随 payload 交错误流程
                    return;
                }

                m.Request(ProcedureId.Main);                     // ⑤ 放行
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
