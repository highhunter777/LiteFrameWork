using System;
using LiteFramework;
using LiteFramework;
using LiteSim;
using LiteView;
using UnityEngine;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// 对局**战斗反馈装配器**：命中分发器 + 伤害数字 / 枪口火光 / 敌人命中特效 / 远端开火火光 /
    /// 对局音效 / 权威命中反馈。消费者按注册序接收帧事件；VFX/Audio/世界钟缺服务时整段降级（如实警告）。
    /// 驱动实例归本件；租约与材质归 <see cref="BattleViewAssets"/>。
    /// </summary>
    internal sealed class BattleFeedbackAssembler
    {
        private readonly IVFXService _vfx;
        private readonly AudioService _audio;
        private readonly IWorldClock _worldClock;
        private readonly ICameraService _camera;
        private readonly BattleViewAssets _assets;
        private readonly Transform _viewRoot;
        private readonly IDevSession _dev;            // 开发会话端口（伤害数字开关；可空 = 测试装配）

        private HitFeedbackDispatcher _dispatcher;
        private AuthoritativeCombatFeedback _authority;
        private BattleDamageNumberDriver _damageNumbers;
        private BattleMuzzleFlashDriver _muzzleFlash;
        private BattleImpactFxDriver _impactFx;
        private BattleRemoteMuzzleFlashDriver _remoteFire;
        private BattleAudioDriver _battleAudio;

        public BattleFeedbackAssembler(IVFXService vfx, AudioService audio, IWorldClock worldClock,
            ICameraService camera, BattleViewAssets assets, Transform viewRoot, IDevSession dev = null)
        {
            _vfx = vfx;
            _audio = audio;
            _worldClock = worldClock;
            _camera = camera;
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _viewRoot = viewRoot ?? throw new ArgumentNullException(nameof(viewRoot));
            _dev = dev;
        }

        public HitFeedbackDispatcher Dispatcher => _dispatcher;

        /// <summary>建命中分发器（视图已建；全体消费者注册到它）。</summary>
        public HitFeedbackDispatcher CreateDispatcher(SimView view)
        {
            _dispatcher = new HitFeedbackDispatcher(view);
            return _dispatcher;
        }

        /// <summary>调试/自瞄件等外部消费者注册（null 拒收）。</summary>
        public void Register(IHitFeedbackConsumer consumer) => _dispatcher?.Register(consumer);

        /// <summary>建反馈消费者并接线（伤害数字 → 战斗特效 → 远端火光 → 对局音效 → 权威反馈）。</summary>
        public void AttachConsumers(BattleContext context, SimView view)
        {
            AttachDamageNumbers(view);
            AttachCombatFx(view);
            AttachRemoteFireFx(context, view);
            AttachAudio();
            _authority = new AuthoritativeCombatFeedback(context.CombatFeed, view, _dispatcher);
        }

        /// <summary>权威命中/击杀队列 → 分发器（先于 HUD/消费者）。</summary>
        public void TickAuthority() => _authority?.Tick();

        /// <summary>帧事件消费者：伤害数字 → 远端火光（同帧顺序固定）。</summary>
        public void TickVisual()
        {
            _damageNumbers?.Tick();
            _remoteFire?.Tick();
        }

        public void DisposeRemoteFire()
        {
            _remoteFire?.Dispose();               // 挂点缓存归零——同属视图消费者
            _remoteFire = null;
        }

        public void DisposeDamageNumbers()
        {
            _damageNumbers?.Dispose();            // 消费者先于分发器拆——实例全归还池
            _damageNumbers = null;
        }

        public void DisposeCombatFx()
        {
            _muzzleFlash?.Dispose();              // 同上——消费者先于分发器拆
            _muzzleFlash = null;
            _impactFx?.Dispose();
            _impactFx = null;
        }

        public void DisposeAuthority()
        {
            _authority?.Dispose();                // 先摘订阅，再拆分发器/视图
            _authority = null;
        }

        public void DisposeAudio()
        {
            _battleAudio?.Dispose();              // 消费者先于分发器/作用域拆；在播代理自然播完
            _battleAudio = null;
        }

        public void DisposeDispatcher()
        {
            _dispatcher?.Dispose();               // 视图消费者——先于视图本体拆
            _dispatcher = null;
        }

        /// <summary>
        /// 挂伤害数字（命中反馈首个消费者；文本/位形/池归驱动；寿命与合并窗走世界钟）。
        /// 底件缺 TMP 或世界钟未注入 → 警告并降级，不失败。
        /// </summary>
        private void AttachDamageNumbers(SimView view)
        {
            if (_assets.DamageNumberLease == null) return;             // 取件降级已警告
            if (_assets.DamageNumberLease.Asset == null
                || _assets.DamageNumberLease.Asset.GetComponent<TMPro.TextMeshPro>() == null)
            {
                Log.Warning($"伤害数字底件缺 TextMeshPro——飘字降级:{BattleViewAssets.DamageNumberPrefab}", LogTag.Battle);
                return;
            }
            if (_worldClock == null)
            {
                Log.Warning("世界钟未注入——伤害数字降级（装配缺口，见 ContainerModule）", LogTag.Battle);
                return;
            }

            _damageNumbers = new BattleDamageNumberDriver(view, NewDamageNumber, _worldClock,
                cameraOf: RenderCameraOf(),       // billboard 面对局渲染相机（非 Camera.main——叠加形态会拿错）
                materials: _assets.DamageNumberMaterials,   // 逐档材质（四档不齐 = null 走底件默认，取件处已警告）
                dev: _dev);                        // 开发会话端口（飘字开关；null = 恒显）
            _dispatcher?.Register(_damageNumbers);
            Log.Info("damage-numbers-attached", LogTag.Battle);
        }

        /// <summary>战斗特效装配（帧事件路由的 VFX 消费者）：火光吃 Fire、命中特效吃本地 Caused 的 Hit/Crit。</summary>
        private void AttachCombatFx(SimView view)
        {
            if (_vfx == null)
            {
                Log.Warning("VFX 服务未注入——枪口火光/命中特效降级（装配缺口，见 ContainerModule）", LogTag.Battle);
                return;
            }
            _muzzleFlash = new BattleMuzzleFlashDriver(view, _vfx);
            _impactFx = new BattleImpactFxDriver(_vfx);
            _dispatcher?.Register(_muzzleFlash);
            _dispatcher?.Register(_impactFx);
        }

        /// <summary>挂远端开火火光：消费会话公共战斗事件面的开火队列，把他人开火落到其枪口挂点。</summary>
        private void AttachRemoteFireFx(BattleContext context, SimView view)
        {
            if (_vfx == null) return;                      // VFX 已整体降级（AttachCombatFx 处已警告）
            _remoteFire = new BattleRemoteMuzzleFlashDriver(view, context.CombatFeed, _vfx);
            Log.Info("remote-fire-attached", LogTag.Battle);
        }

        /// <summary>挂对局音效（命中反馈消费者；并发预算归 AudioService Effect 组）。</summary>
        private void AttachAudio()
        {
            if (_audio == null)
            {
                Log.Warning("Audio 服务未注入——对局音效降级（装配缺口，见 ContainerModule）", LogTag.Battle);
                return;
            }
            if (_assets.AudioLeases.Count == 0) return;           // 全缺已在取件处逐条警告

            _battleAudio = new BattleAudioDriver(_audio,
                fire: _assets.LeasedClip(BattleAudioDriver.FireClipLocation),
                hit: _assets.LeasedClip(BattleAudioDriver.HitClipLocation),
                crit: _assets.LeasedClip(BattleAudioDriver.CritClipLocation),
                death: _assets.LeasedClip(BattleAudioDriver.DeathClipLocation));
            _dispatcher?.Register(_battleAudio);
        }

        /// <summary>
        /// 对局**渲染相机**解析（伤害飘字 billboard 面用）：Cinemachine brain 的 <c>OutputCamera</c>——
        /// **不走 <c>Camera.main</c>**（叠加开发形态下 boot 相机不随场景销毁且带 MainCamera tag）。
        /// 解析不到 → null，驱动退回不跟随朝向。
        /// </summary>
        private Func<Camera> RenderCameraOf()
            => _camera is CinemachineCameraService ccs
                ? new Func<Camera>(ccs.TryGetRenderCamera)
                : (Func<Camera>)null;

        /// <summary>飘字实例工厂（驱动池的 create 回调）：底件实例化挂视图根，随离场拆。</summary>
        private TMPro.TextMeshPro NewDamageNumber()
        {
            var go = UnityEngine.Object.Instantiate(_assets.DamageNumberLease.Asset, _viewRoot);
            go.name = "DamageNumber";
            go.SetActive(false);                                 // 池化语义：归还/空闲即隐藏
            return go.GetComponent<TMPro.TextMeshPro>();
        }
    }
}
