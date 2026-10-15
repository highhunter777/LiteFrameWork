using LiteGame.Editor.UiTools.Binding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 编排面板的目标解析（《UI编辑器工具专项设计》§5）：把"prefab 资产 / 是否正开 Prefab Stage / 展示根"
    /// 三件判据收成**单源**——绑定标记工具与编排面板共用，不各写一套。
    ///
    /// 展示根语义：目标 prefab 正在 Prefab Stage 打开＝Stage 实例（活体，编辑走 Unity Undo），
    /// 否则＝prefab 资产对象（只读展示）。
    /// </summary>
    public sealed class UiComposeTarget
    {
        public GameObject PrefabAsset { get; private set; }

        public string PrefabPath { get; private set; }

        /// <summary>展示根：Stage 实例或 prefab 资产对象。</summary>
        public GameObject DisplayRoot { get; private set; }

        /// <summary>展示根是否为 Prefab Stage 活体（真＝编辑经 Unity Undo）。</summary>
        public bool ViaStage { get; private set; }

        /// <summary>当前 Prefab Stage 的开根——当且仅当 Stage 打开且其资产正是 <paramref name="prefabPath"/>。</summary>
        public static GameObject StageRootFor(string prefabPath)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || string.IsNullOrEmpty(prefabPath)) return null;
            if (stage.assetPath != prefabPath || stage.prefabContentsRoot == null) return null;
            return stage.prefabContentsRoot;
        }

        /// <summary>解析一个 prefab 资产为编排目标；非 prefab 资产（或空）返回 null。</summary>
        public static UiComposeTarget Resolve(GameObject asset)
        {
            if (asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset)) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            var stageRoot = StageRootFor(path);
            return new UiComposeTarget
            {
                PrefabAsset = asset,
                PrefabPath = path,
                DisplayRoot = stageRoot != null ? stageRoot : asset,
                ViaStage = stageRoot != null,
            };
        }

        /// <summary>
        /// 按校验报告的 <c>node</c> 路径反查节点（面板"定位节点"）：报告口径**含根名**
        /// （<c>Page_X/Cot_A/Txt_B</c>），绑定树口径**不含根**（<c>Cot_A/Txt_B</c>）——
        /// 先按报告原样找，未命中则剥掉首段根名再按树口径找；都不中＝null（报告节点可能属模板实例内部）。
        /// </summary>
        public static Transform FindNodeForReport(GameObject displayRoot, string reportNodePath)
        {
            if (displayRoot == null) return null;
            if (string.IsNullOrEmpty(reportNodePath)) return displayRoot.transform;

            var found = UiBindingRename.FindByPath(displayRoot, reportNodePath);
            if (found != null) return found;

            int slash = reportNodePath.IndexOf('/');
            return slash < 0 ? null : UiBindingRename.FindByPath(displayRoot, reportNodePath.Substring(slash + 1));
        }
    }
}
