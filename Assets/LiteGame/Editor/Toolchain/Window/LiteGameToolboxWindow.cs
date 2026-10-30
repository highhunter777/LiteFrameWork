using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    /// <summary>
    /// 开发工具链 Hub：唯一顶层入口 LiteGame/工具链。
    /// 目录来自 scripts/gate/toolchain.json；Hub 只按 page/window/action/cli/docs 做薄适配，
    /// hud 是运行内载体（只说明），平面筛选按清单 plane 字段过滤。
    /// </summary>
    public sealed class LiteGameToolboxWindow : EditorWindow
    {
        private const string PrefSelected = "LiteGame.Toolchain.Selected";
        private const string PrefSidebarWidth = "LiteGame.Toolchain.SidebarWidth";
        private const float SidebarDefaultWidth = 300f;
        private const float SidebarMinWidth = 240f;
        private const float SidebarMaxWidth = 560f;
        private const float SplitterWidth = 5f;
        private const float SearchFieldHeight = 20f;
        private const float SearchFieldMinWidth = 120f;
        private const float ClearButtonWidth = 40f;
        private const double PollInterval = 0.5;

        /// <summary>平面筛选：索引 0＝全部；其余与清单 plane 取值一一对应。</summary>
        private static readonly string[] PlaneFilters =
        {
            string.Empty,
            ToolchainPlanes.Editor,
            ToolchainPlanes.Runtime,
            ToolchainPlanes.External,
            ToolchainPlanes.None,
        };

        private static readonly string[] PlaneFilterLabels = BuildPlaneFilterLabels();

        private static string[] BuildPlaneFilterLabels()
        {
            var labels = new string[PlaneFilters.Length];
            labels[0] = "全部平面";
            for (int index = 1; index < labels.Length; index++)
            {
                labels[index] = ToolchainPlanes.Label(PlaneFilters[index]);
            }
            return labels;
        }

        /// <summary>用途筛选：索引 0＝全部；其余与清单 purpose 取值一一对应。</summary>
        private static readonly string[] PurposeFilters =
        {
            string.Empty,
            ToolchainPurposes.Debug,
            ToolchainPurposes.Dev,
            ToolchainPurposes.Delivery,
        };

        private static readonly string[] PurposeFilterLabels = BuildPurposeFilterLabels();

        private static string[] BuildPurposeFilterLabels()
        {
            var labels = new string[PurposeFilters.Length];
            labels[0] = "全部用途";
            for (int index = 1; index < labels.Length; index++)
            {
                labels[index] = ToolchainPurposes.Label(PurposeFilters[index]);
            }
            return labels;
        }

        /// <summary>种类筛选：索引 0＝全部；其余与清单 kind 取值一一对应。</summary>
        private static readonly string[] KindFilters = BuildKindFilters();

        private static readonly string[] KindFilterLabels = BuildKindFilterLabels();

        private static string[] BuildKindFilters()
        {
            string[] kinds = ToolchainKinds.All();
            var filters = new string[kinds.Length + 1];
            filters[0] = string.Empty;
            for (int index = 0; index < kinds.Length; index++)
            {
                filters[index + 1] = kinds[index];
            }
            return filters;
        }

        private static string[] BuildKindFilterLabels()
        {
            var labels = new string[KindFilters.Length];
            labels[0] = "全部种类";
            for (int index = 1; index < labels.Length; index++)
            {
                labels[index] = ToolchainKinds.Label(KindFilters[index]);
            }
            return labels;
        }

        private EditorToolCatalog _catalog;
        private IEditorToolPage _page;
        private string _selectedId = string.Empty;

        /// <summary>当前选中的工具 id（CLI/测试读取；选中行为本身由目录驱动）。</summary>
        public string SelectedToolId { get { return _selectedId; } }
        private string _search = string.Empty;
        private int _planeIndex;
        private int _purposeIndex;
        private int _kindIndex;
        private float _sidebarWidth = SidebarDefaultWidth;
        private bool _resizingSidebar;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private double _nextPoll;

        [MenuItem("LiteGame/工具链", false, 0)]
        public static void Open()
        {
            OpenTool(null);
        }

        public static void Open(string id)
        {
            OpenTool(id);
        }

        private static void OpenTool(string id)
        {
            var window = GetWindow<LiteGameToolboxWindow>("LiteGame 工具链");
            window.minSize = new Vector2(800, 560);
            window.EnsureCatalog();
            if (!string.IsNullOrEmpty(id) && window._catalog != null)
            {
                if (window._catalog.Find(id) != null)
                {
                    window.SelectTool(id);
                }
                else
                {
                    Debug.LogWarning("[Toolchain] 未找到工具：" + id);
                }
            }
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            _sidebarWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(PrefSidebarWidth, SidebarDefaultWidth), SidebarMinWidth, SidebarMaxWidth);
            EnsureCatalog();
        }

        private void OnDisable()
        {
            if (_page != null)
            {
                _page.OnHide();
                _page = null;
            }
        }

        private void Update()
        {
            if (_page == null || !Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;
            _page.Poll();
            Repaint();
        }

        private void EnsureCatalog()
        {
            _catalog = EditorToolCatalog.Load();
            if (_catalog.Errors.Count > 0)
            {
                if (_page != null) _page.OnHide();
                _page = new ManifestErrorPage(_catalog.ManifestPath, _catalog.Errors, ReloadCatalog);
                _page.OnShow();
                _selectedId = string.Empty;
                return;
            }

            string remembered = EditorPrefs.GetString(PrefSelected, string.Empty);
            EditorToolDescriptor descriptor = _catalog.Find(remembered) ?? _catalog.FirstTool();
            if (descriptor != null)
            {
                SelectTool(descriptor.Id);
            }
        }

        private void ReloadCatalog()
        {
            if (_page != null)
            {
                _page.OnHide();
                _page = null;
            }
            _catalog = null;
            EnsureCatalog();
            Repaint();
        }

        private void SelectTool(string id)
        {
            if (_catalog == null || string.IsNullOrEmpty(id)) return;
            EditorToolDescriptor descriptor = _catalog.Find(id);
            if (descriptor == null) return;

            if (_page != null) _page.OnHide();
            _selectedId = id;
            _page = descriptor.CreatePage != null ? descriptor.CreatePage() : null;
            if (_page == null)
            {
                _page = new ErrorToolPage(descriptor.Title, "该工具没有可用的页面/打开行为。");
            }
            _page.OnShow();
            EditorPrefs.SetString(PrefSelected, id);
            Repaint();
        }

        private void OnGUI()
        {
            if (_catalog == null) EnsureCatalog();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(_sidebarWidth)))
                {
                    DrawSidebar();
                }
                DrawSidebarSplitter();
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawContent();
                }
            }
        }

        /// <summary>左右分栏拖拽条：宽度落 EditorPrefs（统一前缀 LiteGame.Toolchain.*），下次打开保持。</summary>
        private void DrawSidebarSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event current = Event.current;
            if (current == null) return;
            switch (current.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(current.mousePosition))
                    {
                        _resizingSidebar = true;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_resizingSidebar)
                    {
                        _sidebarWidth = Mathf.Clamp(
                            _sidebarWidth + current.delta.x, SidebarMinWidth, SidebarMaxWidth);
                        current.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_resizingSidebar)
                    {
                        _resizingSidebar = false;
                        EditorPrefs.SetFloat(PrefSidebarWidth, _sidebarWidth);
                        current.Use();
                    }
                    break;
            }
        }

        private void DrawSidebar()
        {
            EditorGUILayout.LabelField("LiteGame 工具链", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = GUILayout.TextField(
                    _search ?? string.Empty,
                    EditorStyles.toolbarTextField,
                    GUILayout.ExpandWidth(true),
                    GUILayout.MinWidth(SearchFieldMinWidth),
                    GUILayout.Height(SearchFieldHeight));
                if (GUILayout.Button("清空", GUILayout.Width(ClearButtonWidth), GUILayout.Height(SearchFieldHeight)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _planeIndex = EditorGUILayout.Popup(_planeIndex, PlaneFilterLabels);
                _purposeIndex = EditorGUILayout.Popup(_purposeIndex, PurposeFilterLabels);
                _kindIndex = EditorGUILayout.Popup(_kindIndex, KindFilterLabels);
            }

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            if (_catalog != null && _catalog.Errors.Count == 0)
            {
                string query = (_search ?? string.Empty).Trim();
                foreach (ToolchainCategory category in _catalog.Categories())
                {
                    EditorToolDescriptor[] tools = _catalog.ToolsInCategory(category.id)
                        .Where(descriptor =>
                            Matches(descriptor, query)
                            && MatchesPlane(descriptor)
                            && MatchesPurpose(descriptor)
                            && MatchesKind(descriptor))
                        .ToArray();
                    if (tools.Length == 0) continue;

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(category.title, EditorStyles.miniBoldLabel);
                    foreach (EditorToolDescriptor descriptor in tools)
                    {
                        bool selected = descriptor.Id == _selectedId;
                        Color previous = GUI.backgroundColor;
                        if (selected) GUI.backgroundColor = new Color(0.55f, 0.78f, 1f);
                        if (GUILayout.Button(descriptor.Title, EditorStyles.toolbarButton))
                        {
                            SelectTool(descriptor.Id);
                        }
                        GUI.backgroundColor = previous;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static bool Matches(EditorToolDescriptor descriptor, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            string haystack = descriptor.Id + " " + descriptor.Title + " " + descriptor.Tool.summary;
            return haystack.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchesPlane(EditorToolDescriptor descriptor)
        {
            string plane = PlaneFilters[_planeIndex];
            return string.IsNullOrEmpty(plane) || descriptor.Plane == plane;
        }

        private bool MatchesPurpose(EditorToolDescriptor descriptor)
        {
            string purpose = PurposeFilters[_purposeIndex];
            return string.IsNullOrEmpty(purpose) || descriptor.Purpose == purpose;
        }

        private bool MatchesKind(EditorToolDescriptor descriptor)
        {
            string kind = KindFilters[_kindIndex];
            return string.IsNullOrEmpty(kind) || descriptor.Kind == kind;
        }

        private void DrawContent()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("统一入口", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("重新加载清单", GUILayout.Width(110)))
                {
                    ReloadCatalog();
                }
            }

            if (_catalog != null)
            {
                EditorGUILayout.LabelField(_catalog.ManifestPath, EditorStyles.miniLabel);
            }

            if (_catalog != null && _catalog.Errors.Count > 0)
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                if (_page != null) _page.Draw();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorToolDescriptor descriptor = _catalog != null ? _catalog.Find(_selectedId) : null;
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("从左侧选择一个工具。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(descriptor.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "用途：" + descriptor.PurposeLabel
                + "　平面：" + descriptor.PlaneLabel + "（前置 " + descriptor.RequiresLabel + "）",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "种类：" + descriptor.DisplayKind
                + "　写入面：" + descriptor.WritesLabel + (descriptor.Tool.destructive ? "（破坏性）" : string.Empty),
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("产物：" + descriptor.ArtifactsLabel, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(descriptor.Tool.summary, MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开文档", GUILayout.Width(90)))
                {
                    ToolchainEditorUtility.OpenDocument(descriptor.Tool.docs);
                }
                if (!string.IsNullOrEmpty(descriptor.Tool.menu))
                {
                    EditorGUILayout.LabelField("菜单/别名：" + descriptor.Tool.menu, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            if (_page != null) _page.Draw();
            EditorGUILayout.EndScrollView();
        }
    }
}
