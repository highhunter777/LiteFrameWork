using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using Luban;
using cfg;

namespace LiteGame
{
    /// <summary>
    /// Luban 专用的配置服务视图（`Tables` 是 **Luban 生成类型** `cfg.Tables`）。
    ///
    /// **只有本来就认识 Luban 的消费方**才取这一面：表投影（`UIFormCatalog`）、
    /// Lua 数据桥（`Bridge.Data`）。流程/装配只依赖 <see cref="IConfigTableSource"/>。
    /// 这样"Luban 是序列化适配器"这件事在类型上就成立，而不是靠约定。
    /// </summary>
    public interface IConfigService : IConfigTableSource
    {
        Tables Tables { get; }                     // Luban 生成物，cfg 命名空间；未加载访问抛
    }

    /// <summary>
    /// 配置加载薄壳（DI 单例，ProcedureLaunch 注册只注册不加载，ProcedurePreload 尾部 LoadAsync 放行）。
    /// C1-⑨ 快照化（《商业级通用客户端框架总设计》§9 + §4 原则 6/10 + 热更专项 §11）：
    /// **候选构建 → 校验 → 原子发布**三段式——
    /// ① 候选字节预取（缺表 fail-fast，报错带完整 location）；
    /// ② 候选建表（Luban 同步解析——解析失败抛，**不触碰任何已发布状态**）；
    /// ③ 校验（tbcombatnum 单行表等全表约束）+ 玩法数值回填 + <see cref="ConfigSnapshotService{TSnapshot}"/>
    ///    原子发布（版本单调）。失败保留旧版/空态——纠正旧实现"先设 _tables 再 ApplyCombatNumbers"
    ///    的半发布缺陷（校验炸了 Loaded 已为 true，见热更专项 §2 Current）。
    ///
    /// 其余契约（设计方案 §5.2"松"纪律）不变：
    /// 本类不感知资源方案——构造收字节委托（装配点绑定 IContentService 租约通道——代次/引用统一）；
    /// 表清单显式登记在 <see cref="TableDataFiles"/>（新增 Luban 表时加一行，将来随 Bridge.data 生成器自动产出）；
    /// 加载失败 fail-fast 抛（损坏/缺失不静默），由流程 Fail() 接——存档损坏不能炸启动，配置缺失必须炸。
    /// 运行态重发布（热更安全窗口）与完整版本身份（GameplayDigest/Release 归属）随热更批；对局固定快照归 G2。
    /// </summary>
    public sealed class ConfigService : IConfigService
    {
        /// <summary>表数据收集目录（YooAsset location 前缀；跨平台恒为 Assets 路径，与物理盘符无关）。</summary>
        public const string DataDir = "Assets/GameData/Config/";

        /// <summary>gen.bat 第一遍产出的表数据文件名（GameData/Config 下，不带扩展名）——与 Tables.cs 的 loader 键一一对应。</summary>
        public static readonly string[] TableDataFiles =
        {
            "demo_tbitem",
            "tbuiform",
            "tbcontententry",
            "tbstrategy",
            "tbcombatnum",      // 玩法数值（单行表；装载后回填 CombatConfig——两端同源，见《玩法数值解耦审查与Luban表设计》）
        };

        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;
        private readonly ConfigSnapshotService<Tables> _snapshots;   // 原子发布（候选校验→替换→版本单调）
        private Tables _tables;

        public ConfigService(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider)
        {
            _bytesProvider = bytesProvider ?? throw new ArgumentNullException(nameof(bytesProvider));
            _snapshots = new ConfigSnapshotService<Tables>(ValidateCandidate);
        }

        public bool Loaded => _tables != null;

        /// <summary>已发布快照版本（每次成功发布 +1；0 = 尚未发布——诊断/后续热更安全窗口用）。</summary>
        public ulong Version => _snapshots.Version;

        /// <summary>当前已发布快照（未发布为 null——与 <see cref="Loaded"/> 同判据）。</summary>
        public Tables CurrentSnapshot => _snapshots.Current;

        public Tables Tables
        {
            get
            {
                if (_tables == null)
                    throw new InvalidOperationException("配置未加载——LoadAsync 完成前禁止查表（ProcedurePreload 尾部放行）");
                return _tables;
            }
        }

        public async UniTask LoadAsync(CancellationToken ct)
        {
            if (_tables != null) return;                   // 幂等：重复 Load 直接返回

            // ① 候选字节预取（缺表 fail-fast，报错带 location）
            var cache = new Dictionary<string, byte[]>(TableDataFiles.Length);
            foreach (string file in TableDataFiles)
            {
                ct.ThrowIfCancellationRequested();
                string location = $"{DataDir}{file}.bytes";
                try
                {
                    cache[file] = await _bytesProvider(location, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 报错带 location 与根因——"删掉一个 .bytes → LoadAsync 报错明确"的自测即此路径
                    throw new InvalidOperationException($"配置文件加载失败:{location}", ex);
                }
            }

            // ② 候选建表（Luban 同步解析——解析失败抛；此刻未触碰任何已发布状态/全局数值）
            var candidate = new Tables(file => new ByteBuf(cache[file]));

            // ③ 校验 + 原子发布 + 回填（顺序钉死：发布失败 → Loaded 保持 false、CombatConfig 不动）
            _snapshots.Publish(candidate);                 // 校验失败抛（保留旧版/空态），版本 +1
            ApplyCombatNumbers(candidate);                 // 校验已过（行存在性由 ValidateCandidate 保证）
            _tables = candidate;                           // 对外可见（发布成功后）
            Log.Info($"配置快照发布完成:{TableDataFiles.Length} 张表 version={Version}", "Config");
        }

        /// <summary>候选校验（发布闸门——返回 null 通过，非 null 为拒绝原因上抛）。</summary>
        private static string ValidateCandidate(Tables candidate)
        {
            if (candidate == null) return "候选表为 null";
            if (candidate.Tbcombatnum == null || candidate.Tbcombatnum.Get(1) == null)
                return "tbcombatnum 缺 id=1 行（单行数值表）——表源被改坏或生成物过期";
            return null;
        }

        /// <summary>
        /// 玩法数值回填（表 → `CombatConfig`）：LiteSim 是零依赖程序集，读表能力只能由外部喂 primitives。
        /// 表值即手感参数唯一真相；**本类的默认值须与表一致**（L1 守卫用例卡漂移）。
        /// 服务端读同一表源的 json 产物（`RoomServer/Data/tbcombatnum.json`）——两端同值，受 buildHash 闭包保护。
        /// 调用契约：<see cref="ValidateCandidate"/> 已通过（行存在性保证——本方法不再兜底判空）。
        /// </summary>
        private static void ApplyCombatNumbers(Tables tables)
        {
            cfg.combatnum row = tables.Tbcombatnum.Get(1);        // 单行表固定 id=1

            CombatConfig.LoadFrom(
                row.MoveSpeed, row.Gravity,
                row.HitscanRange, row.HitscanRadius, row.HitscanHeight,
                row.BaseDamage, row.DamageSpread, row.EntityHp);

            Log.Info(
                $"玩法数值装载：move={row.MoveSpeed} gravity={row.Gravity} " +
                $"hitscan={row.HitscanRange}/{row.HitscanRadius}/{row.HitscanHeight} " +
                $"dmg={row.BaseDamage}±{row.DamageSpread} hp={row.EntityHp}", "Config");
        }
    }
}
