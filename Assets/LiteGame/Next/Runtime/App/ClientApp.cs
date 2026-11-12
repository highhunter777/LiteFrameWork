using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.Input;
using LiteGameNext.Features.Battle.World;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Features.Records;
using LiteGameNext.Features.Result;
using LiteGameNext.Features.Settings;
using LiteGameNext.Platform.Audio;
using LiteGameNext.Platform.Content;
using LiteGameNext.Platform.Dev;
using LiteGameNext.Platform.Localization;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using LiteNet.Transport;
using UnityEngine;

namespace LiteGameNext
{
    /// <summary>组合根（热更侧）：显式构造平台切片与功能，不持容器；RunAsync 进 AppFlow，Tick 驱动帧，ShutdownAsync 逆序关闭。</summary>
    public sealed class ClientApp : IClientApp
    {
        private readonly LiteFrameworkNext.Platform _platform;
        private readonly NextContentService _content;
        private readonly UiRuntime _ui;
        private readonly AudioService _audio;
        private readonly LocalizationService _text;
        private readonly MetaClient _meta;
        private readonly AccountFeature _account;
        private readonly MatchmakingFeature _matchmaking;
        private readonly BattleFeature _battle;
        private readonly ResultFeature _result;
        private readonly SettingsStore _store;
        private readonly SettingsFeature _settings;
        private readonly RecordsFeature _records;
        private readonly IDevHooks _dev;
        private readonly AppFlow _flow;
        private readonly Lifetime _lifetime = new Lifetime();

        /// <summary>构造：功能只拿自己需要的平台切片（不传平台聚合体/容器）；登记逆序释放顺序。</summary>
        // 平台参数全限定：本命名空间下 LiteGameNext.Platform 会遮蔽框架的 Platform 类型（CS0118）。
        // devHooksOverride：测试可传 NullDevHooks 以固定"无开发面"的契约（生产不传）。
        public ClientApp(LiteFrameworkNext.Platform platform, IDevHooks devHooksOverride = null)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _content = new NextContentService();
            _ui = new UiRuntime(CreatePageAsync);
            _audio = new AudioService();
            _text = new LocalizationService(BuildTextCatalog(), developmentMode: true);
            _meta = CreateMetaClient();
            _store = new SettingsStore();
            _store.Load();
            _settings = new SettingsFeature(_ui, new GameSettings(_store, Application.isMobilePlatform));
            WireSettingEffects();
            _account = new AccountFeature(_ui, _meta, () => SystemInfo.deviceUniqueIdentifier);
            _matchmaking = _meta == null ? null : new MatchmakingFeature(_ui, _account, _meta);
            _records = _meta == null ? null : new RecordsFeature(_ui, _account, _meta);
            _result = new ResultFeature(_ui);
            _dev = devHooksOverride ?? CreateDevHooks();
            // Dev 本地服不依赖 Meta（进程内房间），故有 Dev 面时也建战斗功能。
            _battle = (_meta == null && (_dev == null || !_dev.Enabled)) ? null : new BattleFeature(_ui, RunRoomSessionAsync,
                () => CreateWorld());
            _flow = new AppFlow(_ui, _account, _matchmaking, _battle, _result, _settings, _records,
                (_dev != null && _dev.Enabled) ? CreateLocalMatch : (Func<MatchInfo>)null);
            _lifetime.Add(_ui);
            _lifetime.Add(_account);
            if (_matchmaking != null) _lifetime.Add(_matchmaking);
            if (_records != null) _lifetime.Add(_records);
            if (_battle != null) _lifetime.Add(_battle);
            _lifetime.Add(_result);
            _lifetime.Add(_settings);
            if (_dev != null && _dev.Enabled) _lifetime.Add(new DevHooksDisposer(_dev));
        }

        /// <summary>UI 运行时（只读观察面：启动链与用例据此看栈顶页面）。</summary>
        public UiRuntime Ui => _ui;

        /// <summary>是否已登录（只读透传账号功能）。</summary>
        public bool IsSignedIn => _account.IsSignedIn;

        /// <summary>设置功能（只读观察面：入口按钮据此打开设置页）。</summary>
        public SettingsFeature Settings => _settings;

        /// <summary>战绩功能（只读观察面；Meta 未配置时为 null）。</summary>
        public RecordsFeature Records => _records;

        /// <summary>初始化内容包、UI 运行时与账号功能；同时把存档读回的音量/画质/显示项落到引擎。</summary>
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _content.InitializeAsync(ct);
            _ui.Initialize();
            _account.Initialize();
            _settings.Settings.ApplyAll();
            Log.Info("ClientApp 初始化：platform=" + _platform.Name + " version=" + _platform.Version
                + " 页面工厂=" + PageFactoryMode);
        }

        /// <summary>顶层流程交给 AppFlow（登录 → 占位页 → 等取消）；取消由 AppFlow 静默收口。</summary>
        public UniTask RunAsync(CancellationToken ct) => _flow.RunAsync(ct);

        /// <summary>逐帧驱动：UI 运行时优先（功能 Tick 在 P1 起按需接入）。</summary>
        public void Tick(float deltaTime)
        {
            _ui.Tick(deltaTime);
            _audio.Tick(deltaTime);
            _battle?.Tick(deltaTime);
            _dev?.Tick(deltaTime);
        }

        /// <summary>逆序关闭：账号功能先于 UI 运行时；内容服务最后归还句柄；关闭链末尾落盘设置。</summary>
        public async UniTask ShutdownAsync()
        {
            try
            {
                _lifetime.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("ClientApp 关闭期异常：" + ex);
            }

            await _content.ShutdownAsync(CancellationToken.None);
            _audio.Shutdown();
            await _store.FlushAsync(CancellationToken.None);
            Log.Info("ClientApp 已关闭");
        }

        private static string PageFactoryMode
        {
            get
            {
#if UNITY_EDITOR
                return "NextContentService(AssetDatabase)";
#else
                return "NextContentService(YooAsset/Offline)";
#endif
            }
        }

        /// <summary>Dev 本地对局用的 MatchInfo：无地址/票据（本地服走进程内房间，不解析 endpoint）。</summary>
        private static MatchInfo CreateLocalMatch()
            => new MatchInfo("local", string.Empty, "local-dev", string.Empty, 0L);

        /// <summary>
        /// 反射装载开发面（Dev 程序集不在 hot 引用里，发布构建缺该程序集即回退 NullDev）。
        /// 类型名 `LiteGameNext.Dev.DevFeature, LiteGameNext.Dev`。用运行期判定而非编译宏（开发宏只许在 Dev 程序集）。
        /// </summary>
        private static IDevHooks CreateDevHooks()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return NullDevHooks.Instance;
            Type type = Type.GetType("LiteGameNext.Dev.DevFeature, LiteGameNext.Dev", throwOnError: false);
            if (type == null || !typeof(IDevHooks).IsAssignableFrom(type)) return NullDevHooks.Instance;
            try
            {
                return (IDevHooks)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Log.Warning("开发面装载失败，回退 NullDev：" + ex.Message);
                return NullDevHooks.Instance;
            }
        }

        /// <summary>把 IDevHooks 包成可登记进 Lifetime 的释放件。</summary>
        private sealed class DevHooksDisposer : IDisposable
        {
            private readonly IDevHooks _hooks;

            public DevHooksDisposer(IDevHooks hooks) => _hooks = hooks;

            public void Dispose() => _hooks?.Shutdown();
        }

        /// <summary>文案目录：内置最小集合（zh-CN 源 + en）；接 Luban tbltext 后由表投影替换。</summary>
        private static TextCatalog BuildTextCatalog()
        {
            var catalog = new TextCatalog()
                .Add("UI.Lobby.Title", "zh-CN", "大厅")
                .Add("UI.Lobby.Title", "en", "Lobby")
                .Add("UI.Settings.Title", "zh-CN", "设置")
                .Add("UI.Settings.Title", "en", "Settings")
                .Add("UI.Records.Title", "zh-CN", "战绩")
                .Add("UI.Records.Title", "en", "Records")
                .Add("UI.Result.Title", "zh-CN", "结算")
                .Add("UI.Result.Title", "en", "Result")
                .Add("Common.Close", "zh-CN", "关闭")
                .Add("Common.Close", "en", "Close");
            return catalog;
        }

        /// <summary>把设置项接到真实消费面：音量→音频分组、语言→本地化、sfx→Effect 组静音。</summary>
        private void WireSettingEffects()
        {
            GameSettings settings = _settings.Settings;
            settings.MasterVolume.Changed += _ => ApplyAudioVolumes();
            settings.SoundEnabled.Changed += _ => ApplyAudioVolumes();
            settings.Language.Changed += locale => _text.SetLocale(locale);
            _text.SetLocale(settings.Language.Value);
            ApplyAudioVolumes();
        }

        private void ApplyAudioVolumes()
        {
            GameSettings settings = _settings.Settings;
            float master = settings.MasterVolume.Value;
            _audio.SetGroupVolume(AudioGroup.Effect, settings.SoundEnabled.Value ? master : 0f);
            _audio.SetGroupVolume(AudioGroup.UI, master);
            _audio.SetGroupVolume(AudioGroup.Voice, master);
            _audio.SetGroupVolume(AudioGroup.Bgm, master);
        }

        /// <summary>世界视图 + 角色实体工厂：角色 prefab 首帧加载失败时自动回退立方体。</summary>
        private BattleWorldView CreateWorld()
        {
            BattleWorldView world = BattleWorldFactory.Create(_content);
            world.SetEntityFactory(new PrefabEntityViewFactory(
                name => _content.InstantiateSync("Assets/LiteGame/Modules/Battle/Content/Characters/" + name + ".prefab")));
            return world;
        }

        /// <summary>真实房间会话：解析地址 → RoomSession；JoinAck 定图后加载地图场景，结束时必卸。</summary>
        private async UniTask<BattleResult> RunRoomSessionAsync(MatchInfo match, BattleHudModel hud,
            BattleWorldView world, CancellationToken cancellationToken)
        {
            var input = new UnityBattleInput(
                () => world == null ? Vector3.zero : world.LocalPosition,
                () => world == null ? Vector3.zero : world.AimPoint,
                (dx, dy) => world?.Look(dx, dy));

            // Dev 本地服优先：进程内真 RoomRuntime，不需要 KCP/服务端。
            if (_dev != null && _dev.Enabled)
            {
                BattleResult local = await TryRunLocalSessionAsync(match, hud, world, input, cancellationToken);
                if (local != null) return local;
            }

            if (!TryParseEndpoint(match.Address, out string host, out int port))
                return BattleResult.Failed("房间地址无效：" + match.Address);
            using (var session = new RoomSession(host, port, match.RoomId, match.Ticket, BuildHashValue(),
                       hud: hud, world: world, input: input,
                       audioDriver: new BattleAudioDriver(_audio, null, null, null, null),
                       damageNumbers: new DamageNumberAggregator()))
            {
                session.JoinAcked += mapId => LoadMapForget(mapId, cancellationToken);
                try
                {
                    return await session.RunAsync(cancellationToken);
                }
                finally
                {
                    await _content.UnloadScenesAsync(CancellationToken.None);
                }
            }
        }

        /// <summary>
        /// 本地服路径：起进程内房间并把它的传输交给 RoomSession。
        /// 返回 null = 本地服不可用（回退 KCP）。
        /// </summary>
        private async UniTask<BattleResult> TryRunLocalSessionAsync(MatchInfo match, BattleHudModel hud,
            BattleWorldView world, IBattleInput input, CancellationToken cancellationToken)
        {
            ILocalRoomSession local = await _dev.StartLocalRoomAsync(match.RoomId, 2, cancellationToken);
            if (local == null) return null;
            if (local.Transport is not IClientTransport transport)
            {
                Log.Warning("本地服传输类型不符，回退 KCP：" + local.Transport?.GetType().Name);
                local.Dispose();
                return null;
            }

            using (local)
            using (var session = new RoomSession("local", 1, match.RoomId, string.Empty, BuildHashValue(),
                       transport: transport, hud: hud, world: world, input: input,
                       audioDriver: new BattleAudioDriver(_audio, null, null, null, null),
                       damageNumbers: new DamageNumberAggregator()))
            {
                Log.Info("使用 Dev 本地服对局：room=" + match.RoomId);
                session.JoinAcked += mapId => LoadMapForget(mapId, cancellationToken);
                try
                {
                    return await session.RunAsync(cancellationToken);
                }
                finally
                {
                    await _content.UnloadScenesAsync(CancellationToken.None);
                }
            }
        }

        /// <summary>JoinAck 回调是同步事件：地图加载 kick 出去，失败只记日志（不阻塞会话）。</summary>
        private void LoadMapForget(int mapId, CancellationToken cancellationToken)
        {
            string scenePath = BattleMapCatalog.SceneOf(BattleMapCatalog.Resolve(mapId));
            Log.Info("加载地图：" + scenePath + "（mapId=" + mapId + "）");
            LoadMapAsync(scenePath, cancellationToken).Forget(ex => Log.Error("地图加载失败：" + ex));
        }

        private async UniTask LoadMapAsync(string scenePath, CancellationToken cancellationToken)
            => await _content.LoadSceneAsync(scenePath, cancellationToken);

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

        /// <summary>页面工厂：由内容服务按 PageSpec 归一后的文件名装载（编辑器/Player 同一入口）。</summary>
        private async UniTask<PageBase> CreatePageAsync(PageSpec spec, CancellationToken cancellationToken)
            => await EditorPageFactory.CreateAsync(spec, cancellationToken, _content);
    }
}
