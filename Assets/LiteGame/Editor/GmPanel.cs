using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 面板（编辑器）：测试模式入口 + 配置链路。
    /// 测试模式区——进入/退出测试模式（本地服 Room-Test）、传送到准心、生效开关一览；
    /// 配置链路四区——运行状态（Play 中 0.5s 刷新）/ 表查询（表清单反射自 cfg.Tables）/
    /// 场景操作（加载/卸载/叠加语义验证）/ 产物核对（TableDataFiles 逐项 File.Exists）。
    /// 配置持久化走 EditorPrefs；正式断言权威在测试项目（Luban.Runtime 带引擎依赖进不了 xUnit 双轨）。
    /// </summary>
    public sealed class GmPanel : EditorWindow
    {
        private const string PrefTable = "LiteGame.ConfigPanel.Table";
        private const string PrefKey = "LiteGame.ConfigPanel.Key";
        private const string PrefWriteLog = "LiteGame.ConfigPanel.WriteLog";
        private const string PrefScene = "LiteGame.ConfigPanel.Scene";
        private const float RefreshInterval = 0.5f;

        private string[] _tableNames = Array.Empty<string>();
        private int _tableIndex;
        private string _keyText = "1001";
        private bool _writeLog = true;
        private Vector2 _scroll;

        private string _status = "(未刷新)";
        private string _queryResult = "(未查询)";
        private readonly List<string> _missingFiles = new List<string>();
        private int _fileTotal;
        private double _nextRefresh;

        // 场景区
        private string[] _scenePaths = Array.Empty<string>();
        private int _sceneIndex = -1;
        private string _sceneLocation = "";
        private string _sceneState = "(未刷新)";
        private string _sceneResult = "(未操作)";
        private bool _sceneBusy;

        [MenuItem("LiteGame/测试/GM 面板")]
        private static void Open()
        {
            var window = GetWindow<GmPanel>("GM 面板");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _keyText = EditorPrefs.GetString(PrefKey, "1001");
            _writeLog = EditorPrefs.GetBool(PrefWriteLog, true);
            RefreshTables();
            RefreshScenePaths();
            RefreshStatus();
            _nextRefresh = 0;
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefKey, _keyText);
            EditorPrefs.SetBool(PrefWriteLog, _writeLog);
            EditorPrefs.SetString(PrefScene, _sceneLocation);
            if (_tableNames.Length > 0) EditorPrefs.SetString(PrefTable, _tableNames[_tableIndex]);
        }

        private void Update()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            RefreshStatus();
            RefreshSceneState();
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTestModeSection();
            EditorGUILayout.Space(6);
            DrawStatusSection();
            EditorGUILayout.Space(6);
            DrawQuerySection();
            EditorGUILayout.Space(6);
            DrawSceneSection();
            EditorGUILayout.Space(6);
            DrawFilesSection();
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "查询走运行时真实配置（需 Play 且配置已加载）；场景操作为机制级验证（加载/卸载/叠加语义）；产物核对为文件级预检，不依赖运行时。",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        // ---- ⓪ 测试模式 ----

        private void DrawTestModeSection()
        {
            EditorGUILayout.LabelField("测试模式", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(TestModeRuntime.Active ? "进行中（本地服 Room-Test）" : "未进入");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("进入测试模式"))
                {
                    TestModePanel.Apply();                  // 资产快照 → 运行时（未配置时套默认口径）
                    TestModeRuntime.EnterRequested = true;  // 主菜单消费 → 进测试房
                }
                if (GUILayout.Button("退出测试模式"))
                    TestModeRuntime.ExitRequested = true;   // 对局内 = 离场并关模式；主菜单 = 只关模式
                if (GUILayout.Button("传送到准心"))
                    TestModeRuntime.TeleportRequested = true;
            }
            if (GUILayout.Button("打开测试面板（配置开关）"))
                EditorWindow.GetWindow<TestModePanel>("测试面板");

            EditorGUILayout.LabelField(
                $"免死={TestModeRuntime.NoDeath} 缩放={TestModeRuntime.TimeScale:0.##} 暂停={TestModeRuntime.Paused} "
                + $"传送={TestModeRuntime.TeleportEnabled} 冻结={TestModeRuntime.BotFrozen} bot={TestModeRuntime.BotCount}",
                EditorStyles.wordWrappedLabel);
            if (Application.isPlaying
                && UnityEngine.Object.FindObjectsByType<DebugTuner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                EditorGUILayout.HelpBox("场景未挂 DebugTuner：时间缩放/暂停不会生效（其余开关不受影响）。", MessageType.Warning);
        }

        // ---- ① 运行状态 ----

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("立即刷新", GUILayout.Width(90)))
                {
                    RefreshTables();
                    RefreshStatus();
                }
                EditorGUILayout.LabelField(Application.isPlaying ? "Play 中（0.5s 自动刷新）" : "非 Play 状态");
            }
        }

        private void RefreshStatus()
        {
            string fsmState = "—";
            var fsm = GetFsm();
            if (fsm != null) fsmState = fsm.Started ? fsm.Current.ToString() : "(未启动)";

            var config = GetConfigService();
            string configText = config == null ? "未装配" : config.Loaded ? "已加载" : "未加载";

            _status = $"Play={(Application.isPlaying ? "是" : "否")}｜AssetService={(AssetService.Initialized ? "就绪" : "未初始化")}"
                      + $"｜配置={configText}｜FSM={fsmState}｜ErrorCount={Log.ErrorCount}";
        }

        // ---- ② 表查询 ----

        private void DrawQuerySection()
        {
            EditorGUILayout.LabelField("表查询（表清单由 cfg.Tables 反射自动发现）", EditorStyles.boldLabel);
            if (_tableNames.Length == 0)
            {
                EditorGUILayout.HelpBox("暂无表：需 Play 且配置已加载后刷新。", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(_tableNames.Length == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                _tableIndex = EditorGUILayout.Popup(_tableIndex, _tableNames, GUILayout.Width(180));
                _keyText = EditorGUILayout.TextField(_keyText);
                if (GUILayout.Button("查询", GUILayout.Width(60))) DoQuery();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("枚举全部表行数", GUILayout.Width(130))) DoEnumerateAll();
                _writeLog = EditorGUILayout.ToggleLeft("结果写入日志（tag Smoke）", _writeLog, GUILayout.Width(200));
            }

            EditorGUILayout.LabelField("结果", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(_queryResult, GUILayout.MinHeight(80));
        }

        private void DoQuery()
        {
            var tables = GetTables();
            if (tables == null)
            {
                SetQueryResult("配置未加载——请先进 Play 并等 FSM 到达 ProcedureMain（状态区可看）");
                return;
            }

            string tableName = _tableNames[_tableIndex];
            try
            {
                PropertyInfo prop = tables.GetType().GetProperty(tableName);
                object table = prop?.GetValue(tables);
                if (table == null) { SetQueryResult($"表 {tableName} 反射取值为 null"); return; }

                object dataMap = table.GetType().GetProperty("DataMap")?.GetValue(table);
                int rowCount = dataMap == null ? -1 : (int)dataMap.GetType().GetProperty("Count").GetValue(dataMap);

                object row;
                try
                {
                    row = InvokeGet(table, _keyText);
                }
                catch (Exception getEx) when (Unwrap(getEx) is KeyNotFoundException)
                {
                    // Luban 表未命中是抛 KeyNotFoundException——归"未命中"语义，不是异常
                    SetQueryResult($"{tableName} 共 {rowCount} 行\r\n未命中：key=\"{_keyText}\" 不存在（表内主键见列表）");
                    return;
                }

                if (row == null)
                {
                    SetQueryResult($"{tableName} 共 {rowCount} 行\r\n未命中：key=\"{_keyText}\"（主键类型不匹配或无此重载）");
                    return;
                }

                SetQueryResult($"{tableName} 共 {rowCount} 行，命中 key=\"{_keyText}\"：\r\n{DumpRow(row)}");
            }
            catch (Exception ex)
            {
                SetQueryResult($"{tableName} 查询异常：{Unwrap(ex).Message}");
            }
        }

        private void DoEnumerateAll()
        {
            var tables = GetTables();
            if (tables == null) { SetQueryResult("配置未加载——无法枚举"); return; }

            var lines = new List<string>();
            foreach (var name in _tableNames)
            {
                try
                {
                    object table = tables.GetType().GetProperty(name)?.GetValue(tables);
                    object dataMap = table?.GetType().GetProperty("DataMap")?.GetValue(table);
                    int count = dataMap == null ? -1 : (int)dataMap.GetType().GetProperty("Count").GetValue(dataMap);
                    lines.Add($"{name,-24} {count} 行");
                }
                catch (Exception ex)
                {
                    lines.Add($"{name,-24} 异常：{Unwrap(ex).Message}");
                }
            }
            SetQueryResult(string.Join("\r\n", lines));
        }

        /// <summary>int key 优先；非数字则退化 string key（Luban 表按主键类型二选一）。</summary>
        private static object InvokeGet(object table, string keyText)
        {
            Type tableType = table.GetType();
            if (int.TryParse(keyText, out int id))
            {
                MethodInfo getInt = tableType.GetMethod("Get", new[] { typeof(int) });
                if (getInt != null) return getInt.Invoke(table, new object[] { id });
            }
            MethodInfo getStr = tableType.GetMethod("Get", new[] { typeof(string) });
            if (getStr != null && !int.TryParse(keyText, out _)) return getStr.Invoke(table, new object[] { keyText });
            return null;
        }

        private static string DumpRow(object row)
        {
            var sb = new System.Text.StringBuilder();
            foreach (FieldInfo f in row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                sb.Append("  ").Append(f.Name).Append(" = ").Append(f.GetValue(row) ?? "null").AppendLine();
            return sb.ToString().TrimEnd();
        }

        private void SetQueryResult(string text)
        {
            _queryResult = text;
            if (_writeLog) Log.Info(text.Replace("\r\n", " | "), "Smoke");
        }

        // ---- ③ 场景服务（加载机制验证：单场景切换 / 叠加） ----

        private void DrawSceneSection()
        {
            EditorGUILayout.LabelField("场景服务（SceneService：单场景切换 / 叠加）", EditorStyles.boldLabel);

            // 场景下拉：EditorBuildSettings 清单（可加载的前提之一；收集组见 BundleCollectorSetting）
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_scenePaths.Length > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    _sceneIndex = EditorGUILayout.Popup(_sceneIndex, _scenePaths, GUILayout.Width(240));
                    if (EditorGUI.EndChangeCheck()) _sceneLocation = _scenePaths[_sceneIndex];
                }
                _sceneLocation = EditorGUILayout.TextField(_sceneLocation);
                if (GUILayout.Button("↻", GUILayout.Width(24))) RefreshScenePaths();
            }

            bool ready = Application.isPlaying && AssetService.Initialized && !_sceneBusy;
            using (new EditorGUI.DisabledScope(!ready))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("单场景加载")) RunSceneOp(() => GetSceneService().LoadSingleAsync(_sceneLocation), "单场景加载").Forget();
                if (GUILayout.Button("叠加加载")) RunSceneOp(() => GetSceneService().LoadAdditiveAsync(_sceneLocation), "叠加加载").Forget();
                if (GUILayout.Button("卸载单场景")) RunSceneOp(() => GetSceneService().UnloadSingleAsync(), "卸载单场景").Forget();
                if (GUILayout.Button("卸载叠加")) RunSceneOp(() => GetSceneService().UnloadAdditiveAsync(_sceneLocation), "卸载叠加").Forget();
            }

            if (!Application.isPlaying) EditorGUILayout.HelpBox("场景操作需 Play（AssetService 初始化后才可加载）", MessageType.Info);
            else if (_sceneBusy) EditorGUILayout.HelpBox("场景操作进行中……", MessageType.None);

            EditorGUILayout.LabelField(_sceneState, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("结果", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(_sceneResult, GUILayout.MinHeight(40));
        }

        /// <summary>场景操作（fire-and-forget + 忙碌守卫 + 结果落面板；异常不外泄到编辑器）。</summary>
        private async UniTaskVoid RunSceneOp(Func<UniTask> op, string label)
        {
            ISceneService scene = GetSceneService();
            if (scene == null) { _sceneResult = $"{label}失败：SceneService 未装配"; return; }

            _sceneBusy = true;
            _sceneResult = $"{label}中……";
            try
            {
                await op();
                _sceneResult = $"{label}完成：{DescribeScenes(scene)}";
            }
            catch (Exception ex)
            {
                _sceneResult = $"{label}失败：{ex.Message}";
            }
            finally
            {
                _sceneBusy = false;
                RefreshSceneState();
                Repaint();
            }
        }

        private void RefreshScenePaths()
        {
            var paths = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (!string.IsNullOrEmpty(s.path)) paths.Add(s.path);
            _scenePaths = paths.ToArray();

            string remembered = EditorPrefs.GetString(PrefScene, string.Empty);
            int index = Array.IndexOf(_scenePaths, remembered);
            if (index < 0) index = _scenePaths.Length > 0 ? 0 : -1;
            _sceneIndex = index;
            _sceneLocation = index >= 0 ? _scenePaths[index] : remembered;
        }

        private void RefreshSceneState()
        {
            ISceneService scene = Application.isPlaying ? GetSceneService() : null;
            _sceneState = scene == null
                ? "场景状态：未运行（Play 后可用）"
                : DescribeScenes(scene);
        }

        private static string DescribeScenes(ISceneService scene)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("单场景=").Append(scene.SingleSceneName ?? "(无)")
              .Append("｜叠加数=").Append(scene.AdditiveCount);
            if (scene.AdditiveCount > 0)
            {
                sb.Append("｜叠加列表=");
                bool first = true;
                foreach (string loc in scene.AdditiveLocations)
                {
                    if (!first) sb.Append("、");
                    sb.Append(System.IO.Path.GetFileNameWithoutExtension(loc));
                    first = false;
                }
            }
            sb.Append("（描述用；权威读数见 Unity 场景列表）");
            return sb.ToString();
        }

        private ISceneService GetSceneService() => ResolveFromContainer<ISceneService>();

        // ---- ④ 产物核对 ----

        private void DrawFilesSection()
        {
            EditorGUILayout.LabelField("配置产物核对（GameData/Config）", EditorStyles.boldLabel);
            if (GUILayout.Button("核对文件清单", GUILayout.Width(130))) RefreshFiles();

            if (_fileTotal == 0)
            {
                EditorGUILayout.LabelField("(未核对)");
                return;
            }

            if (_missingFiles.Count == 0)
                EditorGUILayout.LabelField($"✔ {_fileTotal}/{_fileTotal} 产物齐（缺失一个即会在 Play 启动时停机 ProcedureError）");
            else
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField($"✘ 缺失 {_missingFiles.Count}/{_fileTotal}：{string.Join("、", _missingFiles)}");
                GUI.color = prev;
            }
        }

        private void RefreshFiles()
        {
            _missingFiles.Clear();
            _fileTotal = ConfigService.TableDataFiles.Length;
            foreach (string file in ConfigService.TableDataFiles)
            {
                string path = $"Assets/GameData/Config/{file}.bytes";
                if (!File.Exists(path)) _missingFiles.Add(file);
            }
        }

        // ---- 取件（Editor 工具豁免：反射进容器，业务侧禁止此形态）----

        private void RefreshTables()
        {
            var tables = GetTables();
            if (tables == null) { _tableNames = Array.Empty<string>(); return; }

            var names = new List<string>();
            foreach (PropertyInfo p in tables.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.PropertyType.IsClass) names.Add(p.Name);
            names.Sort(StringComparer.Ordinal);
            if (names.Count == 0) { _tableNames = Array.Empty<string>(); return; }

            string remembered = EditorPrefs.GetString(PrefTable, string.Empty);
            _tableNames = names.ToArray();
            int index = System.Array.IndexOf(_tableNames, remembered);
            if (index < 0) index = System.Array.IndexOf(_tableNames, "Tbitemconfig");   // 首次打开默认道具表
            _tableIndex = Mathf.Clamp(index, 0, _tableNames.Length - 1);
        }

        private static object GetTables()
        {
            var config = GetConfigService();
            if (config == null || !config.Loaded) return null;
            try { return config.Tables; }
            catch (Exception) { return null; }
        }

        private static IConfigService GetConfigService() => ResolveFromContainer<IConfigService>();

        private static StageMachine<ProcedureId, ProcedureArgs> GetFsm()
            => ResolveFromContainer<StageMachine<ProcedureId, ProcedureArgs>>();

        private static T ResolveFromContainer<T>() where T : class
        {
            var entry = UnityEngine.Object.FindAnyObjectByType<GameEntry>();
            if (entry == null) return null;
            FieldInfo field = typeof(GameEntry).GetField("s_container", BindingFlags.NonPublic | BindingFlags.Static);
            if (field?.GetValue(null) is not ServiceContainer container) return null;
            try { return container.Resolve<T>(); }
            catch (Exception) { return null; }
        }

        private static Exception Unwrap(Exception ex)
            => ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
    }
}
