using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>⑮ 联机入口（局外业务）：部署快照 + 联机客户端 + 入口编排件（房间/登录/重入的上游）。
    /// 只产出事实与客户端；页面与脚本经 Bridge 门面消费（装订在组合根）。</summary>
    public sealed class MetaComposition : IClientModule, IClientModuleGraph
    {
        private readonly DeployBake _deployBake;   // 包内烙入（与内容段同一实例；实例只读一次）

        public MetaComposition(DeployBake deployBake)
            => _deployBake = deployBake ?? throw new ArgumentNullException(nameof(deployBake));

        public string Name => "Meta";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "PlatformInfrastructure", "Identity" };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 联机部署三源注入（-meta.url/-envelope.key——命令行 > 包内 deploy.args 烙入 > 默认；单次快照）
            MetaEntryConfig deploy = MetaDeployConfig.Load(Environment.GetCommandLineArgs(), _deployBake.ReadArgs());
            context.Put<MetaEntryConfig>(deploy);

            // 未配置 Meta 基址 = client 置空（IsConfigured false——页面显性拒绝），不造"指向空址"的客户端。
            // 设备标识注入 Unity 面：-device.id 非空时以它为准（同机多客户端各自身份；同值 = 服务端同账号）。
            Func<string> accountDeviceId = string.IsNullOrEmpty(deploy.DeviceId)
                ? (Func<string>)(() => UnityEngine.SystemInfo.deviceUniqueIdentifier)
                : () => deploy.DeviceId;
            var metaEntry = new MetaEntryService(
                deploy.HasMeta
                    ? new UnityMetaClient(deploy.MetaUrl, deploy.TimeoutSeconds, context.Require<IJsonCodec>())
                    : null,
                accountDeviceId,
                deploy.MetaUrl,
                context.Require<IdentityContext>());
            // 云端测试房：模板/口令随部署注入（开发者本地才有；测试者包不带 ⇒ 建不了云端测试房）
            metaEntry.ConfigureTestRoom(deploy.RoomTemplate, deploy.RoomGmKey);
            context.Put(metaEntry);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
