using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 一条透视声明（《UI 编辑器工具链专项设计》§2.2）：id/显示名/顺序/启用态/能力标签。
    /// 能力标签是“迁移未丢动作”的可测锚点（夹具断言），新增透视或迁移分区时同步维护。
    /// </summary>
    public sealed class UiWorkbenchPerspective
    {
        public UiWorkbenchPerspectiveId Id;
        public string Title;
        public int Order;
        public bool Enabled;
        public string[] Capabilities = new string[0];
    }

    /// <summary>透视清单单源：顺序、显示名与能力标签只在这里定义，窗口与夹具共用。</summary>
    public static class UiWorkbenchPerspectives
    {
        private static readonly UiWorkbenchPerspective[] Items =
        {
            Item(UiWorkbenchPerspectiveId.Page, "页面", 10, "page.list", "page.metadata", "page.scaffold", "page.register"),
            Item(UiWorkbenchPerspectiveId.Widgets, "控件库", 20, "widgets.insert", "widgets.properties"),
            Item(UiWorkbenchPerspectiveId.Binding, "绑定", 30, "binding.edit", "binding.generate", "binding.hash"),
            Item(UiWorkbenchPerspectiveId.Contract, "契约", 40, "contract.generate", "contract.diff"),
            Item(UiWorkbenchPerspectiveId.Validation, "校验", 50, "validate.narrow", "validate.full"),
        };

        /// <summary>全部透视（P2-1 五个；样式/预览由 P2-3 追加）。</summary>
        public static IReadOnlyList<UiWorkbenchPerspective> All => Items;

        /// <summary>按 id 查找；未知返回 null。</summary>
        public static UiWorkbenchPerspective Find(UiWorkbenchPerspectiveId id)
            => Items.FirstOrDefault(item => item.Id == id);

        /// <summary>全部能力标签（按透视顺序去重），用于夹具断言“映射未丢失”。</summary>
        public static IReadOnlyList<string> AllCapabilities()
            => Items.SelectMany(item => item.Capabilities).Distinct().ToList();

        private static UiWorkbenchPerspective Item(UiWorkbenchPerspectiveId id, string title, int order,
            params string[] capabilities)
            => new UiWorkbenchPerspective
            {
                Id = id,
                Title = title,
                Order = order,
                Enabled = true,
                Capabilities = capabilities,
            };
    }
}
