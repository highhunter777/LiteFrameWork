using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using LiteView;
using LiteView.Animation;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局视图装配件（<see cref="ProcedureBattle"/> 的视图域，一处收口全部表现件接线）：
    /// 自有 `BattleView` 子作用域 → 资源租约（角色 prefab / 激光束 / 飘字底件与四档材质 / 片段清单 /
    /// 四组音效 clip）→ <see cref="SimView"/> → 移动动画驱动 / 准心 / HUD / 瞄准激光 / 命中分发 /
    /// 伤害数字 / 枪口火光与命中特效 / 对局音效 /〔编辑器〕爆头诊断与自动验证件。
    ///
    /// **三面**：<see cref="LoadAsync"/>（取件与降级）、<see cref="Attach"/>（建视图与接线）、
    /// <see cref="Tick"/>（每帧驱动视图消费者）、<see cref="Dispose"/>（逆序拆：消费者先于生产者）。
    /// 流程层只保留"取会话 → 等信令 → 每帧驱动 → 收尾"的编排。
    ///
    /// **所有权**：视图根 GameObject、全部驱动实例与全部租约归本件；<see cref="SimView"/> 与实体 prefab
    /// 的租约由本件持有——视图是上下文的消费者，**先拆视图再拆上下文**（反向释放会读到已拆的 Sim）。
    /// 服务本体（内容/输入/相机/VFX/音频/命令）归装配根，本件只持引用。
    ///
    /// **降级口径**：缺资源/缺服务一律"警告 + 显式降级"（缺角色 prefab → 程序化灰盒；缺 HUD/激光/飘字/
    /// 音效 → 该项静默降级），不把表现增益当成对局失败。
    /// </summary>
    internal sealed class BattleViewRig
    {
        /// <summary>瞄准滞回的最小保持宽度——与 Brain DefaultBlend（EaseInOut 0.3s）对齐：
        /// 短按也走完一次完整混合，连点不重启混合（相机抖动的输入侧治理）。</summary>
        private const float AimMinHoldSeconds = 0.3f;

        /// <summary>玩家实体 prefab（内容包路径；资源缺失时 <see cref="AssetService"/> 侧**回退程序化灰盒**）。</summary>
        private const string EntityPrefab = SimView.DefaultEntityPrefab;

        /// <summary>瞄准激光束 prefab（内容包路径：Assets/FX 归置副本 fx_lazer_sight——KriptoFX Lazer 单源）。</summary>
        private const string LaserSightPrefab = "Assets/ShareResource/FX/fx_lazer_sight.prefab";

        /// <summary>伤害数字底件 prefab（世界空间 TMP 飘字；实例由驱动按预算逐条实例化）。</summary>
        private const string DamageNumberPrefab = "Assets/ShareResource/FX/fx_damage_number.prefab";

        /// <summary>伤害数字逐档材质（黑描边+软影+按档光晕；面部颜色归顶点渐变，不进材质）。
        /// **四档路径与 builder（AgentScripts/BuildDamageNumberAssets）单源同串**——任缺整组不注入。</summary>
        private const string DamageNumberMatCaused = "Assets/ShareResource/FX/fx_damage_number_caused.mat";
        private const string DamageNumberMatCausedCrit = "Assets/ShareResource/FX/fx_damage_number_caused_crit.mat";
        private const string DamageNumberMatReceived = "Assets/ShareResource/FX/fx_damage_number_received.mat";
        private const string DamageNumberMatReceivedCrit = "Assets/ShareResource/FX/fx_damage_number_received_crit.mat";

        /// <summary>角色片段清单（去 AC 主路径的片源：键→Clip 显式引用的纯配置资产）。
        /// 路径单源在动画 Profile——角色族配置归一处。</summary>
        private const string ClipManifestPath = CombatGirlsAnimationProfile.ClipManifestPath;

        private readonly IContentService _content;
        private readonly IVFXService _vfx;
        private readonly AudioService _audio;        // 声音壳（装配根注册；对局音效的组+代理后端）
        private readonly CommandCenter _commands;    // 命令中心（传送命令化：撤销/重做/排队派发）
        private readonly IInputService _input;       // 输入服务（设备源注入/清派发状态）
        private readonly ICameraService _camera;     // 相机服务（Cinemachine 适配器；装配根建、跨对局复用）
        private readonly LiteFramework.IWorldClock _worldClock;   // 世界钟（伤害数字寿命/合并窗）
        private readonly AimHoldGate _aimGate = new AimHoldGate(AimMinHoldSeconds);   // 瞄准态滞回

        private readonly ClientScope _viewScope;     // 视图资源（prefab 租约/视图根）自有作用域
        private BattleContext _context;
        private SimView _view;
        private CharacterLocomotionDriver _locomotion;   // 移动动画驱动（视图的消费者——先于视图拆除）
        private BattleCrosshairDriver _crosshair;       // 对局准心（Hud_Battle prefab）
        private BattleHudDriver _hud;                   // HUD 面板（血条/弹药——技能栏静态占位）
        private BattleRankboardDriver _rankboard;       // 局内排行榜（FFA 比分＝击杀——镜像公共面）
        private UnityEngine.Events.UnityAction _menuClick;   // 菜单钮监听（装配期加、拆除期摘——存实例供移除）
        private BattleLaserDriver _laser;               // 瞄准激光（准心的世界空间兄弟件）
        private GameObject _laserBeam;                  // 激光束实例（挂 _viewRoot，随离场拆）
        private AssetLease<GameObject> _laserLease;     // 激光束租约（登记 _viewScope）
        private AssetLease<GameObject> _hudLease;       // HUD prefab 租约（登记 _viewScope）
        private GameObject _hudInstance;                // HUD 根件（ScreenSpace Canvas——随离场销毁）
        private BattleHudView _hudView;                 // 引用件（序列化引用——八节点，零 Find）
        private HitFeedbackDispatcher _hitFeedback;     // 命中分发（帧事件 → 命中反馈消费者）
        private BattleDamageNumberDriver _damageNumbers;   // 伤害数字（命中反馈首个消费者）
        private BattleMuzzleFlashDriver _muzzleFlash;      // 枪口火光（Fire 帧事件 → 武器挂点跟随 VFX）
        private BattleImpactFxDriver _impactFx;            // 敌人命中特效（本地 Caused Hit/Crit → 受击点）
        private BattleAudioDriver _battleAudio;            // 对局音效（帧事件路由的 Audio 消费者）
        private readonly List<AssetLease<AudioClip>> _audioLeases = new List<AssetLease<AudioClip>>(4);
        private IDisposable _teleportReg;                  // 传送命令处理器注册（进房登记 / 离场注销）
        private AssetLease<GameObject> _damageNumberLease; // 飘字底件租约
        private DamageNumberMaterialSet _damageNumberMaterials;   // 飘字逐档材质（四档不齐则不注入）
        private AssetLease<AnimationClipManifest> _clipManifestLease;    // 片段清单租约
        private Func<Vector3?> _aimPointOf;             // 瞄准点来源（世界坐标；null = 无设备形态）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private BattleHeadshotDebugDrawer _headshotDebug;   // 身位圆柱/爆头带常驻可视化（release 剥离）
        private AutoHeadshotRig _autoHeadshot;              // 爆头自动验证件（GM 面板现场开关；release 剥离）
#endif
        private Transform _viewRoot;
        private GameObject _viewRootGo;
        private GameObject _prefab;
        private AssetLease<GameObject> _prefabLease;

        /// <param name="account">Account 作用域（本件在其下建 `BattleView` 子作用域——离场即整体回收）。</param>
        /// <param name="worldClock">世界钟（伤害数字寿命/合并窗；null = 飘字降级）。</param>
        internal BattleViewRig(IContentService content, IInputService input, ICameraService camera,
            ClientScope account, IVFXService vfx = null, LiteFramework.IWorldClock worldClock = null,
            AudioService audio = null, CommandCenter commands = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            if (account == null) throw new ArgumentNullException(nameof(account));
            _input = input;
            _camera = camera;
            _vfx = vfx;
            _worldClock = worldClock;
            _audio = audio;
            _commands = commands;

            _viewScope = account.CreateChild("BattleView");
            _viewRootGo = new GameObject("[BattleView]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_viewRootGo);
            _viewRoot = _viewRootGo.transform;
        }

        // ---- 取件（资源租约；逐项降级）----

        /// <summary>取全部对局表现资源（角色 prefab → 激光束 → 飘字底件 → 逐档材质 → 片段清单 → 音效 clip）。
        /// 逐项失败只降级该项（缺角色资源回退程序化灰盒），不把缺美术当对局失败。</summary>
        internal async UniTask LoadAsync(CancellationToken ct)
        {
            await AcquireEntityPrefabAsync(ct);
            await AcquireLaserSightAsync(ct);
            await AcquireHudAsync(ct);
            await AcquireDamageNumberAsync(ct);
            await AcquireDamageNumberMaterialsAsync(ct);
            await AcquireClipManifestAsync(ct);
            await AcquireAudioClipsAsync(ct);
        }

        /// <summary>
        /// 取玩家实体 prefab（经内容服务租约，登记进视图作用域）。资源包未入库的克隆（第三方素材按
        /// 仓库政策不入 VCS）或加载失败时**回退程序化灰盒**——缺角色资源是可复现降级而非错误态。
        /// </summary>
        private async UniTask AcquireEntityPrefabAsync(CancellationToken ct)
        {
            try
            {
                _prefabLease = await _content.AcquireAsync<GameObject>(EntityPrefab, ct: ct);
                _viewScope.Register(_prefabLease);
                _prefab = _prefabLease.Asset;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"实体 prefab 缺失，回退程序化灰盒:{EntityPrefab}（{ex.Message}）", "Battle");
                _prefabLease = null;
                _prefab = GreyboxEntity.Make();
                _viewScope.Register(new DelegatedDisposable(() =>
                {
                    if (_prefab != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(_prefab);
                        else UnityEngine.Object.DestroyImmediate(_prefab);
                    }
                }));
            }
        }

        /// <summary>取瞄准激光束 prefab 并实例化（实例挂 _viewRoot 随离场拆）。资源缺失/加载失败 →
        /// **静默降级**（只警告）：激光是表现增益，不把"缺特效资产"当对局失败。</summary>
        private async UniTask AcquireLaserSightAsync(CancellationToken ct)
        {
            try
            {
                _laserLease = await _content.AcquireAsync<GameObject>(LaserSightPrefab, ct: ct);
                _viewScope.Register(_laserLease);
                _laserBeam = UnityEngine.Object.Instantiate(_laserLease.Asset, _viewRoot);
                _laserBeam.name = "LaserSight";
                _laserBeam.SetActive(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"激光束 prefab 缺失/加载失败，瞄准激光降级:{LaserSightPrefab}（{ex.Message}）", "Battle");
                _laserLease = null;
                _laserBeam = null;
            }
        }

        /// <summary>取伤害数字底件 prefab（只取租约不实例化——实例由驱动工厂按预算逐条实例化）。
        /// 资源缺失/加载失败 → **静默降级**（只警告）。</summary>
        /// <summary>
        /// 取战斗 HUD prefab 并实例化（经内容服务租约，登记进视图作用域；ScreenSpace Overlay 根件随离场销毁）。
        /// 资源缺失/加载失败 → **静默降级**（只警告）：HUD 是表现增益，不把"缺 HUD 资产"当对局失败
        /// （同激光/飘字/准心缺失口径）。——《战斗HUD专项设计》批 A：场景对象退役，HUD 改 prefab 实例。
        /// </summary>
        private async UniTask AcquireHudAsync(CancellationToken ct)
        {
            try
            {
                _hudLease = await _content.AcquireAsync<GameObject>(BattleHudView.PrefabLocation, ct: ct);
                _viewScope.Register(_hudLease);
                _hudInstance = UnityEngine.Object.Instantiate(_hudLease.Asset);
                _hudInstance.name = "Hud_Battle";
                _hudView = _hudInstance.GetComponent<BattleHudView>();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"[Battle] HUD prefab 缺失/加载失败，HUD 降级:{BattleHudView.PrefabLocation}（{ex.Message}）", "Battle");
                _hudLease = null;
                _hudInstance = null;
                _hudView = null;
            }
        }

        private async UniTask AcquireDamageNumberAsync(CancellationToken ct)
        {
            try
            {
                _damageNumberLease = await _content.AcquireAsync<GameObject>(DamageNumberPrefab, ct: ct);
                _viewScope.Register(_damageNumberLease);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"伤害数字底件缺失/加载失败，飘字降级:{DamageNumberPrefab}（{ex.Message}）", "Battle");
                _damageNumberLease = null;
            }
        }

        /// <summary>
        /// 取伤害数字逐档材质（黑描边/软影/按档光晕）。**整组注入**：任缺一档 → 整组不注入
        /// （警告），飘字退回底件默认材质——不做部分档位的混合观感（"为什么这发没有光晕"会变成
        /// 不可解释的随机）。取件序 = <see cref="DamageNumberMaterialSet"/> 构造参数序（enum 序）。
        /// </summary>
        private async UniTask AcquireDamageNumberMaterialsAsync(CancellationToken ct)
        {
            string[] locations =
            {
                DamageNumberMatCaused,
                DamageNumberMatCausedCrit,
                DamageNumberMatReceived,
                DamageNumberMatReceivedCrit,
            };
            var materials = new Material[locations.Length];
            for (int i = 0; i < locations.Length; i++)
            {
                try
                {
                    AssetLease<Material> lease = await _content.AcquireAsync<Material>(locations[i], ct: ct);
                    _viewScope.Register(lease);
                    materials[i] = lease.Asset;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log.Warning($"伤害数字材质缺失/加载失败，飘字退回默认材质:{locations[i]}（{ex.Message}）", "Battle");
                    return;                                  // 整组不注入（已取件租约已登记，随 _viewScope 归还）
                }
            }
            _damageNumberMaterials = new DamageNumberMaterialSet(
                materials[0], materials[1], materials[2], materials[3]);
        }

        /// <summary>
        /// 取角色片段清单（去 AC 主路径的片源——键→Clip 显式引用的纯配置，键单源在动画 Profile）。
        /// 资源缺失/加载失败 → **静默降级**（只警告）：驱动回退控制器便利源。清单在则驱动按 Profile 绑定
        /// 做全覆盖校验——清单与 Profile 不同步属配置错误，由驱动构造期显性失败。
        /// </summary>
        private async UniTask AcquireClipManifestAsync(CancellationToken ct)
        {
            try
            {
                _clipManifestLease = await _content.AcquireAsync<AnimationClipManifest>(ClipManifestPath, ct: ct);
                _viewScope.Register(_clipManifestLease);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"片段清单缺失/加载失败，动画回退控制器便利源:{ClipManifestPath}（{ex.Message}）", "Battle");
                _clipManifestLease = null;
            }
        }

        /// <summary>取对局音效 clip 租约（开火/命中/爆头/死亡——逐条独立降级，单条缺失只静默该 kind）。</summary>
        private async UniTask AcquireAudioClipsAsync(CancellationToken ct)
        {
            string[] locations =
            {
                BattleAudioDriver.FireClipLocation,
                BattleAudioDriver.HitClipLocation,
                BattleAudioDriver.CritClipLocation,
                BattleAudioDriver.DeathClipLocation,
            };
            foreach (string location in locations)
            {
                try
                {
                    AssetLease<AudioClip> lease = await _content.AcquireAsync<AudioClip>(location, ct: ct);
                    _viewScope.Register(lease);
                    _audioLeases.Add(lease);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log.Warning($"音效缺失/加载失败，该音降级:{location}（{ex.Message}）", "Battle");
                }
            }
        }

        // ---- 装配（建视图 + 接线；须在 StartGame 之后调用）----

        /// <summary>
        /// 建视图并接线：<see cref="SimView"/>（镜像/插值/静默门/相机服务）→ 移动动画驱动 → 准心 / HUD /
        /// 瞄准激光 → 命中分发 → 伤害数字 / 战斗特效 / 对局音效 →〔编辑器〕爆头诊断件 → 传送命令登记。
        ///
        /// **相机不由本件创建**：装配根建好 <see cref="ICameraService"/>（内含场景配置好的虚拟相机）并
        /// 跨对局复用，本件只把端口交给 SimView，并在开局 <see cref="ICameraService.Reset"/> 一次
        /// （镜头重新落位，不从上局位置飞过来）。
        ///
        /// <paramref name="openBattleMenu"/>＝对局菜单钮回调（HUD 菜单钮点击 → 流程层开设置页——退场链；
        /// null / 旧 prefab 无此钮 = 静默降级，HUD 其余面照常）。
        /// </summary>
        internal void Attach(BattleContext context, Action openBattleMenu = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (openBattleMenu != null) _menuClick = new UnityEngine.Events.UnityAction(openBattleMenu);

            _view = new SimView(_context.Sim.State, _viewRoot,
                factory: InstantiateView,
                recycler: null,                                   // 自有池（EntityService 接线归表现壳批）
                camera: _camera);
            _context.AttachView(_view);

            // 动画 Profile：表单源（读口行 → 按模型族装载——缺行/坏行在装配期显性失败）
            var animationProfile = AnimationProfileLoader.FromRows(
                AnimationProfileConfig.Rows, CombatGirlsAnimationProfile.ModelFamily);
            _locomotion = new CharacterLocomotionDriver(_view, animationProfile,
                manifest: _clipManifestLease?.Asset);   // 清单在 = 去 AC 主路径供片；缺清单回退控制器便利源

            AttachCrosshair();                                  // 对局准心（视图/输入均已就绪，场景对象按名解析）
            AttachHud();                                        // HUD 面板（血条/弹药——同根场景对象，静默降级）
            AttachRankboard();                                  // 局内排行榜（FFA 比分＝击杀——8 格预置，静默降级）
            _aimPointOf = AimPointOf(_input);                   // 瞄准点（世界）→ 相机构图 z 偏移 + 激光收敛端点
            AttachLaserSight();                                 // 瞄准激光（束实例已取，接线驱动）
            _hitFeedback = new HitFeedbackDispatcher(_view);   // 命中分发骨架：EventSink 并列订阅
            AttachDamageNumbers();                             // 伤害数字（命中反馈首个消费者）
            AttachCombatFx();                                  // 枪口火光 + 敌人命中特效（帧事件路由的 VFX 消费者）
            AttachAudio();                                     // 对局音效（帧事件路由的 Audio 消费者）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 爆头区域常驻可视化（测试模式诊断件）：只读预测态，不改判定；release 剥离
            _headshotDebug = new BattleHeadshotDebugDrawer(_context.Sim);
            // 爆头自动验证件（GM 面板现场开关）：替换输入源 + 统计爆头/命中，用于真实对局端到端验链路
            _autoHeadshot = new AutoHeadshotRig(_context.Sim.State, _context.LocalEntityId);
            _hitFeedback?.Register(_autoHeadshot);
#endif
            AttachInputWiring();
            _teleportReg = _commands?.RegisterUndoable(new TeleportToAimHandler(
                aimOf: () => _aimPointOf?.Invoke(),
                originOf: ReadLocalOrigin,
                dispatch: DispatchTeleport));                       // 传送命令化：撤销/重做随命令中心可用
            Log.Info($"view-attached prefab={EntityPrefab}", "Battle");
        }

        /// <summary>每帧驱动视图消费者（<see cref="BattleContext.Tick"/> 之后调用——权威已应用）：移动动画 →
        /// 准心 → HUD → 激光 → 伤害数字 → 瞄准相机（语义态 + 构图 z 偏移）→〔编辑器〕诊断件。</summary>
        internal void Tick(float elapseSeconds)
        {
            _locomotion?.Tick(elapseSeconds);    // 移动动画：视图位置已更新，再解析目标姿态
            _crosshair?.Tick();                  // 对局准心（HUD）：位置/形态/光标
            _hud?.Tick();                        // HUD 面板：血条/弹药（预测态值变化写）
            _rankboard?.Tick();                  // 局内排行榜：击杀分布变化才重排/写（镜像公共面）
            _laser?.Tick();                      // 瞄准激光：端点 = 开火射线（实体/障碍截停）
            _damageNumbers?.Tick();              // 伤害数字：命中事件的视觉收尾（合并窗口/淡出/跟随）

            // 瞄准相机接管（每帧幂等；适配器按沿生效——视图态已解析；滞回门控短按，防频繁点按重启混合）
            _camera?.SetAiming(_aimGate.Feed(IsLocalAiming(), elapseSeconds));
            if (_camera != null)                 // 瞄准点（世界）→ 构图 z 偏移曲线（适配器按到焦点的前向投影换算）
            {
                Vector3? aimPoint = _aimPointOf?.Invoke();
                _camera.SetAimPoint(aimPoint ?? default, aimPoint.HasValue);
            }
        }

        /// <summary>渲染帧采样**之前**的诊断件驱动（编辑器专用；release 空实现）：自瞄验证件要替换输入源，
        /// 必须在本帧采样前落位，否则自瞄点落后一帧（GM 面板现场开关；关闭即还原设备源）。</summary>
        internal void TickPreSample()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _headshotDebug?.Tick();              // 爆头区域常驻可视化（开关关时零开销）
            if (TestModeRuntime.AutoHeadshot)
            {
                if (_autoHeadshot != null && !_autoHeadshot.Active) _autoHeadshot.Enable(_input);
            }
            else if (_autoHeadshot != null && _autoHeadshot.Active)
            {
                _autoHeadshot.Disable();         // 还原设备源（与离场清理同一路径）
            }
            _autoHeadshot?.Tick();
#endif
        }

        /// <summary>
        /// 输入接线（§3 输入三件）：**设备源已在装配根装好**（<see cref="InputModule"/>），
        /// 本件只处理"对局相机注入 + 把服务交给对局上下文 + 镜头落位"。
        /// **上下文门不在这里裁决**——拦截源在装配根按名登记（`ui.modal`），由服务采样时统一裁决。
        /// </summary>
        private void AttachInputWiring()
        {
            if (_input == null) return;                           // 无服务（替身/测试装配）= 无输入形态

            // **对局相机注入**：设备源持有的是装配根 boot 相机——叠加开发形态下它
            // **不随场景销毁**，`Camera.main` 回落也拿错机（两台相机在场），瞄准/相机相对移动会
            // 全部算在错机上。这里把真对局渲染相机（brain 所在 Camera）显式换进去。
            if (_camera is CinemachineCameraService ccs && _input.Source is NewInputIntentSource source)
            {
                ccs.EnsureCamera();                                // 视图未建时 Follow 还没跑过——先解析 vcam
                Camera renderCamera = ccs.TryGetRenderCamera();
                if (renderCamera != null) source.SetAimCamera(renderCamera);
                else Log.Warning("对局渲染相机未解析到——瞄准/移动继续用设备源现持相机", "Battle");
            }

            _context.AttachInput(_input);
            _camera?.Reset();                                     // 镜头重新落位（不从上一局位置飞过来）
        }

        /// <summary>拆视图（逆序：动画 → HUD → 准心 → 激光 → 飘字 → 特效 → 音效 → 传送命令 → 诊断件 →
        /// 命中分发 → 相机语义态 → 视图根 → 作用域）。不触发对局结束——那是流程层的裁决。</summary>
        internal void Dispose()
        {
            _locomotion?.Dispose();               // 先停动画驱动（视图消费者），再拆视图本体
            _locomotion = null;
            _rankboard?.Dispose();                // 排行榜（格归位——同属视图消费者）
            _rankboard = null;
            _hud?.Dispose();                      // HUD 面板（面板归零——视图消费者，先于准心拆）
            _hud = null;
            _crosshair?.Dispose();                // 准心驱动（还系统光标——同属视图消费者）
            _crosshair = null;
            _laser?.Dispose();                    // 激光驱动（束实例随 _viewRoot 销毁；租约随 _viewScope.Dispose 归还）
            _laser = null;
            _laserBeam = null;
            _laserLease = null;
            if (_hudInstance != null)             // HUD 根件（ScreenSpace Canvas——随离场销毁）
            {
                if (_hudView != null && _hudView.SettingsButton != null && _menuClick != null)
                    _hudView.SettingsButton.onClick.RemoveListener(_menuClick);   // 菜单钮监听随拆（存实例移除）
                _menuClick = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_hudInstance);
                else UnityEngine.Object.DestroyImmediate(_hudInstance);
                _hudInstance = null;
            }
            _hudView = null;
            _hudLease = null;                     // 租约随 _viewScope.Dispose 归还
            _damageNumbers?.Dispose();            // 伤害数字（消费者先于分发器拆——实例全归还池）
            _damageNumbers = null;
            _damageNumberMaterials = null;        // 材质引用随驱动拆弃（租约本体已登记 _viewScope）
            _muzzleFlash?.Dispose();              // 枪口火光/命中特效（同上——消费者先于分发器拆）
            _muzzleFlash = null;
            _impactFx?.Dispose();
            _impactFx = null;
            _battleAudio?.Dispose();              // 对局音效（消费者先于分发器/作用域拆；在播代理自然播完）
            _battleAudio = null;
            _audioLeases.Clear();                 // 租约本体已登记 _viewScope（作用域统一归还）
            _teleportReg?.Dispose();              // 传送命令处理器（随本局装配注销——下局重登记）
            _teleportReg = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _headshotDebug = null;                // 纯每帧重绘（无持有状态），随分发器一并走
            // 自动验证件：**先关闭**（还原设备源）再弃引用——否则输入源会停留在替身态直到下次进房
            _autoHeadshot?.Disable();
            _autoHeadshot = null;
#endif
            _hitFeedback?.Dispose();              // 命中分发（视图消费者——先于视图本体拆；消费者随各自批自管）
            _hitFeedback = null;
            _aimPointOf = null;                   // 瞄准点来源随本局设备源一起摘（方法组不跨局持有）
            _camera?.SetAiming(false);            // 瞄准机还原（优先级/Follow 归还场景值——离场不得遗留接管态）
            _context?.AttachInput(null);
            _context = null;
            _view = null;                                         // 视图实例随 _viewRoot 销毁
            if (_viewRootGo != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_viewRootGo);
                else UnityEngine.Object.DestroyImmediate(_viewRootGo);
                _viewRootGo = null;
            }
            _viewRoot = null;
            _prefab = null;
            _prefabLease = null;                                  // 租约随 _viewScope.Dispose 归还
            _clipManifestLease = null;                            // 同上（清单租约只持引用——登记面已在驱动装配完成）
            _viewScope.Dispose();
        }

        // ---- 装配细节 ----

        /// <summary>本地实体当前位置（传送出发位；未就绪 = null——处理器拒绝并留因）。</summary>
        private SimVector3? ReadLocalOrigin()
        {
            if (_context == null || _context.Sim == null || _context.LocalEntityId == 0) return null;
            if (!_context.Sim.State.TryResolve(_context.LocalEntityId, out int slot)) return null;
            return _context.Sim.State.Entities[slot].Pos;
        }

        /// <summary>传送分发单点：写宿主钩子（本地服宿主循环消费——T 键/GM 面板/撤销共用同一通道）。</summary>
        private static void DispatchTeleport(SimVector3 pos)
        {
            SimTestRules.TeleportTarget = pos;
            SimTestRules.TeleportDispatch = true;      // 交权威侧（本地服宿主）执行
        }

        /// <summary>
        /// 挂对局准心（场景对象 `/<see cref="HudRootName"/>`——构建器确定性生成，AgentScripts/BuildBattleHud.cs）
        /// 由本件按名解析；**缺失静默降级**（只警告）——HUD 是表现增益，不把"场景没配 HUD"当对局失败。
        /// 准心画布无 GraphicRaycaster 且 CanvasGroup 不拦射线——**永不参与输入**；上下文门被拦
        /// （模态 UI 开）时由驱动自己藏准心还系统光标。
        /// </summary>
        private void AttachCrosshair()
        {
            if (_hudView == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——准心静默降级", "Battle");
                return;
            }

            _crosshair = new BattleCrosshairDriver(_view, _input, (RectTransform)_hudView.transform, _hudView.CrosshairRoot,
                hip: _hudView.HipForm,
                ads: _hudView.AdsForm,
                screenPosition: ScreenPositionOf(_input));           // 鼠标位来自设备源适配器（R12）
            Log.Info("crosshair-attached", "Battle");
        }

        /// <summary>
        /// 挂 HUD 面板（血条/弹药——技能栏静态占位）：与准心同根（`/<see cref="HudRootName"/>/Panel`）
        /// 按名解析；**缺失/结构不符静默降级**（只警告）。驱动读本地**预测态**（HP/已选武器/弹匣/换弹
        /// ——本人私有面），值变化才写、未对齐整组隐藏。
        /// </summary>
        private void AttachHud()
        {
            if (_hudView == null)
            {
                Log.Warning("HUD prefab 缺失/未实例化——HUD 面板静默降级", "Battle");
                return;
            }

            _hud = new BattleHudDriver(_context.Sim.State, () => _view != null ? _view.LocalEntityId : 0L,
                _hudView.PanelGroup, _hudView.HpBar, _hudView.AmmoText,
                _hudView.ReloadLabel);                              // 缺标签件=驱动内静默降级（不显示换弹态）
            if (_hudView.SettingsButton != null && _menuClick != null)
                _hudView.SettingsButton.onClick.AddListener(_menuClick);   // 对局菜单钮 → 流程层开设置页（退场链）
            Log.Info("hud-attached", "Battle");
        }

        /// <summary>
        /// 挂局内排行榜（<see cref="BattleRankboardDriver"/>——FFA "比分"＝击杀，排行榜批）：
        /// 读**镜像公共面**（参赛玩家 <see cref="EntityFlags.Player"/> ＋ <see cref="EntitySlot.Kills"/>），
        /// **8 格全部 prefab 预置** → 驱动只显隐/写值（禁运行时建视觉元素）。格件不齐（旧 prefab /
        /// 结构变更）→ **静默降级**（只警告）：排行榜是表现增益，不影响 HUD 其余面。
        /// </summary>
        private void AttachRankboard()
        {
            if (_hudView == null) return;                  // HUD 已整体降级（AttachHud/准心处已警告）
            if (!_hudView.HasRankboard)
            {
                Log.Warning("HUD prefab 排行榜格不齐（名字/击杀/高亮）×8——排行榜静默降级", "Battle");
                return;
            }
            _rankboard = new BattleRankboardDriver(_context.Sim.State,
                () => _view != null ? _view.LocalEntityId : 0L,
                _hudView.RankNames, _hudView.RankKills, _hudView.RankHighlights,
                _hudView.TeamScoreText);                   // 团队比分预留节点（缺 = 驱动内跳过）
            Log.Info("rankboard-attached", "Battle");
        }

        /// <summary>
        /// 挂瞄准激光（<see cref="BattleLaserDriver"/>——端点判定与显隐归驱动；本方法只装配）。
        /// 束实例缺 <see cref="UnityEngine.LineRenderer"/>（资产被改）或 Sim 未建（装配序理论不达）→
        /// 警告并降级，不失败。
        /// </summary>
        private void AttachLaserSight()
        {
            if (_laserBeam == null) return;                      // 资产缺失已在取件处降级（警告过）
            if (_context.Sim == null)
            {
                Log.Warning("Sim 未建——瞄准激光本局降级（Attach 早于 StartGame 的装配序）", "Battle");
                return;
            }
            var line = _laserBeam.GetComponent<UnityEngine.LineRenderer>();
            if (line == null)
            {
                Log.Warning($"激光束 prefab 缺 LineRenderer——瞄准激光降级:{LaserSightPrefab}", "Battle");
                return;
            }
            _laser = new BattleLaserDriver(_view, _input, _context.Sim, _context.Map, line,
                aimPointOf: _aimPointOf);                       // 瞄准地面点注入（准心同源——端点压准心）
            Log.Info("laser-attached", "Battle");
        }

        /// <summary>
        /// 挂伤害数字（<see cref="BattleDamageNumberDriver"/>——命中反馈首个消费者；文本/位形/池归驱动；
        /// 寿命与合并窗走世界钟——变速/暂停语义内建）。底件缺 <see cref="TMPro.TextMeshPro"/>（资产被改）
        /// 或世界钟未注入（装配缺口）→ 警告并降级，不失败。
        /// </summary>
        private void AttachDamageNumbers()
        {
            if (_damageNumberLease == null) return;             // 取件降级已警告
            if (_damageNumberLease.Asset == null || _damageNumberLease.Asset.GetComponent<TMPro.TextMeshPro>() == null)
            {
                Log.Warning($"伤害数字底件缺 TextMeshPro——飘字降级:{DamageNumberPrefab}", "Battle");
                _damageNumberLease = null;
                return;
            }
            if (_worldClock == null)
            {
                Log.Warning("世界钟未注入——伤害数字降级（装配缺口，见 ContainerModule）", "Battle");
                _damageNumberLease = null;
                return;
            }

            _damageNumbers = new BattleDamageNumberDriver(_view, NewDamageNumber, _worldClock,
                cameraOf: RenderCameraOf(),       // billboard 面对局渲染相机（非 Camera.main——叠加形态会拿错）
                materials: _damageNumberMaterials);   // 逐档材质（四档不齐 = null 走底件默认，取件处已警告）
            _hitFeedback?.Register(_damageNumbers);
            Log.Info("damage-numbers-attached", "Battle");
        }

        /// <summary>战斗特效装配（帧事件路由的 VFX 消费者）：火光吃 Fire、命中特效吃本地 Caused 的 Hit/Crit；
        /// VFX 服务由容器注入（缺服务整段降级——如实警告，不静默）。特效实例由 VfxService 池化/预算，
        /// 随实体视图回收（StopAll 归各自宿主——火光 follow 挂点、弹着落世界容器）。</summary>
        private void AttachCombatFx()
        {
            if (_vfx == null)
            {
                Log.Warning("VFX 服务未注入——枪口火光/命中特效降级（装配缺口，见 ContainerModule）", "Battle");
                return;
            }
            _muzzleFlash = new BattleMuzzleFlashDriver(_view, _vfx);
            _impactFx = new BattleImpactFxDriver(_vfx);
            _hitFeedback?.Register(_muzzleFlash);
            _hitFeedback?.Register(_impactFx);
        }

        /// <summary>
        /// 挂对局音效（<see cref="BattleAudioDriver"/>——命中反馈消费者；并发预算归 AudioService Effect 组）。
        /// Audio 服务未注入（装配缺口）或全部 clip 缺失 → 警告并整段降级，不失败。
        /// </summary>
        private void AttachAudio()
        {
            if (_audio == null)
            {
                Log.Warning("Audio 服务未注入——对局音效降级（装配缺口，见 ContainerModule）", "Battle");
                return;
            }
            if (_audioLeases.Count == 0) return;                 // 全缺已在取件处逐条警告

            _battleAudio = new BattleAudioDriver(_audio,
                fire: LeasedClip(BattleAudioDriver.FireClipLocation),
                hit: LeasedClip(BattleAudioDriver.HitClipLocation),
                crit: LeasedClip(BattleAudioDriver.CritClipLocation),
                death: LeasedClip(BattleAudioDriver.DeathClipLocation));
            _hitFeedback?.Register(_battleAudio);
        }

        /// <summary>按 location 反查已取得的 clip（缺 = null——驱动按 kind 降级）。</summary>
        private AudioClip LeasedClip(string location)
        {
            for (int i = 0; i < _audioLeases.Count; i++)
                if (_audioLeases[i].Key == location)
                    return _audioLeases[i].Asset;
            return null;
        }

        /// <summary>
        /// 对局**渲染相机**解析（伤害飘字 billboard 面用；每帧调，故须轻量且如实降级）：
        /// Cinemachine brain 的 <c>OutputCamera</c>——**不走 <c>Camera.main</c>**：叠加开发形态下
        /// boot 相机不随场景销毁且带 MainCamera tag，<c>Camera.main</c> 会拿到错的那台。
        /// 解析不到 → null，驱动退回不跟随朝向。
        /// </summary>
        private Func<Camera> RenderCameraOf()
            => _camera is CinemachineCameraService ccs
                ? new Func<Camera>(ccs.TryGetRenderCamera)
                : (Func<Camera>)null;

        /// <summary>飘字实例工厂（驱动池的 create 回调）：底件实例化挂 _viewRoot，随离场拆。</summary>
        private TMPro.TextMeshPro NewDamageNumber()
        {
            var go = UnityEngine.Object.Instantiate(_damageNumberLease.Asset, _viewRoot);
            go.name = "DamageNumber";
            go.SetActive(false);                                 // 池化语义：归还/空闲即隐藏
            return go.GetComponent<TMPro.TextMeshPro>();
        }

        /// <summary>
        /// 准心的鼠标位来源：设备源适配器的 <see cref="NewInputIntentSource.MouseScreenPosition"/>。
        /// 非该实现（测试替身/无设备形态）返回 null——驱动回退屏幕零点。**不在此 import InputSystem**
        /// （纪律 R12：适配器 import 只许在 Platform.Unity 边界目录内）。
        /// </summary>
        private static Func<Vector2> ScreenPositionOf(IInputService input)
        {
            return input != null && input.Source is NewInputIntentSource source
                ? () => source.MouseScreenPosition
                : null;
        }

        /// <summary>
        /// 瞄准点来源：**待上行输入帧**（<see cref="IInputService.Pending"/>）的 AimPoint 三分量——
        /// 与准心/开火/上行同一份事实（AimPoint 单口径）。**为什么不绑具体设备源**：曾经绑定
        /// "鼠标源实例"（开局抓取），自瞄验证件/触屏/测试替身换源后激光与构图仍追旧源（鼠标停在地面
        /// ⇒ 激光"瞄地"、与真实弹道脱节）。改绑 Pending 后**任何意图源**下"激光=准心=弹道"同源不失效。
        /// 零值 = 无点（与判定同约定）→ null。**不在此 import InputSystem**（R12）。
        /// </summary>
        private static Func<Vector3?> AimPointOf(IInputService input)
        {
            if (input == null) return null;
            return () =>
            {
                SimInputFrame f = input.Pending;
                return (f.AimPointX != 0f || f.AimPointY != 0f || f.AimPointZ != 0f)
                    ? (Vector3?)new Vector3(f.AimPointX, f.AimPointY, f.AimPointZ)
                    : null;
            };
        }

        /// <summary>按子路径取对象（形态组缺失 = 单形态退化——准心驱动容忍 null）。</summary>
        /// <summary>
        /// 本地玩家是否处于瞄准态（右键 ADS）——喂 <see cref="ICameraService.SetAiming"/> 的语义源。
        /// 读 <see cref="SimView.IsAiming"/>（本地预测态：连续位进 PredictedButtons，与本地手感同帧），
        /// 按实体 Id 解析槽位（同准心驱动的读法）；未对齐/视图未建 = false。
        /// </summary>
        private bool IsLocalAiming()
        {
            return _view != null && _view.LocalEntityId != 0
                && _view.TryGetSlot(_view.LocalEntityId, out int slot)
                && _view.IsAiming(slot);
        }

        private GameObject InstantiateView(string location, Transform parent)
        {
            if (_prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(_prefab, parent);
            go.name = location;
            return go;
        }
    }
}
