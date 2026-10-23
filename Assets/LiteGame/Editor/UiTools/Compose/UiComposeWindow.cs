using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Preview;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>
    /// UI 制作主窗口（《UI编辑器工具专项设计》§5/§8）——**Odin 呈现层**：页面选择器 + 目标 prefab（Odin 绘制）
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
            var window = GetWindow<UiComposeWindow>("UI 制作");
            window.minSize = new Vector2(760f, 720f);
            window.Show();
        }

        [PropertyOrder(-10)]
        [Title("目标 UI Prefab", "从已登记页面选择；或在 Project 选中 / Prefab Stage 打开时自动跟随")]
        [ShowInInspector, HideLabel]
        public GameObject TargetPrefab;

        private GameObject _boundPrefab;
        private UiComposeTarget _target;
        private bool _autoPicked;
        private List<UiPageScaffold.PageRow> _pages = new List<UiPageScaffold.PageRow>();
        private string[] _pageLabels = Array.Empty<string>();
        private int _pageIndex = -1;
        private bool _pagesLoaded;

        private readonly UiBindingsSection _bindings = new UiBindingsSection();
        private readonly UiWidgetPropertySection _properties = new UiWidgetPropertySection();
        private readonly UiValidationSection _validation = new UiValidationSection();
        private readonly UiGenerateSection _generate = new UiGenerateSection();
        private readonly UiWidgetLibrarySection _widgets = new UiWidgetLibrarySection();

        // ---------------- 目标（页签组之外，置顶） ----------------

        [OnInspectorGUI, PropertyOrder(-10)]
        private void DrawTargetStatus()
        {
            if (!_pagesLoaded) ReloadPages();
            DrawPagePicker();
            FollowTarget();
            if (TargetPrefab != _boundPrefab) Bind(TargetPrefab);
            SyncPageIndexToTarget();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新页面", GUILayout.Width(72f))) ReloadPages();
                if (GUILayout.Button("新建页", GUILayout.Width(68f))) UiPageWizardWindow.Open();
                using (new EditorGUI.DisabledScope(_target == null || _target.PrefabAsset == null))
                {
                    if (GUILayout.Button("预览", GUILayout.Width(60f)))
                        UiPagePreview.Open(_target.PrefabPath);
                }
                EditorGUILayout.LabelField("主线：选页/布局 → 命名绑定 → 校验生成 → 预览",
                    EditorStyles.miniLabel);
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox("从上方页面列表选页，或在 Project/Prefab Stage 选择 prefab。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"{_target.PrefabPath} · {(_target.ViaStage ? "Prefab Stage 打开（编辑可 Undo）" : "未开 Stage——只读展示")}",
                EditorStyles.miniLabel);
        }

        private void DrawPagePicker()
        {
            if (_pages.Count == 0)
            {
                EditorGUILayout.LabelField("已登记页面：无（点「刷新页面」重读 tbuiform）", EditorStyles.miniLabel);
                return;
            }

            int display = _pageIndex + 1;
            int picked = EditorGUILayout.Popup("已登记页面", Mathf.Max(0, display), _pageLabels);
            if (picked == display) return;

            _pageIndex = picked - 1;
            if (_pageIndex >= 0 && _pageIndex < _pages.Count) SelectPage(_pages[_pageIndex]);
        }

        /// <summary>重读 tbuiform 页面清单（新建页后由向导触发）。</summary>
        public void ReloadPages()
        {
            _pages = UiPageScaffold.ListPages();
            _pageLabels = new string[_pages.Count + 1];
            _pageLabels[0] = _pages.Count == 0 ? "（无已登记页面）" : "（选择页面）";
            for (int i = 0; i < _pages.Count; i++)
                _pageLabels[i + 1] = $"{_pages[i].id}  {_pages[i].lua}  ({_pages[i].FileName})";
            _pagesLoaded = true;
            SyncPageIndexToTarget();
            Repaint();
        }

        /// <summary>所有打开的 UI 制作窗口刷新页面清单（向导创建成功后调用）。</summary>
        public static void RefreshOpenPageList()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<UiComposeWindow>())
            {
                if (window == null) continue;
                window.ReloadPages();
                window.Repaint();
            }
        }

        private void SyncPageIndexToTarget()
        {
            _pageIndex = -1;
            if (TargetPrefab == null) return;
            string path = AssetDatabase.GetAssetPath(TargetPrefab);
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < _pages.Count; i++)
            {
                if (!string.Equals(_pages[i].PrefabPath, path, StringComparison.Ordinal)) continue;
                _pageIndex = i;
                return;
            }
        }

        private void SelectPage(UiPageScaffold.PageRow row)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(row.PrefabPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("UI 制作", "页面 prefab 不存在：\n" + row.PrefabPath, "OK");
                return;
            }

            var stage = PrefabStageUtility.OpenPrefab(row.PrefabPath);
            if (stage == null) Debug.LogWarning("[UI 制作] Prefab Stage 打开失败：" + row.PrefabPath);
            TargetPrefab = asset;
            Bind(asset);
            Repaint();
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

        [OnInspectorGUI, TabGroup(TabGroupName, "校验·生成")]
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
