//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    /// <summary>生成主流程:搜索标记 → 写文件 → 排入"编译后回填"队列。</summary>
    public static class BindGenerator
    {
        public static bool Generate(GameObject rootObject)
        {
            try
            {
                return DoGenerate(rootObject);
            }
            catch (Exception e)
            {
                Debug.LogError("[LiteCodeGen] 生成失败:\n" + e);
                return false;
            }
        }

        private static bool DoGenerate(GameObject rootObject)
        {
            if (rootObject == null)
            {
                Debug.LogError("[LiteCodeGen] 根节点为空");
                return false;
            }

            var root = rootObject.GetComponent<BindRoot>();
            if (root == null)
            {
                Debug.LogError("[LiteCodeGen] 根节点缺少 BindRoot 组件,请先添加");
                return false;
            }

            // 1. 收集标记并校验
            var errors = new List<string>();
            var marks = BindSearch.Search(rootObject.transform, errors);

            var className = root.ScriptName == null ? string.Empty : root.ScriptName.Trim();
            string nameReason;
            if (!BindSearch.IsValidIdentifier(className, out nameReason))
            {
                errors.Add("脚本名 '" + className + "' 非法:" + nameReason);
            }

            if (errors.Count > 0)
            {
                Debug.LogError("[LiteCodeGen] 生成中止:\n" + string.Join("\n", errors.ToArray()));
                return false;
            }

            var ns = root.Namespace == null ? string.Empty : root.Namespace.Trim();
            var folder = NormalizeSlash(root.ScriptsFolder).Trim('/');
            if (string.IsNullOrEmpty(folder))
            {
                folder = "Assets/Scripts";
            }

            if (!folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                Debug.LogError("[LiteCodeGen] 脚本目录必须以 Assets 开头(当前:" + folder + ")");
                return false;
            }
            string policyError;
            if (BindOutputPolicy.IsForbidden(folder, out policyError))
            {
                Debug.LogError("[LiteCodeGen] " + policyError);
                return false;
            }

            // 2. 写文件
            var mainPath = folder + "/" + className + ".cs";
            var designerPath = folder + "/" + className + ".Designer.cs";

            var mainContent = BindTemplates.BuildMainFile(className, ns, root.BaseClassFullName);
            var designerContent = BindTemplates.BuildDesignerFile(className, ns, marks, root.EmitRegisterControl);

            EnsureFolderExists(folder);

            var mainChanged = BindFileWriter.WriteMainIfMissing(mainPath, mainContent);
            var designerChanged = BindFileWriter.WriteIfChanged(designerPath, designerContent);

            root.LastGeneratedClassName = className;
            root.LastGeneratedNamespace = ns;
            EditorUtility.SetDirty(rootObject);

            if (mainChanged)
            {
                AssetDatabase.ImportAsset(mainPath);
            }

            if (designerChanged)
            {
                AssetDatabase.ImportAsset(designerPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefabPath = GetPrefabTargetPath(rootObject);
            var ancestorChain = BuildAncestorChain(rootObject.transform);

            if (mainChanged || designerChanged)
            {
                // 3a. 内容有变化:等编译完成后自动回填
                if (prefabPath != null)
                {
                    BindPostCompile.AddPrefab(prefabPath, ns, className, ancestorChain);
                }
                else
                {
                    BindPostCompile.AddScene(ns, className, ancestorChain);
                }

                CompilationPipeline.RequestScriptCompilation();

                Debug.Log("[LiteCodeGen] 已生成 " + className + ",编译完成后自动回填引用。\n  " + mainPath + "\n  " +
                          designerPath);
            }
            else
            {
                // 3b. 内容没变化:直接回填
                string error;
                var ok = prefabPath != null
                    ? BindSerializer.TryFillPrefabAsset(prefabPath, ancestorChain, ns, className, out error)
                    : BindSerializer.TryFill(rootObject, ns, className, out error);

                if (ok)
                {
                    Debug.Log("[LiteCodeGen] 内容未变化,已直接回填引用: " + className);
                }
                else
                {
                    Debug.LogWarning("[LiteCodeGen] " + error);
                }
            }

            // 首次生成逻辑文件时打开它,方便直接写逻辑
            if (mainChanged)
            {
                var mainAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(mainPath);
                if (mainAsset != null)
                {
                    AssetDatabase.OpenAsset(mainAsset);
                }
            }

            return true;
        }

        // ---------------- 工具 ----------------

        private static string NormalizeSlash(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Replace('\\', '/');
        }

        private static void EnsureFolderExists(string assetsRelativeFolder)
        {
            var relative = assetsRelativeFolder;
            var dataPath = NormalizeSlash(Application.dataPath);

            string full;
            if (relative.StartsWith("Assets/", StringComparison.Ordinal))
            {
                full = dataPath + "/" + relative.Substring("Assets/".Length);
            }
            else if (relative == "Assets")
            {
                full = dataPath;
            }
            else
            {
                full = dataPath + "/" + relative.TrimStart('/');
            }

            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 判断当前根节点属于哪个目标:
        /// 1. prefab 资产本身 → 返回资产路径;
        /// 2. prefab 编辑模式下 → 先落盘,再返回资产路径;
        /// 3. 普通场景对象 → 返回 null(走 scene 流程)。
        /// </summary>
        private static string GetPrefabTargetPath(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                return AssetDatabase.GetAssetPath(go);
            }

#if UNITY_2022_1_OR_NEWER
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null &&
                (go.transform == stage.prefabContentsRoot.transform || go.transform.IsChildOf(stage.prefabContentsRoot.transform)))
            {
                try
                {
                    // 把 Prefab 编辑模式下当前内容先落盘,确保编译后回填读到最新标记
                    var contentsRoot = stage.prefabContentsRoot;
                    if (contentsRoot != null)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contentsRoot, stage.assetPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[LiteCodeGen] 保存 Prefab 失败:" + e.Message);
                }

                return stage.assetPath;
            }
#endif

            return null;
        }

        /// <summary>从根一直收集到最顶层祖先的名字链(用于编译后重新定位该对象)。</summary>
        public static string[] BuildAncestorChain(Transform t)
        {
            if (t == null)
            {
                return new string[0];
            }

            var reversed = new List<string>();
            var cur = t;
            while (cur != null)
            {
                reversed.Add(cur.name);
                cur = cur.parent;
            }

            reversed.Reverse();
            return reversed.ToArray();
        }
    }
}
