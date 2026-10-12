using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·场景操作页：SceneService 加载机制验证（单场景切换 / 叠加 / 卸载）。
    /// 场景下拉取 EditorBuildSettings 清单（可加载前提之一）；操作经命令中心派发（GmOnly 带参+异步
    /// 场景命令的专面板——权限门/拦截器/审计环全覆盖；忙碌守卫保留），异常不外泄到编辑器、结果落面板。
    /// 位置记忆走 EditorPrefs（统一前缀 LiteGame.Gm.*）。
    /// </summary>
    internal sealed class GmScenePage : IGmPage
    {
        private const string PrefScene = "LiteGame.Gm.Scene.Location";

        private string[] _scenePaths = Array.Empty<string>();
        private int _sceneIndex = -1;
        private string _sceneLocation = "";
        private string _sceneState = "(未刷新)";
        private string _sceneResult = "(未操作)";
        private bool _sceneBusy;

        public GUIContent Title => new GUIContent("场景操作");

        public void OnShow() => RefreshScenePaths();

        public void OnHide() { }

        public void Poll() => RefreshSceneState();

        public void Draw()
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
                if (GUILayout.Button("单场景加载")) RunSceneOp(new GmLoadSingleSceneCommand { Location = _sceneLocation }, "单场景加载").Forget();
                if (GUILayout.Button("叠加加载")) RunSceneOp(new GmLoadAdditiveSceneCommand { Location = _sceneLocation }, "叠加加载").Forget();
                if (GUILayout.Button("卸载单场景")) RunSceneOp(new GmUnloadSingleSceneCommand(), "卸载单场景").Forget();
                if (GUILayout.Button("卸载叠加")) RunSceneOp(new GmUnloadAdditiveSceneCommand { Location = _sceneLocation }, "卸载叠加").Forget();
            }

            if (!Application.isPlaying) EditorGUILayout.HelpBox("场景操作需 Play（AssetService 初始化后才可加载）", MessageType.Info);
            else if (_sceneBusy) EditorGUILayout.HelpBox("场景操作进行中……", MessageType.None);

            EditorGUILayout.LabelField(_sceneState, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("结果", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(_sceneResult, GUILayout.MinHeight(40));
            EditorGUILayout.HelpBox("本页为加载机制的机制级验证（加载/卸载/叠加语义），不承担业务流程跳转。", MessageType.Info);
        }

        /// <summary>场景操作经命令中心（SendAsync：权限门/拦截器/审计链全覆盖——加载/卸载进审计环；
        /// 运行态失败以结果返回，编程错误兜底不外泄到编辑器；忙碌守卫与结果落面板保留）。</summary>
        private async UniTaskVoid RunSceneOp<TCommand>(TCommand command, string label) where TCommand : ICommand
        {
            CommandCenter center = GetCommandCenter();
            if (center == null) { _sceneResult = $"{label}失败：CommandCenter 未装配"; return; }

            _sceneBusy = true;
            _sceneResult = $"{label}中……";
            try
            {
                CommandResult result = await center.SendAsync(command);
                _sceneResult = result.Ok
                    ? $"{label}完成：{result.Detail}"
                    : $"{label}拒（{result.Reason}）：{result.Detail}";
            }
            catch (Exception ex)
            {
                _sceneResult = $"{label}失败：{ex.Message}";   // 无处理器/类型错配等编程错误——兜底不外泄
            }
            finally
            {
                _sceneBusy = false;
                RefreshSceneState();
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

        private ISceneService GetSceneService() => GmEditorResolve.ResolveFromContainer<ISceneService>();

        private static CommandCenter GetCommandCenter() => GmEditorResolve.ResolveFromContainer<ICommandCenter>() as CommandCenter;
    }
}
