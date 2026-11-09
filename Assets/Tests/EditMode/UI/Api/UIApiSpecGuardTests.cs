using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteFramework.UI;
using LiteGame;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// M3-4 守卫（《UI 系统总设计》§5.14）：UIApiSpec 生成物与手写实现不得漂移。
    /// 编译期已由 partial 方法强制签名一致；本用例兜住清单与版本两面。
    /// </summary>
    public sealed class UIApiSpecGuardTests
    {
        [Test]
        public void 清单无重复且版本来自规格单源()
        {
            Assert.AreEqual(UIApiManifest.SpecMethods.Count, UIApiManifest.SpecMethods.Distinct().Count(),
                "门面名重复");
            Assert.AreEqual(UIApiManifest.SpecMethods.Count, UIApiManifest.GeneratedMethods.Count);
            var specSet = new HashSet<string>(UIApiManifest.SpecMethods);
            foreach (string manual in UIApiManifest.ManualMethods)
                Assert.IsTrue(specSet.Contains(manual), "手工处理器不在清单里：" + manual);
            Assert.GreaterOrEqual(UIApiManifest.ApiVersion, 7, "B3 新增 battle 门面，ApiVersion 不得低于 7");
        }
        [Test]
        public void 索引清单在类型上均有公开方法()
        {
            var names = new HashSet<string>(typeof(UIBindIndex)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name));
            foreach (string name in UIApiManifest.IndexMethods)
                Assert.IsTrue(names.Contains(name), "UIBindIndex 缺公开方法：" + name);
        }
    }
}
