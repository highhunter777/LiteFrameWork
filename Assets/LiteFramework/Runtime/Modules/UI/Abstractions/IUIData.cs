using System;

namespace LiteFramework.UI
{
    /// <summary>
    /// 界面数据契约：ShowAsync / OnInit / OnShow 的传参统一收口到此接口——
    /// 生命周期不裸传 object（编译期类型安全；调用方定义数据记录实现本接口即可）。
    /// </summary>
    public interface IUIData
    {
    }
}
