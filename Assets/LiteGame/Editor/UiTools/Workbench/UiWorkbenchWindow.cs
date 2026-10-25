using System;
using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Page;
using LiteGame.Editor.UiTools.Preview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// UI 工作台（《UI 编辑器工具链专项设计》§2）：**唯一制作入口**——一个宿主窗口 + 五个透视
    /// （页面/控件库/绑定/契约/校验）+ 共享上下文（页面/目标 prefab/Lane）。
    /// 绘制层零 Sirenix；判定全部委托给可测内核（<see cref="UiLanePolicy"/> / <see cref="UiWorkbenchAvailability"/> /
    /// <see cref="UiWorkbenchState"/> / <see cref="UiWorkbenchPageList"/> / <see cref="UiContractReportView"/>）。
    /// </summary>
    public sealed class UiWorkbenchWindow : EditorWindow
    {
        private const string WindowTitle = "UI 工作台";
        private const string LogPrefix = "[ui/workbench]";

        private readonly UiBindingsSection _bindings = new UiBindingsSection();
        private readonly UiWidgetPropertySection _properties = new UiWidgetPropertySection();
        private readonly UiWidgetLibrarySection _widgets = new UiWidgetLibrarySection();
        private readonly UiValidationSection _validation = new UiValidationSection();
        private readonly UiGenerateSection _generate = new UiGenerateSection();
        private readonly UiWorkbenchPageSection _page = new UiWorkbenchPageSection();
        private readonly UiWorkbenchContractSection _contract = new UiWorkbenchContractSection();

        private IWorkbenchSettings _settings;
        private UiWorkbenchState _state;
        private UiComposeTarget _target;
        private GameObject _targetPrefab;
        private UiDeviceProfileDocument _deviceDocument;
        private string _status = "就绪";
        private Vector2 _perspectiveScroll;

        private static readonly UiWorkbenchPerspective[] Ordered =
            UiWorkbenchPerspectives.All.Where(item => item.Enabled).OrderBy(item => item.Order).ToArray();

        /// <summary>偏好注入口（默认 EditorPrefs；夹具/无偏好环境可替换，不改窗口代码）。</summary>
        internal static Func<IWorkbenchSettings> SettingsFactory = () => new UiWorkbenchSettings();

        /// <summary>设备档下拉选项（P2-3 B3；首项「跟随预览」＝空档，其余＝档案 inMatrix 档位 id Ordinal）。
        /// 纯函数面：夹具不依赖 IMGUI 渲染即可断言选项集。</summary>
        internal static IReadOnlyList<string> DeviceProfileOptions(UiDeviceProfileDocument document)
        {
            var options = new List<string> { string.Empty };
            if (document?.Profiles != null)
                options.AddRange(UiDeviceProfileCatalog.MatrixProfiles(document).Select(profile => profile.Id));
            return options;
        }

        public static void Open()
        {
            var window = GetWindow<UiWorkbenchWindow>(WindowTitle);
            window.minSize = new Vector2(880f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            _settings = SettingsFactory();
            ReloadDeviceProfiles();
            _state = new UiWorkbenchState(_settings.Lane);
            _state.SetPerspective(_settings.Perspective);
            BuildDrawers();
            _page.PageSelected += OnPageSelected;
            _page.ScaffoldRequested += OnScaffoldRequested;
            _page.RegisterRequested += OnRegisterRequested;
            _page.Reload(_state);
            FollowTarget();
        }

        private void OnDisable()
        {
            if (_settings != null && _state != null)
            {
                _settings.Lane = _state.Lane;
                _settings.Perspective = _state.Perspective;
            }

            if (_page != null) _page.PageSelected -= OnPageSelected;
            if (_page != null) _page.ScaffoldRequested -= OnScaffoldRequested;
            if (_page != null) _page.RegisterRequested -= OnRegisterRequested;

            // 关闭时未保存提示（域重载/编译/进出 Play 不弹，避免打断导入与运行）。
            if (_state == null || !_state.HasUnsavedEdits || EditorApplication.isCompiling
                || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                bool save = EditorUtility.DisplayDialog(WindowTitle,
                    "页面清单有未保存编辑。是否保存后再关闭？", "保存", "丢弃");
                if (save) _page.TrySave(_state, Log);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ui/workbench] 关闭提示失败（已放弃内存编辑）：" + exception.Message);
            }
        }

        /// <summary>工具菜单/夹具可直接打开两个子工具；工作台按钮是编排入口。</summary>
        private void OnScaffoldRequested()
        {
            UiPageScaffoldWindow.Open();
            _status = LogPrefix + " 已打开起页窗口";
            Repaint();
        }

        private void OnRegisterRequested()
        {
            UiPageRegisterWindow.Open(_state.SelectedPage);
            _status = LogPrefix + " 已打开页面登记窗口";
            Repaint();
        }
        private void OnPageSelected(UiWorkbenchPageRow row)
        {
            var prefab = row == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(row.PrefabPath);
            if (prefab == null)
            {
                Log(LogPrefix + " 页面 prefab 不存在：" + (row?.PrefabPath ?? "null"));
                return;
            }

            BindTarget(prefab);

            // 已有别的 Stage 打开时切到所选页（否则 Stage 与目标不一致，属性/生成会落到错资产）。
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null
                && !string.Equals(stage.assetPath, row.PrefabPath, StringComparison.Ordinal)
                && PrefabStageUtility.OpenPrefab(row.PrefabPath) != null)
            {
                BindTarget(prefab);
            }

            Log(LogPrefix + " 选中页面：" + row.Name);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawPerspectiveBar();

            // 页面/契约透视自带列表滚动；其余透视由宿主统一滚动，避免嵌套 ScrollView。
            bool selfScrolling = _state.Perspective == UiWorkbenchPerspectiveId.Page
                                 || _state.Perspective == UiWorkbenchPerspectiveId.Contract;
            if (selfScrolling)
            {
                DrawPerspective();
            }
            else
            {
                _perspectiveScroll = EditorGUILayout.BeginScrollView(_perspectiveScroll);
                DrawPerspective();
                EditorGUILayout.EndScrollView();
            }

            DrawStatusBar();
        }

        // ---------------- 顶部上下文条 ----------------

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新清单", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    if (!_state.HasUnsavedEdits || EditorUtility.DisplayDialog(WindowTitle,
                            "刷新会丢弃未保存的页面清单编辑。是否继续？", "刷新", "取消"))
                        _page.Reload(_state);
                }

                DrawPagePicker();
                GUILayout.FlexibleSpace();

                GUILayout.Label("Lane", EditorStyles.miniLabel, GUILayout.Width(34f));
                var lane = (UiWorkbenchLane)EditorGUILayout.EnumPopup(
                    _state.Lane, EditorStyles.toolbarPopup, GUILayout.Width(84f));
                if (lane != _state.Lane)
                {
                    _state.SetLane(lane);
                    if (_settings != null) _settings.Lane = lane;
                    _status = LogPrefix + " Lane → " + lane;
                }

                GUILayout.Label("设备档", EditorStyles.miniLabel, GUILayout.Width(44f));
                string[] options = DeviceProfileOptions(_deviceDocument).ToArray();
                if (options.Length <= 1)
                {
                    EditorGUILayout.LabelField("档案不可用", EditorStyles.miniLabel, GUILayout.Width(120f));
                }
                else
                {
                    string selectedId = _settings?.PreviewDeviceProfileId ?? string.Empty;
                    int index = Array.IndexOf(options, selectedId);
                    if (index < 0) index = 0;
                    string[] display = options.Select((id, i) => i == 0 ? "跟随预览" : id).ToArray();
                    int picked = EditorGUILayout.Popup(index, display, EditorStyles.toolbarPopup, GUILayout.Width(150f));
                    if (picked != index)
                    {
                        string id = options[picked];
                        if (_settings != null) _settings.PreviewDeviceProfileId = id;
                        _status = LogPrefix + " 预览设备档 → " + (string.IsNullOrEmpty(id) ? "跟随预览" : id);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var picked = (GameObject)EditorGUILayout.ObjectField("目标 prefab", _targetPrefab, typeof(GameObject), false);
                if (picked != _targetPrefab) BindTarget(picked);
                using (new EditorGUI.DisabledScope(_targetPrefab == null))
                {
                    if (GuardedButton(UiWorkbenchAction.OpenPage, "打开 Prefab Stage", 130f)) OpenTargetStage();
                    if (GuardedButton(UiWorkbenchAction.Preview, "预览", 60f)) OpenPreview();
                }
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox("从页面清单选页，或在 Project/Prefab Stage 选择 prefab。布局仍在 Unity Prefab Stage 原生完成。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(
                    _target.PrefabPath + (_target.ViaStage ? " · Stage 已打开" : " · 只读（未开 Stage）"),
                    EditorStyles.miniLabel);
            }
        }

        private void DrawPagePicker()
        {
            if (_page.Rows.Count == 0)
            {
                EditorGUILayout.LabelField("（无已登记页面）", EditorStyles.miniLabel, GUILayout.Width(160f));
                return;
            }

            string[] labels = new string[_page.Rows.Count + 1];
            labels[0] = "（未选择页面）";
            for (int i = 0; i < _page.Rows.Count; i++)
                labels[i + 1] = _page.Rows[i].Name + " · " + _page.Rows[i].Status;

            // 哨兵位：未选择时停在下标 0，避免 Mathf.Clamp(-1,0,…) 首帧自动选中并绑定第 0 页。
            int current = _page.SelectedIndex < 0 ? 0 : _page.SelectedIndex + 1;
            int picked = EditorGUILayout.Popup(current, labels, EditorStyles.toolbarPopup, GUILayout.Width(220f));
            int index = picked - 1;
            if (index >= 0 && index != _page.SelectedIndex) _page.Select(_state, index);
        }

        // ---------------- 透视 ----------------

        private void DrawPerspectiveBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                foreach (UiWorkbenchPerspective perspective in Ordered)
                {
                    bool active = _state.Perspective == perspective.Id;
                    var style = active ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                    var content = new GUIContent(perspective.Title,
                        "能力：" + string.Join(" / ", perspective.Capabilities));
                    if (GUILayout.Button(content, style, GUILayout.Width(74f))
                        && _state.SetPerspective(perspective.Id))
                    {
                        if (_settings != null) _settings.Perspective = perspective.Id;
                    }
                }

                GUILayout.FlexibleSpace();
                if (_state.HasUnsavedEdits)
                {
                    Color previous = GUI.color;
                    GUI.color = new Color(1f, 0.75f, 0.4f);
                    GUILayout.Label("● 有未保存编辑", EditorStyles.miniBoldLabel);
                    GUI.color = previous;
                }
            }
        }

        /// <summary>透视绘制表：新增透视＝加表项 + 一个绘制器（宿主不再改 switch）。</summary>
        private readonly Dictionary<UiWorkbenchPerspectiveId, Action> _drawers =
            new Dictionary<UiWorkbenchPerspectiveId, Action>();

        private void BuildDrawers()
        {
            _drawers[UiWorkbenchPerspectiveId.Page] = () => _page.Draw(_state, CanRun, DisabledReasonFor, Log);
            _drawers[UiWorkbenchPerspectiveId.Widgets] = DrawWidgetsPerspective;
            _drawers[UiWorkbenchPerspectiveId.Binding] = DrawBindingPerspective;
            _drawers[UiWorkbenchPerspectiveId.Contract] = () => _contract.Draw(CanRun, DisabledReasonFor, Log);
            _drawers[UiWorkbenchPerspectiveId.Validation] = DrawValidationPerspective;
        }

        private void DrawPerspective()
        {
            if (_drawers.TryGetValue(_state.Perspective, out Action draw))
            {
                draw();
                return;
            }

            EditorGUILayout.HelpBox("未注册的透视：" + _state.Perspective, MessageType.Error);
        }

        private void DrawWidgetsPerspective()
        {
            using (new EditorGUI.DisabledScope(!CanRun(UiWorkbenchAction.InsertWidget)))
            {
                _widgets.Draw(_target);
                _properties.Draw(_target);
            }
        }

        private void DrawBindingPerspective()
        {
            using (new EditorGUI.DisabledScope(!CanRun(UiWorkbenchAction.EditBinding)))
            {
                _bindings.Draw(_target);
            }

            using (new EditorGUI.DisabledScope(!CanRun(UiWorkbenchAction.GenerateBinding)))
            {
                _generate.Draw(_target);
            }
        }

        private void DrawValidationPerspective()
        {
            using (new EditorGUI.DisabledScope(!CanRun(UiWorkbenchAction.Validate)))
            {
                _validation.Draw(_target);
            }
        }

        // ---------------- 门控 ----------------

        private bool CanRun(UiWorkbenchAction action)
            => UiWorkbenchAvailability.IsImplemented(action) && UiLanePolicy.IsAllowed(_state.Lane, action);

        /// <summary>禁用原因：阶段原因优先（工具未建），其次 Lane 原因（角色不该做）。</summary>
        private string DisabledReasonFor(UiWorkbenchAction action)
            => UiWorkbenchAvailability.DisabledReason(action)
               ?? UiLanePolicy.DisabledReason(_state.Lane, action);

        private bool GuardedButton(UiWorkbenchAction action, string label, float width)
        {
            using (new EditorGUI.DisabledScope(!CanRun(action)))
            {
                var content = new GUIContent(label, DisabledReasonFor(action) ?? label);
                if (!GUILayout.Button(content, GUILayout.Width(width))) return false;
            }

            return true;
        }

        private void Log(string message)
        {
            _status = message;
            Repaint();
        }

        // ---------------- 状态行 ----------------

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(_status, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (_state.HasUnsavedEdits)
                    GUILayout.Label("未保存：" + (_state.SelectedPage ?? "-"), EditorStyles.miniBoldLabel);
            }
        }

        // ---------------- 目标跟随 ----------------

        private void FollowTarget()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            string stagePath = stage != null && stage.prefabContentsRoot != null ? stage.assetPath : null;

            if (stagePath != null)
            {
                if (_targetPrefab == null || _target == null)
                {
                    BindTarget(AssetDatabase.LoadAssetAtPath<GameObject>(stagePath));
                    return;
                }

                bool sameAsStage = string.Equals(stagePath, _target.PrefabPath, StringComparison.Ordinal);
                bool staleTarget = _target.DisplayRoot == null;   // 目标原 Stage 已关：DisplayRoot 已销毁
                if (sameAsStage)
                {
                    if (!_target.ViaStage || staleTarget) BindTarget(_targetPrefab);
                    return;
                }

                // 另一个 Stage 打开：不覆盖用户选择；但目标悬空时回绑到自己的资产根（避免头显/属性树误判）。
                if (_target.ViaStage || staleTarget) BindTarget(_targetPrefab);
                return;
            }

            // 无 Stage：关掉 Stage 后目标仍是 Stage 副本（DisplayRoot 已销毁）→ 回绑资产根。
            if (_target != null && _target.ViaStage && _targetPrefab != null)
            {
                BindTarget(_targetPrefab);
                return;
            }

            if (_targetPrefab == null && Selection.activeObject is GameObject picked
                && PrefabUtility.IsPartOfPrefabAsset(picked))
            {
                BindTarget(picked);
            }
        }

        /// <summary>窗口重新获得焦点时恢复目标跟随（打开/切换 Stage 后回绑）。</summary>
        private void OnFocus()
        {
            if (_page != null && _state != null && _page.ReloadIfChanged(_state))
                _status = LogPrefix + " 清单已被外部工具更新，已重载";
            FollowTarget();
        }

        private void BindTarget(GameObject prefab)
        {
            _targetPrefab = prefab;
            string path = prefab == null ? null : AssetDatabase.GetAssetPath(prefab);
            _state.SetTarget(path);
            _target = prefab == null ? null : UiComposeTarget.Resolve(prefab);
        }

        private void OpenTargetStage()
        {
            if (_target == null) return;
            if (PrefabStageUtility.OpenPrefab(_target.PrefabPath) == null)
            {
                Log(LogPrefix + " Prefab Stage 打开失败：" + _target.PrefabPath);
                return;
            }

            // 打开后立刻重新解析：DisplayRoot/ViaStage 必须指向 Stage 副本（否则控件属性/生成会写资产根）。
            BindTarget(_targetPrefab);
            Log(LogPrefix + " 已打开 Prefab Stage：" + _target.PrefabPath);
        }

        private void OpenPreview()
        {
            if (_target == null) return;
            string deviceProfileId = _settings?.PreviewDeviceProfileId ?? string.Empty;
            UiPagePreview.Open(_target.PrefabPath, deviceProfileId);
            Log(LogPrefix + " 已打开布局预览：" + _target.PrefabPath +
                (string.IsNullOrEmpty(deviceProfileId) ? "（跟随预览）" : "（设备档 " + deviceProfileId + "）"));
        }

        private void ReloadDeviceProfiles()
        {
            try
            {
                _deviceDocument = UiDeviceProfileCatalog.Load();
            }
            catch (Exception exception)
            {
                _deviceDocument = null;
                _status = LogPrefix + " 设备档案装载失败：" + exception.Message;
            }
        }

    }
}
