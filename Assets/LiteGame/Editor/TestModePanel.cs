using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>测试面板：编辑 <see cref="TestModeSettings"/> 资产（测试模式各开关），并把快照应用到运行时 /
    /// 一键进入测试模式。进入后的对局行为（免死/人数/冻结/传送/时间缩放）由 <see cref="TestModeRuntime"/> 快照驱动。</summary>
    public sealed class TestModePanel : EditorWindow
    {
        private TestModeSettings _settings;

        [MenuItem("LiteGame/测试/测试面板")]
        private static void Open()
        {
            var window = GetWindow<TestModePanel>("测试面板");
            window.minSize = new Vector2(420, 340);
            window.Show();
        }

        private void OnEnable() => _settings = Load();

        private void OnGUI()
        {
            if (_settings == null)
            {
                EditorGUILayout.HelpBox($"未找到配置资产：{TestModeSettings.AssetPath}", MessageType.Warning);
                if (GUILayout.Button("创建默认配置资产"))
                    _settings = Create();
                return;
            }

            EditorGUILayout.LabelField("测试模式开关（配置资产）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _settings.NoDeath = EditorGUILayout.ToggleLeft("全房免死（Hp 保底 1、目标不消失）", _settings.NoDeath);
            _settings.TimeScale = EditorGUILayout.Slider("世界时钟缩放", _settings.TimeScale, 0f, 2f);
            _settings.Paused = EditorGUILayout.ToggleLeft("世界时钟暂停", _settings.Paused);
            _settings.TeleportEnabled = EditorGUILayout.ToggleLeft("定点传送（T 键 / GM 面板按钮 → 准心点）", _settings.TeleportEnabled);
            _settings.BotFrozen = EditorGUILayout.ToggleLeft("bot 冻结（位置每帧回写）", _settings.BotFrozen);
            _settings.DrawHeadshotDebug = EditorGUILayout.ToggleLeft("爆头区域可视化（每个活体身上常驻画爆头带；F11 可局内切）", _settings.DrawHeadshotDebug);
            _settings.BotCount = EditorGUILayout.IntSlider("bot 数量（补位席位）", _settings.BotCount, 0, 7);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);               // 资产随库：可审查、队友共享

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
                // 判定与 F11 绘制同读 Live——本地服同进程同值，滑杆一动实弹即见 Crit 档变化
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
                "对局内滑动即生效（判定与 F11 黄/红圈实时随动，可实弹试 Crit 档——黄红圈之间的窄柱切片才是爆头区）；"
                + "定型才导出。对局中点导出会触发重编译并中断对局（domain reload）——可先记下数值，出对局后再导。",
                MessageType.Info);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存并应用"))
                    Apply();
                if (GUILayout.Button("保存并进入测试模式"))
                {
                    Apply();
                    TestModeRuntime.EnterRequested = true;
                }
            }
            EditorGUILayout.HelpBox(
                "进入后：主菜单消费请求 → 本地服 Room-Test（房号隔离）。时间缩放/暂停由场景里的 DebugTuner 落钟（未挂则不生效）。",
                MessageType.Info);
        }

        /// <summary>把资产快照写进运行时并标记已配置（GM 面板/本面板共用同一路径）。</summary>
        internal static void Apply()
        {
            TestModeSettings s = Load();
            if (s == null)
            {
                TestModeRuntime.ApplyDefaults();
            }
            else
            {
                TestModeRuntime.NoDeath = s.NoDeath;
                TestModeRuntime.TimeScale = s.TimeScale;
                TestModeRuntime.Paused = s.Paused;
                TestModeRuntime.TeleportEnabled = s.TeleportEnabled;
                TestModeRuntime.BotFrozen = s.BotFrozen;
                TestModeRuntime.DrawHeadshotDebug = s.DrawHeadshotDebug;
                TestModeRuntime.BotCount = s.BotCount;
            }
            TestModeRuntime.Configured = true;
            TestModeRuntime.Active = true;
        }

        private static TestModeSettings Load()
            => AssetDatabase.LoadAssetAtPath<TestModeSettings>(TestModeSettings.AssetPath);

        private static TestModeSettings Create()
        {
            var s = ScriptableObject.CreateInstance<TestModeSettings>();
            AssetDatabase.CreateAsset(s, TestModeSettings.AssetPath);
            AssetDatabase.SaveAssets();
            return s;
        }
    }
}