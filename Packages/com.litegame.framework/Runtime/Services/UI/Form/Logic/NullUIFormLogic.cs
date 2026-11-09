using System;

namespace LiteFramework.UI
{

    /// <summary>空逻辑（无 C# 逻辑时的占位——前壳可独立运行）。</summary>
    public sealed class NullUIFormLogic : IUIFormLogic
    {
        public static readonly NullUIFormLogic Instance = new NullUIFormLogic();

        private NullUIFormLogic() { }

        public void OnInit(UIForm form, IUIData data) { }
        public void OnShow(IUIData data) { }
        public void OnUpdate(float deltaTime) { }
        public void OnPause() { }
        public void OnCover() { }
        public void OnReveal() { }
        public void OnHide() { }
    }
}
