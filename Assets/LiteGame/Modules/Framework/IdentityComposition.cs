using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteNet;

namespace LiteGame.Modules.Framework
{
    /// <summary>
    /// ② 身份上下文（路线图 §4.9）：Build/Schema 的唯一构造点。
    /// 只读编译期/平台事实；内容身份由装配末段绑定（内容/配置装配完成后）。
    /// 业务代码不直接读 BuildHash/ConfigHash——经本上下文投影。
    /// </summary>
    public sealed class IdentityModule : IClientModule, IClientModuleGraph
    {
        public string Name => "Identity";

        public IReadOnlyList<string> DependsOn => Array.Empty<string>();

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            var build = new BuildIdentity(
                BuildHash.Value,
                UnityEngine.Application.version,
                UnityEngine.Application.platform.ToString(),
                channel: string.Empty,
                BridgeCapabilitySet.Default().ApiVersion);

            // 0 = 未声明（协议/Sim 轴尚无单源常量；Off 下不参与数值比较）。
            var schema = new SchemaIdentity(
                protocolVersion: 0,
                simVersion: 0,
                configSchemaVersion: 0,              // Luban 表未声明 schema 版本；0 = 未声明
                saveSchemaVersion: ActivationRecord.CurrentSchemaVersion);

            context.Put(new IdentityContext(build, schema));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
