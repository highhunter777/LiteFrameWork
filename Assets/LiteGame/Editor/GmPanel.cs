using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEditor;
using UnityEngine;
using LiteClient;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 面板（编辑器）：**局内可调调试面**唯一入口。
    /// ① 测试模式——进入/退出/传送到准心（进入前把场景 DebugTuner 的启动配置快照进运行时）+ 现场开关（无限子弹/爆头框/瞄准激光/伤害数字，即时生效）+ 爆头区实时调；
    /// ② 命令与审计——CommandCenter 发现面（GetRegisteredInfos）列出 + 无参命令执行（走权限门/拦截器/审计）+ 审计环（GetAuditLog）；
    /// ③ 运行状态 / ④ 表查询 / ⑤ 场景操作 / ⑥ 产物核对——配置链路四区（Play 中 0.5s 刷新）。
    /// **静态启动配置不在本面板**：免死/人数/冻结/传送/时钟等旋钮与进房初值在场景 DebugTuner（Inspector）上编辑。
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
            if (_cmdAutoRefresh && EditorApplication.timeSinceStartup >= _nextCmdRefresh)
            {
                _nextCmdRefresh = EditorApplication.timeSinceStartup + 1.0;
                RefreshCommands();                 // 审计环低频轮询（发现面注释口径）
            }
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTestModeSection();
            EditorGUILayout.Space(6);
            DrawCommandSection();
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

        // ---- ① 测试模式（启动配置在场景 DebugTuner；本区只管进入/退出与局内可调项）----

        private void DrawTestModeSection()
        {
            EditorGUILayout.LabelField("测试模式", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(TestModeRuntime.Active ? "进行中（本地服 Room-Test）" : "未进入");

            using (new EditorGUILayout.HorizontalScope())
            {
                // 进入前把场景 DebugTuner 的启动配置快照进运行时（未挂则套默认口径）；F10 同一条链
                if (GUILayout.Button("进入测试模式")) { DebugTuner.ApplySnapshotOrDefaults(); TestModeRuntime.EnterRequested = true; }
                if (GUILayout.Button("退出测试模式")) TestModeRuntime.ExitRequested = true;              // 对局内 = 离场并关模式；主菜单 = 只关模式
                if (GUILayout.Button("传送到准心")) TestModeRuntime.TeleportRequested = true;
            }
            EditorGUILayout.HelpBox(
                "启动项（免死/bot 数/冻结/传送）与表现类开关的**进房初值**都在场景 DebugTuner 上编辑（Inspector，随场景保存）；"
                + "本节以下是**局内可调**项，即时生效。", MessageType.Info);

            EditorGUILayout.LabelField("现场开关（即时生效，不必重进房）", EditorStyles.miniBoldLabel);
            // 开关清单/显示名/写点全部经 TestModeOptions（新增开关只改那张表——本面板零改动）
            foreach (TestModeOptions.Info option in TestModeOptions.All)
            {
                if (option.Group != TestModeOptions.Group.Live) continue;      // 启动项在 DebugTuner（下方只读一览）
                bool value = GUILayout.Toggle(TestModeOptions.Get(option.Id), "  " + option.Label);
                if (value != TestModeOptions.Get(option.Id)) TestModeOptions.Set(option.Id, value);
            }

            var startup = new System.Text.StringBuilder("启动项（进房时快照）：");
            foreach (TestModeOptions.Info option in TestModeOptions.All)
            {
                if (option.Group != TestModeOptions.Group.Startup) continue;
                startup.Append(' ').Append(option.Id).Append('=').Append(TestModeOptions.Get(option.Id) ? "开" : "关");
            }
            startup.Append(" bot=").Append(TestModeRuntime.BotCount);
            EditorGUILayout.LabelField(startup.ToString(), EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选中场景 DebugTuner", GUILayout.Width(150)))
                {
                    var tuner = UnityEngine.Object.FindAnyObjectByType<DebugTuner>(FindObjectsInactive.Include);
                    if (tuner != null) Selection.activeObject = tuner;
                }
                EditorGUILayout.LabelField("启动配置在 Inspector 改（随场景保存）；改完再进房生效。", EditorStyles.miniLabel);
            }
            if (Application.isPlaying
                && UnityEngine.Object.FindObjectsByType<DebugTuner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                EditorGUILayout.HelpBox("场景未挂 DebugTuner：启动配置不可用（进房套默认口径），时钟旋钮也不生效。", MessageType.Warning);

            DrawHeadTuneBlock();
        }

        /// <summary>爆头区实时调（运行时覆写，不落盘；判定与身位可视化同读 Live——本地服同进程同值，滑杆一动实弹即见 Crit 档变化）。</summary>
        private static void DrawHeadTuneBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("爆头区实时调（运行时覆写，不落盘）", EditorStyles.boldLabel);
            float height = LiteSim.CombatConfig.HitscanHeight;
            float shown = LiteSim.CombatConfig.HeadHitLineDevOverride >= 0f
                ? LiteSim.CombatConfig.HeadHitLineDevOverride : LiteSim.CombatConfig.HeadHitLine;
            float radiusShown = LiteSim.CombatConfig.HeadshotRadiusDevOverride >= 0f
                ? LiteSim.CombatConfig.HeadshotRadiusDevOverride : LiteSim.CombatConfig.HeadshotRadius;
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider("爆头线下沿（m）", shown, 0.9f, height);
            float nextRadius = EditorGUILayout.Slider("爆头柱半径（m，窄于命中柱 0.45）", radiusShown, 0.15f, 0.45f);
            if (EditorGUI.EndChangeCheck())
            {
                LiteSim.CombatConfig.HeadHitLineDevOverride = next;
                LiteSim.CombatConfig.HeadshotRadiusDevOverride = nextRadius;
            }
            EditorGUILayout.LabelField($"比例 {shown / height:F3}    带高 {height - shown:F3} m    当前导出值：下沿 {LiteSim.CombatConfig.HeadHitLine:F3} m / 半径 {LiteSim.CombatConfig.HeadshotRadius:F3} m");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复位（回导出值）"))
                {
                    LiteSim.CombatConfig.HeadHitLineDevOverride = -1f;
                    LiteSim.CombatConfig.HeadshotRadiusDevOverride = -1f;
                }
                if (GUILayout.Button("导出当前爆头区到 HeadBake.g.cs"))
                {
                    string report = LiteGame.EditorTools.HeadHitLineTuner.Export(shown / height, radiusShown);
                    EditorUtility.DisplayDialog("爆头区导出", report, "OK");
                }
            }
            EditorGUILayout.HelpBox(
                "对局内滑动即生效（判定与身位可视化的黄/红圈实时随动，可实弹试 Crit 档——黄红圈之间的窄柱切片才是爆头区）；"
                + "定型才导出。对局中点导出会触发重编译并中断对局（domain reload）——可先记下数值，出对局后再导。",
                MessageType.Info);
        }

        // ---- ② 命令与审计（CommandCenter 发现/执行/审计面——《命令中心专项设计》§6）----

        private readonly List<CommandInfo> _cmds = new List<CommandInfo>();
        private readonly List<CommandAuditEntry> _audit = new List<CommandAuditEntry>();
        private string _cmdResult = "(未刷新)";
        private bool _cmdAutoRefresh = true;
        private double _nextCmdRefresh;

        private void DrawCommandSection()
        {
            EditorGUILayout.LabelField("命令与审计（CommandCenter）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新", GUILayout.Width(60))) RefreshCommands();
                _cmdAutoRefresh = EditorGUILayout.ToggleLeft("自动刷新（1s，Play 中）", _cmdAutoRefresh, GUILayout.Width(190));
                EditorGUILayout.LabelField(_cmdResult, EditorStyles.miniLabel);
            }

            CommandCenter center = GetCommandCenter();
            if (center == null)
            {
                EditorGUILayout.HelpBox("命令中心未装配（需 Play 且容器就绪）——列出/执行/审计均不可用。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("已注册命令（发现面清单，按注册序）", EditorStyles.miniBoldLabel);
            if (_cmds.Count == 0) EditorGUILayout.LabelField("(暂无注册命令——进 Play 后刷新)");
            foreach (CommandInfo info in _cmds)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{(info.GmOnly ? "[GM] " : "")}{info.CommandType.Name}", GUILayout.Width(220));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(info.Description) ? "—" : info.Description);
                    if (info.CommandType.GetConstructor(Type.EmptyTypes) == null)
                        EditorGUILayout.LabelField("需专面板", EditorStyles.miniLabel, GUILayout.Width(64));
                    else if (GUILayout.Button("执行", GUILayout.Width(48)))
                        RunDiscoveredCommand(center, info.CommandType);
                }
            }

            EditorGUILayout.LabelField("审计环（最近 32 条，旧→新）", EditorStyles.miniBoldLabel);
            if (_audit.Count == 0) EditorGUILayout.LabelField("(暂无审计记录)");
            foreach (CommandAuditEntry entry in _audit)
                EditorGUILayout.LabelField(
                    $"#{entry.Seq} {entry.CommandType?.Name} {(entry.Ok ? "OK" : $"拒:{entry.Reason}")} {entry.Detail}",
                    EditorStyles.miniLabel);
        }

        private void RefreshCommands()
        {
            CommandCenter center = GetCommandCenter();
            _cmds.Clear();
            _audit.Clear();
            if (center == null) { _cmdResult = "(未装配：需 Play)"; return; }
            _cmds.AddRange(center.GetRegisteredInfos());
            _audit.AddRange(center.GetAuditLog());
            _cmdResult = $"命令 {_cmds.Count} 条｜审计 {_audit.Count} 条｜{DateTime.Now:HH:mm:ss}";
        }

        /// <summary>执行发现的**无参**命令：Activator 构造 + 反射 Send&lt;TCommand&gt;（编辑器工具豁免；
        /// 带参命令在列表标注"需专面板"不在此执行）。权限门/拦截器/审计链零改动——GmOnly 拦截自然进审计环。</summary>
        private void RunDiscoveredCommand(CommandCenter center, Type commandType)
        {
            try
            {
                object command = Activator.CreateInstance(commandType);
                MethodInfo typed = typeof(CommandCenter).GetMethod(nameof(CommandCenter.Send)).MakeGenericMethod(commandType);
                var result = (CommandResult)typed.Invoke(center, new[] { command });
                _cmdResult = $"{commandType.Name} → {(result.Ok ? $"成功：{result.Detail}" : $"拒（{result.Reason}）：{result.Detail}")}";
            }
            catch (Exception ex)
            {
                _cmdResult = $"{commandType.Name} 执行异常：{Unwrap(ex).Message}";
            }
            RefreshCommands();
        }

        private static CommandCenter GetCommandCenter() => ResolveFromContainer<ICommandCenter>() as CommandCenter;

        // ---- ③ 运行状态（Play 中 0.5s 刷新）----

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

        // ---- ④ 表查询 ----

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

        // ---- ⑤ 场景服务（加载机制验证：单场景切换 / 叠加） ----

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

        // ---- ⑥ 产物核对 ----

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
            try { return entry.TakeContainer().Resolve<T>(); }
            catch (Exception) { return null; }   // 未装配（引导未完成）/未注册：面板各区显示空态
        }

        private static Exception Unwrap(Exception ex)
            => ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
    }
}
