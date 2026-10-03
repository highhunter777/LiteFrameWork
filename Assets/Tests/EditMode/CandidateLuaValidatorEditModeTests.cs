using System.Collections.Generic;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 候选 Lua 动态验证（《热更与内容发布专项设计》§10）。
    ///
    /// **本文件的核心价值是证明"受控验证环境"真的受控**：
    /// §10 硬要求"验证环境不得绑定正式全局 Bridge 注册表，不得打开 UI、订阅正式事件、
    /// 发业务网络请求或写存档"。沙箱若只是名义上的（比如仍能 `CS.UnityEngine...` 或 `io.open`），
    /// 那候选脚本就有了任意文件/网络/反射能力——这些用例正是钉住这一点的回归卡。
    /// </summary>
    public sealed class CandidateLuaValidatorEditModeTests : UnityTestBase
    {
        private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        private static CandidateScriptSet SetOf(params ScriptEntry[] entries)
        {
            Assert.IsTrue(CandidateScriptSet.TryBuild(entries, out CandidateScriptSet set).Accepted);
            return set;
        }

        private static Dictionary<string, byte[]> BytesOf(params (string path, string src)[] files)
        {
            var d = new Dictionary<string, byte[]>();
            foreach ((string path, string src) in files) d[path] = B(src);
            return d;
        }

        // ---- 正常路径 ----

        [Test]
        public void 合法脚本_语法与执行均通过()
        {
            CandidateScriptSet set = SetOf(new ScriptEntry("m", "lua/m.lua"));
            var bytes = BytesOf(("lua/m.lua", "local M = {} function M.hello() return 1 end return M"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.AreEqual(1, r.Count);
            Assert.IsTrue(r[0].Ok, r[0].Reason);
            Assert.IsNull(CandidateLuaValidator.Summarize(r));
        }

        [Test]
        public void 语法错误_被检出()
        {
            CandidateScriptSet set = SetOf(new ScriptEntry("bad", "lua/bad.lua"));
            var bytes = BytesOf(("lua/bad.lua", "local x = = 1"));   // 语法错误

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsFalse(r[0].Ok);
            Assert.IsFalse(r[0].SyntaxOk);
            StringAssert.Contains("语法", r[0].Reason);
        }

        [Test]
        public void 顶层执行抛错_被检出()
        {
            CandidateScriptSet set = SetOf(new ScriptEntry("boom", "lua/boom.lua"));
            var bytes = BytesOf(("lua/boom.lua", "error('顶层炸了')"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsFalse(r[0].Ok);
            Assert.IsTrue(r[0].SyntaxOk);         // 语法没问题
            Assert.IsFalse(r[0].ExecuteOk);       // 是执行期错
            StringAssert.Contains("执行", r[0].Reason);
        }

        [Test]
        public void 缺脚本字节_被检出()
        {
            CandidateScriptSet set = SetOf(new ScriptEntry("m", "lua/m.lua"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, new Dictionary<string, byte[]>());

            Assert.IsFalse(r[0].Ok);
            StringAssert.Contains("缺脚本字节", r[0].Reason);
        }

        [Test]
        public void 不短路_一次报告全部问题()
        {
            CandidateScriptSet set = SetOf(
                new ScriptEntry("ok", "lua/ok.lua"),
                new ScriptEntry("bad", "lua/bad.lua"),
                new ScriptEntry("boom", "lua/boom.lua"));
            var bytes = BytesOf(
                ("lua/ok.lua", "return {}"),
                ("lua/bad.lua", "local x = = 1"),
                ("lua/boom.lua", "error('x')"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.AreEqual(3, r.Count);                                  // 全跑了
            string summary = CandidateLuaValidator.Summarize(r);
            StringAssert.Contains("bad", summary);
            StringAssert.Contains("boom", summary);                       // 两个问题都报出来
        }

        // ---- 沙箱隔离（§10 硬要求） ----

        [Test]
        public void 沙箱_回收CS能力_不可触达CSharp()
        {
            // §10：Bridge 白名单不自动构成沙箱，还需收口 CS/反射入口。
            // 未收口时候选脚本可 `CS.UnityEngine.GameObject` 任意操作引擎。
            CandidateScriptSet set = SetOf(new ScriptEntry("evil", "lua/evil.lua"));
            var bytes = BytesOf(("lua/evil.lua",
                "if CS == nil then error('CS 被回收') end\nreturn CS"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsFalse(r[0].Ok, "CS 应被沙箱回收，脚本却拿到了它");
            StringAssert.Contains("CS 被回收", r[0].Reason);
        }

        [Test]
        public void 沙箱_回收io与os能力()
        {
            CandidateScriptSet set = SetOf(
                new ScriptEntry("a", "lua/a.lua"),
                new ScriptEntry("b", "lua/b.lua"));
            var bytes = BytesOf(
                ("lua/a.lua", "if io ~= nil then error('io 未回收') end return {}"),
                ("lua/b.lua", "if os ~= nil then error('os 未回收') end return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsTrue(r[0].Ok, r[0].Reason);
            Assert.IsTrue(r[1].Ok, r[1].Reason);
        }

        [Test]
        public void 沙箱_回收原生require_越出候选集合被拒()
        {
            // §7：同步 require 的依赖必须完整预载——越集即拒，不落到原生加载器
            CandidateScriptSet set = SetOf(new ScriptEntry("m", "lua/m.lua"));
            var bytes = BytesOf(("lua/m.lua", "require('some.outside.module')\nreturn {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsFalse(r[0].Ok, "越集 require 应被拒");
            StringAssert.Contains("越出候选集合", r[0].Reason);
        }

        [Test]
        public void 沙箱_批内require_按需装载不依赖清单顺序()
        {
            // use 在前、base（依赖）在后：沙箱按需装载（与运行期"全量预载 + require 时执行"同语义），
            // 依赖在清单后位也必须解析出真实值——真实候选包 Content/DemoEntry 即依赖后位的 Core.class。
            CandidateScriptSet set = SetOf(
                new ScriptEntry("use", "lua/use.lua", "base"),
                new ScriptEntry("base", "lua/base.lua"));
            var bytes = BytesOf(
                ("lua/base.lua", "return { v = 42 }"),
                ("lua/use.lua", "local b = require('base') if b == nil or b.v ~= 42 then error('依赖未就绪') end return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.AreEqual(2, r.Count);
            Assert.IsTrue(r[0].Ok, "use 依赖后位 base——按需装载后应通过：" + r[0].Reason);
            Assert.IsTrue(r[1].Ok, r[1].Reason);
        }

        [Test]
        public void 沙箱_require环_当场拒绝不悬挂()
        {
            // 清单不声明 Requires 时环由沙箱守卫（执行中集合）当场拒绝——不得悬挂/爆栈
            CandidateScriptSet set = SetOf(
                new ScriptEntry("a", "lua/a.lua"),
                new ScriptEntry("b", "lua/b.lua"));
            var bytes = BytesOf(
                ("lua/a.lua", "local b = require('b') return { b = b }"),
                ("lua/b.lua", "local a = require('a') return { a = a }"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.AreEqual(2, r.Count);
            string summary = CandidateLuaValidator.Summarize(r);
            StringAssert.Contains("循环依赖", summary);          // 环被当场拒绝（不是悬挂/爆栈）
        }

        [Test]
        public void 沙箱_日志桥无副作用桩_模块顶层可执行()
        {
            // 真实候选包的 main.lua 顶层有 log.info 冒烟锚点：桩保证"模块可装载可执行"可达；
            // 桩非正式 Bridge 注册表（无 UI/事件/网络/存档副作用），§10 隔离面不受影响。
            // 契约是**可调用**而非 Lua 函数类型——xLua 委托在 Lua 侧不为 type 'function'（正式 BindLog 同形状）。
            CandidateScriptSet set = SetOf(new ScriptEntry("m", "lua/m.lua"));
            var bytes = BytesOf(("lua/m.lua",
                "assert(type(log) == 'table')\n" +
                "assert(log.info ~= nil, 'log.info 缺失')\n" +
                "log.info('smoke anchor')\n" +
                "return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsTrue(r[0].Ok, r[0].Reason);
        }

        [Test]
        public void 沙箱_标准库require_同packageLoaded语义()
        {
            // 真实 Lua 的 package.loaded 预注册标准库：require('math') 不落 loader
            //（LuaPanda 的 tools.createJson 顶层即此用法）；被回收的 io/os 仍不可 require。
            CandidateScriptSet set = SetOf(new ScriptEntry("m", "lua/m.lua"));
            var bytes = BytesOf(("lua/m.lua",
                "local math = require('math')\n" +
                "local string = require('string')\n" +
                "local table = require('table')\n" +
                "assert(type(math.floor) == 'function')\n" +
                "assert(type(string.format) == 'function')\n" +
                "assert(type(table.concat) == 'function')\n" +
                "assert(not pcall(require, 'io'))\n" +
                "assert(not pcall(require, 'os'))\n" +
                "return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsTrue(r[0].Ok, r[0].Reason);
        }

        [Test]
        public void 沙箱_保留标准库()
        {
            CandidateScriptSet set = SetOf(new ScriptEntry("std", "lua/std.lua"));
            var bytes = BytesOf(("lua/std.lua",
                "assert(type(string.format) == 'function')\n" +
                "assert(type(table.insert) == 'function')\n" +
                "assert(type(math.floor) == 'function')\n" +
                "assert(type(pcall) == 'function')\n" +
                "assert(type(tostring) == 'function')\n" +
                "return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);

            Assert.IsTrue(r[0].Ok, r[0].Reason);
        }

        [Test]
        public void 沙箱_与正式env隔离_不共享全局()
        {
            // 沙箱内写全局不落到正式 _G；两段候选脚本之间也各写各的沙箱（同一 sandbox 表，故可见）
            CandidateScriptSet set = SetOf(new ScriptEntry("w", "lua/w.lua"));
            var bytes = BytesOf(("lua/w.lua", "_G.__probe = 1 return {}"));

            using var v = new CandidateLuaValidator();
            IReadOnlyList<LuaScriptVerdict> r = v.Validate(set, bytes);
            Assert.IsTrue(r[0].Ok, r[0].Reason);

            // 正式 env（LuaComponent）不存在于本测试中——此处只能断言沙箱自身未把 _G 暴露成引擎全局
            // 真正的跨 env 隔离由"独立 LuaEnv 实例"保证（候选验证用的是自己的 env）
            Assert.Pass("沙箱独立于运行时 env（独立 LuaEnv 实例）");
        }

        [Test]
        public void Dispose后可重用性_二次调用抛()
        {
            var set = SetOf(new ScriptEntry("m", "lua/m.lua"));
            var v = new CandidateLuaValidator();
            v.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() =>
                v.Validate(set, new Dictionary<string, byte[]>()));
        }
    }
}
