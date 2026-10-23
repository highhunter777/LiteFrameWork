using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·联机诊断页：DevHUD 联机诊断面的编辑器投影（《GM 与调试工具分层设计》§5 双入口形态）——
    /// ①HUD 控制：总开关/段开关（编辑器工具豁免形态直改 DevHUD 实例 public 字段——与 F1 等效）；
    /// ②读数投影：<see cref="DebugKernel.DescribeBattle"/> 单源文本（与 HUD 同一内核读口——本页零拼装、零状态）。
    /// 形态边界：GM 面板是编辑器投影（EditorWindow 不进包）；开发包 Player 里 HUD 由 F1 开、控制台由 F8 开。
    /// 页件零业务逻辑：诊断事实全在对局上下文，本页只读。
    /// </summary>
    internal sealed class GmNetDiagPage : IGmPage
    {
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
            }
        }
    }
}
