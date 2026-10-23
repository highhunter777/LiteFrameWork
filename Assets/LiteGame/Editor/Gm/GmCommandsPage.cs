using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·命令与审计页：调试内核（DebugKernel）的命令发现/无参执行/审计投影。
    /// 页内不再持有 CommandCenter 或反射执行逻辑；权限门/拦截器/审计链全在内核之后。
    /// 审计环自动刷新 1s（仅本页在场时轮询——壳只调活动页）。
    /// </summary>
    internal sealed class GmCommandsPage : IGmPage
    {
        private readonly List<CommandInfo> _cmds = new List<CommandInfo>();
        private readonly List<CommandAuditEntry> _audit = new List<CommandAuditEntry>();
        private string _cmdResult = "(未刷新)";
        private bool _cmdAutoRefresh = true;
        private double _nextCmdRefresh;

        public GUIContent Title => new GUIContent("命令与审计");

        public void OnShow() { }
        public void OnHide() { }

        public void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_cmdAutoRefresh && now >= _nextCmdRefresh)
            {
                _nextCmdRefresh = now + 1.0;
                RefreshCommands();                 // 审计环低频轮询（发现面注释口径）
            }
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("命令与审计（CommandCenter）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新", GUILayout.Width(60))) RefreshCommands();
                _cmdAutoRefresh = EditorGUILayout.ToggleLeft("自动刷新（1s，Play 中）", _cmdAutoRefresh, GUILayout.Width(190));
                EditorGUILayout.LabelField(_cmdResult, EditorStyles.miniLabel);
            }

            DebugKernel kernel = GetKernel();
            if (kernel == null)
            {
                EditorGUILayout.HelpBox("调试内核未装配（需 Play 且容器就绪）——列出/执行/审计均不可用。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("已注册命令（发现面清单，按注册序）", EditorStyles.miniBoldLabel);
            if (_cmds.Count == 0) EditorGUILayout.LabelField("(暂无注册命令——进 Play 后刷新)");
            foreach (CommandInfo info in _cmds)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{(info.GmOnly ? "[GM] " : "")}{info.CommandType.Name}", GUILayout.Width(220));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(info.Description) ? "—" : info.Description);
                    if (info.CommandType.GetConstructor(Type.EmptyTypes) == null)
                        EditorGUILayout.LabelField("需专面板", EditorStyles.miniLabel, GUILayout.Width(64));
                    else if (GUILayout.Button("执行", GUILayout.Width(48)))
                        RunDiscoveredCommand(kernel, info.CommandType);
                }
            }

            EditorGUILayout.LabelField("审计环（最近 32 条，旧→新）", EditorStyles.miniBoldLabel);
            if (_audit.Count == 0) EditorGUILayout.LabelField("(暂无审计记录)");
            foreach (CommandAuditEntry entry in _audit)
                EditorGUILayout.LabelField(
                    $"#{entry.Seq} {entry.CommandType?.Name} {(entry.Ok ? "OK" : $"拒:{entry.Reason}")} {entry.Detail}",
                    EditorStyles.miniLabel);
        }

        private void RefreshCommands()
        {
            DebugKernel kernel = GetKernel();
            _cmds.Clear();
            _audit.Clear();
            if (kernel == null) { _cmdResult = "(未装配：需 Play)"; return; }
            _cmds.AddRange(kernel.Commands);
            _audit.AddRange(kernel.Audit);
            _cmdResult = $"命令 {_cmds.Count} 条｜审计 {_audit.Count} 条｜{DateTime.Now:HH:mm:ss}";
        }

        /// <summary>执行发现的**无参**命令：统一走内核；带参命令在列表标注"需专面板"。
        /// 权限门/拦截器/审计链零改动——内核只负责构造与派发，拒绝原因原样回显。</summary>
        private void RunDiscoveredCommand(DebugKernel kernel, Type commandType)
        {
            bool executed = kernel.TryExecute(commandType, out CommandResult result, out string error);
            _cmdResult = executed
                ? $"{commandType.Name} → {(result.Ok ? $"成功：{result.Detail}" : $"拒（{result.Reason}）：{result.Detail}")}"
                : $"{commandType.Name} → {error}";
            RefreshCommands();
        }

        private static DebugKernel GetKernel() => GmEditorResolve.ResolveFromContainer<DebugKernel>();
    }
}
