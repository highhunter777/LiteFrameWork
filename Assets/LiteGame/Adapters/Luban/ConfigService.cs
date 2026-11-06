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
    /// 配置装载服务：候选构建 → 校验 → 原子发布，失败保留旧版；只认 Luban 的消费方取本类。
    /// 未实现：热更安全窗口 / Release 归属 / 对局固定快照（见《商业级通用客户端框架总设计》§9）。
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
        /// 玩法数据身份基摘要（判定面表字节全量，算法单源 CombatConfigDigest.ComputeBase）：与本次装载同批字节算出。
        /// 消费面为 MatchSessionFactory → StartGame.ConfigHash；未装载前返回 0。
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

            // ① 候选字节预取（缺表 fail-fast，报错带 location）；键=裸表名、位置=领域相对路径，均派生自 TableLayout。
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
                        // 报错带 location 与根因——"删掉一个 .bytes → LoadAsync 报错明确"的自测即此路径。
                        throw new InvalidOperationException($"配置文件加载失败:{location}", ex);
                    }
                }
            }

            // ② 候选建表（Luban 同步解析——解析失败抛；此刻未触碰任何已发布状态/全局数值）。
            var candidate = new Tables(file => new ByteBuf(cache[file]));
            // 策略表投影先于发布构建：非法值在"触碰已发布状态"之前 fail-fast。
            BattleStrategyProfile strategy = BattleStrategyProfileFactory.Create(candidate.Tbbattlestrategy);

            // ③ 校验 + 原子发布 + 回填（顺序钉死：发布失败 → Loaded 保持 false、CombatConfig 不动）。
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
        /// 玩法数值装载（表 → CombatValues 并原子发布）：LiteSim 零依赖，由外部喂 primitives。
        /// 默认值须与表一致（L1 守卫）；调用前须已过 <see cref="ValidateCandidate"/>。
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
        /// 移动数值装载（表 → MovementValues 并发布读口）：与 ApplyCombatValues 同纪律，默认值须与表一致。
        /// 调用前须已过 <see cref="ValidateCandidate"/>；机制消费随 Sim 系统落地接入。
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

        /// <summary>
        /// 动画 Profile 装载（表 → AnimationProfileRow 列表并原子发布）；语义校验已在前置步骤完成。
        /// 消费方经 <see cref="AnimationProfileLoader.FromRows"/> 按模型族装配，失败炸在装配期。
        /// </summary>
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
