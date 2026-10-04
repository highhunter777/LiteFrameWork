using System.Collections;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 飘字池守卫语义 PlayMode 用例：模板缺 CanvasGroup = 只跳过淡出，
    /// 位移与回收照常（守卫进循环条件会首帧退出：不位移/不淡出/**不归还池**）。
    ///
    /// **为什么必须是 PlayMode**：FlyAsync 走 UniTask.NextFrame + Time.unscaledDeltaTime 累积——
    /// EditMode 没有 PlayerLoop，飞行/回收路径根本不执行（模板重建丢 CanvasGroup 的回归
    /// 没被 EditMode 抓住的原因：借出/克隆语义同步可测，飞行收尾只有帧驱动才走得到）。
    ///
    /// 模板两形态：**缺 CanvasGroup**（复刻真 FlyText.prefab `_Template` 的单节点形状）与
    /// **带 CanvasGroup**（完整淡出路径）。时序判据按**真实时间累积**（unscaled）——帧数随机器
    /// 浮动、累积时长不浮动，避免时序抽奖。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class FlyTextPlayModeTests
    {
        private PlayModeTestScope _scope;
        private FlyTextPool _pool;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(FlyTextPlayModeTests));
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _scope.DisposeAsync();
            _pool = null;
        }

        /// <summary>建池：Canvas 宿主 + 飞字池 + 非激活模板（withCanvasGroup 决定模板形态）。</summary>
        private FlyTextPool NewPool(bool withCanvasGroup)
        {
            var canvasGo = _scope.CreateGameObject("FlyCanvas");
            canvasGo.AddComponent<Canvas>();

            var poolGo = new GameObject("FlyPool");
            poolGo.transform.SetParent(canvasGo.transform, false);
            _pool = poolGo.AddComponent<FlyTextPool>();

            var template = new GameObject("Template", typeof(RectTransform));
            template.transform.SetParent(poolGo.transform, false);
            var text = template.AddComponent<TextMeshProUGUI>();
            text.text = "t";
            if (withCanvasGroup) template.AddComponent<CanvasGroup>();
            template.SetActive(false);                       // 非激活模板（池 create 复制此形态）
            _pool.Template = (RectTransform)template.transform;
            return _pool;
        }

        /// <summary>池宿主上唯一激活的实例（模板与回收件都是非激活的）。</summary>
        private static Transform ActiveChild(FlyTextPool pool)
        {
            foreach (Transform child in pool.transform)
                if (child.gameObject.activeSelf)
                    return child;
            return null;
        }

        [UnityTest]
        public IEnumerator 飘字_模板缺CanvasGroup_只跳过淡出_位移与回收照常()
        {
            FlyTextPool pool = NewPool(withCanvasGroup: false);
            pool.Show("-1", Vector2.zero);

            float t = 0f;
            while (t < 0.4f) { yield return null; t += Time.unscaledDeltaTime; }   // 半程（Duration=0.8）

            Transform instance = ActiveChild(pool);
            Assert.IsNotNull(instance, "半程时实例应在飞行中（未回收）");
            var rt = (RectTransform)instance;
            Assert.Greater(rt.anchoredPosition.y, 0f,
                "缺 CanvasGroup 必须照常位移（回归：首帧退出 → 不位移不回收）");

            while (t < 1.2f) { yield return null; t += Time.unscaledDeltaTime; }   // 越过 Duration 收尾

            Assert.IsNull(ActiveChild(pool), "飞行结束必须回收（缺组件不得吞掉归还）");
            Assert.AreEqual(1, pool.PooledCount, "实例应回到池中（驻留恰一份）");
        }

        [UnityTest]
        public IEnumerator 飘字_模板带CanvasGroup_上飘淡出_结束回收并复位()
        {
            FlyTextPool pool = NewPool(withCanvasGroup: true);
            pool.Show("+1", Vector2.zero);

            float t = 0f;
            while (t < 0.4f) { yield return null; t += Time.unscaledDeltaTime; }

            Transform instance = ActiveChild(pool);
            Assert.IsNotNull(instance, "半程时实例应在飞行中");
            CanvasGroup cg = instance.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg, "带组形态下实例应持 CanvasGroup");
            Assert.LessOrEqual(cg.alpha, 0.5f, "半程（累计 0.4s/0.8s）alpha 应已线性衰减至 ≤ 0.5");
            Assert.Greater(((RectTransform)instance).anchoredPosition.y, 0f, "带组形态同样照常位移");

            while (t < 1.2f) { yield return null; t += Time.unscaledDeltaTime; }

            Assert.IsNull(ActiveChild(pool), "飞行结束回收");
            Assert.AreEqual(1, pool.PooledCount, "回收后池中恰一份");
            Assert.AreEqual(1f, cg.alpha, 0.001f, "归还路径应把 alpha 复位为 1（下次取件直接可用）");
        }
    }
}
