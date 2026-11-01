using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using LiteFramework;
using LiteGame;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 爆头区调带器（烘焙工具箱「爆头带」页承载——《固定斜视角射击方案专项设计》§5 的视觉调带裁决工具）：
    /// Scene 视图里对 <see cref="BakeTargets.PrefabPath"/>（AimIdle 参考姿态）画判定几何——命中柱圈（灰）/
    /// 爆头柱切片（黄下沿·红上沿）/ 锚点参考线（颈/面顶/帽顶/发冠顶），**拖黄球或滑杆**调爆头带下沿、
    /// **滑杆**调爆头柱半径（窄于命中柱），导出写判定几何表 `#combatgeom` 的 <c>head_ratio</c>/<c>head_radius</c>
    /// （下沿比例随表内 hitscan_height 缩放；半径独立值）。
    ///
    /// 双模式口径：本工具（编辑器模式）与**GM 面板**（对局内滑杆 + 导出按钮）共用同一导出
    /// <see cref="Export"/>；对局内实时预调走 <c>MatchTestRules</c> 爆头带覆写（<c>HeadHitLineOverride</c>/
    /// <c>HeadshotRadiusOverride</c>——判定与身位可视化同读注入实例，滑杆一动即见 Crit 档变化），
    /// 定型才导出落盘。prefab 不入版本管理——本工具需本地 prefab（同 BodyCylinderBaker 前提）。
    ///
    /// **形态**：独立菜单已并入 <see cref="BakingToolbox"/>（统一入口）；本类是可寄宿工具件——
    /// 由工具箱 <c>Activate/Deactivate/DrawGUI</c> 驱动，不再自带窗口。
    /// </summary>
    public sealed class HeadHitLineTuner
    {
        private const float RatioMin = 0.5f;
        private const float RatioMax = 0.95f;
        private const float RadiusMin = 0.15f;
        private const float RadiusMax = 0.45f;

        private GameObject _preview;
        private float _ratio = RatioMin;
        private float _radius = RadiusMin;
        private float _tableRatio = RatioMin;
        private float _tableRadius = RadiusMin;
        private float _tableHeight;
        private float _tableHitscanRadius;
        private bool _tableLoaded;
        private string _tableError;

        /// <summary>挂 Scene 绘制与预览（工具箱进入「爆头带」页时调）。</summary>
        public void Activate()
        {
            CreatePreview();
            ReloadTable();
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>摘 Scene 绘制并销毁预览（翻页/关窗必达——幂等）。</summary>
        public void Deactivate()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            DestroyPreview();
        }

        public void DrawGUI()
        {
            if (!_tableLoaded) ReloadTable();
            float height = _tableLoaded ? _tableHeight : LiteSim.CombatConfig.Geometry.HitscanHeight;

            EditorGUILayout.LabelField("爆头柱切片（下沿高 × 水平半径）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _ratio = EditorGUILayout.Slider("下沿比例（×HitscanHeight）", _ratio, RatioMin, RatioMax);
            _radius = EditorGUILayout.Slider("爆头柱半径（m，窄于命中柱）", _radius, RadiusMin, RadiusMax);
            if (EditorGUI.EndChangeCheck()) RepaintScene();

            float line = height * _ratio;
            EditorGUILayout.LabelField($"下沿 Y = {line:F3} m    带高 = {height - line:F3} m（身高的 {(1f - _ratio):P0}）    半径 = {_radius:F3} m");
            EditorGUILayout.LabelField($"当前导出值（表 #combatgeom）：比例 {_tableRatio:F3}（下沿 {height * _tableRatio:F3} m）  半径 {_tableRadius:F3} m");
            if (!string.IsNullOrEmpty(_tableError))
                EditorGUILayout.HelpBox("表值读取失败：" + _tableError, MessageType.Warning);

            EditorGUILayout.Space(6);
            if (_preview == null)
            {
                EditorGUILayout.HelpBox($"预览不可用：缺 {BakeTargets.PrefabPath}（prefab 不入版本管理，需本地还原）", MessageType.Warning);
                if (GUILayout.Button("重试创建预览")) CreatePreview();
            }

            using (new EditorGUI.DisabledScope(_preview == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出到表（#combatgeom）"))
                {
                    string report = Export(_ratio, _radius);
                    EditorUtility.DisplayDialog("爆头区导出", report, "OK");
                    ReloadTable();
                }
                if (GUILayout.Button("取当前值"))
                {
                    float lineNow = TestModeRuntime.Current.HeadHitLineOverride >= 0f
                        ? TestModeRuntime.Current.HeadHitLineOverride
                        : height * _tableRatio;
                    _ratio = Mathf.Clamp(lineNow / height, RatioMin, RatioMax);
                    float radiusNow = TestModeRuntime.Current.HeadshotRadiusOverride >= 0f
                        ? TestModeRuntime.Current.HeadshotRadiusOverride
                        : _tableRadius;
                    _radius = Mathf.Clamp(radiusNow, RadiusMin, RadiusMax);
                }
                if (GUILayout.Button("刷新表值")) ReloadTable();
                if (GUILayout.Button("复位覆写"))
                {
                    TestModeRuntime.Current.HeadHitLineOverride = -1f;
                    TestModeRuntime.Current.HeadshotRadiusOverride = -1f;
                }
            }

            EditorGUILayout.HelpBox(
                "对局内实机调：F9 进测试模式 → GM 面板开身位可视化 → 拖「爆头线下沿/爆头柱半径」滑杆（判定实时随动），"
                + "定型后回本页/面板「导出」。导出只写表源——需跑 Luban/gen.bat → python scripts/codegen/toolbox.py build-hash 后进房生效。",
                MessageType.Info);
        }

        /// <summary>导出口径（编辑器调带器与测试面板共用）：下沿比例 + 爆头柱半径写 `#combatgeom` 表行。</summary>
        public static string Export(float ratio, float radius)
        {
            ratio = Mathf.Clamp(ratio, RatioMin, RatioMax);
            radius = Mathf.Clamp(radius, RadiusMin, RadiusMax);
            var inv = CultureInfo.InvariantCulture;
            var sets = new[]
            {
                new[] { "head_ratio", ratio.ToString("R", inv) },
                new[] { "head_radius", radius.ToString("R", inv) },
            };
            if (!CombatGeomTableWriter.TryWrite(sets, true, out string report, out string error))
                return "ABORT 写 #combatgeom 表失败：" + error;

            string derived = string.Empty;
            if (CombatGeomTableWriter.TryReadRow(out Dictionary<string, string> row, out _)
                && CombatGeomTableWriter.TryGetFloat(row, "hitscan_height", out float height, out _))
            {
                derived = $"（下沿 {height * ratio:F3} m，带高 {height * (1f - ratio):F3} m）";
            }
            return $"已导出：下沿比例 {ratio:R}{derived}；爆头柱半径 {radius:R} m\n"
                + report;
        }

        /// <summary>从表源重读现值（工具进入页面、导出后与手动刷新时调）。</summary>
        private void ReloadTable()
        {
            _tableLoaded = false;
            _tableError = null;
            if (!CombatGeomTableWriter.TryReadRow(out Dictionary<string, string> row, out string error))
            {
                _tableError = error;
                return;
            }
            if (!CombatGeomTableWriter.TryGetFloat(row, "head_ratio", out float ratio, out error)
                || !CombatGeomTableWriter.TryGetFloat(row, "head_radius", out float radius, out error)
                || !CombatGeomTableWriter.TryGetFloat(row, "hitscan_height", out float height, out error)
                || !CombatGeomTableWriter.TryGetFloat(row, "hitscan_radius", out float hitscanRadius, out error))
            {
                _tableError = error;
                return;
            }

            _tableRatio = ratio;
            _tableRadius = radius;
            _tableHeight = height;
            _tableHitscanRadius = hitscanRadius;
            _tableLoaded = true;
            _ratio = Mathf.Clamp(ratio, RatioMin, RatioMax);
            _radius = Mathf.Clamp(radius, RadiusMin, RadiusMax);
        }

        // ---- 预览与 Scene 绘制 ----

        private void CreatePreview()
        {
            DestroyPreview();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BakeTargets.PrefabPath);
            if (prefab == null) return;
            _preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _preview.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            var clip = BakeTargets.LoadReferenceClip();
            if (clip != null) clip.SampleAnimation(_preview, 0f);
        }

        private void DestroyPreview()
        {
            if (_preview == null) return;
            UnityEngine.Object.DestroyImmediate(_preview);
            _preview = null;
        }

        private static void RepaintScene()
        {
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
        }

        private void OnSceneGui(SceneView view)
        {
            if (_preview == null) return;
            Vector3 foot = _preview.transform.position;
            float hitR = LiteSim.CombatConfig.Geometry.HitscanRadius;
            float height = LiteSim.CombatConfig.Geometry.HitscanHeight;

            DrawAnchorLines();                                                      // 模型锚点（蓝细线+标签）

            // 双柱阶梯（与 SimRaycast 判定几何一致）：灰＝身体柱 [0, 分界) × 命中柱全径（含台阶顶面圈）
            float line = height * _ratio;
            Vector3 headLow = foot + Vector3.up * line;
            Vector3 top = foot + Vector3.up * height;
            DrawCylinder(foot, headLow, hitR, new Color(0.65f, 0.65f, 0.65f, 1f));

            // 爆头柱切片：下沿黄圈（可拖黄球竖直调）+ 带内母线 + 上沿红圈——半径用爆头柱（窄于灰柱）
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(headLow, Vector3.up, _radius);
            Vector3 handlePos = headLow + new Vector3(_radius, 0f, 0f);
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.Slider(handlePos, Vector3.up, HandleUtility.GetHandleSize(handlePos) * 0.12f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                _ratio = Mathf.Clamp(dragged.y / height, RatioMin, RatioMax);
                RepaintScene();
            }
            Handles.Label(headLow + Vector3.up * 0.05f, $"爆头线下沿 {_ratio:F3} × H = {line:F3} m（拖黄球竖调）");
            Handles.DrawLine(headLow + new Vector3(_radius, 0, 0), top + new Vector3(_radius, 0, 0));
            Handles.DrawLine(headLow + new Vector3(-_radius, 0, 0), top + new Vector3(-_radius, 0, 0));
            Handles.color = Color.red;
            Handles.DrawWireDisc(top, Vector3.up, _radius);
            Handles.Label(top + Vector3.up * 0.05f, $"爆头柱半径 {_radius:F3} m（红圈=上沿）");
        }

        private void DrawCylinder(Vector3 foot, Vector3 top, float radius, Color col)
        {
            Handles.color = col;
            Handles.DrawWireDisc(foot, Vector3.up, radius);
            Handles.DrawWireDisc(top, Vector3.up, radius);
            Handles.DrawLine(foot + new Vector3(radius, 0, 0), top + new Vector3(radius, 0, 0));
            Handles.DrawLine(foot + new Vector3(-radius, 0, 0), top + new Vector3(-radius, 0, 0));
            Handles.DrawLine(foot + new Vector3(0, 0, radius), top + new Vector3(0, 0, radius));
            Handles.DrawLine(foot + new Vector3(0, 0, -radius), top + new Vector3(0, 0, -radius));
        }

        /// <summary>模型锚点参考（蓝细线）：颈骨 / 面顶 / 帽顶 / 发冠顶——拖带时对照"可见头从哪开始"。</summary>
        private void DrawAnchorLines()
        {
            if (_preview == null) return;
            var an = _preview.GetComponentInChildren<Animator>();
            Vector3 foot = _preview.transform.position;
            float radius = LiteSim.CombatConfig.Geometry.HitscanRadius * 1.35f;

            void Anchor(float y, string label)
            {
                if (y <= 0f) return;
                Vector3 p = foot + Vector3.up * y;
                Handles.color = new Color(0.3f, 0.7f, 1f, 0.9f);
                Handles.DrawLine(p + new Vector3(-radius, 0, 0), p + new Vector3(radius, 0, 0));
                Handles.Label(p + Vector3.up * 0.04f, label);
            }

            var neck = an != null ? an.GetBoneTransform(HumanBodyBones.Neck) : null;
            Anchor(neck != null ? neck.position.y - foot.y : 1.28f, $"颈 ≈{(neck != null ? neck.position.y - foot.y : 1.28f):F2}");
            Anchor(RendererTop("Face"), "面顶");
            Anchor(RendererTop("Hat"), "帽顶");
            Anchor(RendererTop("Hair"), "发冠顶");

            float RendererTop(string namePart)
            {
                float top = -1f;
                foreach (var r in _preview.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.name.Contains(namePart)) continue;
                    top = Mathf.Max(top, r.bounds.max.y - foot.y);
                }
                return top;
            }
        }
    }
}
