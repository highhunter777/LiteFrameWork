using System;
using System.Collections;
using System.Collections.Generic;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.PlayMode
{
    /// <summary>
    /// Dev 本地对局端到端：真实 BootNext 场景 → 真实指针点击游客登录 → 进入本地对局（战斗 HUD 出现）。
    /// 不需要网络/MetaServer/KCP（进程内 RoomRuntime）。
    /// </summary>
    public sealed class ClientNextLocalMatchPlayModeTests
    {
        private const string SceneName = "BootNext";
        private const float TimeoutSeconds = 25f;

        [UnityTest]
        [Category("ClientNextE2E")]
        public IEnumerator Dev本地对局_真实点击游客登录后进入战斗HUD()
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

            // 本地对局会尝试加载地图场景；PlayMode 下地图不在 Build Settings 时只记日志、不阻断对局
            // （与 S2-2 设计的「地图加载失败不阻塞会话」一致）——显式声明该预期错误。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("couldn't be loaded"));

            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);

            yield return WaitUntil(() => UnityEngine.Object.FindObjectOfType<BattleHudView>() != null,
                "真实点击登录后未在超时内打开 BattleHudView（Dev 本地对局未启动）");
            Assert.IsNotNull(UnityEngine.Object.FindObjectOfType<BattleHudView>(),
                "Dev 本地对局必须出现战斗 HUD");
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
