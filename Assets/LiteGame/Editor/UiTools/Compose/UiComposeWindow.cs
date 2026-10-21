using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LiteClient.UI;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// UI 编排面板宿主窗口（《UI编辑器工具专项设计》§5）——**Odin 呈现层**：目标 prefab（Odin 绘制）
    /// ＋四个 **Odin TabGroup 页签**（绑定暴露 / 受控属性 / 校验·生成 / 控件库）。布局仍由 Unity
    /// Prefab Stage 原生提供，产物即标准 prefab。
    ///
    /// **依赖边界**：Odin 只进 <c>LiteGame.Editor</c>（运行时组件零 Odin 依赖）；各分区类的
    /// **逻辑面**（<see cref="UiComposeTarget"/> / <see cref="UiWidgetPropertyCatalog"/> /
    /// <see cref="UiWidgetLibrary"/> / <see cref="UiWidgetInstaller"/>）保持零 Sirenix——"摘除 Odin 只换绘制层"。
    /// 页签体（树/报告/列表）走既有 IMGUI 绘制器，在 Odin 页签内混排。
    /// </summary>
    public sealed class UiComposeWindow : OdinEditorWindow
    {
        /// <summary>四个分区共属的页签组名（<c>[TabGroup(组名, 页签名)]</c>）。</summary>
        private const string TabGroupName = "Compose";

        public static void Open()
        {
            var window = GetWindow<UiComposeWindow>("UI 编排面板");
            window.minSize = new Vector2(680f, 660f);
            window.Show();
        }

        [PropertyOrder(-10)]
        [Title("目标 UI Prefab", "Project 选中 prefab 后打开本窗口；在 Prefab Stage 中打开则自动跟随")]
        [ShowInInspector, HideLabel]
        public GameObject TargetPrefab;

        private GameObject _boundPrefab;
        private UiComposeTarget _target;
        private bool _autoPicked;

        private readonly UiBindingsSection _bindings = new UiBindingsSection();
        private readonly UiWidgetPropertySection _properties = new UiWidgetPropertySection();
        private readonly UiValidationSection _validation = new UiValidationSection();
        private readonly UiGenerateSection _generate = new UiGenerateSection();
        private readonly UiWidgetLibrarySection _widgets = new UiWidgetLibrarySection();

        // ---------------- 目标（页签组之外，置顶） ----------------

        [OnInspectorGUI, PropertyOrder(-10)]
        private void DrawTargetStatus()
        {
            FollowTarget();
            if (TargetPrefab != _boundPrefab) Bind(TargetPrefab);

            if (_target == null)
            {
                EditorGUILayout.HelpBox("选择 UI prefab 资产（在上方字段指定，或在 Prefab Stage 中打开）。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"{_target.PrefabPath} · {(_target.ViaStage ? "Prefab Stage 打开（编辑可 Undo）" : "未开 Stage——只读展示")}",
                EditorStyles.miniLabel);
        }

        // ---------------- 四个分区页签（Odin TabGroup） ----------------

        [OnInspectorGUI, TabGroup(TabGroupName, "绑定暴露")]
        private void DrawBindingsTab()
        {
            if (_target == null) return;
            _bindings.Draw(_target);
        }

        [OnInspectorGUI, TabGroup(TabGroupName, "受控属性")]
        private void DrawWidgetPropertyTab()
        {
            if (_target == null) return;
            _properties.Draw(_target);
        }

        [OnInspectorGUI, TabGroup(TabGroupName, "校验/生成")]
        private void DrawValidationTab()
        {
            if (_target == null) return;
            _validation.Draw(_target);
            _generate.Draw(_target);
        }

        [OnInspectorGUI, TabGroup(TabGroupName, "控件库")]
        private void DrawWidgetLibraryTab()
        {
            if (_target == null) return;
            _widgets.Draw(_target);
        }

        // ---------------- 目标跟随 ----------------

        /// <summary>目标跟随（判据单源 <see cref="UiComposeTarget.StageRootFor"/>）：Stage 打开且换了资产即切目标；
        /// 无 Stage 时首次打开带入 Project 选中。</summary>
        private void FollowTarget()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                string current = _target != null ? _target.PrefabPath : null;
                if (stage.assetPath != current)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
                    if (asset != null) TargetPrefab = asset;
                }
                return;
            }

            if (_autoPicked || TargetPrefab != null) return;
            _autoPicked = true;
            var picked = Selection.activeObject as GameObject;
            if (picked != null && PrefabUtility.IsPartOfPrefabAsset(picked)) TargetPrefab = picked;
        }

        private void Bind(GameObject asset)
        {
            _boundPrefab = asset;
            _target = UiComposeTarget.Resolve(asset);
        }
    }
}
