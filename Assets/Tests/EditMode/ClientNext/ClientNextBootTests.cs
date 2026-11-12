using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Platform.Ui;
using LiteGameNext.Platform.Ui.Content;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// T2 启动链（EditMode 落地版）：BootNext 场景组成（GameEntry + EventSystem），以及
    /// ClientApp 用真实页面 prefab 走完「登录页 → 假登录 → 占位页 → 取消关闭」。PlayMode 版见同名 PlayMode 用例。
    /// </summary>
    public sealed class ClientNextBootTests
    {
        private const string ScenePath = "Assets/Scenes/BootNext.unity";
        private const string GameEntryTypeName = "LiteGameNext.Bootstrap.GameEntry";

        [Test]
        public void BootNext场景含GameEntry与EventSystem()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                Assert.IsTrue(roots.Any(HasGameEntry), "BootNext 必须含引导件 GameEntry（AOT 入口）");

                EventSystem eventSystem = roots
                    .SelectMany(go => go.GetComponentsInChildren<EventSystem>(true))
                    .FirstOrDefault();
                Assert.IsNotNull(eventSystem, "BootNext 必须含 EventSystem（页面点击前置）");
                Assert.IsNotNull(eventSystem.GetComponent<StandaloneInputModule>(),
                    "EventSystem 必须带 StandaloneInputModule（与项目既有 UI 输入一致）");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ClientApp真实页面从登录页假登录进占位页并关闭()
        {
            var platform = new LiteFrameworkNext.Platform("ClientNextBootTests", "0.0.0");
            platform.Initialize();
            var app = new ClientApp(platform, LiteGameNext.Platform.Dev.NullDevHooks.Instance);
            using var cts = new CancellationTokenSource();
            UniTask run = default;
            try
            {
                app.InitializeAsync(cts.Token).GetAwaiter().GetResult();
                Assert.IsTrue(app.Ui.IsInitialized, "InitializeAsync 必须初始化 UI 运行时");

                run = app.RunAsync(cts.Token);

                var login = app.Ui.Top == null ? null : app.Ui.Top.Page as LoginPage;
                Assert.IsNotNull(login, "启动链第一步必须打开真实 LoginPage prefab");
                PageHandle loginHandle = app.Ui.Top;
                Assert.IsNotNull(login.GetComponentInChildren<GraphicRaycaster>(true),
                    "登录页 Canvas 必须有 GraphicRaycaster（EventSystem 射线命中前置）");

                Button guest = FindButton(login, "Btn_GuestLogin");
                Assert.IsNotNull(guest, "LoginPage prefab 必须有游客登录按钮");
                guest.onClick.Invoke();

                Assert.IsTrue(app.IsSignedIn, "假登录必须置登录态");
                Assert.IsInstanceOf<PlaceholderPage>(app.Ui.Top.Page, "登录后必须打开真实 PlaceholderPage");
                Assert.IsTrue(loginHandle.IsClosed, "登录成功必须关闭登录页");
                Assert.AreEqual(1, app.Ui.OpenPageCount, "栈上只应剩占位页");

                cts.Cancel();
                Assert.AreEqual(UniTaskStatus.Succeeded, run.Status, "App 取消必须静默结束流程");
                run.GetAwaiter().GetResult();
            }
            finally
            {
                app.ShutdownAsync().GetAwaiter().GetResult();
                platform.Shutdown();
            }
        }

        private static bool HasGameEntry(GameObject root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().FullName == GameEntryTypeName) return true;
            return false;
        }

        private static Button FindButton(PageBase page, string bindName)
        {
            foreach (BindName bind in page.GetComponentsInChildren<BindName>(true))
                if (bind.Name == bindName) return bind.GetComponent<Button>();
            return null;
        }
    }
}
