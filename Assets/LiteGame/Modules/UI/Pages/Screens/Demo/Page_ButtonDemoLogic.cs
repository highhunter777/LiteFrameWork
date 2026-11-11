using LiteFramework.UI;

namespace LiteGame.Modules.UI.Pages.Screens.Demo
{
    /// <summary>起页练习页：按钮点击计数（复用页计数复位）。</summary>
    public sealed class Page_ButtonDemoLogic : PageLogicBase
    {
        private int _count;

        protected override void OnShow(IUIData data)
        {
            _count = 0;
            Render();
            OnButtonClick("Button_Ping", Ping);
        }

        private void Ping()
        {
            _count++;
            Render();
            Index.Pulse("Button_Ping");
        }

        private void Render() => SetText("Txt_Count", "点击 " + _count + " 次");

        protected override void OnPageHide() => _count = 0;
    }
}
