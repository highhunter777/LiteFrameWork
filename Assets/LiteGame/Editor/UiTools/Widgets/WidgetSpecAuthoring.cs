using System;
using System.Collections.Generic;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Binding;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Widgets
{
    /// <summary>
    /// WidgetSpec 的作者面引擎（《UI 系统总设计》§6.5 插槽容器约定）：从模板节点发现可声明的插槽，
    /// 供 Inspector 一键填充。遍历复用 <see cref="UiBindSurfaceWalker"/>（与绑定 hash/契约同一归属口径）。
    /// </summary>
    public static class WidgetSpecAuthoring
    {
        /// <summary>插槽容器的命名前缀（设计 §6.5 的 `Cot_*` 约定 + 词表的 `Slot_*`）。</summary>
        private static readonly string[] SlotPrefixes = { "Cot_", "Slot_" };

        /// <summary>该节点名是否为插槽容器（`Cot_*`/`Slot_*`）。</summary>
        public static bool IsSlotName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string prefix in SlotPrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>发现根下的插槽容器（排除 `Clone_` 子树、模板内部件、嵌套 `BindRoot` 子树）。</summary>
        public static List<WidgetSlotSpec> DiscoverSlots(GameObject root)
        {
            var slots = new List<WidgetSlotSpec>();
            if (root == null) return slots;

            foreach (UiSurfaceNode node in UiBindSurfaceWalker.Walk(root))
            {
                if (node.IsRoot || node.InCloneScope || node.InsideTemplate || node.InsideNestedBindRoot) continue;
                if (!IsSlotName(node.Node.name)) continue;
                slots.Add(new WidgetSlotSpec
                {
                    Name = node.Path,
                    Required = true,
                    Description = string.Empty,
                });
            }
            return slots;
        }

        /// <summary>把发现结果并入声明：只补缺失项（按 Name），已有条目保留作者填写；返回新增条数。</summary>
        public static int ApplyDiscoveredSlots(WidgetSpec spec, GameObject root)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            List<WidgetSlotSpec> discovered = DiscoverSlots(root);
            if (discovered.Count == 0) return 0;

            var merged = new List<WidgetSlotSpec>(spec.Slots ?? Array.Empty<WidgetSlotSpec>());
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (WidgetSlotSpec slot in merged)
                if (slot != null && !string.IsNullOrEmpty(slot.Name)) known.Add(slot.Name);

            int added = 0;
            foreach (WidgetSlotSpec slot in discovered)
            {
                if (!known.Add(slot.Name)) continue;
                merged.Add(slot);
                added++;
            }

            if (added > 0) spec.Slots = merged.ToArray();
            return added;
        }
    }
}
