using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>② 相机薄段：主相机保证 + 相机服务（Cinemachine 实现由组合根注入，本段只接工厂委托）。
    ///
    /// **主相机一律本段保证**（场景没配就建一台默认的）：它是**投影面**，与"构图归 vcam 配置"
    /// 是两件事——没有 Camera 组件连画面都没有，那不是构图问题。
    ///
    /// **虚拟相机（vcam）是场景对象**：装配期只建服务、不做存在性裁决——
    /// - 有 vcam → 服务解析并接线（对局相机可用）；
    /// - 没 vcam → 记一条**装配缺口**日志，服务保持"未解析"，等切到带 vcam 的场景自动接上。
    ///
    /// 位置在 `Ui`/`Input` 之前：启动页要有投影面，输入段的瞄准相机迟绑定也读本段产物。
    /// </summary>
    public sealed class CameraComposition : IClientModule, IClientModuleGraph
    {
        private readonly Func<ICameraService> _createCamera;

        private ICameraService _camera;
        private GameObject _cameraHost;

        public CameraComposition(Func<ICameraService> createCamera)
            => _createCamera = createCamera ?? throw new ArgumentNullException(nameof(createCamera));

        public string Name => "Camera";

        /// <summary>本段不消费任何模块产物（主相机/相机服务只依赖引擎与注入的工厂委托）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败时已建的主相机/服务随 scope 回收。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));

            context.Put(EnsureCamera());                         // 主相机（输入段做瞄准解算、UI 投影面要用）
            _camera = _createCamera();                           // 相机服务常驻；vcam 在场景里，由服务按需解析
            context.Put(_camera);                                // 对局流程经端口取

            if (_camera is IRenderCameraSource cameraSource && !cameraSource.HasCamera)
                Log.Info($"[Camera] 启动场景暂无虚拟相机：{cameraSource.LastResolveReason}——" +
                         "切到配了 vcam 的场景后会自动接线（相机配置归场景）", LogTag.Camera);
            return UniTask.CompletedTask;
        }

        /// <summary>模块资源释放（由模块作用域在关闭/回滚时调用）：先关服务再销毁自建主相机。</summary>
        private void DisposeModuleResources()
        {
            _camera?.Shutdown();                                 // 只销毁自建的焦点目标；vcam/Brain 归场景
            _camera = null;
            if (_cameraHost != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_cameraHost);
                else UnityEngine.Object.DestroyImmediate(_cameraHost);
                _cameraHost = null;
            }
        }

        /// <summary>关闭面：相机资源已随 <see cref="ModuleContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 保证有一台主相机：场景里有（含 tag MainCamera）就用它，没有才建一台默认的。
        /// **只保证"有投影面"**——构图/档位/跟随全部归 vcam 的场景配置（本段不写档位常量）。
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
