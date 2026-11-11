using LiteGameNext.Platform.Ui;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>战斗 HUD 视图：只读模型，刷新生命/弹药/击杀；不参与输入与判定。</summary>
    public sealed class BattleHudView : PageBase
    {
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleHud.prefab",
            PageLayer.Overlay, fullScreen: false, PageCachePolicy.DestroyOnClose);

        private BattleHudModel _model;

        public void Bind(BattleHudModel model)
        {
            _model = model;
            if (IsOpen) Refresh();
        }

        protected override void OnShow() => Refresh();

        public void Refresh()
        {
            if (_model == null) return;
            SetText("Txt_Health", _model.Health + " / " + _model.MaxHealth);
            SetText("Txt_Ammo", "AMMO " + _model.Ammo);
            SetText("Txt_Score", "K " + _model.Kills + "  D " + _model.Deaths);
        }

        private bool IsOpen => Handle != null && !Handle.IsClosed;
    }
}
