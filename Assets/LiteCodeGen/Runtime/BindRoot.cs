//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
// 思路源自 QFramework CodeGenKit/UIKit (MIT License, Copyright (c) 2016~2025 liangxiegame)
// 本文件不依赖任何第三方框架,仅依赖 UnityEngine。
//------------------------------------------------------------
using UnityEngine;

namespace LiteCodeGen
{
    /// <summary>
    /// 绑定生成"根标记"。
    /// 挂在生成根节点上,记录生成配置(命名空间/脚本名/脚本目录/基类),并提供"生成代码"入口。
    /// 一个根节点 + 若干子节点上的 BindNode 标记 = 一次生成的最小单位。
    /// </summary>
    [AddComponentMenu("LiteCodeGen/BindRoot(根标记)")]
    public class BindRoot : MonoBehaviour
    {
        [Header("生成配置")]
        [Tooltip("要生成的类名(建议大写开头)。留空默认取根节点 GameObject 名")]
        public string ScriptName = string.Empty;

        [Tooltip("生成脚本的命名空间;留空则不生成命名空间")]
        public string Namespace = string.Empty;

        [Tooltip("脚本输出目录(相对工程,如 Assets/Scripts/Generated)。必须位于 Assets 下")]
        public string ScriptsFolder = "Assets/Scripts/Generated";

        [Tooltip("主类(Xxx.cs,手动逻辑文件)的基类完整名,如 MyGame.MyBaseComponent;留空默认 MonoBehaviour")]
        public string BaseClassFullName = string.Empty;

        [Tooltip("Designer 文件是否生成 CollectControls 登记代码(逐字段调用基类 RegisterControl)。仅当基类提供 RegisterControl(string, Component) 时勾选;默认关闭,勾选后 Designer 才与运行期标记索引汇合")]
        public bool EmitRegisterControl = false;

        /// <summary>在编辑器中已生成过的类型名(用于编译完成后自动把组件挂到根上并回填引用)</summary>
        [HideInInspector] public string LastGeneratedClassName = string.Empty;

        /// <summary>在编辑器中已生成过的命名空间(用于回填时定位类型)</summary>
        [HideInInspector] public string LastGeneratedNamespace = string.Empty;

        private void Reset()
        {
            ScriptName = gameObject.name;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(ScriptName))
            {
                ScriptName = gameObject.name;
            }
        }
    }
}
