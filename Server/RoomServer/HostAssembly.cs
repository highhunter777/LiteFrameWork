using System;
using Microsoft.Extensions.DependencyInjection;
using LiteSim;
using LiteNet.Transport;
using RoomServer.Application;

namespace RoomServer
{
    /// <summary>
    /// 宿主组合根（《服务端宿主装配收敛专项设计》批1）：MS DI 装配收敛。
    ///
    /// 纪律（专项 §2/§3）：
    /// - 容器只在**装配层**——ServerHost 与内核（Runtime/Application/Sim/LiteNet）零容器感知；
    /// - 运行期零解析——对象图在启动段一次性解析，60Hz 循环与 Worker 热路径不得触容器；
    /// - 解析序=创建序（Program 按 outbox → transport → host 解析）：容器按创建序**逆序**释放，
    ///   与历史 using 声明序（host→transport→outbox）等价；释放序有排空手验基线，不得漂移；
    /// - per-room 对象（RoomInstance/Runtime/Pipeline）是运行期数据，不进容器。
    /// </summary>
    public static class HostAssembly
    {
        /// <summary>装配输入——Program 完成 CLI/配置/密钥处理后的产物（fail-closed 判定此前已完成）。</summary>
        public sealed class Inputs
        {
            /// <summary>服务端配置（必需：房间模板/容量/Worker 参/结算日志路径）。</summary>
            public RoomServerConfig Config;

            /// <summary>票据受众标识（可空 = 不做受众校验）。</summary>
            public string Audience;

            /// <summary>票据验签器（可 null = 未装配——token 只作原型级非空校验，告警口径在 Program）。</summary>
            public IJoinTicketValidator TicketValidator;

            /// <summary>安全信封选项（可 null = 裸 KCP——仅 --allow-insecure-local 路径，Program 已 fail-closed）。</summary>
            public SecureEnvelopeOptions EnvelopeOptions;

            /// <summary>玩法数值实例（**必填**——服务端必须显式传入装载产物 `CombatNumValues.ToValues()`；
            /// 缺失即拒装配，不得静默跑默认值——技术债 #1 的结构性防线）。</summary>
            public CombatValues? CombatValues;

            /// <summary>武器表实例（**必填**——服务端必须显式传入装载产物 `ServerTableLoad.Weapons`；
            /// 缺失即拒装配——与 CombatValues 同纪律）。</summary>
            public WeaponTable Weapons;

            /// <summary>
            /// Lobby 注册参数（可 null = 不注册——单机/离线形态）。密钥已由 Program 从环境变量/命令行
            /// 组装（不进配置文件）；配置了 lobby.url 但缺密钥由 Program 先行 fail-closed。
            /// </summary>
            public LobbyRegistrationClient.Settings Lobby;
        }

        /// <summary>
        /// 注册宿主对象图（装配期 only）。测试可在其后用 last-wins 覆写端口替身再 <see cref="Build"/>；
        /// 工厂注册是**惰性**的——未被解析的产物不创建（测试覆写接口即可完全避开文件/套接字面）。
        /// </summary>
        public static void Register(IServiceCollection services, Inputs inputs)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            RoomServerConfig config = inputs.Config
                ?? throw new ArgumentException("装配输入 Config 不能为空", nameof(inputs));
            CombatValues combat = inputs.CombatValues
                ?? throw new ArgumentException(
                    "装配输入 CombatValues 不能为空——服务端必须显式传入玩法数值装载产物（不得静默跑默认值）",
                    nameof(inputs));
            WeaponTable weapons = inputs.Weapons
                ?? throw new ArgumentException(
                    "装配输入 Weapons 不能为空——服务端必须显式传入武器表装载产物（不得静默跑默认表）",
                    nameof(inputs));

            services.AddSingleton(config);
            if (inputs.EnvelopeOptions != null) services.AddSingleton(inputs.EnvelopeOptions);
            if (inputs.TicketValidator != null) services.AddSingleton<IJoinTicketValidator>(inputs.TicketValidator);

            // 结算 Outbox：文件实装（宿主层 IO）。concrete + 接口前向——Program 取 concrete 打重放信息，
            // 宿主经接口消费；两处解析到同一单例。
            services.AddSingleton(_ => FileSettlementOutbox.Open(
                config.SettlementJournalPath, config.SettlementOutboxCapacity));
            services.AddSingleton<ISettlementOutbox>(sp => sp.GetRequiredService<FileSettlementOutbox>());

            // 传输：默认 KCP；配了信封选项则装饰（与客户端侧装配同构）。
            services.AddSingleton<IRoomTransport>(sp =>
            {
                var kcp = new KcpTransportServer();
                SecureEnvelopeOptions options = sp.GetService<SecureEnvelopeOptions>();
                return options == null ? (IRoomTransport)kcp : new SecureEnvelopeRoomTransport(kcp, options);
            });

            // 生产形态（§8.2）：房间命令与快照广播在 hash 归属的 Worker 上执行，宿主只做
            // Transport IO、准入与回传应用；嵌入式/历史用例保持默认的宿主 owner 直驱形态（不经本装配）。
            string audience = inputs.Audience;
            services.AddSingleton(sp => new ServerHost(
                sp.GetRequiredService<IRoomTransport>(),
                null,
                sp.GetService<IJoinTicketValidator>(),
                audience,
                config,
                sp.GetRequiredService<ISettlementOutbox>(),
                mailboxRouting: true,
                drainMailboxesImmediately: false,
                workerExecution: true,
                combatValues: combat,
                weapons: weapons));

            services.AddSingleton(sp =>
            {
                ServerHost host = sp.GetRequiredService<ServerHost>();
                var loop = new ServerLoop(host);
                host.LoopStats = loop.Stats;                       // Ops 行节拍/掉债观测入口
                return loop;
            });

            // Lobby 注册客户端（《Meta 服务专项设计》§7；《服务端宿主装配收敛专项设计》批2 首批新对象）：
            // 工厂惰性——Program 在宿主之后解析，创建序=解析序（释放逆序先停心跳、再收宿主）。
            // 快照读宿主事实（buildHash/容量/占用/排空位），不接受估算值。
            if (inputs.Lobby != null)
            {
                services.AddSingleton(sp =>
                {
                    ServerHost host = sp.GetRequiredService<ServerHost>();
                    return new LobbyRegistrationClient(inputs.Lobby, () => new LobbyRegistrationClient.Snapshot
                    {
                        BuildHash = ServerHost.ServerBuildHash,
                        MaxRooms = host.MaxRooms,
                        RoomCount = host.RoomCount,
                        MaxPlayers = host.MaxRooms * config.MaxExpectedPlayers,
                        PlayerCount = host.OccupiedPlayerCount,
                        Draining = host.Draining,
                        Port = host.BoundPort,
                    }, Console.WriteLine);
                });
            }
        }

        /// <summary>
        /// 标准构建：ValidateOnBuild/ValidateScopes 常开（启动期一次性成本）。工厂注册不在
        /// ValidateOnBuild 覆盖内——图完整性由启动段一次性解析兜底（错误固定在启动，专项 §6）。
        /// </summary>
        public static ServiceProvider Build(IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        }
    }
}
