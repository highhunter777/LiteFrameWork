using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UITools.Workbench
{
    /// <summary>
    /// 一条透视声明（《UI 编辑器工具链专项设计》§2.2）：id/显示名/顺序/启用态/能力标签。
    /// 能力标签是“迁移未丢动作”的可测锚点（夹具断言），新增透视或迁移分区时同步维护。
    /// </summary>
    public sealed class UIWorkbenchPerspective
    {
        public UIWorkbenchPerspectiveId Id;
        public string Title;
        public int Order;
        public bool Enabled;
        public string[] Capabilities = new string[0];
    }

    /// <summary>透视清单单源：顺序、显示名与能力标签只在这里定义，窗口与夹具共用。</summary>
    public static class UIWorkbenchPerspectives
    {
        private static readonly UIWorkbenchPerspective[] Items =
        {
            Item(UIWorkbenchPerspectiveId.Page, "页面", 10, "page.list", "page.metadata", "page.scaffold", "page.register"),
            Item(UIWorkbenchPerspectiveId.Widgets, "控件库", 20, "widgets.insert", "widgets.properties"),
            Item(UIWorkbenchPerspectiveId.Binding, "绑定", 30, "binding.edit", "binding.generate", "binding.hash"),
            Item(UIWorkbenchPerspectiveId.Validation, "校验", 50, "validate.narrow", "validate.full"),
        };

        /// <summary>全部透视（P2-1 四个；样式/预览由 P2-3 追加）。</summary>
        public static IReadOnlyList<UIWorkbenchPerspective> All => Items;

        /// <summary>按 id 查找；未知返回 null。</summary>
        public static UIWorkbenchPerspective Find(UIWorkbenchPerspectiveId id)
            => Items.FirstOrDefault(item => item.Id == id);

        /// <summary>全部能力标签（按透视顺序去重），用于夹具断言“映射未丢失”。</summary>
        public static IReadOnlyList<string> AllCapabilities()
            => Items.SelectMany(item => item.Capabilities).Distinct().ToList();

        private static UIWorkbenchPerspective Item(UIWorkbenchPerspectiveId id, string title, int order,
            params string[] capabilities)
            => new UIWorkbenchPerspective
            {
                Id = id,
                Title = title,
                Order = order,
                Enabled = true,
                Capabilities = capabilities,
            };
    }
}
