using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteView;
using UnityEngine;
using LiteClient;

namespace LiteGame
{
    /// <summary>⑨ 表现壳：实体/声音/世界 VFX + 相机服务。
    /// 加载口统一绑 <see cref="PrefabLeaseCache"/>（租约持有，避免悬空引用）；Shutdown 释放面
    /// （音频全局停止 + 租约归零）。
    ///
    /// **相机（Cinemachine）**：本模块建**相机服务**并把主相机登记进上下文，
    /// 但**不要求模块初始化期就已经有虚拟相机**——vcam 是场景对象，而启动场景（Test.unity）
    /// 里不一定配、训练场那类玩法场景才配。服务自己会在场景切换后重新解析
    /// （见 <see cref="CinemachineCameraService"/> 的"场景切换后重新解析"段），
    /// 所以装配期只建服务、不做存在性裁决：
    /// - 有 vcam → 服务解析并接线（对局相机可用）；
    /// - 没 vcam → 记一条**装配缺口**日志，服务保持"未解析"，等切到带 vcam 的场景自动接上。
    ///
    /// 主相机一律本模块保证（场景没配就建一台默认的）：**它是投影面**，与"构图归 vcam 配置"
    /// 是两件事——没有 Camera 组件连画面都没有，那不是构图问题。</summary>
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
            context.Put(_prefabs);                               // 租约缓存驻留面（HeldLocations 诊断/内存实测读）
            context.Put(_entities);
            context.Put(_audio);
            context.Put(_vfxService);

            context.Put(EnsureCamera());                         // 主相机（⑩ 输入服务做瞄准解算要用）
            _camera = new CinemachineCameraService();            // 相机服务常驻；vcam 在场景里，由服务按需解析
            context.Put<ICameraService>(_camera);                // 对局流程（ContainerModule）经端口取

            if (!_camera.HasCamera)
                Log.Info($"[Camera] 启动场景暂无虚拟相机：{_camera.LastResolveReason}——" +
                         "切到配了 vcam 的场景后会自动接线（相机配置归场景）", "Camera");
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
