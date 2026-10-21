using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.WriteBack
{
    /// <summary>
    /// prefab 改动回写窗口（《UI工作台设计》§2「回写」——**面板常驻入口**的落点）：
    /// 选中 prefab 实例（游戏视图/场景视图均可，**Play 中亦可**）→「取当前选择」枚举"实例 vs 源 prefab"逐属性差异
    /// → 勾选 → 「回写选中项」把实例值直写源 prefab 资产（不退 Play、不整场景保存）。
    ///
    /// 验收＝Game 视图、编辑＝Inspector（Play 模式选实例改属性）、固化＝本窗口勾选直写——三步闭环。
    /// 驱动每帧写面（数据值/动态位置）与 Unity 只维护默认记录的路径（如 RectTransform 尺寸）都列在
    /// 「数值差异」侧**默认不勾**——它们归驱动常量/表，落地前须逐条核对（判据实测校准见施工记录）。
    ///
    /// 入口两处：菜单 + 工作台工具条按钮（`UIWorkbenchDriver.OnWriteBack` 经 <see cref="MenuPath"/> 跳转；
    /// 两程序集无引用关系，同值由 EditMode 守卫用例钉住）。
    /// </summary>
    public sealed class PrefabWriteBackWindow : EditorWindow
    {
        /// <summary>菜单路径（单源——驱动件按此跳转；守卫用例断言两处同值）。</summary>
        public const string MenuPath = "LiteGame/快捷/Prefab 回写";

        private GameObject _target;
        private PrefabWriteBackScanResult _scan;
        private Vector2 _scroll;
        private string _status = "选中 prefab 实例（或其实例内任意子对象）后点「取当前选择」。";
        private bool _showBlocked = true;

        [MenuItem(MenuPath, false, 30)]
        public static void Open() => GetWindow<PrefabWriteBackWindow>("Prefab 回写");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Play 内可用：改 Inspector 属性 → 本窗口「取当前选择」→ 勾选 → 回写（直写源 prefab 资产，不退 Play）。\n"
                + "「显式记录」= Unity 记录在案的改动（默认勾选）；「数值差异」= 其余当前值差异"
                + "——含驱动/脚本每帧写出的数据文本与动态位置，也含 Unity 只维护默认记录的路径（如 RectTransform 尺寸）。\n"
                + "两者都请核对「实例值 → 资产值」后再落地：数值/动态位置归驱动常量或表，不该烤进 prefab。\n"
                + "新增组件/子对象、指向场景对象的引用、不支持的类型一律显式列出且不可回写（结构面归 Prefab Stage 与 Unity Overrides）。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _target = (GameObject)EditorGUILayout.ObjectField("目标（实例内任意对象）", _target, typeof(GameObject), true);
                if (GUILayout.Button("取当前选择", GUILayout.Width(110)))
                {
                    _target = Selection.activeGameObject;
                    Scan();
                }
                if (GUILayout.Button("重新扫描", GUILayout.Width(90))) Scan();
            }

            if (_scan != null && !string.IsNullOrEmpty(_scan.Error))
            {
                EditorGUILayout.HelpBox(_scan.Error, MessageType.Warning);
                DrawStatus();
                return;
            }

            if (_scan != null)
            {
                EditorGUILayout.LabelField($"实例：{_scan.InstanceRootPath}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"源资产：{_scan.AssetPath}", EditorStyles.miniLabel);
                int explicitCount = 0, runtimeCount = 0, blockedCount = 0;
                foreach (var item in _scan.Items)
                {
                    if (!item.CanApply) blockedCount++;
                    else if (item.Origin == PrefabWriteBackOrigin.RecordedOverride) explicitCount++;
                    else runtimeCount++;
                }
                EditorGUILayout.LabelField(
                    $"差异 {_scan.Items.Count}：显式记录 {explicitCount} · 数值差异 {runtimeCount} · 不可回写 {blockedCount}"
                    + (_scan.Notes.Count > 0 ? $" · 结构面说明 {_scan.Notes.Count}" : ""));

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全选显式记录", GUILayout.Width(130)))
                        foreach (var item in _scan.Items)
                            if (item.CanApply && item.Origin == PrefabWriteBackOrigin.RecordedOverride) item.Selected = true;
                    if (GUILayout.Button("全选数值差异", GUILayout.Width(130)))
                        foreach (var item in _scan.Items)
                            if (item.CanApply && item.Origin == PrefabWriteBackOrigin.RuntimeDiff) item.Selected = true;
                    if (GUILayout.Button("全不选", GUILayout.Width(70)))
                        foreach (var item in _scan.Items) item.Selected = false;
                    _showBlocked = GUILayout.Toggle(_showBlocked, "显示不可回写项", GUILayout.Width(130));
                }

                DrawItems();
                DrawNotes();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("回写选中项", GUILayout.Height(24))) ApplySelected();
                    if (GUILayout.Button("清空清单", GUILayout.Width(80), GUILayout.Height(24))) { _scan = null; _status = "已清空。"; }
                }
            }

            DrawStatus();
        }

        private void DrawItems()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            string currentOwner = null;
            foreach (var item in _scan.Items)
            {
                if (!_showBlocked && !item.CanApply) continue;
                if (item.OwnerLabel != currentOwner)
                {
                    currentOwner = item.OwnerLabel;
                    EditorGUILayout.LabelField(currentOwner, EditorStyles.boldLabel);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (item.CanApply)
                    {
                        item.Selected = EditorGUILayout.Toggle(item.Selected, GUILayout.Width(18));
                    }
                    else
                    {
                        GUILayout.Space(18);
                    }

                    string tag = !item.CanApply ? $"✗ {BlockLabel(item.Block)}"
                        : item.Origin == PrefabWriteBackOrigin.RecordedOverride ? "显式记录" : "数值差异";
                    EditorGUILayout.LabelField($"{item.PropertyPath}   {item.InstanceValue}  →  {item.SourceValue}   [{tag}]");
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawNotes()
        {
            if (_scan.Notes.Count == 0) return;
            EditorGUILayout.LabelField("结构面说明（不自动回写）", EditorStyles.boldLabel);
            foreach (var note in _scan.Notes) EditorGUILayout.LabelField("· " + note, EditorStyles.miniLabel);
        }

        private void DrawStatus()
        {
            EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        private void Scan()
        {
            _scan = PrefabWriteBackScan.Scan(_target);
            if (!string.IsNullOrEmpty(_scan.Error)) _status = "扫描未开始：" + _scan.Error;
            else _status = $"扫描完成：差异 {_scan.Items.Count}（可回写 {_scan.ApplicableCount}）。勾选后点「回写选中项」。";
        }

        private void ApplySelected()
        {
            if (_scan == null) { _status = "先扫描。"; return; }
            var report = PrefabWriteBackApply.Apply(_scan.Items);
            if (!string.IsNullOrEmpty(report.Error)) { _status = report.Error; return; }

            var sb = new System.Text.StringBuilder();
            sb.Append($"回写 {report.Applied} 条");
            if (report.Failed > 0) sb.Append($"，失败 {report.Failed} 条");
            if (report.Assets.Count > 0) sb.Append(" → ").Append(string.Join("、", report.Assets));
            if (report.Failures.Count > 0) sb.Append("\n").Append(string.Join("\n", report.Failures));
            _status = sb.ToString();

            Scan();                                                    // 复扫：已固化的差异不再出现（等值即无差异）
            _status = sb.Append("\n（复扫确认：已固化项不再列为差异）").ToString();
        }

        private static string BlockLabel(PrefabWriteBackBlock block) => block switch
        {
            PrefabWriteBackBlock.AddedComponent => "实例新增组件（结构面）",
            PrefabWriteBackBlock.SceneReference => "指向场景对象（不可烤进资产）",
            PrefabWriteBackBlock.UnsupportedType => "类型不支持",
            PrefabWriteBackBlock.StructureDiff => "结构差异",
            _ => "不可回写",
        };
    }
}
