using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱（菜单 <c>LiteGame/测试/GM 面板</c>）——**局内可调调试面唯一入口**的壳：
    /// 四页件（测试模式 / 命令与审计 / 场景操作 / 配置诊断）的统一窗口，页件契约见 <see cref="IGmPage"/>
    /// （烘焙工具箱页件范式同款）。分层裁决：静态启动配置 = 场景 DebugTuner；运行态规则 = MatchTestRules
    /// 每局持有件（开关写口唯一 TestModeOptions）；本窗口 = 纯投影（页件零业务逻辑，只读容器服务与请求桥）。
    /// 壳职责仅三件：页签生命周期（OnShow/OnHide）、Play 中 0.5s 轮询调度（仅活动页——非活动页不轮询）、
    /// 页签记忆（EditorPrefs 统一前缀 LiteGame.Gm.*）。
    /// </summary>
    public sealed class GmPanel : EditorWindow
    {
        private const string PrefTab = "LiteGame.Gm.Tab";

        private static readonly GUIContent[] Tabs =
        {
            new GUIContent("测试模式"),
            new GUIContent("命令与审计"),
            new GUIContent("场景操作"),
            new GUIContent("配置诊断"),
        };

        private readonly IGmPage[] _pages =
        {
            new GmTestModePage(),
            new GmCommandsPage(),
            new GmScenePage(),
            new GmConfigDiagnosticsPage(),
        };

        private int _tab;
        private double _nextPoll;
        private Vector2 _scroll;

        [MenuItem("LiteGame/测试/GM 面板")]
        private static void Open()
        {
            var window = GetWindow<GmPanel>("GM 面板");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _tab = Mathf.Clamp(EditorPrefs.GetInt(PrefTab, 0), 0, _pages.Length - 1);
            _pages[_tab].OnShow();
            _nextPoll = 0;
        }

        private void OnDisable()
        {
            _pages[_tab].OnHide();
            EditorPrefs.SetInt(PrefTab, _tab);
        }

        private void Update()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.5;
            _pages[_tab].Poll();
            Repaint();
        }

        private void OnGUI()
        {
            int next = GUILayout.Toolbar(_tab, Tabs);
            if (next != _tab)
            {
                _pages[_tab].OnHide();
                _tab = next;
                _pages[_tab].OnShow();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _pages[_tab].Draw();
            EditorGUILayout.EndScrollView();
        }
    }
}
