using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 预载流程：配置加载 → **五步序**——
    /// ① Lua 全量预载 → ② Init env + 执行 main.lua → ③ RegistryFiller 读三件套填充注册表 →
    /// ④ 报告整批统一判定（有失败即 Fail 阻断）→ ⑤ 放行（**模态登记已就位**）进 Main。
    /// LuaComponent/RegistryFiller 由装配点构造注入（依赖不从 payload 取）。
    /// </summary>
    public sealed class ProcedurePreload : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IContentService _content;
        private readonly IConfigService _config;
        private readonly LuaComponent _lua;
        private readonly RegistryFiller _filler;
        private readonly IEventBus _events;
        private readonly UIService _ui;
        private readonly FeedbackService _feedback;        // 启动走廊装载面（窗口③）
        private readonly Func<string[]> _listLuaFiles;     // Lua 清单（装配点绑定——静态门面收口于装配点）

        public ProcedurePreload(IContentService content, IConfigService config, LuaComponent lua, RegistryFiller filler, IEventBus events,
            UIService ui, Func<string[]> listLuaFiles = null, CancellationToken rootToken = default, FeedbackService feedback = null)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _filler = filler ?? throw new ArgumentNullException(nameof(filler));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _feedback = feedback;                          // 可空 = 测试装配（LaunchLoading.Begin 空引用跳过）
            _listLuaFiles = listLuaFiles;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // 资源包初始化在 Patch 流程完成（§7.1 Patch 先于 Preload）
                await _config.LoadAsync(ct);

                // ---- Lua 预载与注册表填充段（勿在此行上方插入消费逻辑）----
                // ⓪ 启动走廊开场（窗口③——不定态，Preload 开 / Main 报进度 / 首屏收，§通用加载页 §1）：
                //    依赖上面已装载的配置（202 表行）与就绪的 UIService。
                LaunchLoading.Begin(_feedback, "正在预载资源…");
                // ① 全量预载：同步 loader 的咽喉（§4.2），env 依赖它，先建缓存再 Init
                //    （字节经 IContentService 租约通道——代次/引用统一，提取即释放；
                //     清单经装配点注入——不直查静态门面）
                var preloader = new LuaPreloader(LoadLuaBytesViaContent, _listLuaFiles);
                await preloader.PreloadAllAsync(ct);
                _lua.Init(preloader, _events, ProductBridges.BindGlobals);   // env + 事件桥；游戏桥经注入（产品→框架——框架四门面+meta 单源装订）
                _lua.DoMain();                                   // ② 执行 main.lua（require/定义，§4.4）
                _lua.TickEnabled = true;                         // tick 派发开（宿主心跳；main.lua 无定时器也无害）

                var report = _filler.FillAll(ct);                // ③ 读三件套逐行 require/校验 → Fill → 报告
                if (report.HasFailures)                          // ④ 整批统一判定（不遇错即停，一次拿全问题）
                {
                    foreach (var f in report.Failures)
                        Log.Error($"注册表填充失败 [{f.kind}] {f.key}:{f.reason}", LogTag.Lua);
                    var ex = new InvalidOperationException(
                        $"注册表填充失败 {report.Failed}/{report.Total}——阻止进 Main（§3.4 fail-fast，不做带病启动）");
                    LaunchLoading.End();                         // 启动走廊随失败收口（错误页不能被遮罩挡住）
                    Fail(m, ex, nameof(RunAsyncCore));
                    m.Request(ProcedureId.Error, new ProcedureArgs(ex));   // 失败原因随 payload 交错误流程
                    return;
                }

                // ---- 模态登记（配置已装载——表驱动反查单源；装配期查表必抛，见 ContainerModule 注）----
                // 设置页模态要在**任何打开路径**（登录/大厅/结算/对局菜单；Lua 直开与 PageFlow 两条链）之前全局注册，
                // 故落在放行闸口而非页面流内部。
                int settingsFormId = Bridge.Data.ResolveUIFormId(LuaKeys.UI.Page_Settings);
                if (settingsFormId >= 0) _ui.RegisterModal(settingsFormId);
                else Log.Error($"模态登记失败：生成页面目录未登记 '{LuaKeys.UI.Page_Settings}'", LogTag.UI);

                m.Request(ProcedureId.Main);                     // ⑤ 放行（启动走廊跨流程存续——Main 报进度、首屏收）
            }
            catch (OperationCanceledException) { LaunchLoading.End(); /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                LaunchLoading.End();                             // 失败收口（错误页不能被遮罩挡住）
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
