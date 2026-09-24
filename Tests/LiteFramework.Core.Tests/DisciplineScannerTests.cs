using System.Collections.Generic;
using System.Text;
using Tools.DisciplineScan;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 纪律扫描（《测试开发方案》§7.3 ②）：规则集自测 + 真实源码扫描。
    /// 引擎在 <c>Assets/Tools/DisciplineScanner</c>（零依赖），Editor 菜单与这里**共用同一份规则**——
    /// 取代了此前"规则在 LiteSim 引擎 / 协程扫描 / 宏扫描三处各写一遍"的重复。
    /// </summary>
    public sealed class DisciplineScannerTests
    {
        [Fact]
        public void 纪律_R1_超越函数被命中()
        {
            Assert.Equal(1, Count("var v = Math.Sin(x);", LintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = MathF.Cos(x);", LintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = Math.Atan2(y, x);", LintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = Math.Pow(a, 2.0);", LintRule.R1Transcendental));
        }

        [Fact]
        public void 纪律_R1_允许的基础运算不误报()
        {
            Assert.Equal(0, Count("var v = Math.Sqrt(x);", LintRule.R1Transcendental));
            Assert.Equal(0, Count("var v = Math.Abs(x);", LintRule.R1Transcendental));
            Assert.Equal(0, Count("var v = Math.Floor(x);", LintRule.R1Transcendental));
        }

        [Fact]
        public void 纪律_R2_FMA写法被命中()
        {
            Assert.Equal(1, Count("var v = MathF.FusedMultiplyAdd(a, b, c);", LintRule.R2Fma));
            Assert.Equal(1, Count("var v = System.Math.FusedMultiplyAdd(a, b, c);", LintRule.R2Fma));
        }

        [Fact]
        public void 纪律_R3_浮点等值比较被命中()
        {
            Assert.Equal(1, Count("if (a == b) { }", LintRule.R3FloatEquality));
            Assert.Equal(1, Count("if (a != b) { }", LintRule.R3FloatEquality));
        }

        [Fact]
        public void 纪律_R3_零常量与关系运算豁免()
        {
            Assert.Equal(0, Count("if (a == 0f) { }", LintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a != 0) { }", LintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a <= b) { }", LintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a >= b) { }", LintRule.R3FloatEquality));
        }

        [Fact]
        public void 纪律_R4_按规则集门控()
        {
            // 未启用 R4 时不报
            Assert.Equal(0, Count("using System.Linq;", LintRule.R1Transcendental));
            Assert.Equal(1, Count("using System.Linq;", LintRule.R4DeterminismContainer));
            Assert.Equal(1, Count("var q = xs.OrderBy(x => x);", LintRule.R4DeterminismContainer));
        }

        [Fact]
        public void 纪律_R5_裸UNITY_EDITOR被命中_三宏并集豁免()
        {
            Assert.Equal(1, Count("#if UNITY_EDITOR\n", LintRule.R5BareUnityEditor));
            Assert.Equal(1, Count("#if UNITY_EDITOR || DEVELOPMENT_BUILD\n", LintRule.R5BareUnityEditor));
            Assert.Equal(1, Count("#elif UNITY_EDITOR\n", LintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG\n", LintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("#if LITEFRAMEWORK_DEBUG\n", LintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("// #if UNITY_EDITOR\n", LintRule.R5BareUnityEditor));
        }

        [Fact]
        public void 纪律_R6_原生协程被命中()
        {
            Assert.Equal(1, Count("private IEnumerator Co() { }", LintRule.R6NativeCoroutine));
            Assert.Equal(1, Count("StartCoroutine(Co());", LintRule.R6NativeCoroutine));
            Assert.Equal(1, Count("StopAllCoroutines();", LintRule.R6NativeCoroutine));
            Assert.Equal(1, Count("yield return null;", LintRule.R6NativeCoroutine));
        }

        [Fact]
        public void 纪律_注释内容不参与匹配()
        {
            // 注释里提到禁用 API 不算违规（UIBubble.cs 曾因此误报）
            Assert.Equal(0, Count("// 原 StopAllCoroutines 语义，用 CTS 显式表达", LintRule.R6NativeCoroutine));
            Assert.Equal(0, Count("/// 禁 FusedMultiplyAdd 注释", LintRule.R2Fma));
            Assert.Equal(0, Count("// if (a == b) 注释里的比较", LintRule.R3FloatEquality));
            Assert.Equal(0, Count("/* Math.Sin(x) */ var y = 1;", LintRule.R1Transcendental));
            Assert.Equal(0, Count("/* FusedMultiplyAdd\n*/ var x = 1;", LintRule.R2Fma));
            // 代码上的真违规 + 行尾注释：仍应命中
            Assert.Equal(1, Count("var v = Math.Sin(x); // 注释", LintRule.R1Transcendental));
        }

        [Fact]
        public void 纪律_豁免注释生效()
        {
            Assert.Equal(0, Count("if (a == b) { } // lint-allow R3", LintRule.R3FloatEquality));
            // 只豁免 R3，R1 仍应命中
            Assert.Equal(1, Count("var v = Math.Sin(x); if (a == b) { } // lint-allow R3", LintRule.R1Transcendental));
            // 无规则号 → 整行豁免
            Assert.Equal(0, Count("var v = Math.Sin(x); // lint-allow", LintRule.R1Transcendental));
        }

        [Fact]
        public void 纪律_违规输出格式为文件行列规则代码()
        {
            List<LintViolation> v = DisciplineScanner.ScanText(
                "Foo.cs", "var x = Math.Sin(1f);", new[] { LintRule.R1Transcendental });
            Assert.Single(v);
            Assert.Equal(1, v[0].Line);
            Assert.Equal("Foo.cs:1:R1:var x = Math.Sin(1f);", v[0].ToString());
        }

        [Fact]
        public void 纪律_默认排除_编辑器目录与生成物与引擎自身()
        {
            Assert.True(DisciplineScanner.IsExcluded("Assets/LiteSim/Core/Scripts/Editor/Foo.cs"));
            Assert.True(DisciplineScanner.IsExcluded("Assets/LiteSim/Core/Scripts/SimTrigTables.cs"));
            Assert.True(DisciplineScanner.IsExcluded("Assets/Tools/DisciplineScanner/Scripts/DisciplineScanner.cs"));
            Assert.False(DisciplineScanner.IsExcluded("Assets/LiteSim/Core/Scripts/SimTrig.cs"));
        }

        [Fact]
        public void 真实源码_全部目标_零违规()
        {
            string projectRoot = ProjectLocator.FindProjectRoot();
            List<KeyValuePair<string, LintViolation>> hits = DisciplineScanner.ScanDefault(projectRoot);

            var sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                sb.Append('[').Append(hits[i].Key).Append("] ").Append(hits[i].Value).Append('\n');
            }
            Assert.True(hits.Count == 0, "纪律扫描发现违规：\n" + sb);
        }

        /// <summary>
        /// MetaServer 的 R11 边界登记（《Meta 服务专项设计》§3.2/§4.2）：
        /// 模块/契约层受纯化把守，宿主装配层（Kestrel/IO/Console）按 §12 合法豁免。
        ///
        /// 排除按**目录段前缀**匹配（<c>IsUnderExcludedRoot</c>），故 <c>MetaServer/Host</c>
        /// 只排 <c>Host/</c> 子目录、不误伤将来的 <c>Hosting/</c>。
        /// 真实源码是否干净由上面的"零违规"用例证明；本用例钉的是**登记意图**，
        /// 避免后续误把宿主层扫进去或误把模块层排除掉。
        /// </summary>
        [Fact]
        public void 纪律_MetaServer_登记为受守根_仅宿主层豁免()
        {
            bool found = false;
            ScanTarget meta = default;
            for (int i = 0; i < ScanTargets.Default.Length; i++)
            {
                if (ScanTargets.Default[i].Root == "MetaServer")
                {
                    meta = ScanTargets.Default[i];
                    found = true;
                    break;
                }
            }

            Assert.True(found, "ScanTargets.Default 缺少 MetaServer 目标——模块层将不受纪律扫描把守");
            Assert.Contains(LintRule.R11RuntimePurity, meta.Rules);
            Assert.NotNull(meta.ExcludeRoots);
            Assert.Contains("MetaServer/Host", meta.ExcludeRoots);
            // 只排宿主层的 Host/ 子目录；模块与契约层必须留在扫描内
            Assert.DoesNotContain("MetaServer/Modules", meta.ExcludeRoots);
            Assert.DoesNotContain("MetaServer/Contracts", meta.ExcludeRoots);
        }

        [Fact]
        public void 纪律_R7_非法metaGUID被命中()
        {
            // 2026-09-15 事故形态：64 位 base64 guid（Unity 拒收 → 资源静默消失）
            var v = DisciplineScanner.ScanMetaText(
                "A.cs.meta",
                "fileFormatVersion: 2\nguid: CnpNtin4W3zo6TQqjvzFRl7jkSDkR2TEnPSUX6izpYrJlgIFe7QCcvs=\n");
            Assert.Single(v);
            Assert.Equal(LintRule.R7InvalidMetaGuid, v[0].Rule);
            Assert.Equal(2, v[0].Line);
            Assert.Contains("CnpNtin4", v[0].Code);
        }

        [Fact]
        public void 纪律_R7_合法GUID不误报_大小写均接受()
        {
            Assert.Empty(DisciplineScanner.ScanMetaText(
                "A.cs.meta",
                "fileFormatVersion: 2\nguid: 6c159f085a3f6d0408542447296ba288\n"));
            Assert.Empty(DisciplineScanner.ScanMetaText(
                "A.cs.meta",
                "fileFormatVersion: 2\nguid: 6C159F085A3F6D0408542447296BA288\n"));
        }

        [Fact]
        public void 纪律_R7_缺guid行被命中()
        {
            var v = DisciplineScanner.ScanMetaText("A.cs.meta", "fileFormatVersion: 2\n");
            Assert.Single(v);
            Assert.Equal(1, v[0].Line);
            Assert.Equal("(缺少 guid 行)", v[0].Code);
        }

        [Fact]
        public void 纪律_R7_真实仓库meta全部合法()
        {
            // 守卫本次事故形态：任何非法 guid 的 .meta 都会在 Unity 里静默失效
            string projectRoot = ProjectLocator.FindProjectRoot();
            var hits = DisciplineScanner.ScanMetas(projectRoot, "Assets");
            var sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++) sb.Append(hits[i]).Append('\n');
            Assert.True(hits.Count == 0, "发现非法 .meta GUID：\n" + sb);
        }

        [Fact]
        public void 纪律_R8_ResourcesLoad被命中()
        {
            Assert.Equal(1, Count("var go = Resources.Load<GameObject>(\"x\");", LintRule.R8ResourcesLoad));
            Assert.Equal(1, Count("var op = Resources.LoadAsync(\"x\");", LintRule.R8ResourcesLoad));
            Assert.Equal(1, Count("var go = Resources . Load (\"x\");", LintRule.R8ResourcesLoad));   // 空白容忍
            Assert.Equal(0, Count("// 曾用 Resources.Load 取配置，已改资源服务", LintRule.R8ResourcesLoad));
            Assert.Equal(0, Count("_assets.Load<GameObject>(\"x\");", LintRule.R8ResourcesLoad));    // 正确入口不报
        }

        [Fact]
        public void 纪律_R9_Mod类型被命中_驼峰与独立词与接口前缀()
        {
            Assert.Equal(1, Count("var m = new ModLoader();", LintRule.R9ModInSim));
            Assert.Equal(1, Count("ModManager.Init();", LintRule.R9ModInSim));
            Assert.Equal(1, Count("private IModContext _ctx;", LintRule.R9ModInSim));
            Assert.Equal(1, Count("var mod = Mod;", LintRule.R9ModInSim));
        }

        [Fact]
        public void 纪律_R9_Mod加小写不误报()
        {
            // Mod 后接小写 = Mode/Model/Modify/Modules/Modulo —— 都不是模组
            Assert.Equal(0, Count("var mode = SimMode.Duel;", LintRule.R9ModInSim));
            Assert.Equal(0, Count("var m = _model;", LintRule.R9ModInSim));
            Assert.Equal(0, Count("ModifyValue(x);", LintRule.R9ModInSim));
            Assert.Equal(0, Count("var mods = modules;", LintRule.R9ModInSim));
        }

        [Fact]
        public void 纪律_R10_壳UI直发INetworkService被命中()
        {
            Assert.Equal(1, Count("private readonly INetworkService _net;", LintRule.R10ShellSendsBusinessPacket));
            Assert.Equal(1, Count("var n = container.Resolve<INetworkService>();", LintRule.R10ShellSendsBusinessPacket));
            Assert.Equal(0, Count("var s = new DockSlotService();", LintRule.R10ShellSendsBusinessPacket));
        }

        [Fact]
        public void 纪律_R8R9R10_按规则集门控_不污染其他根()
        {
            // R8 只在 GameRules 生效：Sim 的 SimRules 里没有 R8 → 同文本不报（规则集门控语义）
            Assert.Equal(0, Count("Resources.Load(\"x\");", LintRule.R1Transcendental));
            // 规则号命名与 lint-allow 解析
            Assert.Equal("R8", DisciplineScanner.RuleId(LintRule.R8ResourcesLoad));
            Assert.Equal("R9", DisciplineScanner.RuleId(LintRule.R9ModInSim));
            Assert.Equal("R10", DisciplineScanner.RuleId(LintRule.R10ShellSendsBusinessPacket));
            // 行内豁免仍适用（含规则号的豁免只免该条）
            Assert.Equal(0, Count("Resources.Load(\"x\"); // lint-allow R8", LintRule.R8ResourcesLoad));
        }

        // ---- R11：RoomServer/Runtime 纯化（《商业级通用服务端框架总设计》§8.1 禁止项）----

        [Fact]
        public void 纪律_R11_运行时纯化违例被逐类命中()
        {
            // 传输/Socket/协议
            Assert.Equal(1, Count("var t = new KcpTransportServer();", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("private readonly IRoomTransport _transport;", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var s = new Socket(...);", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var ep = new System.Net.IPEndPoint(...);", LintRule.R11RuntimePurity));
            // proto / LiteNet 引用
            Assert.Equal(1, Count("using LiteNet.Protocol;", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var x = LiteNet.Protocol.PacketType.Join;", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var b = Google.Protobuf.IMessage.Extensions;", LintRule.R11RuntimePurity));
            // 墙钟/等待/Console/文件/随机
            Assert.Equal(1, Count("var t = DateTime.Now;", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var t = Environment.TickCount64;", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var sw = Stopwatch.StartNew();", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("Thread.Sleep(16);", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("Console.WriteLine(\"tick\");", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("File.ReadAllText(path);", LintRule.R11RuntimePurity));
            Assert.Equal(1, Count("var r = new Random();", LintRule.R11RuntimePurity));
        }

        [Fact]
        public void 纪律_R11_运行时合法面不误报()
        {
            // Sim 域引用与确定性工具是 Runtime 的合法面
            Assert.Equal(0, Count("using LiteSim;", LintRule.R11RuntimePurity));
            Assert.Equal(0, Count("var v = SimMath.MulAdd2(a, b, c, d);", LintRule.R11RuntimePurity));
            Assert.Equal(0, Count("AuthSim.RngState = (ulong)Seed;", LintRule.R11RuntimePurity));
            Assert.Equal(0, Count("var ring = new PendingInputRing(16);", LintRule.R11RuntimePurity));
            // 常量复述不引用 LiteNet（ClientInputBatch.MaxFrames 的既定形态）
            Assert.Equal(0, Count("public const int MaxFrames = 4;", LintRule.R11RuntimePurity));
            // 注释里的禁用 API 名不算违规
            Assert.Equal(0, Count("// 不打印 Console、不读 DateTime.Now（§8.1 禁止项）", LintRule.R11RuntimePurity));
            // 规则号命名 + 门控（R11 只对 RoomServer/Runtime 目标启用，其他根的文本不经过本规则）
            Assert.Equal("R11", DisciplineScanner.RuleId(LintRule.R11RuntimePurity));
            Assert.Equal(0, Count("Console.WriteLine(\"ok\"); // lint-allow R11", LintRule.R11RuntimePurity));
        }

        private static int Count(string text, LintRule rule)
        {
            return DisciplineScanner.ScanText("test.cs", text, new[] { rule }).Count;
        }
    }
}
