using System;
using System.Collections;
using System.Collections.Generic;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Platform.Ui;
using LiteGameNext.Platform.Ui.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// T2/T4 启动链 PlayMode 证据：加载 BootNext 真实场景 → 等 LoginPage → 经 EventSystem 射线命中并
    /// 派发真实指针点击（不直调页面 API）→ 等 PlaceholderPage 出现。EditMode 版见同名 EditMode 用例。
    /// </summary>
    public sealed class ClientNextBootTests
    {
        private const string SceneName = "BootNext";
        private const float TimeoutSeconds = 20f;

        [UnityTest]
        public IEnumerator BootNext启动链_真实指针点击游客登录后进占位页()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);

            yield return WaitUntil(() => EventSystem.current != null, "BootNext 场景缺少可用的 EventSystem");
            yield return WaitUntil(() => LoginPage() != null, "启动链未在超时内打开 LoginPage");

            LoginPage login = LoginPage();
            Button button = FindButton(login, "Btn_GuestLogin");
            Assert.IsNotNull(button, "LoginPage 必须有游客登录按钮");

            PointerEventData pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, button.GetComponent<RectTransform>().position),
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.Greater(hits.Count, 0, "游客登录按钮必须被 UI 射线命中（可点性自检）");
            Assert.IsTrue(IsOn(button.transform, hits[0].gameObject.transform),
                "UI 射线命中的必须是登录按钮（实际命中：" + hits[0].gameObject.name + "）");

            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);

            yield return WaitUntil(() => UnityEngine.Object.FindObjectOfType<PlaceholderPage>() != null,
                "真实点击游客登录后未在超时内打开 PlaceholderPage");
            Assert.IsFalse(login.gameObject.activeInHierarchy, "登录成功后登录页必须离开屏幕");
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<PlaceholderPage>());
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail(message);
                yield return null;
            }
        }

        private static LoginPage LoginPage() => UnityEngine.Object.FindObjectOfType<LoginPage>();

        private static Button FindButton(PageBase page, string bindName)
        {
            foreach (BindName bind in page.GetComponentsInChildren<BindName>(true))
                if (bind.Name == bindName) return bind.GetComponent<Button>();
            return null;
        }

        private static bool IsOn(Transform target, Transform hit)
        {
            for (Transform current = hit; current != null; current = current.parent)
                if (current == target) return true;
            return false;
        }
    }
}
