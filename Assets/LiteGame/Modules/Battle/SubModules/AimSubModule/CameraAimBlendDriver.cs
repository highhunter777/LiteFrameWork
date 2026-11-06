using LiteGame.Modules.Camera;

namespace LiteGame
{
    /// <summary>
    /// 相机瞄准混合驱动：把"原始瞄准位 + 帧间隔"翻译成 ICameraService.SetAiming 调用。
    /// </summary>
    internal sealed class CameraAimBlendDriver
    {
        private readonly ICameraService _camera;
        private readonly CameraAimBlendGate _gate;

        public CameraAimBlendDriver(ICameraService camera, float blendSeconds)
        {
            _camera = camera;
            _gate = new CameraAimBlendGate(blendSeconds);
        }

        /// <summary>每渲染帧：门控后的混合位幂等下发给相机端口。</summary>
        public void Tick(bool rawAiming, float deltaSeconds)
            => _camera?.SetAiming(_gate.Feed(rawAiming, deltaSeconds));

        /// <summary>复位门并还原相机接管（复活/离场；幂等）。</summary>
        public void Reset()
        {
            _gate.Reset();
            _camera?.SetAiming(false);
        }
    }
}
