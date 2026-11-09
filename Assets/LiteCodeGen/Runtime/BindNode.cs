//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
// 思路源自 QFramework CodeGenKit/UIKit (MIT License, Copyright (c) 2016~2025 liangxiegame)
// 本文件不依赖任何第三方框架,仅依赖 UnityEngine。
//------------------------------------------------------------
using UnityEngine;

namespace LiteCodeGen
{
    /// <summary>
    /// 挂在需要生成引用的节点上,用于标记"该节点要被生成进代码里"。
    /// 会自动检测节点上的组件类型(优先级清单对齐 QFramework AbstractBind)。
    /// </summary>
    [AddComponentMenu("LiteCodeGen/BindNode 标记")]
    public class BindNode : MonoBehaviour
    {
        /// <summary>生成到代码里的备注(可选)</summary>
        [Header("BindNode 标记")]
        public string Comment = string.Empty;

        /// <summary>
        /// 手动指定生成的成员类型(组件完整类型名)。
        /// 留空 = 自动检测;"GameObject" = 引用整个 GameObject;其余填组件完整类型名,如 "UnityEngine.UI.Button"。
        /// </summary>
        [Tooltip("留空=自动检测组件;填 GameObject 引用整个物体;或填组件全名(如 UnityEngine.UI.Button)")]
        public string CustomTypeName = string.Empty;

        /// <summary>
        /// 运行期受控索引名(M4 §2.4):BindIndexBuilder 按 BindName 构建"名字→控件"索引,
        /// 受控 API(GetControl/OnButtonClick/SetText...)按此名取控件。留空 = 不进运行期索引(仅参与代码生成)。
        /// </summary>
        [Tooltip("运行期索引名(受控 API 按此名取控件);留空 = 不进运行期索引(仅代码生成用)")]
        public string BindName = string.Empty;

        public Transform RootTransform
        {
            get { return transform; }
        }

        /// <summary>
        /// 自动检测到的类型名(完整名)。检测不到任何已知组件时回退 "UnityEngine.Transform"。
        /// </summary>
        public string AutoTypeName
        {
            get { return AutoDetectTypeName(); }
        }

        /// <summary>生成到代码中的最终类型名(手动优先,否则自动)。</summary>
        public string ResolvedTypeName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomTypeName))
                {
                    return CustomTypeName;
                }

                return AutoTypeName;
            }
        }

        /// <summary>
        /// 解析"应当被引用的目标对象"。
        /// 返回 Component 或 null(null 表示该引用指向本 GameObject 本身,由调用方用 gameObject 赋值)。
        /// </summary>
        public Component ResolveTarget()
        {
            if (!string.IsNullOrEmpty(CustomTypeName))
            {
                if (CustomTypeName == "GameObject" || CustomTypeName == "UnityEngine.GameObject")
                {
                    return null;
                }

                var comp = GetComponent(CustomTypeName);
                if (comp != null)
                {
                    return comp;
                }

                // 兜底:尝试去掉命名空间只留类型名
                var lastDot = CustomTypeName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < CustomTypeName.Length - 1)
                {
                    var shortName = CustomTypeName.Substring(lastDot + 1);
                    if (shortName.Length > 0)
                    {
                        comp = GetComponent(shortName);
                        if (comp != null)
                        {
                            return comp;
                        }
                    }
                }

                return null;
            }

            return AutoDetectComponent();
        }

        private string AutoDetectTypeName()
        {
            var comp = AutoDetectComponent();
            if (comp == null)
            {
                return "UnityEngine.Transform";
            }

            if (comp is Transform)
            {
                // 保持与 QF 一致:UI 节点通常带 RectTransform,优先报告 RectTransform
                return comp.GetType() == typeof(RectTransform) ? "UnityEngine.RectTransform" : "UnityEngine.Transform";
            }

            return comp.GetType().FullName;
        }

        private Component AutoDetectComponent()
        {
            var candidate = AutoTypeCandidates;
            for (var i = 0; i < candidate.Length; i++)
            {
                var typeName = candidate[i];
                var comp = GetComponent(typeName);
                if (comp != null)
                {
                    return comp;
                }
            }

            // 特殊:RectTransform 与 Transform
            var rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                return rect;
            }

            var tr = GetComponent<Transform>();
            if (tr != null)
            {
                return tr;
            }

            return null;
        }

        // 对齐 QFramework AbstractBind.GetDefaultComponentName 的优先级清单(去掉 NGUI/骨骼动画类)。
        private static readonly string[] AutoTypeCandidates =
        {
            "UnityEngine.UI.ScrollRect",
            "UnityEngine.UI.InputField",

            "TMPro.TMP_InputField",
            "TMP.TextMeshProUGUI",
            "TMPro.TextMeshProUGUI",
            "TMPro.TextMeshPro",

            "UnityEngine.UI.Dropdown",
            "UnityEngine.UI.Button",
            "UnityEngine.UI.Text",
            "UnityEngine.UI.RawImage",
            "UnityEngine.UI.Toggle",
            "UnityEngine.UI.Slider",
            "UnityEngine.UI.Scrollbar",
            "UnityEngine.UI.Image",
            "UnityEngine.UI.ToggleGroup",

            "Rigidbody",
            "Rigidbody2D",
            "BoxCollider2D",
            "BoxCollider",
            "CircleCollider2D",
            "SphereCollider",
            "MeshCollider",
            "Collider",
            "Collider2D",

            "Animator",
            "Canvas",
            "Camera",

            "MeshRenderer",
            "SpriteRenderer",
            "ParticleSystem",
            "ParticleSystemRenderer"
        };
    }
}
