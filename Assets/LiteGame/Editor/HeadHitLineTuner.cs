using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 爆头带调带器（《固定斜视角射击方案专项设计》§5 的视觉调带裁决工具）：Scene 视图里对
    /// `Player(Rifle).prefab`（AimIdle 参考姿态）画判定几何——命中柱圈/顶圈/锚点参考线（颈/面顶/帽顶/发冠顶），
    /// **拖动黄圈**或用窗口滑杆调爆头带下沿，导出写 <c>HeadBake.g.cs</c>（比例单源，随烘焙身高自动缩放）。
    ///
    /// 双模式口径：本窗口（编辑器模式）与**测试面板**（对局内滑杆 + 导出按钮）共用同一导出
    /// <see cref="Export"/>；对局内实时预调走 <c>CombatConfig.HeadHitLineDevOverride</c>（判定与 F11 绘制
    /// 同读 Live，滑杆一动即见 Crit 档变化），定型才导出落盘。
    /// prefab 不入版本管理——本工具需本地 prefab（同 BodyCylinderBaker 前提）。
    /// </summary>
    public sealed class HeadHitLineTuner : EditorWindow
    {
        private const string PrefabPath = "Assets/Prefab/Player(Rifle).prefab";
        private const string ControllerPath = "Assets/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";
        private const string OutputPath = "Assets/LiteSim/Core/Scripts/HeadBake.g.cs";

        private const float RatioMin = 0.5f;
        private const float RatioMax = 0.95f;

        private GameObject _preview;
        private float _ratio = LiteSim.HeadBake.Ratio;

        [MenuItem("LiteGame/调整爆头带")]
        private static void Open()
        {
            var window = GetWindow<HeadHitLineTuner>("爆头带调带");
            window.minSize = new Vector2(360, 260);
            window.Show();
        }

        private void OnEnable()
        {
            CreatePreview();
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            DestroyPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("爆头带下沿（拖 Scene 视图黄圈或滑杆）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _ratio = EditorGUILayout.Slider("比例（×HitscanHeight）", _ratio, RatioMin, RatioMax);
            if (EditorGUI.EndChangeCheck()) Repaint();

            float height = LiteSim.CombatConfig.HitscanHeight;
            float line = height * _ratio;
            EditorGUILayout.LabelField($"下沿 Y = {line:F3} m    带高 = {height - line:F3} m（身高的 {(1f - _ratio):P0}）");
            EditorGUILayout.LabelField($"当前导出值：{LiteSim.HeadBake.Ratio:F3} → 下沿 {height * LiteSim.HeadBake.Ratio:F3} m");

            EditorGUILayout.Space(6);
            if (_preview == null)
            {
                EditorGUILayout.HelpBox($"预览不可用：缺 {PrefabPath}（prefab 不入版本管理，需本地还原）", MessageType.Warning);
                if (GUILayout.Button("重试创建预览")) CreatePreview();
                GUI.enabled = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出到 HeadBake.g.cs"))
                {
                    string report = Export(_ratio);
                    EditorUtility.DisplayDialog("爆头带导出", report, "OK");
                }
                if (GUILayout.Button("取当前覆写值"))
                    _ratio = Mathf.Clamp(LiteSim.CombatConfig.HeadHitLineDevOverride / height, RatioMin, RatioMax);
                if (GUILayout.Button("复位覆写"))
                    LiteSim.CombatConfig.HeadHitLineDevOverride = -1f;
            }

            EditorGUILayout.HelpBox(
                "对局内实机调：F9 进测试模式 → F11 开身位可视化 → 测试面板拖「爆头线下沿」滑杆（判定实时随动），"
                + "定型后回本窗口/面板「导出」。导出后重跑 scripts/codegen/gen-build-hash.py（CombatConfig/HeadBake 在 hash 源集）。",
                MessageType.Info);
        }

        /// <summary>导出口径（编辑器调带器与测试面板共用）：比例写 HeadBake.g.cs，触发重编译。</summary>
        internal static string Export(float ratio)
        {
            ratio = Mathf.Clamp(ratio, RatioMin, RatioMax);
            File.WriteAllText(OutputPath, BuildText(ratio));
            AssetDatabase.ImportAsset(OutputPath);
            float height = LiteSim.CombatConfig.HitscanHeight;
            return $"已导出：Ratio={ratio:R}（下沿 {height * ratio:F3} m，带高 {height * (1f - ratio):F3} m）\n"
                + $"输出：{OutputPath}\n"
                + "后续：重跑 scripts/codegen/gen-build-hash.py；L1 命中面回归；F9+F11 实机复核。";
        }

        private static string BuildText(float ratio)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("// 本文件由 HeadHitLineTuner 生成（菜单 LiteGame/调整爆头带 → 导出，或测试面板导出按钮）——勿手改。");
            sb.AppendLine("// 语义：爆头带下沿 = HitscanHeight × Ratio（比例单源，随烘焙身高自动缩放——《固定斜视角射击方案专项设计》§5）。");
            sb.AppendLine("// 消费：CombatConfig.HeadHitLine（爆头带下沿 [HeadHitLine, HitscanHeight] 的下界）。");
            sb.AppendLine("// 来源：视觉调带裁决值（编辑器 Scene 视图拖带 / 测试模式实机滑杆，二者共用同一导出口径）。");
            sb.AppendLine("namespace LiteSim");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>爆头带比例常量（视觉裁决值；重调 = HeadHitLineTuner 拖带后导出）。</summary>");
            sb.AppendLine("    public static class HeadBake");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const float Ratio = {ratio.ToString("R", inv)}f;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---- 预览与 Scene 绘制 ----

        private void CreatePreview()
        {
            DestroyPreview();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;
            _preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _preview.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            foreach (var c in ctrl.animationClips) if (c.name == "AimIdle") { c.SampleAnimation(_preview, 0f); break; }
        }

        private void DestroyPreview()
        {
            if (_preview == null) return;
            DestroyImmediate(_preview);
            _preview = null;
        }

        private void OnSceneGui(SceneView view)
        {
            if (_preview == null) return;
            Vector3 foot = _preview.transform.position;
            float radius = LiteSim.CombatConfig.HitscanRadius;
            float height = LiteSim.CombatConfig.HitscanHeight;

            DrawCylinder(foot, radius, height, new Color(0.65f, 0.65f, 0.65f, 1f));       // 命中柱（灰）
            DrawAnchorLines();                                                             // 模型锚点（蓝细线+标签）

            // 爆头带下沿（黄）——可拖
            float line = height * _ratio;
            Vector3 headLow = foot + Vector3.up * line;
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(headLow, Vector3.up, radius);
            Vector3 handlePos = headLow + new Vector3(radius, 0f, 0f);
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.Slider(handlePos, Vector3.up, HandleUtility.GetHandleSize(handlePos) * 0.12f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                _ratio = Mathf.Clamp(dragged.y / height, RatioMin, RatioMax);
                Repaint();
            }
            Handles.Label(headLow + Vector3.up * 0.05f, $"爆头线 {_ratio:F3} × H = {line:F3} m（拖黄球）");

            // 上沿（红）
            Handles.color = Color.red;
            Handles.DrawWireDisc(foot + Vector3.up * height, Vector3.up, radius);
        }

        private void DrawCylinder(Vector3 foot, float radius, float height, Color col)
        {
            Handles.color = col;
            Handles.DrawWireDisc(foot, Vector3.up, radius);
            Handles.DrawWireDisc(foot + Vector3.up * height, Vector3.up, radius);
            Handles.DrawLine(foot + new Vector3(radius, 0, 0), foot + new Vector3(radius, height, 0));
            Handles.DrawLine(foot + new Vector3(-radius, 0, 0), foot + new Vector3(-radius, height, 0));
            Handles.DrawLine(foot + new Vector3(0, 0, radius), foot + new Vector3(0, 0, radius) + Vector3.up * height);
            Handles.DrawLine(foot + new Vector3(0, 0, -radius), foot + new Vector3(0, 0, -radius) + Vector3.up * height);
        }

        /// <summary>模型锚点参考（蓝细线）：颈骨 / 面顶 / 帽顶 / 发冠顶——拖带时对照"可见头从哪开始"。</summary>
        private void DrawAnchorLines()
        {
            if (_preview == null) return;
            var an = _preview.GetComponentInChildren<Animator>();
            Vector3 foot = _preview.transform.position;
            float radius = LiteSim.CombatConfig.HitscanRadius * 1.35f;

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
