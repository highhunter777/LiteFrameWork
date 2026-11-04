using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 进程壳 → 游戏装配的入口契约（B1.5）：AOT 侧只认本接口，
    /// 具体实现（<c>LiteGame.ClientEntry</c>）经 <see cref="ClientEntryLoader"/> 反射装载。
    /// 调用序固定：<see cref="Configure"/>（宿主建好、初始化前）→ 宿主初始化 → <see cref="StartAsync"/>（Unity Start 后）。
    /// </summary>
    public interface IClientEntry
    {
        /// <summary>登记装配段与组合根（在 <c>ClientHost.InitializeAsync</c> 之前调用一次）。</summary>
        /// <param name="host">场景引导件创建的宿主。</param>
        /// <param name="hostObject">引导件所在 GameObject（开发调参件挂载点；实现方可只读保存）。</param>
        void Configure(ClientHost host, GameObject hostObject);

        /// <summary>
        /// 宿主初始化成功后的启动续体（开发调参注入、探针武装、流程机启动）。
        /// 由 <see cref="GameEntry.Start"/> 调用；异常由引导件统一转错误 UI。
        /// </summary>
        UniTask StartAsync(CancellationToken ct);
    }
}
