using System;
using System.Collections.Generic;
using System.IO;

namespace Tools.DisciplineScan
{
    /// <summary>一个扫描目标：源根（相对项目根）+ 该根启用的规则子集 + 可选排除子根。</summary>
    public struct ScanTarget
    {
        public string Root;
        public LintRule[] Rules;

        /// <summary>从本目标排除的子根（相对项目根，前缀匹配；null/空 = 不排除）。
        /// 用途：同一大根下混有不同领域的代码（如 LiteSim 的确定性 Core 与表现层 View）。</summary>
        public string[] ExcludeRoots;

        public ScanTarget(string root, LintRule[] rules) : this(root, rules, null) { }

        public ScanTarget(string root, LintRule[] rules, string[] excludeRoots)
        {
            Root = root;
            Rules = rules;
            ExcludeRoots = excludeRoots;
        }
    }

    /// <summary>
    /// 默认多根配置（《测试开发方案》§7.3 ②「一个引擎 + 多根规则集」）。
    /// 规则定义只此一份；各根按各自领域启用子集。
    /// </summary>
    public static class ScanTargets
    {
        // 规则子集（必须在 Default 之前初始化——静态字段按声明顺序初始化）
        /// <summary>LiteSim：确定性数值层，R1~R5 全适用 + R9 模组红线（判定必须在权威内）。</summary>
        public static readonly LintRule[] SimRules =
        {
            LintRule.R1Transcendental,
            LintRule.R2Fma,
            LintRule.R3FloatEquality,
            LintRule.R4DeterminismContainer,
            LintRule.R5BareUnityEditor,
            LintRule.R9ModInSim,
        };

        /// <summary>LiteFramework.Core：只守 R5（宏并集）；Core 合法使用 Dictionary，故不启 R4。</summary>
        public static readonly LintRule[] CoreRules =
        {
            LintRule.R5BareUnityEditor,
        };

        /// <summary>LiteNet：客户端与 .NET 服务器共用 → R5 必守（禁裸 UNITY_EDITOR——双端编译是红线）；
        /// R1–R4 属 Sim 确定性纪律，不适用于传输/协议层（广播遍历序非逻辑序）；Vendor/ 已全局排除。</summary>
        public static readonly LintRule[] NetRules =
        {
            LintRule.R5BareUnityEditor,
        };

        /// <summary>业务 Unity 层：只守 R6（原生协程）。</summary>
        public static readonly LintRule[] UnityRules =
        {
            LintRule.R6NativeCoroutine,
        };

        /// <summary>LiteGame 全域：R8 资源唯一入口 + R12 适配器边界（Editor 目录由 IsExcluded 排除）。</summary>
        public static readonly LintRule[] GameRules =
        {
            LintRule.R8ResourcesLoad,
            // R12 适配器边界（《客户端总设计》§5.1"形成逻辑边界和依赖测试"）：
            // YooAsset/xLua/DOTween 的实现只许在各自边界目录内 import——
            // 阻止新耦合扩散：在一体 asmdef 里，没有它，任何人都能再 import 一次而无人察觉。
            LintRule.R12AdapterBoundary,
        };

        /// <summary>薄壳/UI：R10 不得直发业务包（禁 INetworkService 契约）。</summary>
        public static readonly LintRule[] ShellUiRules =
        {
            LintRule.R10ShellSendsBusinessPacket,
        };

        /// <summary>RoomServer/Runtime：R11 纯化（《商业级通用服务端框架总设计》§8.1 禁止项——
        /// Transport/墙钟/Console/文件/proto 引用一概不得进入纯运行时层）。</summary>
        public static readonly LintRule[] RuntimePurityRules =
        {
            LintRule.R11RuntimePurity,
        };

        /// <summary>MetaServer：R11 同类把守（《Meta 服务专项设计》§3.2/§4.2）——
        /// Meta 的模块层与契约层不得引用 IO/墙钟/Console，也不得引用 LiteNet/proto/Kcp：
        /// 局外契约不混进战斗协议，且 Meta 类型永不进入 RoomServer/Runtime。
        /// 宿主装配层（Host/Infrastructure）合法使用 Web/IO，故排除在外。</summary>
        public static readonly LintRule[] MetaPurityRules =
        {
            LintRule.R11RuntimePurity,
        };

        /// <summary>LiteSim 下不适用确定性规则的表现层子根（View 跑引擎、有 GameObject，
        /// 与 Sim 的定点/纯 C# 约束是两回事；只守 R6 原生协程）。</summary>
        public static readonly string[] SimLayerExcludes =
        {
            "Assets/LiteSim/View",
        };

        /// <summary>MetaServer 的宿主装配层：合法使用 Kestrel/Web/IO/Console（服务端总设计 §12 Generic Host），
        /// 不属于模块或契约层，故不进 R11 扫描。目标根内其余子目录（Contracts/、Modules/、Infrastructure/）
        /// **自动**纳入扫描，无需再改本文件。</summary>
        public static readonly string[] MetaHostExcludes =
        {
            "MetaServer/Host",
        };

        /// <summary>
        /// `Assets/LiteGame/` 下**允许存在的顶层目录**（《客户端总设计》§5 顶层分层）；
        /// 框架侧 `LiteClient.*` 住 `Assets/LiteClient/`（见 <see cref="Default"/> 扫描目标）。
        ///
        /// 新增顶层目录必须在此登记，并说明它属于 §5 的哪一层（或为何是例外）。
        /// 未登记的目录会让本规则报红——**目录一变就红**，与 R12 边界表同一纪律。
        /// 非代码目录（`Lua`/`link.xml` 等）也在列：它们不是"层"，但要有名有姓。
        /// </summary>
        public static readonly string[] LiteGameTopLevelDirs =
        {
            "App",                   // §5 顶层第一层 Game.App
            "UI",                    // §5 框图 "UI Runtime"（游戏侧，故 LiteGame.UI 而非 LiteClient.UI）
            "Editor",                // 编辑器工具程序集（不在 §5 层内，登记为例外）
            "DevHUD",                // 开发面工具（同上；**不能并进 Editor/**——其中的 MonoBehaviour 无法 AddComponent）
            "DevLocalServer",        // 开发面：进程内本地服（《共享代码范围专项设计》§3 D2 调试宿主档位；见下）
            "Lua",                   // 脚本资产（非程序集目录；Luban lua pass 的产物 + 业务脚本）
            "LuaBridge",             // 产品侧 Lua 桥（LiteGame.LuaBridge：生命周期/数据门面/注册表；宿主 LiteClient.Scripting.XLua 的消费者）
        };

        /// <summary>
        /// 校验 `Assets/LiteGame` 顶层目录全部已登记（未登记 = 逃出设计分层，见上）。
        /// 返回未登记目录名（无则空）。
        /// </summary>
        public static List<string> ValidateLiteGameTopLevel(string projectRoot)
        {
            var unregistered = new List<string>();
            string root = Path.Combine(projectRoot, "Assets", "LiteGame");
            if (!Directory.Exists(root)) return unregistered;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (Array.IndexOf(LiteGameTopLevelDirs, name) < 0) unregistered.Add(name);
            }
            unregistered.Sort(StringComparer.Ordinal);
            return unregistered;
        }

        /// <summary>默认扫描目标集合。</summary>
        public static readonly ScanTarget[] Default =
        {
            new ScanTarget("Assets/LiteSim", SimRules, SimLayerExcludes),
            new ScanTarget("Assets/LiteSim/View", UnityRules),                                 // 表现层：只守 R6
            new ScanTarget("Assets/LiteNet", NetRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Core", CoreRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Unity", UnityRules),
            // R6 的正则 `\byield\s+return\b` **不区分 C# 迭代器与 Unity 协程**——
            // IEnumerable 迭代器的 `yield return` 会被误报。
            new ScanTarget("Assets/LiteClient", GameRules),                                    // R8 资源唯一入口 + R12 适配器边界（框架侧：Abstractions/Runtime/Adapters）
            new ScanTarget("Assets/LiteGame", GameRules),                                      // R8 资源唯一入口 + R12 适配器边界（产品侧）
            new ScanTarget("Assets/LiteGame/UI", ShellUiRules),                                // R10 薄壳/UI 不发业务包
            // 该目标的存在性由 `纪律_R12_已接入真实扫描目标_且目标路径存在` 钉住。
            new ScanTarget("Assets/RoomServer/Runtime", RuntimePurityRules),                  // R1 纯运行时层（R1《服务端总设计》§8.1）
            new ScanTarget("MetaServer", MetaPurityRules, MetaHostExcludes),                    // Meta 模块/契约层（《Meta 服务专项设计》§4.2）
        };

        /// <summary>
        /// .meta 扫描根（R7 非法 GUID：64 位 base64 guid 会被 Unity 拒收）。
        /// 任何 .meta 的 guid 都必须是 32 位 hex；Editor 目录**不豁免**（meta 不是 C#，
        /// <see cref="DisciplineScanner.IsExcluded"/> 的 Editor 排除不适用于 meta 扫描）。
        /// </summary>
        public static readonly string[] MetaRoots =
        {
            "Assets",
            "Packages",
        };

        /// <summary>
        /// **客户端对服务端程序集的引用登记表**（《客户端与服务端共享代码范围专项设计》§3 规则 D2）。
        ///
        /// 判据（D2）：客户端**可以**依赖服务端程序集，但只在**调试宿主**档位里，
        /// 且该依赖必须落在**具名程序集**上——不得回流进业务程序集
        /// （`#if` 只挡调用点，挡不住 asmdef 引用——引用是程序集级的）。
        ///
        /// 与 R12 的 <see cref="DisciplineScanner.R12Boundaries"/> 同一纪律：**这是唯一登记点**，
        /// 新增一行即放行一个程序集；未登记的 asmdef 引了服务端程序集 → 报红。
        /// 存在性由 <see cref="ValidateDevHostReferences"/> 守卫（路径陈旧 = 静默失效，同 R12 的坑）。
        /// </summary>
        public static readonly string[] DevHostMayReferenceServerAssemblies =
        {
            // 唯一合法档位：进程内本地服（离线隔离开发）。它是 RoomRuntime/SnapshotPipeline 的调试宿主，
            // 不进生产路径——`ProcedureMatch.CreateTransport` 在 release 宏下恒走真 KCP。
            "Assets/LiteGame/DevLocalServer",
        };

        /// <summary>客户端侧禁止出现的服务端程序集名（引它们必须落在上表的登记目录里）。</summary>
        public static readonly string[] ServerAssemblyNames =
        {
            "RoomServer.Runtime",
            "RoomServer.Application.Runtime",
        };

        /// <summary>
        /// **服务端产线**（《共享代码范围专项设计》§3 规则 D1）：服务端宿主工程**只准**依赖
        /// S0/S1/S2，不得依赖客户端面（S3/S4）。本表把该约定钉成可执行判据。
        /// </summary>
        public static readonly string[] ServerHostProjects =
        {
            "RoomServer/RoomServer.csproj",
            "MetaServer/MetaServer.csproj",
        };

        /// <summary>服务端宿主工程不得引用的客户端面程序集关键字（出现在 ProjectReference 即违规）。</summary>
        public static readonly string[] ClientSideProjectMarkers =
        {
            "LiteFramework",
            "LiteGame",
            "LiteClient",
            "LiteSim/View",
        };

        /// <summary>
        /// **共享档位表 = 代码**（《共享代码范围专项设计》§2.1 的 S0–S4，§8.1 守卫 G5）。
        ///
        /// 钉的是**档位义务**中最容易静默退化的那一条——S1/S2 成员的 asmdef 必须是
        /// `noEngineReferences: true`（S2 还要求零第三方实现依赖）。一旦有人给共享件加一个
        /// Unity 引用，它就**编不进 dotnet 侧**，而没有任何东西会红。
        ///
        /// 与 G1（S0 源集单源）、D1/D2（方向）合起来，覆盖档位义务的可机械判定部分；
        /// S0 的"进 hash 源集"由 G1 清单承担，不在此重复。
        /// </summary>
        public static readonly string[] SharedAssemblyDefs =
        {
            // S1 机制共享：两端都要跑这套机制，但结果不需逐位一致
            "Assets/RoomServer/Runtime/RoomServer.Runtime.asmdef",
            "Assets/RoomServer/Application/RoomServer.Application.Runtime.asmdef",
            "Assets/LiteNet/LiteNet.asmdef",
            // S2 契约共享：只有形状共享，两端各自实现（**零第三方实现依赖**——见下）
            "Assets/LiteFramework/Scripts/Core/LiteFramework.Core.asmdef",
        };

        /// <summary>
        /// **S2 档位的额外义务②：零第三方实现依赖**（§2.2）。
        ///
        /// `LiteFramework.Core` 的 `references` 只允许**本仓自研程序集**——第三方实现不得进入
        /// （否则服务端用其契约会被拖进实现级依赖）。
        /// 判据用白名单（而非黑名单）——新增依赖必须显式登记；有人往 Core 加第三方引用即报红。
        /// </summary>
        public static readonly (string Asmdef, string[] AllowedThirdParty)[] ContractZeroDepDefs =
        {
            ("Assets/LiteFramework/Scripts/Core/LiteFramework.Core.asmdef", new string[0]),
        };

        /// <summary>
        /// 校验 S2 契约层**零第三方实现依赖**（§2.2 义务②）。返回违规描述（空 = 通过）。
        /// </summary>
        public static List<string> ValidateContractLayerDependencies(string projectRoot)
        {
            var problems = new List<string>();
            foreach (var (rel, allowed) in ContractZeroDepDefs)
            {
                string path = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    problems.Add("S2 契约层 asmdef 不存在：" + rel + "（规则静默失效）");
                    continue;
                }
                string text = File.ReadAllText(path);
                int i = text.IndexOf("\"references\"", StringComparison.Ordinal);
                if (i < 0) continue;
                int open = text.IndexOf('[', i);
                int close = text.IndexOf(']', open);
                if (open < 0 || close < 0) continue;

                string body = text.Substring(open + 1, close - open - 1);
                foreach (string raw in body.Split(','))
                {
                    // 去引号与空白；空项（references 为空数组）跳过
                    string dep = raw.Replace("\"", string.Empty).Trim();
                    if (dep.Length == 0) continue;
                    bool isAllowed = Array.IndexOf(allowed, dep) >= 0;
                    // 本仓自研程序集（Lite* / RoomServer*）不算第三方
                    if (!isAllowed && (dep.StartsWith("Lite", StringComparison.Ordinal)
                                       || dep.StartsWith("RoomServer", StringComparison.Ordinal)))
                        isAllowed = true;
                    if (!isAllowed)
                        problems.Add("S2 契约层引用了第三方实现依赖（义务②要求零第三方）：" + rel + " -> " + dep);
                }
            }
            return problems;
        }

        /// <summary>
        /// 校验档位成员的 asmdef 满足 S1/S2 的共同义务 ①（`noEngineReferences: true`）。
        /// 返回违规描述（空 = 通过）。**每行的 Path 必须真实存在**，否则规则静默失效（同 R12 的坑）。
        /// </summary>
        public static List<string> ValidateSharedAssemblyFlags(string projectRoot)
        {
            var problems = new List<string>();
            foreach (string rel in SharedAssemblyDefs)
            {
                string path = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    problems.Add("档位成员 asmdef 不存在：" + rel + "（档位表陈旧 → 义务判定静默失效）");
                    continue;
                }
                string text = File.ReadAllText(path);
                // 缺键 = Unity 默认 true（旧式 asmdef）；显式 false 才是违规
                if (text.IndexOf("\"noEngineReferences\": false", StringComparison.Ordinal) >= 0)
                    problems.Add("档位成员声明了引擎引用（S1/S2 义务①要求 noEngineReferences: true）：" + rel);
            }
            return problems;
        }

        /// <summary>
        /// **G2**：`Assets/` 下不得出现 `bin`/`obj` 构建产物目录（点目录除外）。
        ///
        /// 为什么：Unity 会把 `Assets/` 下的 `bin/*.dll` 当插件导入，与 asmdef 编出的同名程序集
        /// 撞车（CS1704），并扫描 `obj/` 里的生成 `.cs`（CS0579）。各双轨目录靠同目录
        /// `Directory.Build.props` 重定向到 `.dotnet/`——**漏配就出这事，而漏配不报错**。
        /// 本判据是预防性的：它会挡住"给新目录建 csproj 时忘配 props"。
        /// </summary>
        public static List<string> ValidateNoBuildOutputsUnderAssets(string projectRoot)
        {
            var problems = new List<string>();
            string assets = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assets)) return problems;

            foreach (string dir in Directory.GetDirectories(assets, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(dir);
                if (name != "bin" && name != "obj") continue;
                // `.dotnet/` 是**约定**的重定向产物目录（点开头 Unity 资产库直接忽略），合法
                string rel = dir.Substring(projectRoot.Length).Replace('\\', '/').TrimStart('/');
                if (rel.IndexOf("/.dotnet/", StringComparison.Ordinal) >= 0) continue;
                problems.Add("Assets 下出现构建产物目录（Unity 会当插件导入 → CS1704/CS0579；"
                    + "给该目录补 Directory.Build.props 重定向到 .dotnet/）：" + rel);
            }
            return problems;
        }

        /// <summary>
        /// §3 规则 **D1** 校验：服务端宿主工程的 `ProjectReference` 不得指向客户端面。
        /// 返回违规描述（空 = 通过）。
        /// </summary>
        public static List<string> ValidateServerHostDirection(string projectRoot)
        {
            var problems = new List<string>();
            foreach (string proj in ServerHostProjects)
            {
                string path = Path.Combine(projectRoot, proj.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    problems.Add("服务端宿主工程不存在：" + proj + "（D1 登记表陈旧 → 规则静默失效）");
                    continue;
                }
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.IndexOf("ProjectReference", StringComparison.Ordinal) < 0) continue;
                    foreach (string marker in ClientSideProjectMarkers)
                        if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                            problems.Add(proj + " 的 ProjectReference 指向客户端面（违反 D1）：" + line.Trim());
                }
            }
            return problems;
        }

        /// <summary>
        /// **登记表存在性守卫**（同 R12 的 <c>ValidateAdapterBoundaries</c>）：登记目录必须在仓库里真实存在，
        /// 且该目录下确有 asmdef 引用了服务端程序集——否则登记表陈旧 = 规则静默失效。
        /// </summary>
        public static List<string> ValidateDevHostReferences(string projectRoot)
        {
            var problems = new List<string>();
            foreach (string root in DevHostMayReferenceServerAssemblies)
            {
                string dir = Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir))
                {
                    problems.Add("登记目录不存在：" + root + "（登记表陈旧 → D2 规则静默失效）");
                    continue;
                }
                bool referencesServer = false;
                foreach (string asmdef in Directory.GetFiles(dir, "*.asmdef", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(asmdef);
                    foreach (string asm in ServerAssemblyNames)
                        if (text.Contains("\"" + asm + "\"")) { referencesServer = true; break; }
                    if (referencesServer) break;
                }
                if (!referencesServer)
                    problems.Add("登记目录下无 asmdef 引用服务端程序集：" + root
                        + "（登记表陈旧 → D2 规则静默失效）");
            }
            return problems;
        }

        /// <summary>
        /// **客户端框架侧纯度**（《客户端总设计》§5.1）：`LiteClient.*`（通用核心 + 适配器）整体不得引用
        /// 任何 `LiteGame.*` 程序集——产品面（含 `LuaBridge`）依赖框架，反向一律禁止。
        /// 方向一旦被破坏，框架侧就只能随产品走（§C5 通用包提取被挡）——本判据把它钉成可执行规则。
        /// </summary>
        public static readonly string[] ClientFrameworkPurityAsmdefs =
        {
            "Assets/LiteClient/Abstractions/LiteClient.Abstractions.asmdef",
            "Assets/LiteClient/Runtime/LiteClient.Runtime.asmdef",
            "Assets/LiteClient/Adapters/Content.YooAsset/LiteClient.Content.YooAsset.asmdef",
            "Assets/LiteClient/Adapters/Network.Kcp/LiteClient.Network.Kcp.asmdef",
            "Assets/LiteClient/Adapters/Platform.Unity/LiteClient.Platform.Unity.asmdef",
            "Assets/LiteClient/Adapters/Scripting.XLua/LiteClient.Scripting.XLua.asmdef",
            "Assets/LiteClient/Adapters/Serialization.Luban/LiteClient.Serialization.Luban.asmdef",
        };

        /// <summary>第一方代码根（注释卫生扫描范围；第三方与生成物不在内）。</summary>
        public static readonly string[] CommentHygieneRoots =
        {
            "Assets/LiteClient",
            "Assets/LiteGame",
            "Assets/LiteFramework",
            "Assets/LiteSim",
            "Assets/LiteNet",
            "Assets/LiteTesting",
            "Assets/Tools",
            "Assets/Tests",
            "RoomServer",
            "Tests",
        };

        /// <summary>注释卫生（AGENTS 代码规范 6）：第一方代码注释不得出现施工痕迹（日期戳、批次号——历史进 Docs/施工进度）。
        /// 只查注释部分（`//` 之后）；数据字面量、生成物、第三方目录不在范围。返回违规描述（空 = 通过）。</summary>
        public static List<string> ValidateCommentHygiene(string projectRoot)
        {
            var problems = new List<string>();
            var marker = new System.Text.RegularExpressions.Regex(@"20\d\d-\d\d-\d\d|批[①②③④⑤⑥⑦⑧⑨⑩]");
            string rootSlash = projectRoot.Replace('\\', '/') + "/";
            foreach (string root in CommentHygieneRoots)
            {
                string dir = Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = file.Replace('\\', '/');
                    if (norm.Contains("/obj/") || norm.Contains("/bin/") || norm.Contains("/.dotnet/")
                        || norm.Contains("/Vendor/") || norm.Contains("/XLua/") || norm.Contains("/Plugins/")
                        || norm.Contains("/Generated/") || norm.EndsWith(".g.cs")) continue;
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int c = lines[i].IndexOf("//", StringComparison.Ordinal);
                        if (c < 0) continue;
                        // 数据字面量形态的 KeyId（release-key-YYYY-MM-DD / rel-YYYY-MM-DD）不算施工痕迹
                        string comment = System.Text.RegularExpressions.Regex.Replace(
                            lines[i].Substring(c), @"(release-key|rel)-\d{4}-\d{2}-\d{2}", "<keyId>");
                        if (marker.IsMatch(comment))
                            problems.Add((norm.StartsWith(rootSlash) ? norm.Substring(rootSlash.Length) : norm)
                                + ":" + (i + 1) + " " + comment.Trim());
                    }
                }
            }
            return problems;
        }

        /// <summary>校验客户端框架侧未引用 `LiteGame.*`（见 <see cref="ClientFrameworkPurityAsmdefs"/>）。返回违规描述（空 = 通过）。</summary>
        public static List<string> ValidateClientFrameworkPurity(string projectRoot)
        {
            var problems = new List<string>();
            foreach (string rel in ClientFrameworkPurityAsmdefs)
            {
                string path = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    problems.Add("框架侧 asmdef 不存在：" + rel + "（规则静默失效）");
                    continue;
                }
                string text = File.ReadAllText(path);
                int i = text.IndexOf("\"references\"", StringComparison.Ordinal);
                if (i < 0) continue;
                int open = text.IndexOf('[', i);
                int close = text.IndexOf(']', open);
                if (open < 0 || close < 0) continue;

                string body = text.Substring(open + 1, close - open - 1);
                foreach (string raw in body.Split(','))
                {
                    string dep = raw.Replace("\"", string.Empty).Trim();
                    if (dep.StartsWith("LiteGame", StringComparison.Ordinal))
                        problems.Add("框架侧引用了产品面程序集：" + rel + " -> " + dep);
                }
            }
            return problems;
        }
    }
}
