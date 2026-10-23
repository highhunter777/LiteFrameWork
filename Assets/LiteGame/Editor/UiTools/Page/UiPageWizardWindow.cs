using System;
using LiteGame.Editor.UiTools.Compose;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 新建 UI 页面向导（《UI编辑器工具专项设计》§8.3）：输入页面名/id/layer/全屏，
    /// 生成 prefab + Lua 骨架 + tbuiform 行，随后跑 Luban 并打开 Prefab Stage。
    /// 结果逐步显示；表写入失败/生成失败显式报告，不静默成功。
    /// </summary>
    public sealed class UiPageWizardWindow : EditorWindow
    {
        private string _pageName = "Page_New";
        private int _id;
        private int _layer;
        private bool _fullScreen = true;
        private bool _busy;
        private string _log = string.Empty;
        private Vector2 _scroll;
        private bool _idInitialized;

        public static void Open()
        {
            var window = GetWindow<UiPageWizardWindow>("新建 UI 页面");
            window.minSize = new Vector2(520f, 440f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_idInitialized) return;
            _idInitialized = true;
            int suggested = UiPageScaffold.SuggestId();
            _id = suggested > 0 ? suggested : 17;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "从 Tmp_Page 脚手架生成 Page prefab + Lua 骨架 + tbuiform 行，然后重生成 Luban。\n" +
                "创建期间编辑器会阻塞；失败会显式报告（表行可回滚）。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_busy))
            {
                _pageName = EditorGUILayout.TextField("页面名", _pageName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _id = EditorGUILayout.IntField("tbuiform id", _id);
                    if (GUILayout.Button("建议", GUILayout.Width(56f)))
                    {
                        int suggested = UiPageScaffold.SuggestId();
                        if (suggested > 0) _id = suggested;
                        else _log = "取建议 id 失败（检查 Luban/Data/#uiform.xlsx 与 Python 环境）";
                    }
                }
                _layer = EditorGUILayout.IntField("layer", _layer);
                _fullScreen = EditorGUILayout.Toggle("full_screen", _fullScreen);
            }

            bool valid = UiPageScaffold.IsValidPageName(_pageName);
            if (!valid)
                EditorGUILayout.HelpBox("页面名须为 Page_<PascalCase>（ASCII 字母数字）。", MessageType.Warning);
            else
                EditorGUILayout.LabelField(
                    "将写入：" + _pageName + " · UI." + _pageName + " · ShareResource/UI/Screens/" + _pageName + ".prefab",
                    EditorStyles.miniLabel);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_busy || !valid))
            {
                if (GUILayout.Button(_busy ? "创建中…" : "创建并登记", GUILayout.Height(28f)))
                    Create();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("结果", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.SelectableLabel(_log, EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Create()
        {
            if (!EditorUtility.DisplayDialog(
                    "新建 UI 页面",
                    $"将创建 {_pageName}.prefab / {_pageName}.lua，并向 tbuiform 追加 id={_id} 的行，然后重生成 Luban。\n\n确定继续？",
                    "创建",
                    "取消"))
                return;

            _busy = true;
            _log = "创建中…";
            Repaint();
            EditorUtility.DisplayProgressBar("新建 UI 页面", "生成 prefab / Lua / tbuiform 并重生成 Luban…", 0.4f);
            try
            {
                var result = UiPageScaffold.Create(_pageName, _id, _layer, _fullScreen);
                _log = string.Join("\n", result.Log);
                if (result.Ok)
                {
                    _id = UiPageScaffold.SuggestId();
                    UiComposeWindow.RefreshOpenPageList();
                    EditorUtility.DisplayDialog("新建 UI 页面", "创建完成。\n\n" + _log, "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("新建 UI 页面失败", result.Error + "\n\n" + _log, "OK");
                }
            }
            catch (Exception exception)
            {
                _log = "异常：" + exception;
                EditorUtility.DisplayDialog("新建 UI 页面失败", _log, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _busy = false;
                Repaint();
            }
        }
    }
}
