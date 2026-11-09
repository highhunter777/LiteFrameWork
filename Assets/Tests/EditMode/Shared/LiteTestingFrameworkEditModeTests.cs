using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    public sealed class LiteTestingFrameworkEditModeTests : UnityTestBase
    {
        [Test]
        [Category(TestCategory.Smoke)]
        public void TrackedGameObjectIsDestroyedWithScope()
        {
            var nested = new UnityTestScope(nameof(TrackedGameObjectIsDestroyedWithScope));
            GameObject gameObject = nested.CreateGameObject("LiteTesting.Probe");

            nested.Dispose();

            Assert.IsTrue(gameObject == null);
        }

        [Test]
        [Category(TestCategory.Asset)]
        public void TemporaryAssetFolderIsDeletedWithScope()
        {
            string path;
            using (var nested = new UnityTestScope(nameof(TemporaryAssetFolderIsDeletedWithScope)))
            {
                path = nested.CreateTempAssetFolder();
                Assert.IsTrue(AssetDatabase.IsValidFolder(path));
            }

            Assert.IsFalse(AssetDatabase.IsValidFolder(path));
        }
    }
}
