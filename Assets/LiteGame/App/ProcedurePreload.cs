using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 预载流程（M3 版；C1-⑧ 资源初始化改经 IContentService——注入装配，为 U1/C2 租约消费者就位）：
    /// 资源初始化 → 配置加载 → **M3 锚点五步序**（手册步骤 4 / M3 指导 §2.4）——
    /// ① Lua 全量预载 → ② Init env + 执行 main.lua → ③ RegistryFiller 读三件套填充注册表 →
    /// ④ 报告整批统一判定（有失败即 Fail 阻断）→ ⑤ 放行进 Main。
    /// LuaComponent/RegistryFiller 由装配点构造注入（依赖不从 payload 取）。
    /// </summary>
    public sealed class ProcedurePreload : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IContentService _content;
        private readonly IConfigService _config;
        private readonly LuaComponent _lua;
        private readonly RegistryFiller _filler;
        private readonly IEventCenter _events;
        private readonly Func<string[]> _listLuaFiles;     // Lua 清单（装配点绑定——G1：静态门面收口于装配点）

        public ProcedurePreload(IContentService content, IConfigService config, LuaComponent lua, RegistryFiller filler, IEventCenter events,
            Func<string[]> listLuaFiles = null, CancellationToken rootToken = default)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _filler = filler ?? throw new ArgumentNullException(nameof(filler));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _listLuaFiles = listLuaFiles;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                UnityEngine.Debug.Log("[Preload] begin");        // C1-③ 临时诊断
                // 资源包初始化已移入 Patch 流程（C1-⑩：内容事务恢复先行——§7.1 Patch 先于 Preload）
                await _config.LoadAsync(ct);

                // ---- M3 锚点：Lua 预载与注册表填充段（勿在此行上方插入消费逻辑）----
                // ① 全量预载：同步 loader 的咽喉（§4.2），env 依赖它，先建缓存再 Init
                //    （C1-⑨：字节经 IContentService 租约通道——代次/引用统一，提取即释放；
                //     G1：清单经装配点注入——运行时不再直查静态门面）
                var preloader = new LuaPreloader(LoadLuaBytesViaContent, _listLuaFiles);
                await preloader.PreloadAllAsync(ct);
                _lua.Init(preloader, _events, Bridge.BindGlobals);   // env + 事件桥；游戏桥经注入（产品→框架）
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

        /// <summary>经内容租约读 Lua 字节（TextAsset 提取 bytes 即释放——复制数据不留源引用，热更 §9）。</summary>
        private async UniTask<byte[]> LoadLuaBytesViaContent(string location, CancellationToken ct)
        {
            var lease = await _content.AcquireAsync<UnityEngine.TextAsset>(location, ct: ct);
            try { return lease.Asset.bytes; }
            finally { lease.Dispose(); }
        }
    }
}
