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