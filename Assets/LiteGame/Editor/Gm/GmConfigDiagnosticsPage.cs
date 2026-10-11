using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LiteClient;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·配置诊断页（原三区合一）：运行状态（FSM/AssetService/配置装载）+ 表查询
    /// （cfg.Tables 反射自动发现，Luban 主键 int 优先退化 string）+ 产物核对（TableDataFiles 逐表预检）。
    /// 产物核对路径走 <see cref="TableLayout.TableDataPath"/> 单源（领域分类布局——禁第二份拼接）；
    /// 正式断言权威在测试项目（Luban.Runtime 带引擎依赖进不了 xUnit 双轨），本页为文件级预检。
    /// </summary>
    internal sealed class GmConfigDiagnosticsPage : IGmPage
    {
        private const string PrefTable = "LiteGame.Gm.Query.Table";
        private const string PrefKey = "LiteGame.Gm.Query.Key";
        private const string PrefWriteLog = "LiteGame.Gm.Query.WriteLog";
        private const string DefaultTableName = "Item/tbitemconfig";   // 首次打开默认道具表（TableLayout.Files 领域相对名）

        private string[] _tableNames = Array.Empty<string>();
        private int _tableIndex;
        private string _keyText = "1001";
        private bool _writeLog = true;

        private string _status = "(未刷新)";
        private string _queryResult = "(未查询)";
        private readonly List<string> _missingFiles = new List<string>();
        private int _fileTotal;

        public GUIContent Title => new GUIContent("配置诊断");

        public void OnShow()
        {
            _keyText = EditorPrefs.GetString(PrefKey, "1001");
            _writeLog = EditorPrefs.GetBool(PrefWriteLog, true);
            RefreshTables();
            RefreshStatus();
        }

        public void OnHide()
        {
            EditorPrefs.SetString(PrefKey, _keyText);
            EditorPrefs.SetBool(PrefWriteLog, _writeLog);
            if (_tableNames.Length > 0) EditorPrefs.SetString(PrefTable, _tableNames[_tableIndex]);
        }

        public void Poll() => RefreshStatus();

        public void Draw()
        {
            DrawStatusSection();
            EditorGUILayout.Space(6);
            DrawQuerySection();
            EditorGUILayout.Space(6);
            DrawFilesSection();
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "查询走运行时真实配置（需 Play 且配置已加载）；产物核对为文件级预检，不依赖运行时。",
                MessageType.Info);
        }

        // ---- 运行状态（Play 中 0.5s 刷新）----

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

        // ---- 表查询 ----

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
                catch (Exception getEx) when (GmEditorResolve.Unwrap(getEx) is KeyNotFoundException)
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
                SetQueryResult($"{tableName} 查询异常：{GmEditorResolve.Unwrap(ex).Message}");
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
                    lines.Add($"{name,-24} 异常：{GmEditorResolve.Unwrap(ex).Message}");
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

        // ---- 产物核对 ----

        private void DrawFilesSection()
        {
            EditorGUILayout.LabelField("配置产物核对（GameData/Config 领域分类）", EditorStyles.boldLabel);
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
                string path = TableLayout.TableDataPath(file);      // 路径口径单源（领域分类布局）
                if (!File.Exists(path)) _missingFiles.Add(file);
            }
        }

        // ---- 取件 ----

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
            if (index < 0) index = System.Array.IndexOf(_tableNames, DefaultTableName);
            _tableIndex = Mathf.Clamp(index, 0, _tableNames.Length - 1);
        }

        private static object GetTables()
        {
            var config = GetConfigService();
            if (config == null || !config.Loaded) return null;
            try { return config.Tables; }
            catch (Exception) { return null; }
        }

        private static IConfigService GetConfigService() => GmEditorResolve.ResolveFromContainer<IConfigService>();

        private static StageMachine<ProcedureId, ProcedureArgs> GetFsm()
            => GmEditorResolve.ResolveFromContainer<StageMachine<ProcedureId, ProcedureArgs>>();
    }
}
