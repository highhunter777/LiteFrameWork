using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using Luban;
using cfg;
using LiteGame.Modules.Config;

namespace LiteGame
{
    /// <summary>
    /// Luban 专用的配置服务视图（`Tables` 是 **Luban 生成类型** `cfg.Tables`）。
    ///
    /// **只有本来就认识 Luban 的消费方**才取这一面：表投影（`LTextCatalog` 等）、
    /// Lua 数据桥（`Bridge.Data`）。流程/装配只依赖 <see cref="IConfigTableSource"/>。
    /// 这样"Luban 是序列化适配器"这件事在类型上就成立，而不是靠约定。
    /// </summary>
    /// <summary>
    /// 配置装载服务（DI 单例，ProcedureLaunch 注册只注册不加载，ProcedurePreload 尾部 LoadAsync 放行）。
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
    /// 运行态重发布（热更安全窗口）、Release 归属与对局固定快照均未实现；
    /// **玩法数据身份摘要已产出**（<see cref="GameplayDigestBase"/>——批 E：判定面表字节全量，两端复算比对）。
    /// </summary>
    public sealed class ConfigService : IConfigService
    {
        /// <summary>表数据收集目录（YooAsset location 前缀；跨平台恒为 Assets 路径，与物理盘符无关）。
        /// 单源在 <see cref="TableLayout"/>（领域分类布局，服务端源链接共编）。</summary>
        public static readonly string DataDir = TableLayout.DataDir;

        /// <summary>gen.bat 产出的表数据文件清单（**领域相对路径**：领域子目录/表名，不带扩展名）
        /// ——单源在 <see cref="TableLayout.Files"/>，gen_luban.py 据此分域归位 bin 产物。</summary>
        public static readonly string[] TableDataFiles = TableLayout.Files;

        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;
        private readonly ICandidateConfigSource _candidateConfig;

        /// <summary>判定参数覆写合并委托（**装载期合并口**，可空）：门（<see cref="SimParamsGate"/>）为真时是
        /// 覆写实例的合并方法，否则 null。产品装配不传（传 null）——覆写合并已下移到进房时点
        /// （<see cref="SimParamsActivation"/>：客户端发布面与本地服房间数值须由同一结果派生，
        /// 而装载期早于"进入本地测试房"）；本类不 new 覆写对象、不读任何静态。</summary>
        private readonly Func<CombatValues, CombatValues> _combatOverrides;

        private readonly ConfigSnapshotService<Tables> _snapshots;   // 原子发布（候选校验→替换→版本单调）
        private Tables _tables;

        /// <summary>策略表投影（装载成功后同刻绑定；未装载访问抛——与 <see cref="Tables"/> 同判据）。</summary>
        private BattleStrategyProfile _strategy;

        /// <summary>表装载的判定数值（**不含覆写**；<see cref="TableCombatValues"/> 的后备）。
        /// 在 <see cref="ApplyCombatValues"/> 里于合并之前保存——每局激活点从这里出发。</summary>
        private CombatValues _tableCombatValues;

        /// <param name="combatOverrides">判定参数覆写合并（可空；null = 不合并，数值即表值）。</param>
        public ConfigService(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider,
            Func<CombatValues, CombatValues> combatOverrides = null,
            ICandidateConfigSource candidateConfig = null)
        {
            _bytesProvider = bytesProvider ?? throw new ArgumentNullException(nameof(bytesProvider));
            _combatOverrides = combatOverrides;
            _candidateConfig = candidateConfig;
            _snapshots = new ConfigSnapshotService<Tables>(ConfigTableValidator.Validate);
        }

        public bool Loaded => _tables != null;

        /// <summary>已发布快照版本（每次成功发布 +1；0 = 尚未发布——诊断用）。</summary>
        public ulong Version => _snapshots.Version;

        /// <summary>当前已发布快照（未发布为 null——与 <see cref="Loaded"/> 同判据）。</summary>
        public Tables CurrentSnapshot => _snapshots.Current;

        /// <summary>
        /// 玩法数据身份**基摘要**（判定面表字节全量；算法/清单单源 = <c>CombatConfigDigest.ComputeBase</c>
        /// + <see cref="TableLayout.JudgmentFiles"/>）——与本次装载**同一批字节**同刻算出（装载热更/回退即变）。
        /// 消费面（经装配注入，不作静态读口）：`MatchSessionFactory` → `BattleContext` 的
        /// `StartGame.ConfigHash` 校验点；本地服形态 `LocalServerTransport`（同进程房间身份）。
        /// 未装载前访问返回 0——装配序上消费点（Match/Battle）恒在 `LoadAsync` 之后（Preload 尾部放行）。
        /// </summary>
        public uint GameplayDigestBase { get; private set; }

        public Tables Tables
        {
            get
            {
                if (_tables == null)
                    throw new InvalidOperationException("配置未加载——LoadAsync 完成前禁止查表（ProcedurePreload 尾部放行）");
                return _tables;
            }
        }

        /// <summary>B3 战斗策略投影（规则/排行/伤害数字参数）。未装载即访问抛——与 <see cref="Tables"/> 同判据。</summary>
        public BattleStrategyProfile Strategy
        {
            get
            {
                if (_tables == null)
                    throw new InvalidOperationException(
                        "配置未加载——Strategy 在 LoadAsync 完成前不可读（对局/本地测试房恒在 Preload 之后）");
                return _strategy;
            }
        }

        /// <summary>表装载的判定数值（合并前保存；不含覆写）。未装载即访问抛——与 <see cref="Tables"/> 同判据。</summary>
        public CombatValues TableCombatValues
        {
            get
            {
                if (_tables == null)
                    throw new InvalidOperationException(
                        "配置未加载——TableCombatValues 在 LoadAsync 完成前不可读（进房恒在 Preload 之后）");
                return _tableCombatValues;
            }
        }

        public async UniTask LoadAsync(CancellationToken ct)
        {
            if (_tables != null) return;                   // 幂等：重复 Load 直接返回

            // ① 候选字节预取（缺表 fail-fast，报错带 location）
            // **键 = 裸表名（Luban 生成物 cfg.Tables 按裸名回调 loader）；位置 = 领域相对路径**
            // ——两者都从 TableLayout 单源派生（与 RoomServer.ServerTableLoader 同范式）。
            var cache = new Dictionary<string, byte[]>(TableDataFiles.Length);
            bool candidateSource = _candidateConfig is { HasConfigSegment: true };
            if (candidateSource)
            {
                if (!_candidateConfig.TryReadAll(out var candidateTables, out string problem))
                    throw new InvalidOperationException($"候选配置加载失败：{problem}");

                foreach (string file in TableDataFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = TableLayout.TableName(file);
                    if (!candidateTables.TryGetValue(name, out byte[] bytes) || bytes == null || bytes.Length == 0)
                        throw new InvalidOperationException($"候选配置缺表：{TableLayout.TableDataPath(file)}");
                    cache[name] = bytes;
                }
            }
            else
            {
                foreach (string file in TableDataFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    string location = TableLayout.TableDataPath(file);
                    try
                    {
                        cache[TableLayout.TableName(file)] = await _bytesProvider(location, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 报错带 location 与根因——"删掉一个 .bytes → LoadAsync 报错明确"的自测即此路径
                        throw new InvalidOperationException($"配置文件加载失败:{location}", ex);
                    }
                }
            }

            // ② 候选建表（Luban 同步解析——解析失败抛；此刻未触碰任何已发布状态/全局数值）
            var candidate = new Tables(file => new ByteBuf(cache[file]));
            // 策略表投影先于发布构建：非法值在"触碰已发布状态"之前 fail-fast。
            BattleStrategyProfile strategy = BattleStrategyProfileFactory.Create(candidate.Tbbattlestrategy);

            // ③ 校验 + 原子发布 + 回填（顺序钉死：发布失败 → Loaded 保持 false、CombatConfig 不动）
            _snapshots.Publish(candidate);                 // 校验失败抛（保留旧版/空态），版本 +1
            GameplayDigestBase = TableLayout.ComputeGameplayDigestBase(cache);   // 数据身份基摘要（与本次装载同一批字节；发布后同刻绑定）
            ApplyCombatValues(candidate);                 // 校验已过（行存在性由 ValidateCandidate 保证）
            ApplyMovementValues(candidate);               // 移动数值（同上：一致性闸门在 ValidateCandidate）
            ApplyWeaponTable(candidate);                   // 武器表（多行回填——WeaponSystem 消费）
            ApplyAnimationProfile(candidate);               // 动画 Profile 行（投影为 Core 行并发布读口——对局装配/构建器/测试消费）
            _strategy = strategy;                          // 策略投影与表快照同刻绑定
            _tables = candidate;                           // 对外可见（发布成功后）
            Log.Info($"配置快照发布完成:{TableDataFiles.Length} 张表 version={Version} " +
                     $"digest={GameplayDigestBase:x8}（判定面 {TableLayout.JudgmentFiles.Length} 表）" +
                     $" source={(candidateSource ? "candidate-config" : "bundle")}", LogTag.Config);
            Log.Info($"战斗策略装载：规则 {strategy.Rules.DurationFrames}/{strategy.Rules.KillLimit}/" +
                     $"{strategy.Rules.RespawnDelayFrames}/{strategy.Rules.SpawnProtectionFrames} " +
                     $"rank={strategy.Rank.ToWireValue()} prefix={strategy.ResultNamePrefix}", LogTag.Config);
        }

        /// <summary>
        /// 玩法数值装载（表 → <see cref="CombatValues"/> 实例并**原子发布读口**）：LiteSim 是零依赖程序集，
        /// 读表能力只能由外部喂 primitives。表值即手感参数唯一真相；**本类的默认值须与表一致**（L1 守卫用例卡漂移）。
        /// 服务端直读同一份 .bytes（`RoomServer/ServerTableLoader`）——两端同值，受 buildHash 闭包保护。
        /// 机制消费经参数传递（技术债 #1）；<see cref="CombatConfig.Publish"/> 只服务单世界表现面便捷读。
        /// 调用契约：<see cref="ValidateCandidate"/> 已通过（行存在性保证——本方法不再兜底判空）。
        /// </summary>
        private void ApplyCombatValues(Tables tables)
        {
            CombatValues tableValues = SimConfigMapper.BuildCombatValues(tables.Tbcombatnum,
                tables.Tbmovementconfig, tables.Tbentityconfig, tables.Tbcombatgeom);
            _tableCombatValues = tableValues;              // 覆写合并的源值（不含覆写——进房单点从这里出发）

            // 装载期合并口：委托由构造参数给入（产品装配为 null）；无覆写时结果与表值逐字段相同。
            CombatValues values = tableValues;
            if (_combatOverrides != null)
            {
                CombatValues merged = _combatOverrides(tableValues);
                if (!SameGeometry(merged.Geometry, tableValues.Geometry))
                    Log.Info("判定参数覆写已合并（装载期合并口；落表走 SimParamsExporter）", LogTag.Config);
                values = merged;
            }

            CombatConfig.Publish(values);

            Log.Info(
                $"玩法数值装载：move={values.MoveSpeed} gravity={values.Gravity} " +
                $"hitscan={values.HitscanRange} hit={values.Geometry.HitscanRadius}(表) body={values.Geometry.BodyRadius}:{values.Geometry.HitscanHeight}(表) " +
                $"dmg={values.BaseDamage}±{values.DamageSpread} hp={values.EntityHp}", LogTag.Config);
        }

        /// <summary>判定几何逐字段相等（只用于"覆写是否真的改了值"的诊断日志——值语义逐列比较即可）。</summary>
        private static bool SameGeometry(in CombatGeometry a, in CombatGeometry b)
            => a.BodyRadius == b.BodyRadius
               && a.HitscanHeight == b.HitscanHeight
               && a.HitscanRadius == b.HitscanRadius
               && a.HeadRatio == b.HeadRatio
               && a.HeadRadius == b.HeadRadius
               && a.MuzzleForward == b.MuzzleForward
               && a.MuzzleRight == b.MuzzleRight
               && a.MuzzleHeight == b.MuzzleHeight
               && a.HeadshotDamageShift == b.HeadshotDamageShift
               && a.HipSpreadFactor == b.HipSpreadFactor
               && a.CorpseFrames == b.CorpseFrames
               && a.HardMaxSpeed == b.HardMaxSpeed
               && a.FaceTurnRadPerSec == b.FaceTurnRadPerSec;

        /// <summary>
        /// 移动数值装载（表 → <see cref="MovementValues"/> 实例并发布读口）：与 ApplyCombatValues 同纪律——
        /// LiteSim 零依赖，由外部喂 primitives；表值即设计软值，硬护栏是表列 <see cref="CombatGeometry.HardMaxSpeed"/>。
        /// 机制消费（走跑冲/滑铲/空中控制/跳跃/钩爪/闪现）随对应 Sim 系统落地逐项接入（接入时经参数传实例）。
        /// 调用契约：<see cref="ValidateCandidate"/> 已通过（行存在性与重力合法性均闸在前）。
        /// </summary>
        private static void ApplyMovementValues(Tables tables)
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
                $"grapple={row.GrappleDistance}/{row.GrappleSpeed} blink={row.BlinkDistance}", LogTag.Config);
        }

        /// <summary>武器表装载（表 → <see cref="WeaponTable"/> 实例并**原子发布读口**）：多行逐条 primitive 喂入；
        /// 击发模式字符串在装载边界翻译成 bool（Sim 不认字符串语义），槽位默认映射（<c>slot</c> 列 → id）同窗注册
        /// （切枪装备/懒装备的"槽位 → 定义"单源）。机制经参数接收实例（技术债 #1 家族）。
        /// 调用契约：<see cref="ValidateCandidate"/> 已过（fire_mode/slot 合法性闸在前——此处不再兜底）。</summary>
        private static void ApplyWeaponTable(Tables tables)
        {
            var table = new WeaponTable();
            int count = 0;
            foreach (cfg.weapon row in tables.Tbweapon.DataList)
            {
                bool automatic = row.FireMode == WeaponConfig.FireModeAuto;
                if (table.SetRow(row.Id, row.Damage, row.Rpm, row.MagazineSize, row.ReserveAmmo, row.MaxReserveAmmo,
                        row.ReloadFrames, row.Range, row.Spread, row.Pellets, row.SwitchFrames, automatic))
                {
                    table.SetSlotDefault(row.Slot, row.Id);
                    count++;
                }
                else
                    Log.Warning($"武器表行被忽略（id 越界）：id={row.Id} {row.Name}", LogTag.Config);
            }
            WeaponConfig.Publish(table);   // 原子发布（客户端单世界读口；机制经参数接收——R13 纪律把守）

            if (table.TryGet(WeaponConfig.DefaultRifleId, out WeaponDef rifle))
                Log.Info($"武器表装载：{count} 行（默认步枪 dmg={rifle.Damage} rpm={rifle.Rpm} " +
                    $"mag={rifle.MagazineSize} 节拍={rifle.FireIntervalFrames}帧）", LogTag.Config);
        }

        /// <summary>动画 Profile 行装载（表 → Core <see cref="AnimationProfileRow"/> 列表并**原子发布读口**）：
        /// 生成行的字符串语义已在翻译边界转枚举、语义校验已在 <see cref="ValidateCandidate"/> 全族演练过——
        /// 此处只投影与发布（消费方：对局装配/构建器/测试——经
        /// <see cref="AnimationProfileLoader.FromRows"/> 按模型族装配，失败炸在装配期）。
        /// 调用契约：<see cref="ValidateCandidate"/> 已过（行存在性与语义合法性闸在前）。</summary>
        private static void ApplyAnimationProfile(Tables tables)
        {
            var rows = AnimationProfileTableMapper.ToRows(tables.Tbanimationprofile);
            AnimationProfileConfig.Publish(rows);

            var families = new HashSet<string>();
            foreach (var row in rows) families.Add(row.ModelFamily);
            Log.Info($"动画 Profile 行装载：{rows.Count} 行/{families.Count} 族（读口 AnimationProfileConfig.Rows）", LogTag.Config);
        }
    }
}
