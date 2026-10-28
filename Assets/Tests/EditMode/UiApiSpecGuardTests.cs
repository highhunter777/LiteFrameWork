using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteClient.UI;
using LiteGame;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// M3-4 守卫（《UI 系统总设计》§5.14）：UiApiSpec 生成物与手写实现不得漂移。
    /// 编译期已由 partial 方法强制签名一致；本用例兜住清单/版本、Lua shim 与 dispatch 三面。
    /// </summary>
    public sealed class UiApiSpecGuardTests
    {
        private const string DispatchRelativePath = "LiteGame/LuaBridge/Generated/LuaBehaviourAdapter.Dispatch.g.cs";

        [Test]
        public void 清单无重复且版本与能力集同源()
        {
            Assert.AreEqual(UiApiManifest.LuaMethods.Count, UiApiManifest.LuaMethods.Distinct().Count(),
                "Lua 门面名重复");
            Assert.AreEqual(UiApiManifest.LuaMethods.Count, UiApiManifest.ShimMethods.Count);
            var luaSet = new HashSet<string>(UiApiManifest.LuaMethods);
            foreach (string manual in UiApiManifest.ManualMethods)
                Assert.IsTrue(luaSet.Contains(manual), "手工处理器不在 Lua 清单里：" + manual);
            Assert.AreEqual(UiApiManifest.ApiVersion, BridgeCapabilitySet.Default().ApiVersion,
                "BridgeApiVersion 必须来自 UiApiManifest（UiApiSpec 单源）");
        }

        [Test]
        public void Lua_shim_覆盖清单每个门面()
        {
            foreach (string shim in UiApiManifest.ShimMethods)
                StringAssert.Contains("\n    " + shim + " = function(", UiApiShim.LuaTemplate);
            foreach (string lua in UiApiManifest.LuaMethods)
                StringAssert.Contains("'" + lua + "'", UiApiShim.LuaTemplate);
        }

        [Test]
        public void dispatch_覆盖非手工门面()
        {
            string path = Path.Combine(Application.dataPath, DispatchRelativePath);
            Assert.IsTrue(File.Exists(path), "生成的 dispatch 缺失：" + path);
            string source = File.ReadAllText(path);
            var manual = new HashSet<string>(UiApiManifest.ManualMethods);
            foreach (string lua in UiApiManifest.LuaMethods)
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
            foreach (string name in UiApiManifest.IndexMethods)
                Assert.IsTrue(names.Contains(name), "UIBindIndex 缺公开方法：" + name);
        }
    }
}
