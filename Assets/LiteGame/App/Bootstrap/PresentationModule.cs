using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;
using UnityEngine;

namespace LiteGame
{
    /// <summary>⑨ 表现壳：实体/声音/世界 VFX + 相机服务。
    /// G1 通用表现批：加载口统一绑 <see cref="PrefabLeaseCache"/>（租约持有——纠正静态门面
    /// "返回前 Release"的悬空引用）；Shutdown 释放面（音频全局停止 + 租约归零）。
    ///
    /// **相机（2026-09-26 Cinemachine 接入）**：本模块负责**主相机与虚拟相机的存在性**
    /// ——场景没配就建一台默认主相机（保证有投影面），虚拟相机交给场景/预制配置
    /// （<see cref="CinemachineCameraService.TryCreateFromScene"/>——**没有就如实报缺口**，
    /// 不在运行期凭空造一台把美术配置掩盖掉）。建好后经 <see cref="IInputService.SetAimCamera"/>
    /// 把相机交给输入服务做瞄准解算（设备源在 ⑩ 才建，故这里是"回填"）。</summary>
    internal sealed class PresentationModule : IClientModule
    {
        private PrefabLeaseCache _prefabs;
        private AudioService _audio;
        private EntityService _entities;
        private VfxService _vfxService;
        private CinemachineCameraService _camera;
        private GameObject _cameraHost;

        public string Name => "Presentation";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
            _prefabs = new PrefabLeaseCache(content);
            _audio = new AudioService();
            _audio.BindClocks(context.Require<IWorldClock>(), context.Require<IUIClock>());   // 淡变分域（§7 时钟表）
            _entities = new EntityService(_prefabs.GetAsync) { HostScope = context.RootScope };   // 实体作用域挂根（宿主退出级联）
            _vfxService = new VfxService(
                loader: _prefabs.GetAsync,
                clock: context.Require<IWorldClock>(),
                catalog: new VfxCatalog(),
                budget: VfxBudget.Default());
            context.Put(_entities);
            context.Put(_audio);
            context.Put(_vfxService);

            Camera camera = EnsureCamera();
            context.Put(camera);                                 // 主相机（⑩ 输入服务做瞄准解算要用）
            if (CinemachineCameraService.TryCreateFromScene(out CinemachineCameraService camService, out string reason))
            {
                _camera = camService;
                context.Put<ICameraService>(_camera);            // 对局流程（ContainerModule）经端口取
            }
            else
            {
                Log.Warning($"[Camera] {reason}——对局相机不可用（画面不动，非崩溃）", "Camera");
            }
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _camera?.Shutdown();                         // 只销毁自建的焦点目标；vcam/Brain 归场景
            _camera = null;
            if (_cameraHost != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_cameraHost);
                else UnityEngine.Object.DestroyImmediate(_cameraHost);
                _cameraHost = null;
            }
            _entities?.Shutdown();                       // 取消在途加载 + 连锁回收活体 + 排空实例池
            _audio?.Shutdown();                          // 总线全局停止 + [Audio] 根销毁
            _vfxService?.Shutdown();                     // 取消在途加载 + 回收活体 + 排空池 + 清缓存引用
            _prefabs?.ReleaseAll();                      // 表现壳 prefab 租约归零（最后：先收使用者再放租约）
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 保证有一台主相机：场景里有（含 tag MainCamera）就用它，没有才建一台默认的。
        /// **只保证"有投影面"**——构图/档位/跟随全部归 vcam 的场景配置（本模块不写档位常量）。
        /// </summary>
        private Camera EnsureCamera()
        {
            Camera scene = Camera.main;
            if (scene != null) return scene;

            _cameraHost = new GameObject("[MainCamera]");
            var created = _cameraHost.AddComponent<Camera>();
            _cameraHost.tag = "MainCamera";
            return created;
        }
    }
}
