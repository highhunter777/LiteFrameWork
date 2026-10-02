using System.Collections.Generic;
using System.Text;
using Tools.DisciplineScan;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 纪律扫描（《测试开发方案》§7.3 ②）：规则集自测 + 真实源码扫描。
    /// 引擎在 <c>Assets/Tools/DisciplineScanner</c>（零依赖），Editor 菜单与这里**共用同一份规则**。
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
            // 注释里提到禁用 API 不算违规
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
            // 事故形态：64 位 base64 guid（Unity 拒收 → 资源静默消失）
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

        [Fact]
        public void 纪律_R12_已接入真实扫描目标_且目标路径存在()
        {
            // **防"静默失效"**：本文件里 R6/R10 的两个 LiteGame 扫描目标可能因路径写错
            // （如 `Assets/LiteGame/Scripts/Runtime/...`，实际无 `Scripts/`）**空扫**——
            // 规则写了但从未生效，且没有任何东西会红。
            // 故此处同时钉两件事：①R12 在 GameRules 里；②每个目标根**目录真实存在**。
            var gameTarget = System.Array.Find(
                Tools.DisciplineScan.ScanTargets.Default,
                t => t.Root == "Assets/LiteGame");
            Assert.Equal("Assets/LiteGame", gameTarget.Root);
            Assert.Contains(LintRule.R12AdapterBoundary, gameTarget.Rules);

            string repoRoot = RepoRoot();
            foreach (var target in Tools.DisciplineScan.ScanTargets.Default)
            {
                Assert.True(System.IO.Directory.Exists(System.IO.Path.Combine(repoRoot, target.Root)),
                    $"扫描目标根不存在，该规则集正在**空扫**：{target.Root}");
            }
        }

        /// <summary>R12 **边界表**的存在性守卫：边界路径一旦因目录搬迁而陈旧，判定会静默失效
        /// （纯前缀比较，不匹配任何真实文件）。与上面的"扫描目标必须存在"防同一类失效——
        /// 此守卫让规则表过期当场可见。</summary>
        [Fact]
        public void 纪律_R12_边界表路径必须真实存在()
        {
            var violations = DisciplineScanner.ValidateAdapterBoundaries(RepoRoot());
            Assert.True(violations.Count == 0,
                "R12 边界表存在陈旧路径（该边界正在静默失效）：" +
                string.Join(" | ", System.Linq.Enumerable.Select(violations, v => v.Code)));
        }

        /// <summary>§5 顶层分层守卫：`Assets/LiteGame/` 下不允许出现未登记的顶层目录。
        /// 顶层目录必须落在设计的层里（四个适配器不可平铺在根上）。</summary>
        [Fact]
        public void 纪律_LiteGame顶层目录必须已登记()
        {
            var unregistered = Tools.DisciplineScan.ScanTargets.ValidateLiteGameTopLevel(RepoRoot());
            Assert.True(unregistered.Count == 0,
                "Assets/LiteGame 下存在未登记的顶层目录（逃出 §5 分层，或需在 LiteGameTopLevelDirs 登记并说明归属）："
                + string.Join(", ", unregistered));
        }

        /// <summary>D2 守卫（《客户端与服务端共享代码范围专项设计》§3）：客户端 asmdef 引服务端程序集，
        /// 必须落在 <c>DevHostMayReferenceServerAssemblies</c> 登记目录里。
        ///
        /// 原因：`LiteClient.Runtime` 若无条件引用两个 RoomServer 程序集，唯一消费者却是一个开发面文件
        /// （进程内本地服）→ 两个服务端程序集进**所有** Player 构建。`ProcedureMatch` 的 `#if`
        /// 只挡调用点、挡不住 asmdef 引用（引用是程序集级的）。</summary>
        [Fact]
        public void 纪律_D2_客户端引服务端程序集必须在调试宿主档位内()
        {
            string root = RepoRoot();
            var problems = Tools.DisciplineScan.ScanTargets.ValidateDevHostReferences(root);
            Assert.True(problems.Count == 0, string.Join(" | ", problems));

            string clientRoot = System.IO.Path.Combine(root, "Assets", "LiteGame");
            var allowed = new System.Collections.Generic.List<string>();
            foreach (string d in Tools.DisciplineScan.ScanTargets.DevHostMayReferenceServerAssemblies)
                allowed.Add(System.IO.Path.Combine(root, d.Replace('/', System.IO.Path.DirectorySeparatorChar))
                    + System.IO.Path.DirectorySeparatorChar);

            var offenders = new System.Collections.Generic.List<string>();
            foreach (string asmdef in System.IO.Directory.GetFiles(clientRoot, "*.asmdef",
                         System.IO.SearchOption.AllDirectories))
            {
                bool isAllowed = false;
                foreach (string a in allowed)
                    if (asmdef.StartsWith(a, System.StringComparison.Ordinal)) { isAllowed = true; break; }
                if (isAllowed) continue;

                string text = System.IO.File.ReadAllText(asmdef);
                foreach (string asm in Tools.DisciplineScan.ScanTargets.ServerAssemblyNames)
                    if (text.Contains("\"" + asm + "\""))
                        offenders.Add(System.IO.Path.GetFileName(asmdef) + " 引用了 " + asm);
            }

            Assert.True(offenders.Count == 0,
                "客户端业务程序集不得依赖服务端程序集（D2：只许落在调试宿主档位 "
                + string.Join("/", Tools.DisciplineScan.ScanTargets.DevHostMayReferenceServerAssemblies)
                + "）：" + string.Join(" | ", offenders));
        }

        /// <summary>客户端框架侧纯度（《客户端总设计》§5.1）：`LiteClient.*`（通用核心 + 适配器）整体
        /// 不得引用 `LiteGame.*`——产品面（含 `LuaBridge`）依赖框架，反向一律禁止。
        /// 没有本用例，"框架侧可提取"只是文字约定。</summary>
        [Fact]
        public void 纪律_客户端框架侧不得引用LiteGame程序集()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateClientFrameworkPurity(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>注释卫生（AGENTS 代码规范 6）：第一方代码注释不得出现日期戳/批次号等施工痕迹（历史进 Docs/施工进度）。</summary>
        [Fact]
        public void 纪律_代码注释不含施工痕迹()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateCommentHygiene(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>D1 守卫（《客户端与服务端共享代码范围专项设计》§3）：服务端宿主工程的
        /// `ProjectReference` **不得**指向客户端面（`LiteFramework`/`LiteGame`/`LiteClient`/`LiteSim/View`）。
        ///
        /// 本用例把它钉成判据，
        /// 防止日后有人图方便让宿主直接引客户端件（那会让"服务端只依赖 S0/S1/S2"从规则退化成习惯）。</summary>
        [Fact]
        public void 纪律_D1_服务端宿主不得依赖客户端面()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateServerHostDirection(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>G5 守卫（§8.1）：共享档位（S1/S2）成员的 asmdef 必须满足义务 ①
        /// （`noEngineReferences: true`）。给共享件加一个 Unity 引用，它就**再也编不进 dotnet 侧**，
        /// 而没有任何东西会红——本用例就是那个"东西"。</summary>
        [Fact]
        public void 纪律_G5_共享档位成员的asmdef必须零引擎()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateSharedAssemblyFlags(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>G5 守卫（§2.2 义务②）：S2 契约层必须**零第三方实现依赖**。
        /// 再往 Core 加第三方引用当场红（否则"服务端可直接消费的契约层"就名存实亡）。</summary>
        [Fact]
        public void 纪律_G5_S2契约层必须零第三方实现依赖()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateContractLayerDependencies(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>G2 守卫（§8.1）：`Assets/` 下不得出现 `bin`/`obj`（Unity 会当插件导入 → CS1704/CS0579）。
        /// 各双轨目录靠同目录 `Directory.Build.props` 重定向到 `.dotnet/`——**漏配就出这事，而漏配不报错**。</summary>
        [Fact]
        public void 纪律_G2_Assets下不得有构建产物目录()
        {
            var problems = Tools.DisciplineScan.ScanTargets.ValidateNoBuildOutputsUnderAssets(RepoRoot());
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与内容夹具同款定位）。</summary>
        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        /// <summary>带路径的计数——R12 的判定依赖文件路径（边界目录内放行），故必须能传路径。</summary>
        private static int CountAt(string relativePath, string text, LintRule rule)
        {
            return DisciplineScanner.ScanText(relativePath, text, new[] { rule }).Count;
        }

        // ---- R12 适配器边界（《客户端总设计》§5.1"形成逻辑边界和依赖测试"）----

        [Fact]
        public void 纪律_R12_边界目录内_对应适配器import放行()
        {
            // **每个边界只放行它对应的那一个适配器**——不是"边界目录里什么都能 import"。
            // 适配器在 `Adapters/` 层；框架侧在 `Assets/LiteClient/`，
            // Lua 桥归 `Assets/LiteGame/LuaBridge/`（目录一变规则就红，本用例即其表现）。
            Assert.Equal(0, CountAt("Assets/LiteClient/Adapters/Content.YooAsset/C.cs",
                "using YooAsset;", LintRule.R12AdapterBoundary));
            Assert.Equal(0, CountAt("Assets/LiteClient/Adapters/Scripting.XLua/Lua/C.cs",
                "using XLua;", LintRule.R12AdapterBoundary));
            Assert.Equal(0, CountAt("Assets/LiteGame/LuaBridge/C.cs",
                "using XLua;", LintRule.R12AdapterBoundary));
            Assert.Equal(0, CountAt("Assets/LiteGame/UI/Anim/C.cs",
                "using DG.Tweening;", LintRule.R12AdapterBoundary));

            // 反向：边界目录**不**放行别人的适配器
            Assert.Equal(1, CountAt("Assets/LiteClient/Adapters/Content.YooAsset/C.cs",
                "using XLua;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteClient/Adapters/Scripting.XLua/Lua/C.cs",
                "using YooAsset;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteGame/LuaBridge/C.cs",
                "using YooAsset;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteGame/UI/Anim/C.cs",
                "using YooAsset;", LintRule.R12AdapterBoundary));
        }

        [Fact]
        public void 纪律_R12_边界外_适配器import被命中()
        {
            Assert.Equal(1, CountAt("Assets/LiteClient/Runtime/C.cs",
                "using YooAsset;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteGame/App/C.cs",
                "using DG.Tweening;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteGame/UI/Adapter.cs",
                "using XLua;", LintRule.R12AdapterBoundary));
        }

        [Fact]
        public void 纪律_R12_前缀相近的目录不误放行()
        {
            // 边界表的路径比较两端补斜杠：`Scripting.XLua` 不得放行 `Scripting.XLuaExtras`
            Assert.Equal(1, CountAt("Assets/LiteClient/Adapters/Scripting.XLuaExtras/C.cs",
                "using XLua;", LintRule.R12AdapterBoundary));
            Assert.Equal(1, CountAt("Assets/LiteGame/UI/Animation/C.cs",
                "using DG.Tweening;", LintRule.R12AdapterBoundary));
        }

        [Fact]
        public void 纪律_R12_非适配器import与限定名不受影响()
        {
            Assert.Equal(0, CountAt("Assets/LiteClient/Runtime/C.cs",
                "using System.Collections;", LintRule.R12AdapterBoundary));

            // 边界表只列 YooAsset/XLua/DG.Tweening——其它第三方不受 R12 管
            Assert.Equal(0, CountAt("Assets/LiteGame/App/C.cs",
                "using UniTask;", LintRule.R12AdapterBoundary));
        }

        [Fact]
        public void 纪律_R12_注释里的using不算违规()
        {
            // 与其它规则同口径：注释先剔除再匹配
            Assert.Equal(0, CountAt("Assets/LiteClient/Runtime/C.cs",
                "// 该文件不用 using YooAsset; 了", LintRule.R12AdapterBoundary));
        }
    }
}
