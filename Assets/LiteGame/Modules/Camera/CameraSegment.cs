using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// ② 相机薄段：主相机保证 + 相机服务（Cinemachine 实现由组合根注入，本段只接工厂委托）。
    /// </summary>
    public sealed class CameraSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly Func<ICameraService> _createCamera;

        private ICameraService _camera;
        private GameObject _cameraHost;

        public CameraSegment(Func<ICameraService> createCamera)
            => _createCamera = createCamera ?? throw new ArgumentNullException(nameof(createCamera));

        public string Name => "Camera";

        /// <summary>本段不消费任何模块产物（主相机/相机服务只依赖引擎与注入的工厂委托）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
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

        /// <summary>关闭面：相机资源已随 <see cref="SegmentContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 保证有一台主相机：场景里有（含 tag MainCamera）就用它，没有才建一台默认的。
        /// **只保证"有投影面"**——构图/档位/跟随全部归 vcam 的场景配置（本段不写档位常量）。
        /// </summary>
        private UnityEngine.Camera EnsureCamera()
        {
            UnityEngine.Camera scene = UnityEngine.Camera.main;
            if (scene != null) return scene;

            _cameraHost = new GameObject("[MainCamera]");
            var created = _cameraHost.AddComponent<UnityEngine.Camera>();
            _cameraHost.tag = "MainCamera";
            return created;
        }
    }
}
