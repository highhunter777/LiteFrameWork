using LiteFramework;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 指针锁定管理契约：进入对局 = 锁定 + 隐藏系统指针（鼠标增量归相机）；门被拦/死亡/退场 = 解锁恢复。
    /// <see cref="PointerLockService"/> 是 Cursor.lockState/Cursor.visible 的唯一写者。
    /// </summary>
    public sealed class TpsPointerLockEditModeTests
    {
        [SetUp]
        public void SetUp()
        {
            Cursor.lockState = CursorLockMode.None;   // 每例起点归零（编辑器指针状态是进程级共享面）
            Cursor.visible = true;
        }

        [TearDown]
        public void TearDown()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [Test]
        public void 指针锁定_锁定并隐藏_解锁恢复()
        {
            var service = new PointerLockService();
            Assert.IsFalse(service.IsLocked, "初始未锁（对局外自由指针）");

            service.Lock();
            Assert.IsTrue(service.IsLocked, "Lock 置锁定态");
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, "平台指针锁定");
            Assert.IsFalse(Cursor.visible, "锁定时隐藏系统指针（准心是唯一瞄准标示）");

            service.Unlock();
            Assert.IsFalse(service.IsLocked, "Unlock 解除锁定态");
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "恢复自由指针");
            Assert.IsTrue(Cursor.visible, "恢复可见（模态 UI 要可点）");
        }

        [Test]
        public void 指针锁定_幂等_重复Lock不翻转()
        {
            var service = new PointerLockService();
            service.Lock();
            service.Lock();
            Assert.IsTrue(service.IsLocked, "重复 Lock 幂等");
            Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState, "锁定态不翻");

            service.Unlock();
            Assert.IsFalse(service.IsLocked, "解锁生效");
        }
    }
}
