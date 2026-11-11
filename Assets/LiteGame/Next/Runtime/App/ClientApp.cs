using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.Input;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Features.Result;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using UnityEngine;

namespace LiteGameNext
{
    /// <summary>组合根（热更侧）：显式构造平台切片与功能，不持容器；RunAsync 进 AppFlow，Tick 驱动帧，ShutdownAsync 逆序关闭。</summary>
    public sealed class ClientApp : IClientApp
    {
        private readonly LiteFrameworkNext.Platform _platform;
        private readonly UiRuntime _ui;
        private readonly MetaClient _meta;
        private readonly AccountFeature _account;
        private readonly MatchmakingFeature _matchmaking;
        private readonly BattleFeature _battle;
        private readonly ResultFeature _result;
        private readonly AppFlow _flow;
        private readonly Lifetime _lifetime = new Lifetime();

        /// <summary>构造：功能只拿自己需要的平台切片（不传平台聚合体/容器）；登记逆序释放顺序。</summary>
        // 平台参数全限定：本命名空间下 LiteGameNext.Platform 会遮蔽框架的 Platform 类型（CS0118）。
        public ClientApp(LiteFrameworkNext.Platform platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _ui = new UiRuntime(CreatePageAsync);
            _meta = CreateMetaClient();
            _account = new AccountFeature(_ui, _meta, () => SystemInfo.deviceUniqueIdentifier);
            _matchmaking = _meta == null ? null : new MatchmakingFeature(_ui, _account, _meta);
            _result = new ResultFeature(_ui);
            _battle = _meta == null ? null : new BattleFeature(_ui, RunRoomSessionAsync);
            _flow = new AppFlow(_ui, _account, _matchmaking, _battle, _result);
            _lifetime.Add(_ui);
            _lifetime.Add(_account);
            if (_matchmaking != null) _lifetime.Add(_matchmaking);
            if (_battle != null) _lifetime.Add(_battle);
            _lifetime.Add(_result);
        }

        /// <summary>UI 运行时（只读观察面：启动链与用例据此看栈顶页面）。</summary>
        public UiRuntime Ui => _ui;

        /// <summary>是否已登录（只读透传账号功能）。</summary>
        public bool IsSignedIn => _account.IsSignedIn;

        /// <summary>初始化 UI 运行时与账号功能；页面由 RunAsync 打开（启动链可观察点）。</summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _ui.Initialize();
            _account.Initialize();
            Log.Info("ClientApp 初始化：platform=" + _platform.Name + " version=" + _platform.Version
                + " 页面工厂=" + PageFactoryMode);
            return UniTask.CompletedTask;
        }

        /// <summary>顶层流程交给 AppFlow（登录 → 占位页 → 等取消）；取消由 AppFlow 静默收口。</summary>
        public UniTask RunAsync(CancellationToken ct) => _flow.RunAsync(ct);

        /// <summary>逐帧驱动：UI 运行时优先（功能 Tick 在 P1 起按需接入）。</summary>
        public void Tick(float deltaTime) => _ui.Tick(deltaTime);

        /// <summary>逆序关闭：账号功能先于 UI 运行时；释放期异常只记日志不向上抛。</summary>
        public UniTask ShutdownAsync()
        {
            try
            {
                _lifetime.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("ClientApp 关闭期异常：" + ex);
            }

            Log.Info("ClientApp 已关闭");
            return UniTask.CompletedTask;
        }

        private static string PageFactoryMode
        {
            get
            {
#if UNITY_EDITOR
                return "EditorPageFactory(AssetDatabase)";
#else
                return "未接入（P0 仅编辑器装载）";
#endif
            }
        }

        /// <summary>真实房间会话：解析 MatchInfo 地址 → RoomSession（KCP/安全信封）。地址非法直接失败，不静默降级。</summary>
        private static async UniTask<BattleResult> RunRoomSessionAsync(MatchInfo match, BattleHudModel hud,
            CancellationToken cancellationToken)
        {
            if (!TryParseEndpoint(match.Address, out string host, out int port))
                return BattleResult.Failed("房间地址无效：" + match.Address);
            using (var session = new RoomSession(host, port, match.RoomId, match.Ticket, BuildHashValue(),
                       hud: hud, input: new UnityBattleInput()))
                return await session.RunAsync(cancellationToken);
        }

        private static bool TryParseEndpoint(string address, out string host, out int port)
        {
            host = string.Empty;
            port = 0;
            if (string.IsNullOrWhiteSpace(address)) return false;
            int separator = address.LastIndexOf(':');
            if (separator <= 0 || separator >= address.Length - 1) return false;
            host = address.Substring(0, separator);
            return int.TryParse(address.Substring(separator + 1), out port) && port > 0 && port <= 65535;
        }

        private static string BuildHashValue()
        {
            return LiteNet.BuildHash.Value;
        }

        /// <summary>Meta 基址由 env `LITEGAME_META_URL` 显式给出；未配置则不建客户端，账号回退 P0 假登录（不硬编码地址）。</summary>
        private static MetaClient CreateMetaClient()
        {
            string url = Environment.GetEnvironmentVariable("LITEGAME_META_URL");
            return string.IsNullOrWhiteSpace(url) ? null : new MetaClient(new UnityMetaTransport(url));
        }

        /// <summary>页面工厂：编辑器按 prefab 资源路径装载；Player 侧 P1 换 YooAsset，当前先告警并抛（不静默造空页）。</summary>
        // 工厂签名必须匹配 UiRuntime 的 Func<PageSpec, CancellationToken, UniTask<PageBase>>。
        private static async UniTask<PageBase> CreatePageAsync(PageSpec spec, CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            return await EditorPageFactory.CreateAsync(spec, cancellationToken);
#else
            await UniTask.CompletedTask;
            Log.Warning("P0 非编辑器未接入内容装载器：页面 " + (spec == null ? "<null>" : spec.Address) + " 打开失败");
            throw new NotSupportedException("P0 非编辑器未接入内容装载器（P1 接 YooAsset）");
#endif
        }
    }
}
