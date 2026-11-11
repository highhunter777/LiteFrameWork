using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Meta.Abstractions.Account;
using LiteGame.Modules.Meta.Client;
using LiteGame.Modules.Meta.Client.Account;
using LiteGame.Modules.Meta.Config;

namespace LiteGame.Modules.Meta
{
    /// <summary>⑮ 联机入口（局外业务）：部署快照 + 登录域 + 端口面（房间/登录/重入的上游）。</summary>
    public sealed class MetaSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly DeployBake _deployBake;   // 包内烙入（与内容段同一实例；实例只读一次）
        private AccountDomain _domain;

        public MetaSegment(DeployBake deployBake)
            => _deployBake = deployBake ?? throw new ArgumentNullException(nameof(deployBake));

        public string Name => "Meta";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "PlatformInfrastructure", "Identity" };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            MetaEntryConfig deploy = MetaDeployConfig.Load(Environment.GetCommandLineArgs(), _deployBake.ReadArgs());
            context.Put<MetaEntryConfig>(deploy);

            Func<string> accountDeviceId = string.IsNullOrEmpty(deploy.DeviceId)
                ? (Func<string>)(() => UnityEngine.SystemInfo.deviceUniqueIdentifier)
                : () => deploy.DeviceId;
            IMetaClient client = deploy.HasMeta
                ? new UnityMetaClient(deploy.MetaUrl, deploy.TimeoutSeconds, context.Require<IJsonCodec>())
                : null;
            var login = new MetaLoginService(client, accountDeviceId, deploy.MetaUrl, context.Require<IdentityContext>());
            context.Put(login);
            context.Put<IMetaConfigFacts>(login);

            _domain = new AccountDomain(context.RootScope, context.Services,
                template: deploy.RoomTemplate, gmKey: deploy.RoomGmKey);
            context.Put<IAccountDomain>(_domain);
            context.RootScope.Register(_domain);

            context.Put<IRoomTicketSource>(new AccountTicketSource(_domain, login));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _domain?.Close(AccountCloseReason.Shutdown);
            return UniTask.CompletedTask;
        }
    }
}
