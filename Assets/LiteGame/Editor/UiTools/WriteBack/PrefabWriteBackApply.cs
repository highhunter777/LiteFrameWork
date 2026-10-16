using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.WriteBack
{
    /// <summary>
    /// prefab 改动回写：写入端（《UI工作台设计》§2「回写」工具）——把勾选条目的**实例值**逐属性直写
    /// **源 prefab 资产**对象（`SerializedObject` 值拷贝 + `ApplyModifiedProperties`，Unity Undo 覆盖）。
    ///
    /// 为什么不经 `PrefabUtility.ApplyPrefabInstance`（全量）或 `ApplyPropertyOverride`（对象级）：
    /// 设计要的是**逐属性勾选固化**——只要用户点中的那几条，未勾选/被标记不可回写的绝不落盘；
    /// 值拷贝同时对"引用指向场景对象"做显式拒绝（烤进资产会成坏引用）。
    ///
    /// **不回滚实例 override**：固化后实例上仍留着已等值的 override 记录（视觉无差、再扫描不再报差异）——
    /// 需要实例回归干净态时用 Unity 原生 Overrides ▸ Revert（本工具不代做，避免误删用户新增的结构改动）。
    /// **Play 内可用**：写的是资产对象，不需退 Play（设计"Play 内勾选直写"）。
    /// </summary>
    public static class PrefabWriteBackApply
    {
        public sealed class Report
        {
            public int Applied;
            public int Failed;
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Assets = new List<string>();   // 本次写到的 prefab 资产（去重）
            public string Error;                                        // 非空 = 未执行（无可写项等）
        }

        public static Report Apply(IEnumerable<PrefabWriteBackItem> items)
        {
            var report = new Report();
            var selected = new List<PrefabWriteBackItem>();
            foreach (var item in items)
                if (item != null && item.Selected && item.CanApply && item.SourceObject != null) selected.Add(item);
            if (selected.Count == 0) { report.Error = "没有选中可回写项"; return report; }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Prefab 改动回写");

            // 数组长度先行：元素可达性由长度决定（长度叶的路径 = 数组属性路径）
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var item in selected)
                {
                    if ((pass == 0) != item.IsArraySize) continue;
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(item.SourceObject)))
                    {
                        report.Failed++;
                        report.Failures.Add($"{item.OwnerLabel} {item.PropertyPath}: 写入目标不是资产对象");
                        continue;
                    }
                    if (CopyOne(item, out string failure))
                    {
                        report.Applied++;
                        if (!report.Assets.Contains(item.AssetPath)) report.Assets.Add(item.AssetPath);
                    }
                    else
                    {
                        report.Failed++;
                        report.Failures.Add(failure);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>单条：两侧按路径现取（结构变更后旧句柄一律作废——重扫由调用方负责）。</summary>
        private static bool CopyOne(PrefabWriteBackItem item, out string failure)
        {
            failure = null;
            var instanceObject = new SerializedObject(item.InstanceObject);      // 句柄随 SerializedObject 存活——持引用不放手
            var leavesInstance = PrefabWriteBackScan.CollectLeaves(instanceObject);
            if (!leavesInstance.TryGetValue(item.PropertyPath, out var source))
            {
                failure = $"{item.OwnerLabel} {item.PropertyPath}: 实例侧属性已不存在（结构变了——重扫）";
                return false;
            }

            var assetObject = new SerializedObject(item.SourceObject);
            var leavesAsset = PrefabWriteBackScan.CollectLeaves(assetObject);
            if (!leavesAsset.TryGetValue(item.PropertyPath, out var target))
            {
                failure = $"{item.OwnerLabel} {item.PropertyPath}: 资产侧属性已不存在（结构变了——重扫）";
                return false;
            }

            if (!CopyValue(source, target))
            {
                failure = $"{item.OwnerLabel} {item.PropertyPath}: 类型不支持（{source.propertyType}）或引用不可固化";
                return false;
            }

            assetObject.ApplyModifiedProperties();          // 含 Undo 记录（资产对象）
            EditorUtility.SetDirty(item.SourceObject);
            return true;
        }

        /// <summary>值拷贝。数组 = 长度（元素值各自成条）；对象引用拒绝场景对象（资产不得引用场景）。</summary>
        internal static bool CopyValue(SerializedProperty source, SerializedProperty target)
        {
            if (source.isArray) { target.arraySize = source.arraySize; return true; }
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    target.intValue = source.intValue; return true;
                case SerializedPropertyType.Boolean: target.boolValue = source.boolValue; return true;
                case SerializedPropertyType.Float: target.floatValue = source.floatValue; return true;
                case SerializedPropertyType.String: target.stringValue = source.stringValue; return true;
                case SerializedPropertyType.Color: target.colorValue = source.colorValue; return true;
                case SerializedPropertyType.Vector2: target.vector2Value = source.vector2Value; return true;
                case SerializedPropertyType.Vector3: target.vector3Value = source.vector3Value; return true;
                case SerializedPropertyType.Vector4: target.vector4Value = source.vector4Value; return true;
                case SerializedPropertyType.Vector2Int: target.vector2IntValue = source.vector2IntValue; return true;
                case SerializedPropertyType.Vector3Int: target.vector3IntValue = source.vector3IntValue; return true;
                case SerializedPropertyType.Rect: target.rectValue = source.rectValue; return true;
                case SerializedPropertyType.RectInt: target.rectIntValue = source.rectIntValue; return true;
                case SerializedPropertyType.Bounds: target.boundsValue = source.boundsValue; return true;
                case SerializedPropertyType.BoundsInt: target.boundsIntValue = source.boundsIntValue; return true;
                case SerializedPropertyType.Quaternion: target.quaternionValue = source.quaternionValue; return true;
                case SerializedPropertyType.ObjectReference:
                    var referenced = source.objectReferenceValue;
                    if (referenced != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(referenced))) return false;
                    target.objectReferenceValue = referenced;
                    return true;
                default:
                    return false;   // AnimationCurve/Gradient/托管引用等——扫描面已显式标记，不静默
            }
        }
    }
}
