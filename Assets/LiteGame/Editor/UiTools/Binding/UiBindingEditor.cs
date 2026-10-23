using System.Collections.Generic;
using System.Linq;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Compose;
using LiteGame.Editor.UiTools.Naming;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>
    /// 绑定编辑面（《UI编辑器工具专项设计》§2.1/§8.2）：前缀组合、批量重命名、预览与应用、
    /// 生成物 hash——作为 **UI 制作主窗口的内嵌分区** 使用，不再单独开窗。
    ///
    /// 写入通道两条：目标正在 Prefab Stage 打开 → 直接编辑 Stage 实例，全程 Unity Undo；
    /// 否则 → <see cref="PrefabUtility.LoadPrefabContents"/> 改后 <c>SaveAsPrefabAsset</c>，
    /// 故一切操作**预览先行**。选择/勾选变动即作废旧预览。
    /// </summary>
    public sealed class UiBindingEditor
    {
        private GameObject _prefab;
        private string _prefabPath;
        private List<UiNodeRow> _rows = new List<UiNodeRow>();
        private Vector2 _treeScroll;
        private Vector2 _previewScroll;

        private string _selectedPath;
        private readonly HashSet<string> _checked = new HashSet<string>();

        private string _prefix = "Button_";
        private string _semantic = string.Empty;
        private string _batchPrefix = "Txt_";

        private enum BatchOp
        {
            RetargetPrefix,
            SyncBindName,
        }

        private BatchOp _batchOp = BatchOp.RetargetPrefix;

        private List<RenamePlan> _previews = new List<RenamePlan>();
        private List<string> _previewErrors = new List<string>();
        private readonly Dictionary<string, List<UiRefHit>> _refHits = new Dictionary<string, List<UiRefHit>>();
        private readonly Dictionary<string, List<UiRefHit>> _hitCache = new Dictionary<string, List<UiRefHit>>();
        private int _rootId = -1;

        /// <summary>树行只读快照（测试与诊断用；窗口绘制仍以本类内部状态为准）。</summary>
        public IReadOnlyList<UiNodeRow> Rows => _rows;

        /// <summary>绑定一个 prefab 资产（目标切换时由主窗口调用；重置预览与勾选）。</summary>
        public void BindPrefab(GameObject asset)
        {
            if (asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset)) return;
            _prefab = asset;
            _prefabPath = AssetDatabase.GetAssetPath(asset);
            _selectedPath = null;
            _checked.Clear();
            _rootId = -1;
            _hitCache.Clear();
            ClearPreview();
            SyncRoot();
        }

        /// <summary>主窗口绘制入口；目标由 <see cref="UiComposeTarget"/> 单源解析。</summary>
        public void Draw(UiComposeTarget target)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("未绑定目标 UI prefab。", MessageType.Info);
                return;
            }

            if (target.PrefabAsset != _prefab || target.PrefabPath != _prefabPath)
                BindPrefab(target.PrefabAsset);
            SyncRoot();

            DrawHeader(target);
            DrawTree();
            DrawPresetSection();
            DrawBatchSection();
            DrawPreviewArea();
            DrawHashSection();
        }

        private GameObject DisplayRoot()
        {
            var stageRoot = UiComposeTarget.StageRootFor(_prefabPath);
            return stageRoot != null ? stageRoot : _prefab;
        }

        private void SyncRoot()
        {
            var root = DisplayRoot();
            int id = root != null ? root.GetInstanceID() : -1;
            if (id == _rootId) return;
            _rootId = id;
            RebuildRows();
            ClearPreview();
        }

        private void RebuildRows()
        {
            _rows = UiBindingRename.BuildTree(DisplayRoot());
            _checked.RemoveWhere(p => _rows.All(r => r.Path != p));
            if (_selectedPath != null && _rows.All(r => r.Path != _selectedPath)) _selectedPath = null;
        }

        // ---------------- 头部 ----------------

        private void DrawHeader(UiComposeTarget target)
        {
            var root = DisplayRoot();
            var bindRoot = root != null ? root.GetComponent<BindRoot>() : null;
            string rootNote = bindRoot != null
                ? $"BindRoot：{bindRoot.ScriptName}（{(string.IsNullOrEmpty(bindRoot.Namespace) ? "无命名空间" : bindRoot.Namespace)}）→ {bindRoot.ScriptsFolder}"
                : "无 BindRoot（页面经运行期索引消费；路径 B 生成未启用）";

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{_prefabPath}　·　{rootNote}", EditorStyles.miniLabel);
                if (GUILayout.Button("刷新", GUILayout.Width(48f)))
                {
                    _rootId = -1;
                    SyncRoot();
                }
            }
            EditorGUILayout.LabelField(
                target.ViaStage ? "Prefab Stage 打开——编辑走 Unity Undo，保存归 Stage"
                                : "未开 Stage——应用经 LoadPrefabContents 写资产文件（预览先行，应用即落盘）",
                EditorStyles.miniLabel);
        }

        // ---------------- 树 ----------------

        private void DrawTree()
        {
            if (UiBindTreeView.Draw(_rows, ref _treeScroll, ref _selectedPath, _checked, out var clicked))
                ClearPreview();
            if (clicked != null) PrefillPresetFromRow(clicked);
            EditorGUILayout.LabelField($"已勾选 {_checked.Count} 项", EditorStyles.miniLabel);
        }

        private void PrefillPresetFromRow(UiNodeRow row)
        {
            if (UiBindingRename.TryDecomposeName(row.Name, out string prefix, out string semantic)
                && UiNamingWords.IsKnownPrefix(prefix))
            {
                _prefix = prefix;
                _semantic = semantic;
            }
        }

        // ---------------- 前缀预设 ----------------

        private void DrawPresetSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("前缀预设（选中节点——非绑定节点仅改名，不同步索引）", EditorStyles.boldLabel);

            string[] prefixOptions = UiBindingRename.AllPrefixes().ToArray();
            int index = System.Array.IndexOf(prefixOptions, _prefix);
            _prefix = prefixOptions[EditorGUILayout.Popup("类别前缀", System.Math.Max(0, index), prefixOptions)];
            _semantic = EditorGUILayout.TextField("语义名", _semantic);

            string composed = null;
            if (UiBindingRename.TryComposeName(_prefix, _semantic, out composed, out string reason))
                EditorGUILayout.LabelField($"组合：{composed}", EditorStyles.miniBoldLabel);
            else
                EditorGUILayout.HelpBox(reason, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                string targetNote = _selectedPath != null ? $"目标：{_selectedPath}" : "未在树中选中节点";
                EditorGUILayout.LabelField(targetNote, EditorStyles.miniLabel);
                GUI.enabled = composed != null && _selectedPath != null;
                if (GUILayout.Button("预览所选", GUILayout.Width(80f))) BuildPreview(single: true);
                GUI.enabled = true;
            }
        }

        // ---------------- 批量 ----------------

        private void DrawBatchSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("批量操作（对勾选项）", EditorStyles.boldLabel);
            _batchOp = (BatchOp)EditorGUILayout.EnumPopup("操作", _batchOp);
            if (_batchOp == BatchOp.RetargetPrefix)
            {
                string[] prefixOptions = UiBindingRename.AllPrefixes().ToArray();
                int index = System.Array.IndexOf(prefixOptions, _batchPrefix);
                _batchPrefix = prefixOptions[EditorGUILayout.Popup("目标前缀", System.Math.Max(0, index), prefixOptions)];
                EditorGUILayout.HelpBox("换前缀（保留语义）：现名不在词表文法内、或语义段不合法的行自动跳过。", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("同文修复：BindName := 节点名（§9.1 同文纪律；Lua/C# 引用须按清单同批同步）。", MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览批量", GUILayout.Width(80f))) BuildPreview(single: false);
                GUI.enabled = _previews.Count > 0 && _previewErrors.Count == 0;
                if (GUILayout.Button($"应用（{_previews.Count}）", GUILayout.Width(110f))) ApplyPreview();
                GUI.enabled = true;
            }
        }

        // ---------------- 预览 ----------------

        private void ClearPreview()
        {
            _previews.Clear();
            _previewErrors.Clear();
            _refHits.Clear();
        }

        private void BuildPreview(bool single)
        {
            ClearPreview();
            var root = DisplayRoot();
            if (root == null) { _previewErrors.Add("无展示根——先绑定 prefab"); return; }

            if (single)
            {
                var node = UiBindingRename.FindByPath(root, _selectedPath);
                if (node == null) { _previewErrors.Add("选中节点不在当前树内——点刷新"); return; }
                if (!UiBindingRename.TryComposeName(_prefix, _semantic, out string composed, out string reason))
                {
                    _previewErrors.Add(reason);
                    return;
                }
                _previews.Add(UiBindingRename.PlanRename(node, composed));
            }
            else if (_batchOp == BatchOp.RetargetPrefix)
            {
                var targets = new List<Transform>();
                foreach (var row in _rows)
                    if (_checked.Contains(row.Path) && row.Node != null) targets.Add(row.Node);
                _previews = UiBindingRename.PlanRetargetPrefix(targets, _batchPrefix);
                if (_previews.Count == 0)
                    _previewErrors.Add("无可用计划（勾选目标、或现名不在词表文法内被跳过）");
            }
            else
            {
                foreach (var row in _rows)
                {
                    if (!_checked.Contains(row.Path) || !row.HasBindNode || row.Node == null) continue;
                    if (row.BindName == row.Name) continue;
                    _previews.Add(UiBindingRename.PlanRename(row.Node, row.Name));
                }
                if (_previews.Count == 0)
                    _previewErrors.Add("勾选项中没有 BindName 与节点名不同文的标记");
            }

            _previewErrors.AddRange(UiBindingRename.ValidatePlans(root, _previews));

            foreach (var plan in _previews)
            {
                var keys = new List<string>();
                if (!string.IsNullOrEmpty(plan.OldName)) keys.Add(plan.OldName);
                if (!string.IsNullOrEmpty(plan.OldBindName) && plan.OldBindName != plan.OldName) keys.Add(plan.OldBindName);
                var hits = new List<UiRefHit>();
                foreach (var key in keys) hits.AddRange(HitsFor(key));
                if (hits.Count > 0) _refHits[plan.Path] = hits;
            }
        }

        private List<UiRefHit> HitsFor(string literal)
        {
            if (_hitCache.TryGetValue(literal, out var cached)) return cached;
            cached = UiBindingReferenceScan.Scan(literal);
            _hitCache[literal] = cached;
            return cached;
        }

        private void DrawPreviewArea()
        {
            if (_previews.Count == 0 && _previewErrors.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("预览（引用点提示＝Lua/C# 同批同步清单，§9.2 改名联动）", EditorStyles.boldLabel);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MaxHeight(180f));
            foreach (var error in _previewErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var plan in _previews)
            {
                string change = plan.Path + "：";
                if (plan.NewName != plan.OldName) change += $"{plan.OldName} → {plan.NewName}";
                if (plan.BindNameChanges)
                    change += $"{(plan.NewName != plan.OldName ? "　" : string.Empty)}BindName {plan.OldBindName} → {plan.NewBindName}";
                if (plan.NewName == plan.OldName && !plan.BindNameChanges) change += "（无变化）";
                EditorGUILayout.LabelField(change, EditorStyles.miniBoldLabel);

                if (!_refHits.TryGetValue(plan.Path, out var hits)) continue;
                EditorGUILayout.HelpBox(
                    $"引用点 {hits.Count} 处（同步责任在作者，工具只提示）：\n" +
                    string.Join("\n", hits.Take(6).Select(h => $"{h.File}:{h.Line}  {h.Text}")) +
                    (hits.Count > 6 ? $"\n… 共 {hits.Count} 处" : string.Empty),
                    MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------- 应用 ----------------

        private void ApplyPreview()
        {
            if (_previews.Count == 0) return;

            var stageRoot = UiComposeTarget.StageRootFor(_prefabPath);
            bool viaStage = stageRoot != null;
            int applied;
            if (viaStage)
            {
                foreach (var plan in _previews)
                {
                    var node = UiBindingRename.FindByPath(stageRoot, plan.Path);
                    if (node != null) Undo.RecordObject(node.gameObject, "UI 绑定改名");
                    if (plan.HasBindNode && node != null)
                    {
                        var bind = node.GetComponent<BindNode>();
                        if (bind != null) Undo.RecordObject(bind, "UI 绑定改名");
                    }
                }
                applied = UiBindingRename.ApplyPlans(stageRoot, _previews);
                EditorUtility.SetDirty(stageRoot);
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(_prefabPath);
                try
                {
                    applied = UiBindingRename.ApplyPlans(contents, _previews);
                    PrefabUtility.SaveAsPrefabAsset(contents, _prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                AssetDatabase.SaveAssets();
            }

            int hits = _refHits.Values.Sum(v => v.Count);
            Debug.Log($"[UI 制作] 已应用 {_previews.Count} 条改名计划（写入 {applied} 处，经 {(viaStage ? "Prefab Stage（可 Undo）" : "资产写入")}）" +
                      (hits > 0 ? $"；**引用点 {hits} 处须同批同步**（见预览清单）" : string.Empty));

            ClearPreview();
            _hitCache.Clear();
            _rootId = -1;
            SyncRoot();
        }

        // ---------------- 生成物 hash ----------------

        private void DrawHashSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("生成物 hash（§2.1.5：记录＝作者动作；校验走 ui.binding-hash 规则）", EditorStyles.boldLabel);
            var root = DisplayRoot();
            if (root == null || root.GetComponent<BindRoot>() == null)
            {
                EditorGUILayout.LabelField("本 prefab 无 BindRoot——路径 B 生成未启用，无 hash 要求。", EditorStyles.miniLabel);
                return;
            }

            UiBindGenerationHash.TryGetRecordedHash(_prefabPath, out string recorded);
            string current = UiBindGenerationHash.ComputeHash(root);
            EditorGUILayout.LabelField(recorded == null
                ? "状态：从未记录（生成后记录）"
                : recorded == current ? $"状态：一致（{recorded.Substring(0, 8)}）"
                : "状态：漂移——源已改，须重新生成并记录", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("记录本 prefab", GUILayout.Width(100f)))
                {
                    UiBindGenerationHash.Record(_prefabPath);
                    RepaintHost();
                }
                if (GUILayout.Button("校验（全部资产面）", GUILayout.Width(120f)))
                {
                    var report = UiValidator.Run(UiValidator.BuildDefaultContext());
                    Debug.Log(report.Passed ? "[UI 校验] 通过\n" + report.Render() : "[UI 校验] 未通过\n" + report.Render());
                }
            }
        }

        private static void RepaintHost()
        {
            if (EditorWindow.focusedWindow != null) EditorWindow.focusedWindow.Repaint();
        }
    }
}
