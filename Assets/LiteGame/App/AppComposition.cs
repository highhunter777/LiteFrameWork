using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// **应用组合根**：适配器的**唯一创建点**（`new` 只出现在本文件里）。
    ///
    /// 为什么需要它：适配器程序集反向引用域程序集（`Adapters.Unity → Modules.Input/Modules.Camera`、
    /// `Adapters.Luban → Modules.Config`），因此域装配段一旦自己 `new` 适配器就成环。
    /// 本类以**工厂委托**的形式把"选哪个实现"留在组合根，段只接委托并在自己的
    /// `InitializeAsync` 里调用——段代码不认识任何具体适配器类型，适配器换了只改这里。
    ///
    /// 命名约定：`FuncXxxFactory()` 返回"够段用"的最小委托，不返回容器键也不返回实现类型。
    /// </summary>
    internal static class AppComposition
    {
        /// <summary>配置服务（表投影）：字节通道经内容租约（代次/引用统一，提取即释放源资产）。</summary>
        internal static Func<IContentService, IConfigService> ConfigServiceFactory()
            => content => new ConfigService((location, token) => LoadBytesViaContentLease(content, location, token));

        /// <summary>意图设备源（New Input System 适配器）：本局规则经读口注入（进房时才有值）。</summary>
        internal static Func<IDevSession, IIntentSource> IntentSourceFactory()
            => dev => new NewInputIntentSource(matchRules: () => dev.MatchRules);

        /// <summary>相机服务（Cinemachine 适配器）：vcam 是场景对象，服务按需解析。</summary>
        internal static Func<ICameraService> CameraServiceFactory()
            => () => new CinemachineCameraService();

        /// <summary>经内容租约读表字节（TextAsset 提取 bytes 后即释放——复制数据不留源引用）。</summary>
        private static async UniTask<byte[]> LoadBytesViaContentLease(IContentService content,
            string location, CancellationToken ct)
        {
            var lease = await content.AcquireAsync<TextAsset>(location, ct: ct);
            try { return lease.Asset.bytes; }
            finally { lease.Dispose(); }
        }
    }
}
