//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using UnityEditor;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    public static class BindMenus
    {
        // 菜单统一收口到 LiteGame：与 LiteGame/UI 下其他工具（构建/校验控件模板、Widget Demo）同处一级
        private const string RootMenuPath = "LiteGame/UI/绑定/添加 BindRoot（生成根）";
        private const string BindMenuPath = "LiteGame/UI/绑定/添加 BindNode 标记";
        private const string AssetMenuPath = "LiteGame/UI/绑定/生成绑定代码";
        private const string ContextBindPath = "CONTEXT/Transform/LiteGame/添加 BindNode 标记";

        // ---------------- 场景/层级菜单 ----------------

        [MenuItem(RootMenuPath, false, 12)]
        private static void AddRoot()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                return;
            }

            if (go.GetComponent<BindRoot>() != null)
            {
                Debug.LogWarning("[LiteCodeGen] '" + go.name + "' 已存在 BindRoot");
                return;
            }

            Undo.AddComponent<BindRoot>(go);
            Selection.activeGameObject = go;
            Debug.Log("[LiteCodeGen] 已添加 BindRoot,请设置命名空间/输出目录后生成代码");
        }

        [MenuItem(RootMenuPath, true)]
        private static bool ValidateAddRoot()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(BindMenuPath, false, 13)]
        private static void AddBind()
        {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0)
            {
                return;
            }

            var count = 0;
            foreach (var go in gos)
            {
                if (go.GetComponent<BindNode>() == null)
                {
                    Undo.AddComponent<BindNode>(go);
                    count++;
                }
            }

            Debug.Log("[LiteCodeGen] 已为 " + count + " 个节点添加 BindNode 标记");
        }

        [MenuItem(BindMenuPath, true)]
        private static bool ValidateAddBind()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        // ---------------- 组件右键菜单 ----------------

        [MenuItem(ContextBindPath, false, 400)]
        private static void AddBindFromContext(MenuCommand cmd)
        {
            var transform = cmd.context as Transform;
            if (transform == null)
            {
                return;
            }

            if (transform.GetComponent<BindNode>() == null)
            {
                Undo.AddComponent<BindNode>(transform.gameObject);
            }
        }

        // ---------------- Project 面板:对选中的 Prefab 生成 ----------------

        [MenuItem(AssetMenuPath, false, 61)]
        private static void GenerateFromSelectedPrefabs()
        {
            var assets = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            if (assets == null || assets.Length == 0)
            {
                return;
            }

            var generated = 0;
            foreach (var asset in assets)
            {
                if (asset == null)
                {
                    continue;
                }

                if (!PrefabUtility.IsPartOfPrefabAsset(asset))
                {
                    continue;
                }

                var target = FindRootInPrefab(asset);
                if (target == null)
                {
                    Debug.LogWarning("[LiteCodeGen] prefab '" + asset.name + "' 内没有 BindRoot,已跳过");
                    continue;
                }

                if (BindGenerator.Generate(target.gameObject))
                {
                    generated++;
                }
            }

            if (generated > 0)
            {
                Debug.Log("[LiteCodeGen] 共生成 " + generated + " 个绑定脚本");
            }
        }

        [MenuItem(AssetMenuPath, true)]
        private static bool ValidateGenerateFromSelectedPrefabs()
        {
            return Selection.GetFiltered<GameObject>(SelectionMode.Assets).Length > 0;
        }

        private static BindRoot FindRootInPrefab(GameObject asset)
        {
            var root = asset.GetComponent<BindRoot>();
            if (root != null)
            {
                return root;
            }

            // prefab 根上没有时,在子物体里找第一个(一个 prefab 含多个生成根时建议分开)
            var roots = asset.GetComponentsInChildren<BindRoot>(true);
            return roots != null && roots.Length > 0 ? roots[0] : null;
        }
    }
}
