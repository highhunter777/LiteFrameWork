using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// 特效资源加载口（可注入）。装配点绑定 <c>AssetService.LoadAssetAsync&lt;GameObject&gt;</c>。
    /// **为什么用委托而不是直接调 AssetService**：① LiteSim.View 不依赖 LiteGame（防程序集环）
    /// ② EditMode 测试可注入替身，零素材验证。
    /// </summary>
    public delegate UniTask<GameObject> VfxAssetLoader(string location, CancellationToken ct);

    /// <summary>
    /// VFX 服务（《动作与特效设计》§2 /《VFX服务实施指导》§2）：**表现层服务**——
    /// 无玩法状态、不参与判定、不改 Sim；**服务不调用其他服务**（音效由编排方分别调）。
    ///
    /// 只管**世界空间**粒子/prefab（加载/池化/挂点跟随/预算降级）；UI 动效走 <c>UiFx</c>+DOTween（不并入）。
    /// 接缝在 View 观察层（SimView）：命中/暴击/死亡与开火即时反馈；实体视图回收 → <see cref="StopAll"/>（谁挂谁清）。
    /// </summary>
    public interface IVFXService : IModuleStats
    {
        /// <summary>播放：名 → 资源（"命名即引用"）。返回无效句柄 = 被降级跳过/超预算拒绝/名为空。</summary>
        VfxHandle Play(string name, Transform attach, bool follow, float scale = 1f);

        /// <summary>世界位播放（不跟随）：落点固定在世界坐标 <paramref name="position"/>——
        /// 弹着/爆炸这类"打在哪就留在哪"的一次性特效走本口（<see cref="Play"/> 的非跟随形态不带位）。
        /// 返回无效句柄的语义同 <see cref="Play"/>。</summary>
        VfxHandle PlayAt(string name, Vector3 position, float scale = 1f);

        /// <summary>停止并回收（**幂等**：无效句柄、已回收、从未存在都静默 no-op）。</summary>
        void Stop(VfxHandle handle);

        /// <summary>清掉挂在某宿主上的全部特效（**宿主回收点调用**，如实体视图销毁）。</summary>
        void StopAll(Transform attach);
    }
}
