using System;
using System.Collections;
using System.Collections.Generic;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Features.Battle.Hud;
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
        [Category("ClientNextE2E")]
        public IEnumerator BootNext启动链_真实指针点击游客登录后离开登录页()
        {
            // 测试隔离：清掉前序用例可能残留的引导件（GameEntry 有重复引导守卫，
            // 残留会把本次引导当重复件销毁，导致 LoginPage 永不出现）。
            foreach (var entry in UnityEngine.Object.FindObjectsOfType<LiteGameNext.Bootstrap.GameEntry>(true))
                UnityEngine.Object.DestroyImmediate(entry.gameObject);

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

            // Dev 面默认启用 → 登录后进本地对局；本地对局会尝试加载地图场景，
            // PlayMode 下该场景不在 Build Settings 时只记日志、不阻断（S2-2 设计）——显式声明该预期错误。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("couldn't be loaded"));

            // Dev 面在编辑器/开发构建默认启用 → 登录后进本地对局（战斗 HUD）；
            // 发布构建无 Dev 面则进占位页。两者都算"登录成功"，故只断言登录页离开屏幕 + 后续有页面。
            yield return WaitUntil(
                () => UnityEngine.Object.FindObjectOfType<BattleHudView>() != null
                      || UnityEngine.Object.FindObjectOfType<PlaceholderPage>() != null,
                "真实点击游客登录后未在超时内进入对局或占位页");
            Assert.IsFalse(login.gameObject.activeInHierarchy, "登录成功后登录页必须离开屏幕");
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
