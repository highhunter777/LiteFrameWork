using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    /// <summary>
    /// 开发工具链 Hub：唯一顶层入口 LiteGame/工具链。
    /// 目录来自 scripts/gate/toolchain.json；Hub 只按 page/window/action/cli/docs 做薄适配。
    /// </summary>
    public sealed class LiteGameToolboxWindow : EditorWindow
    {
        private const string PrefSelected = "LiteGame.Toolchain.Selected";
        private const double PollInterval = 0.5;

        private EditorToolCatalog _catalog;
        private IEditorToolPage _page;
        private string _selectedId = string.Empty;

        /// <summary>当前选中的工具 id（CLI/测试读取；选中行为本身由目录驱动）。</summary>
        public string SelectedToolId { get { return _selectedId; } }
        private string _search = string.Empty;
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
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
                {
                    DrawSidebar();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawContent();
                }
            }
        }

        private void DrawSidebar()
        {
            EditorGUILayout.LabelField("LiteGame 工具链", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarTextField);
                if (GUILayout.Button("清空", GUILayout.Width(44)))
                {
                    _search = string.Empty;
                }
            }

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            if (_catalog != null && _catalog.Errors.Count == 0)
            {
                string query = (_search ?? string.Empty).Trim();
                foreach (ToolchainCategory category in _catalog.Categories())
                {
                    EditorToolDescriptor[] tools = _catalog.ToolsInCategory(category.id)
                        .Where(descriptor => Matches(descriptor, query))
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
                descriptor.DisplayKind + "  ·  写入面 " + descriptor.WritesLabel + (descriptor.Tool.destructive ? "（破坏性）" : string.Empty),
                EditorStyles.miniLabel);
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
