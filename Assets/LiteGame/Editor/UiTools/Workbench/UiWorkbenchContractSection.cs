using System;
using LiteGame.Editor.UiTools.Contracts;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 契约透视（《UI 编辑器工具链专项设计》§2.2 / §5 / §8.3）：`UiContractBatch` 的窗口入口——
    /// 生成全部契约（写 Contracts/）与基线 diff（只读，破坏性判失败）。不复制生成逻辑。
    /// </summary>
    public sealed class UiWorkbenchContractSection
    {
        private const string ContractDir = UiContractPaths.ContractDirectory;

        private UiContractReportView _report;
        private Vector2 _scroll;

        public void Draw(Func<UiWorkbenchAction, bool> canRun, Func<UiWorkbenchAction, string> disabledReason,
            Action<string> log)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.GenerateContract)))
                {
                    var content = new GUIContent("生成全部契约（写 Contracts/）",
                        disabledReason(UiWorkbenchAction.GenerateContract) ?? "生成全部契约");
                    if (GUILayout.Button(content, GUILayout.Width(190f))) Generate(log);
                }

                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.DiffContract)))
                {
                    var content = new GUIContent("与基线 diff",
                        disabledReason(UiWorkbenchAction.DiffContract) ?? "与基线 diff");
                    if (GUILayout.Button(content, GUILayout.Width(110f))) Diff(log);
                }
            }

            if (_report == null)
            {
                EditorGUILayout.HelpBox(
                    "生成＝按 tbuiform 全量写页面契约（确定性、无时间戳）；diff＝只读对账，破坏性变更判失败。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(_report.Title + "：" + _report.Summary, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_report.OutputDirectory))
                EditorGUILayout.LabelField("输出目录：" + _report.OutputDirectory, EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320f));
            foreach (string line in _report.Lines) EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Generate(Action<string> log)
        {
            if (!EditorUtility.DisplayDialog("生成 UI 契约",
                    "按 tbuiform 全量生成页面契约（写 " + ContractDir + "/）。是否继续？", "继续", "取消"))
                return;

            UiContractBatchResult result = UiContractBatch.Generate();
            _report = UiContractReportView.FromGeneration(result, ContractDir);
            log("[ui/workbench] " + result.Summary);
        }

        private void Diff(Action<string> log)
        {
            UiContractBatchDiffResult result = UiContractBatch.Diff();
            _report = UiContractReportView.FromDiff(result);
            log("[ui/workbench] " + result.Summary);
        }
    }
}
