using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.WriteBack
{
    /// <summary>差异来源——决定勾选默认态与展示口径（判据经实测校准，见施工记录探针证据）。</summary>
    public enum PrefabWriteBackOrigin
    {
        /// <summary>Unity **显式记录**在案的改动（<c>GetPropertyModifications</c> 里的非默认记录项）
        /// ——**默认勾选**。</summary>
        RecordedOverride = 0,

        /// <summary>其余数值差异：脚本/驱动每帧写出的当前值**和** Unity 只维护"默认记录"的路径
        /// （RectTransform 的位置/尺寸/锚点即属此类——实测 <c>IsDefaultOverride</c> 恒为真，
        /// 分不出 Inspector 改的还是驱动写的）。**默认不勾**——数据值/动态位置归驱动常量或表，
        /// 勾选前须核对「实例值 → 资产值」（《UI工作台设计》§2）。</summary>
        RuntimeDiff = 1,
    }

    /// <summary>不可回写原因——显式列出、不静默跳过（与《UI编辑器工具专项设计》§5"Unsupported 显式列出"同口径）。</summary>
    public enum PrefabWriteBackBlock
    {
        None = 0,

        /// <summary>实例上新加的组件（源 prefab 无对应件）——结构面，归 Prefab Stage / Unity 原生 Overrides。</summary>
        AddedComponent,

        /// <summary>引用指向场景对象——写进 prefab 资产会成为坏引用（场景引用不得烤进资产）。</summary>
        SceneReference,

        /// <summary>序列化类型不在支持面（AnimationCurve/Gradient/托管引用等）。</summary>
        UnsupportedType,

        /// <summary>层级/属性结构不一致（新增子对象、属性路径只在单侧存在）。</summary>
        StructureDiff,
    }

    /// <summary>一条可勾选差异（**纯数据**——属性句柄在每次扫描/写入时现取，避免跨域重载后悬空）。</summary>
    public sealed class PrefabWriteBackItem
    {
        public UnityEngine.Object InstanceObject;   // 实例侧对象（GameObject 或组件）
        public UnityEngine.Object SourceObject;     // 源资产侧对象（= 写入目标；嵌套件为内层 prefab 内对象）
        public string AssetPath;                    // 写入目标所属 prefab 资产
        public string OwnerLabel;                   // 展示：层级路径 · 组件类型
        public string PropertyPath;                 // 序列化属性路径（SerializedProperty 口径）
        public string InstanceValue;                // 实例当前值（展示）
        public string SourceValue;                  // 资产当前值（展示）
        public bool IsArraySize;                    // 该条 = 数组长度（写入先于元素值）
        public PrefabWriteBackOrigin Origin;
        public PrefabWriteBackBlock Block;
        public bool Selected;

        public bool CanApply => Block == PrefabWriteBackBlock.None;
    }

    /// <summary>扫描结果（差异清单 + 结构面说明 + 失败原因）。</summary>
    public sealed class PrefabWriteBackScanResult
    {
        public string InstanceRootPath;
        public string AssetPath;
        public readonly List<PrefabWriteBackItem> Items = new List<PrefabWriteBackItem>();
        public readonly List<string> Notes = new List<string>();   // 结构面/不可枚举面说明（不静默）
        public string Error;                                       // 非空 = 扫描失败

        public int ApplicableCount
        {
            get
            {
                int n = 0;
                foreach (var item in Items) if (item.CanApply) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// prefab 改动回写：扫描器（《UI工作台设计》§2「回写」工具）——枚举"实例 vs 源 prefab 资产"的**逐属性差异**，
    /// 附 Unity 显式 override 标记与不可回写原因；写入由 <see cref="PrefabWriteBackApply"/> 承担。
    ///
    /// 两条准则：
    /// ①**显式 override 优先**：Unity 记录的 override 默认勾选（= "验收＝Game 视图、编辑＝Inspector"的产物）；
    ///    仅数值差异（驱动写面）列出但默认不勾——设计原文"驱动每帧写面天然屏蔽"由此落地。
    /// ②**结构面不动**：新增组件/新对象/层级差异只登记说明，直写属性会破坏 prefab 结构
    ///    （结构面归 Prefab Stage 与 Unity 原生 Overrides）。
    /// </summary>
    public static class PrefabWriteBackScan
    {
        /// <summary>跳过面：编辑器内部字段、prefab 身份字段、结构数组——都不是调参面，
        /// 直写会破坏引用或结构（结构差异在 <see cref="VisitNotes"/> 里说明）。</summary>
        private static readonly HashSet<string> s_skipRoots = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Script", "m_Component", "m_Children", "m_Father",
        };

        public static PrefabWriteBackScanResult Scan(GameObject target)
        {
            var result = new PrefabWriteBackScanResult();
            if (target == null) { result.Error = "未选择对象"; return result; }

            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(target);
            if (root == null)
            {
                result.Error = "选中对象不是 prefab 实例——运行时 Instantiate 的副本没有 prefab 关联。"
                    + "请在场景中放置实例（或在 Prefab 模式下调整）后再回写。";
                return result;
            }

            var rootSource = PrefabUtility.GetCorrespondingObjectFromSource(root);
            result.InstanceRootPath = HierarchyPath(root, (Transform)null);
            result.AssetPath = rootSource != null
                ? AssetDatabase.GetAssetPath(rootSource)
                : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);

            var recorded = CollectRecordedPaths(root);

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                var owner = HierarchyPath(root, transform);
                var sourceGo = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (sourceGo == null)
                {
                    result.Notes.Add($"结构差异：{owner} 在源 prefab 无对应对象（新对象/新子层不自动回写——请用 Overrides 的 Apply Object）");
                    continue;
                }

                ComparePair(go, sourceGo, owner + " · GameObject", AssetPathOf(sourceGo, result.AssetPath),
                    recorded, result);

                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null)
                    {
                        result.Notes.Add($"缺失脚本：{owner} 上有脚本引用丢失（GUID 解析不到类型）");
                        continue;
                    }
                    string label = owner + " · " + comp.GetType().Name;
                    if (PrefabUtility.IsAddedComponentOverride(comp))
                    {
                        result.Items.Add(new PrefabWriteBackItem
                        {
                            InstanceObject = comp,
                            SourceObject = null,
                            AssetPath = result.AssetPath,
                            OwnerLabel = label,
                            PropertyPath = "(整个组件)",
                            InstanceValue = "实例新增",
                            SourceValue = "源无此件",
                            Block = PrefabWriteBackBlock.AddedComponent,
                        });
                        continue;
                    }
                    var sourceComp = PrefabUtility.GetCorrespondingObjectFromSource(comp);
                    if (sourceComp == null)
                    {
                        result.Items.Add(new PrefabWriteBackItem
                        {
                            InstanceObject = comp,
                            SourceObject = null,
                            AssetPath = result.AssetPath,
                            OwnerLabel = label,
                            PropertyPath = "(整个组件)",
                            InstanceValue = "无源对象",
                            SourceValue = "—",
                            Block = PrefabWriteBackBlock.StructureDiff,
                        });
                        continue;
                    }
                    ComparePair(comp, sourceComp, label, AssetPathOf(sourceComp, result.AssetPath), recorded, result);
                }
            }
            return result;
        }

        /// <summary>递归收集"叶子值"（数组长度叶 + 值叶），键 = <see cref="SerializedProperty.propertyPath"/>。
        /// 两侧结构一致时路径一一对应——写入端按同路径现取，不依赖 FindProperty 的路径语义。</summary>
        internal static Dictionary<string, SerializedProperty> CollectLeaves(SerializedObject so)
        {
            var leaves = new Dictionary<string, SerializedProperty>();
            var iterator = so.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (IsSkippedPath(iterator.propertyPath)) continue;
                Visit(iterator.Copy(), leaves);
            }
            return leaves;
        }

        private static void Visit(SerializedProperty prop, Dictionary<string, SerializedProperty> leaves)
        {
            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                if (prop.isArray)
                {
                    leaves[prop.propertyPath] = prop.Copy();                       // 数组长度叶（.arraySize）
                    for (int i = 0; i < prop.arraySize; i++)
                        Visit(prop.GetArrayElementAtIndex(i).Copy(), leaves);
                }
                else
                {
                    var child = prop.Copy();
                    var end = child.GetEndProperty();
                    bool enter = true;
                    while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                    {
                        enter = false;
                        if (IsSkippedPath(child.propertyPath)) continue;
                        Visit(child.Copy(), leaves);
                    }
                    if (!prop.hasChildren && !prop.isArray) leaves[prop.propertyPath] = prop.Copy();   // 无子 Generic = 不支持型叶（显式列出）
                }
                return;
            }
            leaves[prop.propertyPath] = prop.Copy();
        }

        private static void ComparePair(UnityEngine.Object instance, UnityEngine.Object source, string ownerLabel,
            string assetPath, HashSet<string> recorded, PrefabWriteBackScanResult result)
        {
            var leavesInstance = CollectLeaves(new SerializedObject(instance));
            var leavesSource = CollectLeaves(new SerializedObject(source));
            foreach (var pair in leavesInstance)
            {
                string path = pair.Key;
                if (!leavesSource.TryGetValue(path, out var sourceProp))
                {
                    result.Notes.Add($"结构差异：{ownerLabel} 的 {Short(path)} 在源无对应属性（跳过）");
                    continue;
                }
                var instanceProp = pair.Value;
                if (ValuesEqual(instanceProp, sourceProp)) continue;

                bool isArraySize = instanceProp.isArray;
                var block = PrefabWriteBackBlock.None;
                if (!isArraySize && instanceProp.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var referenced = instanceProp.objectReferenceValue;
                    if (referenced != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(referenced)))
                        block = PrefabWriteBackBlock.SceneReference;
                }
                else if (!isArraySize && instanceProp.propertyType == SerializedPropertyType.Generic)
                {
                    block = PrefabWriteBackBlock.UnsupportedType;
                }

                var origin = recorded.Contains(source.GetInstanceID() + "|" + path)
                    || recorded.Contains(instance.GetInstanceID() + "|" + path)      // target 实测为源资产侧对象；两侧都试更稳
                    ? PrefabWriteBackOrigin.RecordedOverride
                    : PrefabWriteBackOrigin.RuntimeDiff;
                result.Items.Add(new PrefabWriteBackItem
                {
                    InstanceObject = instance,
                    SourceObject = source,
                    AssetPath = assetPath,
                    OwnerLabel = ownerLabel,
                    PropertyPath = path,
                    InstanceValue = Describe(instanceProp),
                    SourceValue = Describe(sourceProp),
                    IsArraySize = isArraySize,
                    Origin = origin,
                    Block = block,
                    Selected = block == PrefabWriteBackBlock.None && origin == PrefabWriteBackOrigin.RecordedOverride,
                });
            }
        }

        /// <summary>Unity 记录的**显式**改动路径集：键 = 记录项 target 对象 instanceID + 属性路径。
        /// 判据（实测校准，见施工记录探针）：①实例根存在非默认 override
        /// （<c>HasPrefabInstanceAnyOverrides(root, false)</c>——无则直接空集）；②<see cref="PrefabUtility.IsDefaultOverride"/>
        /// 为假的记录项；③**target 实测是源资产侧对象**（探针：instanceID 与实例侧不一致）——故按源对象 ID 命中，
        /// 两侧 ID 都试（比较时 <c>source</c> 即源资产对象）。
        /// **实测结论**：Image 等组件的记录式改动产生非默认记录项（可认）；RectTransform 的位置/尺寸/锚点
        /// 路径在实例创建时即被登记为默认项、**此后恒标默认**（Inspector 改的认不出）⇒ 落"数值差异"侧，
        /// 由人工核对勾选。记录常落在子属性上（如 <c>m_Color.b</c>），故每条路径的全部前缀一并登记。</summary>
        private static HashSet<string> CollectRecordedPaths(GameObject root)
        {
            var keys = new HashSet<string>();
            if (!PrefabUtility.HasPrefabInstanceAnyOverrides(root, false)) return keys;
            var mods = PrefabUtility.GetPropertyModifications(root);
            if (mods == null) return keys;
            foreach (var mod in mods)
            {
                if (mod == null || mod.target == null || string.IsNullOrEmpty(mod.propertyPath)) continue;
                if (PrefabUtility.IsDefaultOverride(mod)) continue;
                string prefix = mod.target.GetInstanceID() + "|";
                string path = mod.propertyPath;
                while (true)
                {
                    keys.Add(prefix + path);
                    int dot = path.LastIndexOf('.');
                    if (dot <= 0) break;
                    path = path.Substring(0, dot);
                }
            }
            return keys;
        }

        private static bool ValuesEqual(SerializedProperty a, SerializedProperty b)
        {
            if (a.isArray) return a.arraySize == b.arraySize;
            return SerializedProperty.DataEquals(a, b);      // 静态比对（本 Unity 版本签名：两侧属性作参）
        }

        internal static string Describe(SerializedProperty p)
        {
            if (p.isArray) return "长度 " + p.arraySize;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.###");
                case SerializedPropertyType.String: return Truncate(p.stringValue);
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex]
                        : p.intValue.ToString();
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString();
                case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                case SerializedPropertyType.Rect: return p.rectValue.ToString();
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString();
                case SerializedPropertyType.Quaternion: return p.quaternionValue.eulerAngles.ToString("0.##");
                case SerializedPropertyType.LayerMask: return "Mask " + p.intValue;
                case SerializedPropertyType.ObjectReference:
                    var o = p.objectReferenceValue;
                    return o == null ? "null" : o.name;
                default: return "(" + p.propertyType + ")";
            }
        }

        private static string Truncate(string s)
            => string.IsNullOrEmpty(s) ? "\"\"" : (s.Length <= 32 ? s : s.Substring(0, 32) + "…");

        private static string Short(string path) => path.Length <= 48 ? path : path.Substring(path.Length - 48);

        private static bool IsSkippedPath(string path)
        {
            int dot = path.IndexOf('.');
            string root = dot > 0 ? path.Substring(0, dot) : path;
            return s_skipRoots.Contains(root);
        }

        private static string AssetPathOf(UnityEngine.Object source, string fallback)
        {
            string path = AssetDatabase.GetAssetPath(source);
            return string.IsNullOrEmpty(path) ? fallback : path;
        }

        internal static string HierarchyPath(GameObject root, Transform transform)
        {
            if (transform == null || transform == root.transform) return root.name;
            var chain = new List<string>();
            for (var cur = transform; cur != null && cur != root.transform; cur = cur.parent) chain.Add(cur.name);
            chain.Reverse();
            var sb = new StringBuilder(root.name);
            foreach (var name in chain) sb.Append('/').Append(name);
            return sb.ToString();
        }
    }
}
