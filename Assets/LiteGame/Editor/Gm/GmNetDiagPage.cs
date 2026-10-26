using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·联机诊断页：DevHUD 联机诊断面的编辑器投影（《GM 与调试工具分层设计》§5 双入口形态）——
    /// ①HUD 控制：总开关/段开关/模块 Stats 白名单（编辑器工具豁免形态直改 DevHUD 实例 public 面——与 F1 等效）；
    /// ②读数投影：<see cref="DebugKernel.Network"/> 单源文本（与 HUD/控制台同一网络调试组件——本页零拼装）；
    /// ③会话动作：强制断线 / 强制重连（经 <see cref="DebugKernel"/> 走命令中心审计）。
    /// 形态边界：GM 面板是编辑器投影（EditorWindow 不进包）；开发包 Player 里 HUD 由 F1 开、面板由 F2 开、控制台由 F3 开。
    /// </summary>
    internal sealed class GmNetDiagPage : IGmPage
    {
        private string _lastAction = string.Empty;

        public GUIContent Title => new GUIContent("联机诊断");

        public void OnShow() { }
        public void OnHide() { }

        public void Poll() { }   // 读数在 Draw 现取（壳 0.5s 轮询驱动 Repaint 即刷新，无本页侧状态）

        public void Draw()
        {
            EditorGUILayout.LabelField("联机诊断（DevHUD 投影）", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("HUD 开关", EditorStyles.miniBoldLabel);
            DevHUD hud = UnityEngine.Object.FindAnyObjectByType<DevHUD>();
            if (hud == null)
            {
                EditorGUILayout.HelpBox(
                    "DevHUD 未在场（Play 后自创建；开发包 Player 里按 F1 开）。", MessageType.Info);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(hud.Visible ? "隐藏 HUD" : "打开 HUD", GUILayout.Width(90)))
                        hud.Visible = !hud.Visible;
                    EditorGUILayout.LabelField(hud.Visible ? "F1 等效" : "HUD 隐藏中", EditorStyles.miniLabel);
                }
                hud.showStats = EditorGUILayout.ToggleLeft("Stats 段", hud.showStats, GUILayout.Width(90));
                hud.showNetDiag = EditorGUILayout.ToggleLeft("联机诊断段", hud.showNetDiag, GUILayout.Width(110));
                hud.showPerf = EditorGUILayout.ToggleLeft("性能段", hud.showPerf, GUILayout.Width(90));
                hud.showLogRecent = EditorGUILayout.ToggleLeft("日志尾巴", hud.showLogRecent, GUILayout.Width(110));
                hud.showErrorsLine = EditorGUILayout.ToggleLeft("错误计数行", hud.showErrorsLine, GUILayout.Width(110));
                hud.logRecentLines = EditorGUILayout.IntSlider("日志条数（1-32）", hud.logRecentLines, 1, 32);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("按启动配置重排段", GUILayout.Width(140)))
                        hud.ApplyDefaultSectionLayout();
                    if (GUILayout.Button("选中场景 DevStartupConfig", GUILayout.Width(170)))
                    {
                        var config = UnityEngine.Object.FindAnyObjectByType<DevStartupConfig>(FindObjectsInactive.Include);
                        if (config != null) Selection.activeObject = config;
                    }
                }
                EditorGUILayout.LabelField(
                    "初始展开集在 DevStartupConfig.DevHudDefaultExpanded（随场景保存；仅无折叠记忆时自动生效）。",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("读数（DebugKernel 单源）", EditorStyles.miniBoldLabel);
            DebugKernel kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            if (kernel == null)
            {
                EditorGUILayout.LabelField("(调试内核未装配——需 Play 且容器就绪)", EditorStyles.miniLabel);
            }
            else
            {
                string[] lines = kernel.DescribeBattle();
                EditorGUILayout.LabelField(
                    lines.Length == 0 ? "(无对局——进 Play 并入局后显示)" : string.Join("\n", lines),
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("会话动作（DebugKernel.Network）", EditorStyles.miniBoldLabel);
                NetworkDebug network = kernel.Network;
                if (network == null)
                {
                    EditorGUILayout.LabelField("(网络调试未装配)", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        network.HasBattle
                            ? "相位 " + network.Phase
                              + " · RTT " + (network.Quality.HasSample ? network.Quality.RttMs + "ms" : "无样本")
                              + " · 弱网 " + (network.WeakNet.Enabled ? network.WeakNet.Preset : "未启用")
                            : "无对局（未进房）",
                        EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("强制断线", GUILayout.Width(90)))
                        {
                            bool ok = kernel.TryForceDisconnect(out string error);
                            _lastAction = ok ? "已断线（等待自动重连）" : "拒绝：" + error;
                        }
                        if (GUILayout.Button("强制重连", GUILayout.Width(90)))
                        {
                            bool ok = kernel.TryBeginReconnect(out string error);
                            _lastAction = ok ? "已发起重连" : "拒绝：" + error;
                        }
                    }
                    if (!string.IsNullOrEmpty(_lastAction))
                        EditorGUILayout.HelpBox(_lastAction, MessageType.None);

                    bool suppressed = network.SuppressAutoReconnect;
                    bool nextSuppressed = EditorGUILayout.ToggleLeft(
                        "抑制自动重连（断线后停在 SuspectedLost，用「强制重连」手动恢复）", suppressed);
                    if (nextSuppressed != suppressed)
                    {
                        bool ok = kernel.TrySetSuppressAutoReconnect(nextSuppressed, out string error);
                        _lastAction = ok
                            ? (nextSuppressed ? "已抑制自动重连" : "已恢复自动重连")
                            : "拒绝：" + error;
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("DevHUD 模块 Stats（白名单：未勾选不显示）", EditorStyles.miniBoldLabel);
                DevHUD devHud = UnityEngine.Object.FindAnyObjectByType<DevHUD>();
                if (devHud == null)
                {
                    EditorGUILayout.LabelField("(DevHUD 未在场——Play 后自创建)", EditorStyles.miniLabel);
                }
                else
                {
                    IReadOnlyList<string> names = devHud.StatsSectionNames;
                    if (names.Count == 0)
                    {
                        EditorGUILayout.LabelField("(无统计模块)", EditorStyles.miniLabel);
                    }
                    else
                    {
                        for (int i = 0; i < names.Count; i++)
                        {
                            string name = names[i];
                            bool visible = devHud.IsStatsSectionVisible(name);
                            bool next = EditorGUILayout.ToggleLeft(name, visible);
                            if (next != visible) devHud.SetStatsSectionVisible(name, next);
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("全部隐藏", GUILayout.Width(90))) devHud.SetAllStatsSectionsVisible(false);
                            if (GUILayout.Button("全部显示", GUILayout.Width(90))) devHud.SetAllStatsSectionsVisible(true);
                        }
                    }
                }
            }
        }
    }
}
