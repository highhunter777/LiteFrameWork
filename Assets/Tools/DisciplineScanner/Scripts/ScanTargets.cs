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

        /// <summary>受守根显式含 /Editor/ 子树（默认 IsExcluded 排除 Editor 目录；投影纪律的
        /// Editor/Gm 受守根需要扫进去——只放行该目标，不改变全局排除面）。</summary>
        public bool IncludeEditorDirs;

        public ScanTarget(string root, LintRule[] rules) : this(root, rules, null, false) { }

        public ScanTarget(string root, LintRule[] rules, string[] excludeRoots) : this(root, rules, excludeRoots, false) { }

        public ScanTarget(string root, LintRule[] rules, string[] excludeRoots, bool includeEditorDirs)
        {
            Root = root;
            Rules = rules;
            ExcludeRoots = excludeRoots;
            IncludeEditorDirs = includeEditorDirs;
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

        /// <summary>业务 Unity 层：R6（原生协程）＋ R15 平台特判收敛（平台差异不散落 `#if`）。</summary>
        public static readonly LintRule[] UnityRules =
        {
            LintRule.R6NativeCoroutine,
            LintRule.R15PlatformFork,
        };

        /// <summary>LiteGame 全域：R8 资源唯一入口 + R12 适配器边界（Editor 目录由 IsExcluded 排除）
        /// + R15 平台特判收敛（允许面见 DisciplineScanner.PlatformForkAllowedFiles）。</summary>
        public static readonly LintRule[] GameRules =
        {
            LintRule.R8ResourcesLoad,
            // R12 适配器边界（《客户端总设计》§5.1"形成逻辑边界和依赖测试"）： YooAsset/xLua/DOTween 的实现只许在各自边界目录内 import—— 阻止新耦合扩散：在一体 asmdef 里，没有它，任何人都能再 import 一次而无人察觉。
            LintRule.R12AdapterBoundary,
            // R15 平台特判收敛：Android 适配的平台差异只走 profile/四个装配收敛点，
            // 别处出现 `#if UNITY_ANDROID` 即报红（否则平台分叉会按文件数蔓延）。
            LintRule.R15PlatformFork,
        };

        /// <summary>UI 层：R10 不得直发业务包（禁 INetworkService 契约）。</summary>
        public static readonly LintRule[] UILayerRules =
        {
            LintRule.R10UiSendsBusinessPacket,
        };

        /// <summary>RoomServer/Core：R11 纯化（《商业级通用服务端框架总设计》§8.1 禁止项——
        /// Transport/墙钟/Console/文件/proto 引用一概不得进入纯运行时层）＋ R13 数值读口禁读
        /// （机制面：内核不读静态读口——值经房间快照参数传递）。</summary>
        public static readonly LintRule[] RuntimePurityRules =
        {
            LintRule.R11RuntimePurity,
            LintRule.R13StaticConfigPort,
        };

        /// <summary>R13 数值读口禁读（机制面）——技术债 #1 防复辟：机制代码不读静态读口
        /// （`CombatConfig` 可变值 / `MovementConfig` / `WeaponConfig.Default|Loaded|Publish`；
        /// 值经 `CombatValues`/`MovementValues`/`WeaponTable` 实例参数传递）。</summary>
        public static readonly LintRule[] ConfigPortRules =
        {
            LintRule.R13StaticConfigPort,
        };

        /// <summary>R14 投影禁直调调试源（《GM 与调试工具分层专项设计》§7"统一内核与三投影"）：
        /// 运行期投影程序集（DevTool）与编辑器 GM 页只经 DebugKernel 门面读数/写动作/发现——
        /// 禁直调底层读口与写口；豁免＝契约类型、纯格式化助手（DiagTrace.Line/Log.TagName）、
        /// Log 门面写口；例外行 lint-allow R14 写明理由（判例：场景页＝带参命令专面板）。</summary>
        public static readonly LintRule[] ProjectionRules =
        {
            LintRule.R14ProjectionDebugPort,
        };

        /// <summary>R13 在 LiteSim 大根上的排除：表现层（View——读口服务表现面，线程化退役随 Unity 环境批）
        /// + 三个读口定义文件（定义面自含 Publish/Loaded 声明，非消费）。</summary>
        public static readonly string[] ConfigPortExcludes =
        {
            "Packages/com.litegame.sim/Scripts/Config/CombatConfig.cs",
            "Packages/com.litegame.sim/Scripts/Config/MovementConfig.cs",
            "Packages/com.litegame.sim/Scripts/Config/WeaponConfig.cs",
        };

        /// <summary>MetaServer：R11 同类把守（《Meta 服务专项设计》§3.2/§4.2）——
        /// Meta 的模块层与契约层不得引用 IO/墙钟/Console，也不得引用 LiteNet/proto/Kcp：
        /// 局外契约不混进战斗协议，且 Meta 类型永不进入 RoomServer/Core。
        /// 宿主装配层（Host/Infrastructure）合法使用 Web/IO，故排除在外。</summary>
        public static readonly LintRule[] MetaPurityRules =
        {
            LintRule.R11RuntimePurity,
        };

        /// <summary>LiteSim 扫描目标下的确定性规则排除面（当前为空——表现层在 <c>Assets/LiteView</c>
        /// 独立目标只守 R6，LiteSim 根纯 Sim；本表供 Sim 根下未来例外登记）。</summary>
        public static readonly string[] SimLayerExcludes =
        {
        };

        /// <summary>MetaServer 的宿主装配层：合法使用 Kestrel/Web/IO/Console（服务端总设计 §12 Generic Host），
        /// 不属于模块或契约层，故不进 R11 扫描。目标根内其余子目录（Contracts/、Modules/、Infrastructure/）
        /// **自动**纳入扫描，无需再改本文件。</summary>
        public static readonly string[] MetaHostExcludes =
        {
            "Server/MetaServer/Host",
        };

        /// <summary>
        /// `Assets/LiteGame/` 下**允许存在的顶层目录**（《客户端总设计》§5 顶层分层）。
        /// </summary>
        public static readonly string[] LiteGameTopLevelDirs = ScanTargetsGenerated.LiteGameTopLevelDirs;

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

        /// <summary>
        /// `Assets/` 下**允许存在的顶层目录**（组织级登记；口径＝《客户端架构重构演进路线图》§5.1）。
        /// </summary>
        public static readonly string[] AssetsTopLevelDirs = ScanTargetsGenerated.AssetsTopLevelDirs;

        /// <summary>
        /// 不参与 `Assets/` 顶层登记的**非版本化/瞬时面**：Unity 或工具自建、内容不入库
        /// （`.gitignore` 已有对应规则）。它们不是"逃出设计"，但也不构成合法结构位置。
        /// </summary>
        public static readonly string[] AssetsTopLevelIgnored = ScanTargetsGenerated.AssetsTopLevelIgnored;

        /// <summary>
        /// 校验 `Assets/` 顶层目录全部已登记（未登记 = 逃出组织分层，见上）。
        /// 返回未登记目录名（无则空）。
        /// </summary>
        public static List<string> ValidateAssetsTopLevel(string projectRoot)
        {
            var unregistered = new List<string>();
            string root = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(root)) return unregistered;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (Array.IndexOf(AssetsTopLevelIgnored, name) >= 0) continue;
                if (Array.IndexOf(AssetsTopLevelDirs, name) < 0) unregistered.Add(name);
            }
            unregistered.Sort(StringComparer.Ordinal);
            return unregistered;
        }

        /// <summary>
        /// 默认扫描目标集合（由 arch/units.json 的 scanTargets 投影；规则集仍在本文件）。
        /// </summary>
        public static readonly ScanTarget[] Default = BuildDefault();

        private static ScanTarget[] BuildDefault()
        {
            var result = new ScanTarget[ScanTargetsGenerated.Default.Length];
            for (int i = 0; i < result.Length; i++)
            {
                ScanTargetsGenerated.Entry entry = ScanTargetsGenerated.Default[i];
                result[i] = new ScanTarget(entry.Root, ResolveRules(entry.Rules), ResolveExcludes(entry.Exclude), entry.IncludeEditor);
            }
            return result;
        }

        private static LintRule[] ResolveRules(string name)
        {
            switch (name)
            {
                case "SimRules": return SimRules;
                case "NetRules": return NetRules;
                case "CoreRules": return CoreRules;
                case "UnityRules": return UnityRules;
                case "GameRules": return GameRules;
                case "UILayerRules": return UILayerRules;
                case "RuntimePurityRules": return RuntimePurityRules;
                case "ConfigPortRules": return ConfigPortRules;
                case "MetaPurityRules": return MetaPurityRules;
                case "ProjectionRules": return ProjectionRules;
                default: throw new ArgumentException("未登记的扫描规则集：" + name);
            }
        }

        private static string[] ResolveExcludes(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name)
            {
                case "SimLayerExcludes": return SimLayerExcludes;
                case "ConfigPortExcludes": return ConfigPortExcludes;
                case "MetaHostExcludes": return MetaHostExcludes;
                default: throw new ArgumentException("未登记的扫描排除集：" + name);
            }
        }

        /// <summary>
        /// .meta 扫描根（R7 非法 GUID：64 位 base64 guid 会被 Unity 拒收）。
        /// </summary>
        public static readonly string[] MetaRoots =
        {
            "Assets",
            "Packages",
        };

        /// <summary>
        /// **客户端对服务端程序集的引用登记表**（《客户端与服务端共享代码范围专项设计》§3 规则 D2）。
        /// </summary>
        public static readonly string[] DevHostMayReferenceServerAssemblies =
        {
            // 唯一合法档位：开发装配层（离线隔离开发的本地服 + 本地传输工厂 + 投影件）。
            "Assets/LiteGame/Adapters/Dev",
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
            "Server/RoomServer/RoomServer.csproj",
            "Server/MetaServer/MetaServer.csproj",
        };

        /// <summary>服务端宿主工程不得引用的客户端面程序集关键字（出现在 ProjectReference 即违规）。
        /// 战斗域用**程序集名**作标记（目录无关——程序集 `LiteGame.Modules.Battle`）。</summary>
        public static readonly string[] ClientSideProjectMarkers =
        {
            "LiteFramework",
            "LiteGame",
            "LiteGame.Modules.Battle",
        };

        /// <summary>
        /// **共享档位表 = 代码**（《共享代码范围专项设计》§2.1 的 S0–S4，§8.1 守卫 G5）。
        /// </summary>
        public static readonly string[] SharedAssemblyDefs =
        {
            // S1 机制共享：两端都要跑这套机制，但结果不需逐位一致
            "Assets/RoomServer/Core/RoomServer.Runtime.asmdef",
            "Assets/RoomServer/Modules/RoomServer.Application.Runtime.asmdef",
            "Packages/com.litegame.net/LiteNet.asmdef",
        };

        /// <summary>
        /// **S2 档位的额外义务②：零第三方实现依赖**（§2.2）。
        /// </summary>
        public static readonly (string Asmdef, string[] AllowedThirdParty)[] ContractZeroDepDefs =
        {
            // 框架并根后 Core 无独立 asmdef；S2 义务改由目录级用例
            // ClientAsmdefAllowlistTests.框架Core与SimCore不得依赖Unity 守。
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
        /// **客户端框架侧纯度**（《客户端总设计》§5.1）：`LiteFramework.*`（通用核心 + 适配器）整体不得引用 任何 `LiteGame.*` 程序集——产品面（含 `LuaBridge`）依赖框架。
        /// </summary>
        public static readonly string[] ClientFrameworkPurityAsmdefs =
        {
            "Packages/com.litegame.framework/Runtime/Services/LiteFramework.Services.asmdef",
            "Packages/com.litegame.framework/Runtime/Adapters/LiteFramework.Adapters.asmdef",
            "Packages/com.litegame.framework/Runtime/Hosting/LiteFramework.Hosting.asmdef",
        };

        /// <summary>第一方代码根（注释卫生扫描范围；第三方与生成物不在内）。</summary>
        public static readonly string[] CommentHygieneRoots =
        {
            "Packages/com.litegame.framework/Runtime/Services", "Packages/com.litegame.framework/Runtime/Hosting",
            "Assets/LiteGame",
            "Packages/com.litegame.framework",
            "Packages/com.litegame.sim",
            "Packages/com.litegame.net",
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
