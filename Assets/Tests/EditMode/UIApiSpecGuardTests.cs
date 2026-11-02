using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteFramework.UI;
using LiteGame;
using NUnit.Framework;
using UnityEngine;
using LiteGame.Modules.Config;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// M3-4 守卫（《UI 系统总设计》§5.14）：UIApiSpec 生成物与手写实现不得漂移。
    /// 编译期已由 partial 方法强制签名一致；本用例兜住清单/版本、Lua shim 与 dispatch 三面。
    /// </summary>
    public sealed class UIApiSpecGuardTests
    {
        private const string DispatchRelativePath = "LiteGame/Adapters/LuaBridge/Generated/LuaBehaviourAdapter.Dispatch.g.cs";

        [Test]
        public void 清单无重复且版本与能力集同源()
        {
            Assert.AreEqual(UIApiManifest.LuaMethods.Count, UIApiManifest.LuaMethods.Distinct().Count(),
                "Lua 门面名重复");
            Assert.AreEqual(UIApiManifest.LuaMethods.Count, UIApiManifest.ShimMethods.Count);
            var luaSet = new HashSet<string>(UIApiManifest.LuaMethods);
            foreach (string manual in UIApiManifest.ManualMethods)
                Assert.IsTrue(luaSet.Contains(manual), "手工处理器不在 Lua 清单里：" + manual);
            Assert.AreEqual(UIApiManifest.ApiVersion, BridgeCapabilitySet.Default().ApiVersion,
                "BridgeApiVersion 必须来自 UIApiManifest（UIApiSpec 单源）");
        }

        [Test]
        public void Lua_shim_覆盖清单每个门面()
        {
            foreach (string shim in UIApiManifest.ShimMethods)
                StringAssert.Contains("\n    " + shim + " = function(", UIApiShim.LuaTemplate);
            foreach (string lua in UIApiManifest.LuaMethods)
                StringAssert.Contains("'" + lua + "'", UIApiShim.LuaTemplate);
        }

        [Test]
        public void dispatch_覆盖非手工门面()
        {
            string path = Path.Combine(Application.dataPath, DispatchRelativePath);
            Assert.IsTrue(File.Exists(path), "生成的 dispatch 缺失：" + path);
            string source = File.ReadAllText(path);
            var manual = new HashSet<string>(UIApiManifest.ManualMethods);
            foreach (string lua in UIApiManifest.LuaMethods)
            {
                if (manual.Contains(lua)) continue;
                StringAssert.Contains("case \"" + lua + "\":", source);
            }
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
