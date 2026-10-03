using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 飘字池收编面（《对象池专项设计》§5）：借出面（模板克隆/激活/文本写入）EditMode 直测。
    /// **归还路径走 UniTask.NextFrame（UI 轨）——EditMode 无 PlayerLoop 泵不动**，归还侧等价断言归
    /// PlayMode（FeedbackSurface 面）与夜间 L2；本类只钉借出侧契约。
    /// </summary>
    public sealed class FlyTextPoolEditModeTests : UnityTestBase
    {
        private FlyTextPool NewPool()
        {
            var host = new GameObject("FlyTextHost", typeof(RectTransform));
            var pool = host.AddComponent<FlyTextPool>();

            // 模板结构对齐真 prefab：Template(非激活) → Label(带 TMP_Text；CanvasGroup 由模板自带红线)
            var tpl = new GameObject("Template", typeof(RectTransform)).transform;
            tpl.SetParent(host.transform, false);
            var label = new GameObject("Label", typeof(RectTransform)).transform;
            label.SetParent(tpl, false);
            label.gameObject.AddComponent<TextMeshProUGUI>();
            tpl.gameObject.SetActive(false);
            pool.Template = (RectTransform)tpl;
            return pool;
        }

        [Test]
        public void 借出_克隆模板_激活并写入文本()
        {
            var pool = NewPool();

            pool.Show("命中+1", Vector2.zero);

            Assert.AreEqual(2, pool.transform.childCount, "模板 + 一条克隆件");
            var active = pool.GetComponentsInChildren<TMP_Text>(false);   // 只取激活面——模板非激活不计
            Assert.AreEqual(1, active.Length, "激活面只有克隆件的文本");
            Assert.AreEqual("命中+1", active[0].text);
            Assert.IsTrue(active[0].gameObject.activeSelf, "取件即激活（onGet 口径）");
            Assert.AreEqual(0, pool.PooledCount, "借出未归还：池空");
        }

        [Test]
        public void 借出_多实例并存_各自克隆不互串()
        {
            var pool = NewPool();

            pool.Show("甲", Vector2.zero);
            pool.Show("乙", Vector2.zero);

            Assert.AreEqual(3, pool.transform.childCount, "模板 + 两条克隆件");
            Assert.AreEqual(0, pool.PooledCount);
            var active = pool.GetComponentsInChildren<TMP_Text>(false);
            Assert.AreEqual(2, active.Length, "两条并存（§3.2 多条并存口径同 Toast）");
        }
    }
}
