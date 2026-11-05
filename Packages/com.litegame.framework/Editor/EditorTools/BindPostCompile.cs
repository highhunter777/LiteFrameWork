//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteCodeGen.EditorTools
{
    /// <summary>
    /// "生成代码→等待编译→自动把脚本挂到根并回填引用"的待处理队列。
    /// 以 EditorPrefs 持久化,脚本编译完成后由 [DidReloadScripts] 一次性处理。
    /// </summary>
    public static class BindPostCompile
    {
        private const string EditorPrefsKey = "LiteCodeGen.BindPendingQueue.v1";

        public static void AddPrefab(string prefabPath, string ns, string className, string[] ancestorNames)
        {
            var item = new BindPendingItem
            {
                Kind = "prefab",
                PrefabPath = prefabPath,
                AncestorNames = ancestorNames,
                Namespace = ns,
                ClassName = className
            };
            Push(item);
        }

        public static void AddScene(string ns, string className, string[] ancestorNames)
        {
            var item = new BindPendingItem
            {
                Kind = "scene",
                AncestorNames = ancestorNames,
                Namespace = ns,
                ClassName = className
            };
            Push(item);
        }

        private static void Push(BindPendingItem item)
        {
            var queue = Load();
            queue.Items.Add(item);
            Save(queue);
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            var queue = Load();
            if (queue.Items.Count == 0)
            {
                return;
            }

            // 先清空,避免失败后反复在 reload 里重入
            Save(new BindPendingQueue());

            foreach (var item in queue.Items)
            {
                try
                {
                    if (item.Kind == "prefab")
                    {
                        ProcessPrefab(item);
                    }
                    else
                    {
                        ProcessScene(item);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[LiteCodeGen] 回填失败 " + item.ClassName + " :\n" + e);
                }
            }
        }

        private static void ProcessPrefab(BindPendingItem item)
        {
            if (string.IsNullOrEmpty(item.PrefabPath))
            {
                Debug.LogError("[LiteCodeGen] prefab 路径为空,无法回填 " + item.ClassName);
                return;
            }

            var contentsRoot = PrefabUtility.LoadPrefabContents(item.PrefabPath);
            if (contentsRoot == null)
            {
                Debug.LogError("[LiteCodeGen] 加载 prefab 失败:" + item.PrefabPath);
                return;
            }

            try
            {
                var targetRoot = ResolveTarget(contentsRoot.transform, item);
                if (targetRoot == null)
                {
                    Debug.LogError("[LiteCodeGen] 在 prefab '" + item.PrefabPath + "' 中找不到目标根节点,请手动点击『回填引用』。");
                    return;
                }

                string error;
                if (!BindSerializer.TryFill(targetRoot.gameObject, item.Namespace, item.ClassName, out error))
                {
                    Debug.LogWarning("[LiteCodeGen] " + error);
                }
            }
            finally
            {
                // 2022.3 没有 PrefabUtility.SavePrefabContents,用 SaveAsPrefabAsset + Unload 组合
                PrefabUtility.SaveAsPrefabAsset(contentsRoot, item.PrefabPath);
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }

            Debug.Log("[LiteCodeGen] 已完成 '" + item.ClassName + "' 生成与回填(Prefab)");
        }

        private static void ProcessScene(BindPendingItem item)
        {
            var scenes = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                scenes.Add(SceneManager.GetSceneAt(i));
            }

            List<Transform> candidates = null;
            foreach (var scene in scenes)
            {
                if (!scene.isLoaded)
                {
                    continue;
                }

                if (candidates == null)
                {
                    candidates = new List<Transform>();
                }

                foreach (var sceneRoot in scene.GetRootGameObjects())
                {
                    CollectCandidates(sceneRoot.transform, item.Namespace, item.ClassName, candidates);
                }
            }

            if (candidates == null || candidates.Count == 0)
            {
                Debug.LogWarning("[LiteCodeGen] 找不到场景对象 '" + item.ClassName + "',请选中根节点手动点击『回填引用』。");
                return;
            }

            Transform target = null;
            if (candidates.Count == 1)
            {
                target = candidates[0];
            }
            else
            {
                // 同名多个:用名字链区分
                foreach (var c in candidates)
                {
                    if (NamesMatch(c, item.AncestorNames))
                    {
                        target = c;
                        break;
                    }
                }

                if (target == null)
                {
                    Debug.LogWarning("[LiteCodeGen] 场景存在多个名为 '" + item.ClassName + "' 的根节点,无法自动回填,请手动选中点击『回填引用』。");
                    return;
                }
            }

            string error;
            if (!BindSerializer.TryFill(target.gameObject, item.Namespace, item.ClassName, out error))
            {
                Debug.LogWarning("[LiteCodeGen] " + error);
            }

            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            Debug.Log("[LiteCodeGen] 已完成 '" + item.ClassName + "' 生成与回填(Scene)");
        }

        private static void CollectCandidates(Transform node, string ns, string className, List<Transform> candidates)
        {
            var rootMark = node.GetComponent<BindRoot>();
            if (rootMark != null)
            {
                var nameOk = string.Equals(rootMark.ScriptName, className, StringComparison.Ordinal);
                var nsOk = string.Equals(string.IsNullOrEmpty(rootMark.Namespace) ? string.Empty : rootMark.Namespace,
                    string.IsNullOrEmpty(ns) ? string.Empty : ns, StringComparison.Ordinal);
                if (nameOk && nsOk)
                {
                    candidates.Add(node);
                    return; // 嵌套根内不再深入
                }
            }

            for (var i = 0; i < node.childCount; i++)
            {
                CollectCandidates(node.GetChild(i), ns, className, candidates);
            }
        }

        private static Transform ResolveTarget(Transform top, BindPendingItem item)
        {
            if (top == null)
            {
                return null;
            }

            // prefab 内容根本身就是目标(最常见)
            var rootMark = top.GetComponent<BindRoot>();
            if (rootMark != null &&
                string.Equals(rootMark.ScriptName, item.ClassName, StringComparison.Ordinal) &&
                string.Equals(Normalize(rootMark.Namespace), Normalize(item.Namespace), StringComparison.Ordinal))
            {
                return top;
            }

            if (item.AncestorNames != null && item.AncestorNames.Length > 0)
            {
                var t = top;
                if (t.name != item.AncestorNames[0])
                {
                    return null;
                }

                for (var i = 1; i < item.AncestorNames.Length && t != null; i++)
                {
                    var child = FindChildByName(t, item.AncestorNames[i]);
                    if (child == null)
                    {
                        return null;
                    }

                    t = child;
                }

                if (t != null)
                {
                    var mark = t.GetComponent<BindRoot>();
                    if (mark != null &&
                        string.Equals(mark.ScriptName, item.ClassName, StringComparison.Ordinal) &&
                        string.Equals(Normalize(mark.Namespace), Normalize(item.Namespace), StringComparison.Ordinal))
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private static bool NamesMatch(Transform node, string[] names)
        {
            if (names == null || names.Length == 0)
            {
                return false;
            }

            // 从 node 向上收集到场景根,与存储的链比对(允许 node 是链上任意一层:统一按链末比)
            var chain = new List<string>();
            var cur = node;
            while (cur != null)
            {
                chain.Add(cur.name);
                cur = cur.parent;
            }

            chain.Reverse();

            if (chain.Count < names.Length)
            {
                return false;
            }

            // 允许 node 位于存储链的更深处(例如存储链是场景根→生成根,node 是生成根)
            var offset = chain.Count - names.Length;
            for (var i = 0; i < names.Length; i++)
            {
                if (chain[offset + i] != names[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static string Normalize(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s;
        }

        private static BindPendingQueue Load()
        {
            if (!EditorPrefs.HasKey(EditorPrefsKey))
            {
                return new BindPendingQueue();
            }

            var json = EditorPrefs.GetString(EditorPrefsKey);
            if (string.IsNullOrEmpty(json))
            {
                return new BindPendingQueue();
            }

            try
            {
                return JsonUtility.FromJson<BindPendingQueue>(json);
            }
            catch
            {
                return new BindPendingQueue();
            }
        }

        private static void Save(BindPendingQueue queue)
        {
            if (queue == null)
            {
                EditorPrefs.DeleteKey(EditorPrefsKey);
                return;
            }

            EditorPrefs.SetString(EditorPrefsKey, JsonUtility.ToJson(queue));
        }
    }

    [Serializable]
    internal sealed class BindPendingItem
    {
        public string Kind;          // "prefab" / "scene"
        public string PrefabPath;    // Kind=prefab 时有效
        public string[] AncestorNames; // 从场景/prefab 根到目标根的名字链
        public string Namespace;
        public string ClassName;
    }

    [Serializable]
    internal sealed class BindPendingQueue
    {
        public List<BindPendingItem> Items = new List<BindPendingItem>();
    }
}
