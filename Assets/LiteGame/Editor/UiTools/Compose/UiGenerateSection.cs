using LiteCodeGen;
using LiteCodeGen.EditorTools;
using LiteGame.Editor.UiTools.Binding;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// 生成分区（《UI编辑器工具专项设计》§5"校验/生成"）：路径 B 生成走既有链
    /// <see cref="BindGenerator.Generate(GameObject)"/>，生成成功即<b>同一作者动作</b>记录
    /// <see cref="UiBindGenerationHash.Record"/>（§2.1.5 生成物 hash），使 <c>ui.binding-hash</c> 规则就地转绿。
    /// 无 BindRoot＝路径 B 未启用，显式说明而不报错。
    /// </summary>
    public sealed class UiGenerateSection
    {
        public void Draw(UiComposeTarget target)
        {
            if (target == null) return;
            var root = target.DisplayRoot;
            var bindRoot = root != null ? BindMenus.FindRootInPrefab(root) : null;
            if (bindRoot == null)
            {
                EditorGUILayout.HelpBox("本 prefab 无 BindRoot——路径 B 生成未启用（无生成物 hash 要求）。", MessageType.Info);
                return;
            }

            UiBindGenerationHash.TryGetRecordedHash(target.PrefabPath, out string recorded);
            string current = UiBindGenerationHash.ComputeHash(root);
            EditorGUILayout.LabelField(recorded == null
                ? "生成物 hash：从未记录"
                : string.Equals(recorded, current, System.StringComparison.Ordinal)
                    ? $"生成物 hash：一致（{Short(recorded)}）"
                    : "生成物 hash：漂移——源已改，须重新生成并记录",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成绑定代码（路径 B）并记录", GUILayout.ExpandWidth(false)))
                    Generate(target, bindRoot);
                if (GUILayout.Button("只记录 hash（不生成）", GUILayout.ExpandWidth(false)))
                {
                    UiBindGenerationHash.Record(target.PrefabPath);
                    Debug.Log($"[UI 编排] 已记录 {System.IO.Path.GetFileName(target.PrefabPath)} 的绑定面 hash");
                }
            }
        }

        private static void Generate(UiComposeTarget target, BindRoot bindRoot)
        {
            if (!EditorUtility.DisplayDialog("生成绑定代码",
                    "生成会写入脚本文件并触发脚本重编译（本窗口状态随域重载丢失）。是否继续？", "继续", "取消"))
                return;

            if (BindGenerator.Generate(bindRoot.gameObject))
            {
                UiBindGenerationHash.Record(target.PrefabPath);
                Debug.Log($"[UI 编排] 已生成并记录：{System.IO.Path.GetFileName(target.PrefabPath)}");
            }
        }

        private static string Short(string hash)
            => string.IsNullOrEmpty(hash) ? "-" : hash.Substring(0, hash.Length < 8 ? hash.Length : 8);
    }
}
