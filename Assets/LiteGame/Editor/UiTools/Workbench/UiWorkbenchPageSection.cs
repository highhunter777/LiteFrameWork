using System;
using System.Collections.Generic;
using System.Linq;
using LiteGame.Editor.UiTools.Page;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 页面透视（《UI 编辑器工具链专项设计》§2.2 / §3.1 / §3.6）：页面清单只读列表 + 维护字段编辑 +
    /// 起页/登记入口占位（P2-2）。清单纯内存编辑、显式保存；缺清单/损坏显式报错，不伪造空表。
    /// </summary>
    public sealed class UiWorkbenchPageSection
    {
        private UiPageManifest _manifest;
        private List<UiWorkbenchPageRow> _rows = new List<UiWorkbenchPageRow>();
        private string _error;
        private string _manifestPath = UiPageManifestPaths.DefaultPath;
        private int _selected = -1;

        private string _owner;
        private string _designRef;
        private string _notes;
        private UiPageStatus _status;
        private string _loadedSourceHash;

        private Vector2 _scroll;

        /// <summary>选中页面（宿主据此绑定目标 prefab；只读行数据，不改清单）。</summary>
        public event Action<UiWorkbenchPageRow> PageSelected;

        /// <summary>起页请求（宿主打开 `ui.page-scaffold` 窗口）。</summary>
        public event Action ScaffoldRequested;

        /// <summary>登记请求（宿主打开 `ui.page-register` 窗口并预填选中页）。</summary>
        public event Action RegisterRequested;

        /// <summary>清单在磁盘上被外部窗口改过（保存前需刷新，避免陈旧覆盖）。</summary>
        public bool ExternalChanged { get; private set; }

        public IReadOnlyList<UiWorkbenchPageRow> Rows => _rows;

        public int SelectedIndex => _selected;

        public string Error => _error;

        public int Count => _rows.Count;
        public int RowCount => _rows.Sum(row => row.FormIds.Count);

        /// <summary>读默认清单（缺失/损坏显式错误；不清空窗口其它上下文）。</summary>
        public void Reload(UiWorkbenchState state) => Reload(state, UiPageManifestPaths.DefaultPath);

        /// <summary>读指定清单（夹具注入路径用）。</summary>
        public void Reload(UiWorkbenchState state, string manifestPath)
        {
            _manifestPath = string.IsNullOrEmpty(manifestPath) ? UiPageManifestPaths.DefaultPath : manifestPath;
            _rows = new List<UiWorkbenchPageRow>();
            _manifest = null;
            _error = null;

            IReadOnlyList<UiWorkbenchPageRow> loaded = UiWorkbenchPageList.Load(_manifestPath, out string error);
            if (error != null)
            {
                _error = error;
            }
            else
            {
                _rows = loaded.ToList();
                try
                {
                    _manifest = UiPageManifestStore.Load(_manifestPath);
                }
                catch (Exception exception)
                {
                    _manifest = null;
                    _error = "页面清单不可读：" + exception.Message;
                    _rows = new List<UiWorkbenchPageRow>();
                }
            }

            _selected = -1;
            _loadedSourceHash = _manifest?.SourceHash;
            ExternalChanged = false;
            state?.SelectPage(null);
        }

        public void Draw(UiWorkbenchState state, Func<UiWorkbenchAction, bool> canRun,
            Func<UiWorkbenchAction, string> disabledReason, Action<string> log)
        {
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                if (GUILayout.Button("重新读取清单", GUILayout.Width(120f))) Reload(state, _manifestPath);
                return;
            }

            if (ExternalChanged)
            {
                EditorGUILayout.HelpBox("清单已被外部工具修改；当前未保存编辑未覆盖。", MessageType.Warning);
                if (GUILayout.Button("放弃本地编辑并重新读取", GUILayout.Width(190f)))
                {
                    Reload(state, _manifestPath);
                    log("[ui/workbench] 已重新读取外部清单（本地未保存编辑已放弃）");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.Scaffold)))
                {
                    var content = new GUIContent("起页", disabledReason(UiWorkbenchAction.Scaffold) ?? "起页（仅 prefab + 草稿）");
                    if (GUILayout.Button(content, GUILayout.Width(90f))) RequestScaffold();
                }

                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.Register)))
                {
                    var content = new GUIContent("页面登记", disabledReason(UiWorkbenchAction.Register) ?? "页面登记（表/Lua/清单）");
                    if (GUILayout.Button(content, GUILayout.Width(110f))) RequestRegister();
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("页面 " + Count + " 条 / 行 " + RowCount, EditorStyles.miniLabel,
                    GUILayout.Width(150f));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(220f));
            for (int i = 0; i < _rows.Count; i++)
            {
                UiWorkbenchPageRow row = _rows[i];
                using (new EditorGUILayout.HorizontalScope(i == _selected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(row.Name, EditorStyles.label, GUILayout.Width(170f))) Select(state, i);
                    EditorGUILayout.LabelField(row.Status, EditorStyles.miniLabel, GUILayout.Width(80f));
                    EditorGUILayout.LabelField(row.Owner, EditorStyles.miniLabel, GUILayout.Width(110f));
                    EditorGUILayout.LabelField(row.ContractPresent ? "契约 ✓" : "契约 —", EditorStyles.miniLabel,
                        GUILayout.Width(70f));
                    EditorGUILayout.LabelField("行 " + row.FormIds.Count, EditorStyles.miniLabel, GUILayout.Width(50f));
                }
            }

            EditorGUILayout.EndScrollView();

            if (_selected < 0 || _selected >= _rows.Count)
            {
                EditorGUILayout.HelpBox("选择一条页面以编辑维护字段（Owner/设计稿/备注/状态）。", MessageType.Info);
                return;
            }

            DrawEditor(state, _rows[_selected], canRun, disabledReason, log);
        }

        private void DrawEditor(UiWorkbenchState state, UiWorkbenchPageRow row,
            Func<UiWorkbenchAction, bool> canRun, Func<UiWorkbenchAction, string> disabledReason, Action<string> log)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("维护字段：" + row.Name, EditorStyles.boldLabel);

            bool canEdit = canRun(UiWorkbenchAction.EditMetadata);
            if (!canEdit)
            {
                EditorGUILayout.HelpBox(
                    "当前 Lane 不能编辑页面元数据：" + disabledReason(UiWorkbenchAction.EditMetadata),
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!canEdit))
            {
                EditorGUI.BeginChangeCheck();
                _owner = EditorGUILayout.TextField("Owner", _owner);
                _designRef = EditorGUILayout.TextField("设计稿", _designRef);
                _notes = EditorGUILayout.TextField("备注", _notes);
                _status = (UiPageStatus)EditorGUILayout.EnumPopup("状态", _status);
                if (EditorGUI.EndChangeCheck()) state.MarkDirty();   // 未 Apply 的输入也算未保存

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("应用到内存", GUILayout.Width(100f))) Apply(row, state, log);
                    using (new EditorGUI.DisabledScope(!state.HasUnsavedEdits))
                    {
                        if (GUILayout.Button("保存清单", GUILayout.Width(90f))) Save(state, log);
                    }

                    if (GUILayout.Button("放弃编辑", GUILayout.Width(90f))) LoadEditor(row);
                }
            }

            EditorGUILayout.LabelField(row.PrefabPath, EditorStyles.miniLabel);
        }

        /// <summary>请求起页（窗口按钮与夹具共用入口）。</summary>
        public void RequestScaffold() => ScaffoldRequested?.Invoke();

        /// <summary>请求登记（窗口按钮与夹具共用入口）。</summary>
        public void RequestRegister() => RegisterRequested?.Invoke();

        /// <summary>磁盘清单内容指纹变化且无未保存编辑时重载并保留选中；返回是否重载。</summary>
        public bool ReloadIfChanged(UiWorkbenchState state)
        {
            if (state == null || !ManifestChangedOnDisk()) return false;
            if (state.HasUnsavedEdits)
            {
                ExternalChanged = true;
                return false;
            }

            string selected = state.SelectedPage;
            string target = state.TargetPath;
            Reload(state, _manifestPath);
            if (!string.IsNullOrEmpty(selected))
            {
                int index = _rows.FindIndex(row => string.Equals(row.Name, selected, StringComparison.Ordinal));
                if (index >= 0) Select(state, index);
            }
            state.SetTarget(target);
            return true;
        }

        private bool HasUnappliedEdits(string pageName)
        {
            UiPageManifestEntry entry = _manifest?.Pages?.Find(item => item != null &&
                string.Equals(item.Name, pageName, StringComparison.Ordinal));
            if (entry == null) return false;
            return !string.Equals(_owner ?? string.Empty, entry.Owner ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(_designRef ?? string.Empty, entry.DesignRef ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(_notes ?? string.Empty, entry.Notes ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(_status.ToString(), entry.Status ?? string.Empty, StringComparison.Ordinal);
        }

        private bool ManifestChangedOnDisk()
        {
            try
            {
                UiPageManifest disk = UiPageManifestStore.LoadIfExists(_manifestPath);
                return !string.Equals(disk?.SourceHash, _loadedSourceHash, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>当前内存清单的指纹（夹具用于断言“失败不动清单”）。</summary>
        public string SourceHash => _manifest?.SourceHash;

        /// <summary>编辑**原子**：在副本上依次应用维护字段与状态，全部成功才提交到内存并标脏。</summary>
        private void Apply(UiWorkbenchPageRow row, UiWorkbenchState state, Action<string> log)
            => TryApplyEdits(state, row, _owner, _designRef, _notes, _status, log);

        /// <summary>显式编辑入口（窗口按钮与夹具共用）：任一步失败都不改动内存清单。</summary>
        public bool TryApplyEdits(UiWorkbenchState state, UiWorkbenchPageRow row, string owner,
            string designRef, string notes, UiPageStatus status, Action<string> log)
        {
            if (_manifest == null || row == null) return false;

            UiPageManifest working;
            try
            {
                working = UiPageManifestJson.Deserialize(UiPageManifestJson.Serialize(_manifest));
            }
            catch (Exception exception)
            {
                log("[ui/workbench] 清单复制失败（未改动）：" + exception.Message);
                return false;
            }

            if (!UiWorkbenchPageEditor.ApplyEditorial(working, row.Name, owner, designRef, notes,
                    out string editorialError))
            {
                log("[ui/workbench] 应用失败：" + editorialError);
                return false;
            }

            if (!UiWorkbenchPageEditor.ApplyStatus(working, row.Name, status, out string statusError))
            {
                log("[ui/workbench] 状态迁移失败（本次编辑未生效）：" + statusError);
                return false;
            }

            _manifest = working;
            state.MarkDirty();

            // 应用成功同步编辑器字段与行投影：调用方显式传值（夹具/工具）与 UI 按钮走同一状态。
            UiPageManifestEntry applied = working.Pages.Find(item => item != null &&
                string.Equals(item.Name, row.Name, StringComparison.Ordinal));
            _owner = applied?.Owner ?? owner;
            _designRef = applied?.DesignRef ?? designRef;
            _notes = applied?.Notes ?? notes;
            if (applied != null && UiPageStatusRules.TryParse(applied.Status, out UiPageStatus appliedStatus))
                _status = appliedStatus;
            else
                _status = status;
            row.Owner = applied?.Owner;
            row.DesignRef = applied?.DesignRef;
            row.Notes = applied?.Notes;
            row.Status = applied?.Status;
            log("[ui/workbench] 已应用到内存（未保存）：" + row.Name);
            return true;
        }

        private void Save(UiWorkbenchState state, Action<string> log) => TrySave(state, log);

        /// <summary>显式保存（窗口关闭提示走同一入口）；失败返回 false 且不写盘。</summary>
        public bool TrySave(UiWorkbenchState state, Action<string> log)
        {
            if (_manifest == null) return false;
            if (ExternalChanged || ManifestChangedOnDisk())
            {
                ExternalChanged = true;
                log("[ui/workbench] 清单已被外部修改（磁盘指纹不一致），拒绝覆盖；请先刷新清单");
                return false;
            }

            // 输入即脏，但字段只有「应用到内存」才进 _manifest——未应用的编辑不允许保存后静默丢弃。
            if (_selected >= 0 && _selected < _rows.Count && HasUnappliedEdits(_rows[_selected].Name))
            {
                log("[ui/workbench] 有未应用的编辑：先「应用到内存」再保存（避免静默丢弃）");
                return false;
            }

            string selectedName = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Name : null;
            try
            {
                UiPageManifestStore.Save(_manifest, _manifestPath);
                _loadedSourceHash = _manifest.SourceHash;
                state.ClearDirty();
                log("[ui/workbench] 清单已保存：" + _manifestPath);
                Reload(state, _manifestPath);

                // 保存后保留选中行（重读只刷新投影，不改变作者上下文）。
                if (selectedName != null)
                {
                    int index = _rows.FindIndex(item => item.Name == selectedName);
                    if (index >= 0) Select(state, index);
                }

                return true;
            }
            catch (Exception exception)
            {
                log("[ui/workbench] 保存失败（未落盘或导入失败）：" + exception.Message);
                return false;
            }
        }

        /// <summary>选中页面：更新共享状态并通知宿主绑定目标（宿主订阅 <see cref="PageSelected"/>）。</summary>
        public void Select(UiWorkbenchState state, int index)
        {
            if (index < 0 || index >= _rows.Count) return;
            _selected = index;
            UiWorkbenchPageRow row = _rows[index];
            state.SelectPage(row.Name);
            state.SetTarget(row.PrefabPath);
            LoadEditor(row);
            PageSelected?.Invoke(row);
        }

        private void LoadEditor(UiWorkbenchPageRow row)
        {
            _owner = row.Owner ?? string.Empty;
            _designRef = row.DesignRef ?? string.Empty;
            _notes = row.Notes ?? string.Empty;
            _status = Enum.TryParse(row.Status, out UiPageStatus status) ? status : UiPageStatus.Draft;
        }
    }
}
