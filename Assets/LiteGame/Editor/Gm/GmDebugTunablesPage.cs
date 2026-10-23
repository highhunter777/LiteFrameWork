using System;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·调试参数页：按 <see cref="IDebugTunableProvider"/> 的描述自动渲染开关/滑杆/复位，
    /// 不按功能写专用面板；默认值/范围来自功能 provider（烘焙/表/机制常量）。
    /// 改动只落本机运行时覆写，不落盘、不进摘要；release 面随三宏剥离。
    /// </summary>
    internal sealed class GmDebugTunablesPage : IGmPage
    {
        private DebugKernel _kernel;
        private string _filter = "";
        private string _status = "就绪";

        public GUIContent Title => new GUIContent("调试参数");

        public void OnShow()
        {
            _kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            _kernel?.RebuildTunables();
        }

        public void OnHide() { }

        public void Poll()
        {
            if (_kernel == null) _kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            if (_kernel == null) return;
            if (_kernel.Tunables.Count == 0) _kernel.RebuildTunables();
            else _kernel.PruneTunables();
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("调试参数（功能 provider 自动发现）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "默认值来自功能自己的烘焙/表/机制常量；这里只写本机运行时覆写，不落盘、不进摘要。"
                + "「复位」回到源值（通常清除覆写）。FOV 等编辑器独占场景值仍在「本地测试模式」页。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _filter = EditorGUILayout.TextField("过滤", _filter ?? "");
                if (GUILayout.Button("刷新", GUILayout.Width(52)))
                {
                    _kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
                    _kernel?.RebuildTunables();
                }
            }

            if (_kernel == null)
            {
                EditorGUILayout.HelpBox("DebugKernel 未装配——先进入 Play。", MessageType.Warning);
                return;
            }

            var tunables = _kernel.Tunables;
            if (tunables.Count == 0)
            {
                EditorGUILayout.HelpBox("未发现调试参数 provider（进入 Play/加载场景后刷新）。", MessageType.Warning);
                return;
            }

            string currentGroup = null;
            bool hasProduct = false;
            for (int i = 0; i < tunables.Count; i++)
            {
                DebugTunable tunable = tunables[i];
                if (tunable.Scope == DebugTunableScope.Product) hasProduct = true;
                if (!Matches(tunable)) continue;

                if (!string.Equals(currentGroup, tunable.Group, StringComparison.Ordinal))
                {
                    currentGroup = tunable.Group;
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(currentGroup, EditorStyles.boldLabel);
                }

                DrawTunable(tunable);
            }

            if (hasProduct)
                EditorGUILayout.HelpBox("本页含 Product scope 参数：临时试验后应把定档值迁回表/配置，不要让它成为第二默认值。", MessageType.Warning);
            EditorGUILayout.LabelField("状态：" + _status, EditorStyles.wordWrappedLabel);
        }

        private void DrawTunable(DebugTunable tunable)
        {
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ScopePrefix(tunable.Scope) + tunable.Label,
                        GUILayout.Width(250));
                    if (tunable.Kind == DebugTunableKind.Bool)
                    {
                        bool current = tunable.Get() >= 0.5f;
                        bool next = EditorGUILayout.Toggle(current, GUILayout.Width(20));
                        if (next != current) Set(tunable, next ? 1f : 0f);
                    }
                    else
                    {
                        float current = tunable.Get();
                        float next = EditorGUILayout.Slider(current, tunable.Min, tunable.Max);
                        if (!Mathf.Approximately(next, current)) Set(tunable, next);
                    }

                    if (GUILayout.Button("复位", GUILayout.Width(44))) Reset(tunable);
                }
                EditorGUILayout.LabelField(
                    "  " + tunable.Id + " · 默认 " + DebugTunableText.Format(tunable, tunable.Default)
                    + " · 范围 " + DebugTunableText.Format(tunable, tunable.Min)
                    + ".." + DebugTunableText.Format(tunable, tunable.Max)
                    + (tunable.RuntimeVisible ? " · 运行期可见" : ""),
                    EditorStyles.miniLabel);
            }
            catch (Exception ex)
            {
                _status = tunable.Id + " 读取失败：" + ex.Message;
            }
        }

        private void Set(DebugTunable tunable, float value)
        {
            if (_kernel.TrySetTunable(tunable.Id, value, out string error))
                _status = tunable.Id + " = " + DebugTunableText.Format(tunable, value);
            else _status = error;
        }

        private void Reset(DebugTunable tunable)
        {
            if (_kernel.ResetTunable(tunable.Id, out string error)) _status = tunable.Id + " 已复位";
            else _status = error;
        }

        private bool Matches(DebugTunable tunable)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            return tunable.Label.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                || tunable.Id.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                || tunable.Group.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (tunable.Tags != null && tunable.Tags.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ScopePrefix(DebugTunableScope scope)
        {
            switch (scope)
            {
                case DebugTunableScope.Presentation: return "[表现] ";
                case DebugTunableScope.Product: return "[产品] ";
                default: return "[沙箱] ";
            }
        }

    }
}
