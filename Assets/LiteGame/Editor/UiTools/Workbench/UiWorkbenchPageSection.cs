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

        private Vector2 _scroll;

        /// <summary>选中页面（宿主据此绑定目标 prefab；只读行数据，不改清单）。</summary>
        public event Action<UiWorkbenchPageRow> PageSelected;

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
            state?.SelectPage(null);
        }

        public void Draw(UiWorkbenchState state, Func<UiWorkbenchAction, bool> canRun,
            Func<UiWorkbenchAction, string> disabledReason, Action<string> log)
        {
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                if (GUILayout.Button("重新读取清单", GUILayout.Width(120f))) Reload(state);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.Scaffold)))
                {
                    var content = new GUIContent("起页（P2-2）",
                        disabledReason(UiWorkbenchAction.Scaffold) ?? "起页");
                    // P2-1 阶段门控保证不可点；启用后由 P2-2 接 ui.page-scaffold（此处只留显式提示，不静默）。
                    if (GUILayout.Button(content, GUILayout.Width(110f)))
                        log("[ui/workbench] 起页归 P2-2（ui.page-scaffold），本批未提供");
                }

                using (new EditorGUI.DisabledScope(!canRun(UiWorkbenchAction.Register)))
                {
                    var content = new GUIContent("页面登记（P2-2）",
                        disabledReason(UiWorkbenchAction.Register) ?? "页面登记");
                    if (GUILayout.Button(content, GUILayout.Width(130f)))
                        log("[ui/workbench] 登记归 P2-2（ui.page-register），本批未提供");
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
                _owner = EditorGUILayout.TextField("Owner", _owner);
                _designRef = EditorGUILayout.TextField("设计稿", _designRef);
                _notes = EditorGUILayout.TextField("备注", _notes);
                _status = (UiPageStatus)EditorGUILayout.EnumPopup("状态", _status);

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
            log("[ui/workbench] 已应用到内存（未保存）：" + row.Name);
            return true;
        }

        private void Save(UiWorkbenchState state, Action<string> log) => TrySave(state, log);

        /// <summary>显式保存（窗口关闭提示走同一入口）；失败返回 false 且不写盘。</summary>
        public bool TrySave(UiWorkbenchState state, Action<string> log)
        {
            if (_manifest == null) return false;
            string selectedName = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Name : null;
            try
            {
                UiPageManifestStore.Save(_manifest, _manifestPath);
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
