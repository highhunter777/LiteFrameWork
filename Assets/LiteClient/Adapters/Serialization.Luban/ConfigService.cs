using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using Luban;
using cfg;

namespace LiteClient
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
    /// 快照化（《商业级通用客户端框架总设计》§9 + §4 原则 6/10 + 热更专项 §11）：
    /// **候选构建 → 校验 → 原子发布**三段式——
    /// ① 候选字节预取（缺表 fail-fast，报错带完整 location）；
    /// ② 候选建表（Luban 同步解析——解析失败抛，**不触碰任何已发布状态**）；
    /// ③ 校验（tbcombatnum 单行表等全表约束）+ 玩法数值回填 + <see cref="ConfigSnapshotService{TSnapshot}"/>
    ///    原子发布（版本单调）。失败保留旧版/空态——校验失败时 <see cref="Loaded"/> 保持 false。
    ///
    /// 其余契约（设计方案 §5.2"松"纪律）：
    /// 本类不感知资源方案——构造收字节委托（装配点绑定 IContentService 租约通道——代次/引用统一）；
    /// 表清单显式登记在 <see cref="TableDataFiles"/>（新增 Luban 表时加一行）；
    /// 加载失败 fail-fast 抛（损坏/缺失不静默），由流程 Fail() 接——存档损坏不能炸启动，配置缺失必须炸。
    /// 运行态重发布（热更安全窗口）、完整版本身份（GameplayDigest/Release 归属）与对局固定快照均未实现。
    /// </summary>
    public sealed class ConfigService : IConfigService
    {
        /// <summary>表数据收集目录（YooAsset location 前缀；跨平台恒为 Assets 路径，与物理盘符无关）。</summary>
        public const string DataDir = "Assets/GameData/Config/";

        /// <summary>gen.bat 第一遍产出的表数据文件名（GameData/Config 下，不带扩展名）——与 Tables.cs 的 loader 键一一对应。</summary>
        public static readonly string[] TableDataFiles =
        {
            "tbitemconfig",      // 道具表（类型 + 刷新/拾取/携带/使用 + 各类型效果数值）
            "tbmovementconfig",  // 移动数值（单行表；装载后产出 MovementValues 实例并发布读口——机制消费随系统落地接入）
            "tbuiform",
            "tbcontententry",
            "tbstrategy",
            "tbcombatnum",      // 玩法数值（单行表；装载后产出 CombatValues 实例并发布读口——两端同源，见《玩法数值与Luban配置专项设计》）
            "tbweapon",          // 武器表（多行；装载后回填 WeaponConfig——Sim 武器系统消费）
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

        /// <summary>已发布快照版本（每次成功发布 +1；0 = 尚未发布——诊断用）。</summary>
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
            ApplyMovementNumbers(candidate);               // 移动数值（同上：一致性闸门在 ValidateCandidate）
            ApplyWeaponTable(candidate);                   // 武器表（多行回填——WeaponSystem 消费）
            _tables = candidate;                           // 对外可见（发布成功后）
            Log.Info($"配置快照发布完成:{TableDataFiles.Length} 张表 version={Version}", "Config");
        }

        /// <summary>候选校验（发布闸门——返回 null 通过，非 null 为拒绝原因上抛）。</summary>
        private static string ValidateCandidate(Tables candidate)
        {
            if (candidate == null) return "候选表为 null";
            if (candidate.Tbcombatnum == null || candidate.Tbcombatnum.Get(1) == null)
                return "tbcombatnum 缺 id=1 行（单行数值表）——表源被改坏或生成物过期";
            if (candidate.Tbmovementconfig == null || candidate.Tbmovementconfig.Get(1) == null)
                return "tbmovementconfig 缺 id=1 行（单行数值表）——表源被改坏或生成物过期";
            if (candidate.Tbitemconfig == null || candidate.Tbitemconfig.DataList.Count == 0)
                return "tbitemconfig 空表——表源被改坏或生成物过期";
            if (candidate.Tbweapon == null || candidate.Tbweapon.GetOrDefault(WeaponConfig.DefaultRifleId) == null)
                return "tbweapon 缺默认步枪行（id=0）——武器系统懒装备依赖它";
            if (candidate.Tbmovementconfig.Get(1).Gravity != candidate.Tbcombatnum.Get(1).Gravity)
                return "movementconfig.gravity 与 combatnum.gravity 不一致（重力双表位漂移——单源在 combatnum，消费读装载实例 CombatValues.Gravity）";
            return null;
        }

        /// <summary>
        /// 玩法数值装载（表 → <see cref="CombatValues"/> 实例并**原子发布读口**）：LiteSim 是零依赖程序集，
        /// 读表能力只能由外部喂 primitives。表值即手感参数唯一真相；**本类的默认值须与表一致**（L1 守卫用例卡漂移）。
        /// 服务端直读同一份 .bytes（`RoomServer/CombatNumbers`）——两端同值，受 buildHash 闭包保护。
        /// 机制消费经参数传递（技术债 #1）；<see cref="CombatConfig.Publish"/> 只服务单世界表现面便捷读。
        /// 调用契约：<see cref="ValidateCandidate"/> 已通过（行存在性保证——本方法不再兜底判空）。
        /// </summary>
        private static void ApplyCombatNumbers(Tables tables)
        {
            cfg.combatnum row = tables.Tbcombatnum.Get(1);        // 单行表固定 id=1

            CombatConfig.Publish(new CombatValues(
                row.MoveSpeed, row.Gravity, row.HitscanRange,
                row.BaseDamage, row.DamageSpread, row.EntityHp));

            Log.Info(
                $"玩法数值装载：move={row.MoveSpeed} gravity={row.Gravity} " +
                $"hitscan={row.HitscanRange} hit={CombatConfig.HitscanRadius}(裁决) body={CombatConfig.BodyRadius}:{CombatConfig.HitscanHeight}(烘焙) " +
                $"dmg={row.BaseDamage}±{row.DamageSpread} hp={row.EntityHp}", "Config");
        }

        /// <summary>
        /// 移动数值装载（表 → <see cref="MovementValues"/> 实例并发布读口）：与 ApplyCombatNumbers 同纪律——
        /// LiteSim 零依赖，由外部喂 primitives；表值即设计软值，硬护栏是代码常量 <see cref="CombatConfig.HardMaxSpeed"/>。
        /// 机制消费（走跑冲/滑铲/空中控制/跳跃/钩爪/闪现）随对应 Sim 系统落地逐项接入（接入时经参数传实例）。
        /// 调用契约：<see cref="ValidateCandidate"/> 已通过（行存在性 + 重力双表位一致性均闸在前）。
        /// </summary>
        private static void ApplyMovementNumbers(Tables tables)
        {
            cfg.movementconfig row = tables.Tbmovementconfig.Get(1);        // 单行表固定 id=1

            MovementConfig.Publish(new MovementValues(
                row.WalkSpeed, row.RunSpeed, row.SprintSpeed, row.Acceleration, row.SprintDuration,
                row.SlideSpeed, row.SlideFriction, row.SlideTurnPenalty,
                row.AirControl, row.Gravity,
                row.JumpSpeed, row.DoubleJumpCount, row.DoubleJumpSpeed,
                row.GrappleDistance, row.GrappleSpeed, row.GrappleCooldown,
                row.BlinkDistance, row.BlinkCooldown));

            Log.Info(
                $"移动数值装载：walk={row.WalkSpeed} run={row.RunSpeed} sprint={row.SprintSpeed} " +
                $"slide={row.SlideSpeed}/{row.SlideFriction} jump={row.JumpSpeed}×{row.DoubleJumpCount + 1} " +
                $"grapple={row.GrappleDistance}/{row.GrappleSpeed} blink={row.BlinkDistance}", "Config");
        }

        /// <summary>武器表装载（表 → <see cref="WeaponTable"/> 实例并**原子发布读口**）：多行逐条 primitive 喂入；
        /// 击发模式字符串在装载边界翻译成 bool（Sim 不认字符串语义）。机制经参数接收实例（技术债 #1 家族）。</summary>
        private static void ApplyWeaponTable(Tables tables)
        {
            var table = new WeaponTable();
            int count = 0;
            foreach (cfg.weapon row in tables.Tbweapon.DataList)
            {
                bool automatic = row.FireMode == "auto";
                if (table.SetRow(row.Id, row.Damage, row.Rpm, row.MagazineSize, row.ReserveAmmo,
                        row.ReloadFrames, row.Range, row.Spread, row.Pellets, row.SwitchFrames, automatic))
                    count++;
                else
                    Log.Warning($"武器表行被忽略（id 越界）：id={row.Id} {row.Name}", "Config");
            }
            WeaponConfig.Publish(table);   // 原子发布（客户端单世界读口；机制经参数接收——R13 纪律把守）

            if (table.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef rifle))
                Log.Info($"武器表装载：{count} 行（默认步枪 dmg={rifle.Damage} rpm={rifle.Rpm} " +
                    $"mag={rifle.MagazineSize} 节拍={rifle.FireIntervalFrames}帧）", "Config");
        }
    }
}
