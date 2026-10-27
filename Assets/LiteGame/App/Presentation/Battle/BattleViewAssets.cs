using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using LiteFramework.Animation;
using LiteView;
using LiteView.Animation;
using UnityEngine;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 对局视图的**资源取件域**：角色 prefab / 激光束 / HUD prefab / 出生保护壳 / 飘字底件与四档材质 /
    /// 片段清单 / 四组音效——全部经内容服务租约并登记视图作用域。逐项降级（缺美术不当作对局失败），
    /// 取件失败策略由调用点决定（整组材质任一失败不注入；音效逐条独立降级）。
    /// 租约本体归 <see cref="ClientScope"/>（随对局作用域归还），本件只持引用。
    /// </summary>
    internal sealed class BattleViewAssets
    {
        /// <summary>玩家实体 prefab（内容包路径；资源缺失时**无角色视图**——不造占位替身）。</summary>
        internal const string EntityPrefab = SimView.DefaultEntityPrefab;

        /// <summary>瞄准激光束 prefab（内容包路径：Assets/FX 归置副本 fx_lazer_sight——KriptoFX Lazer 单源）。</summary>
        internal const string LaserSightPrefab = "Assets/ShareResource/FX/fx_lazer_sight.prefab";

        /// <summary>伤害数字底件 prefab（世界空间 TMP 飘字；实例由驱动按预算逐条实例化）。</summary>
        internal const string DamageNumberPrefab = "Assets/ShareResource/FX/fx_damage_number.prefab";

        /// <summary>出生保护壳件 prefab（FX 域；**金色**半透明包裹壳——表现件走 ShareResource/FX，收集组件级热更）。</summary>
        private const string ProtectionShellPrefab = "Assets/ShareResource/FX/fx_spawn_protection.prefab";

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
        private readonly ClientScope _viewScope;
        private readonly Transform _viewRoot;
        private readonly Func<UiThemeAsset> _themeProvider;
        private readonly List<AssetLease<AudioClip>> _audioLeases = new List<AssetLease<AudioClip>>(4);

        public BattleViewAssets(IContentService content, ClientScope viewScope, Transform viewRoot,
            Func<UiThemeAsset> themeProvider)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _viewScope = viewScope ?? throw new ArgumentNullException(nameof(viewScope));
            _viewRoot = viewRoot ?? throw new ArgumentNullException(nameof(viewRoot));
            _themeProvider = themeProvider;
        }

        public GameObject Prefab { get; private set; }
        public AssetLease<GameObject> PrefabLease { get; private set; }
        public GameObject LaserBeam { get; private set; }
        public AssetLease<GameObject> LaserLease { get; private set; }
        public GameObject HudInstance { get; private set; }
        public BattleHudView HudView { get; private set; }
        public AssetLease<GameObject> HudLease { get; private set; }
        public AssetLease<GameObject> ProtectionShellLease { get; private set; }
        public AssetLease<GameObject> DamageNumberLease { get; private set; }
        public DamageNumberMaterialSet DamageNumberMaterials { get; private set; }
        public AssetLease<AnimationClipManifest> ClipManifestLease { get; private set; }
        public IReadOnlyList<AssetLease<AudioClip>> AudioLeases => _audioLeases;

        /// <summary>取全部对局表现资源（角色 prefab → 激光束 → HUD → 保护壳 → 飘字底件 → 逐档材质 → 片段清单 → 音效 clip）。
        /// 逐项失败只降级该项（缺角色资源＝无角色视图），不把缺美术当对局失败。</summary>
        public async UniTask LoadAsync(CancellationToken ct)
        {
            await AcquireEntityPrefabAsync(ct);
            await AcquireLaserSightAsync(ct);
            await AcquireHudAsync(ct);
            await AcquireProtectionShellAsync(ct);
            await AcquireDamageNumberAsync(ct);
            await AcquireDamageNumberMaterialsAsync(ct);
            await AcquireClipManifestAsync(ct);
            await AcquireAudioClipsAsync(ct);
        }

        /// <summary>按 location 反查已取得的 clip（缺 = null——驱动按 kind 降级）。</summary>
        public AudioClip LeasedClip(string location)
        {
            for (int i = 0; i < _audioLeases.Count; i++)
                if (_audioLeases[i].Key == location)
                    return _audioLeases[i].Asset;
            return null;
        }

        /// <summary>离场清引用（租约本体随视图作用域归还；HUD 实例已由 HUD 装配器销毁）。</summary>
        public void ClearReferences()
        {
            // 未 Attach 即离场（房间未开局/场景加载或相机门失败）时 HUD 实例无人接管——此处兜底销毁；
            // 已 Attach 的路径由 BattleHudAssembler.DisposeRoot 先销毁（Unity 伪空判等跳过，不会双杀）。
            if (HudInstance != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(HudInstance);
                else UnityEngine.Object.DestroyImmediate(HudInstance);
            }
            HudInstance = null;
            HudView = null;
            Prefab = null;
            PrefabLease = null;
            LaserBeam = null;
            LaserLease = null;
            HudLease = null;
            ProtectionShellLease = null;
            DamageNumberLease = null;
            DamageNumberMaterials = null;
            ClipManifestLease = null;
            _audioLeases.Clear();
        }

        /// <summary>
        /// 单件取件收口：租约 → 登记视图作用域 → 交给调用点实例化/注入；失败（非取消）警告并降级。
        /// 日志前缀由调用点给（各件文案单源在调用点），返回 null = 已降级。
        /// </summary>
        private async UniTask<AssetLease<T>> AcquireAsync<T>(string location, string degradePrefix,
            Action<AssetLease<T>> use, CancellationToken ct, Action onFail = null) where T : UnityEngine.Object
        {
            try
            {
                AssetLease<T> lease = await _content.AcquireAsync<T>(location, ct: ct);
                _viewScope.Register(lease);
                use?.Invoke(lease);
                return lease;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Warning($"{degradePrefix}:{location}（{ex.Message}）", LogTag.Battle);
                onFail?.Invoke();
                return null;
            }
        }

        /// <summary>取玩家实体 prefab（经内容服务租约）。资源包未入库的克隆或加载失败时**无角色视图**——
        /// 工厂拒绝＝无实体可见（EntityViewMap 契约），不造占位替身。</summary>
        private async UniTask AcquireEntityPrefabAsync(CancellationToken ct)
        {
            PrefabLease = await AcquireAsync<GameObject>(EntityPrefab, "实体 prefab 缺失，本局无角色视图",
                lease => Prefab = lease.Asset, ct, onFail: () => Prefab = null);
        }

        /// <summary>取瞄准激光束 prefab 并实例化（实例挂 _viewRoot 随离场拆）。资源缺失/加载失败 → **静默降级**。</summary>
        private async UniTask AcquireLaserSightAsync(CancellationToken ct)
        {
            LaserLease = await AcquireAsync<GameObject>(LaserSightPrefab, "激光束 prefab 缺失/加载失败，瞄准激光降级",
                lease =>
                {
                    LaserBeam = UnityEngine.Object.Instantiate(lease.Asset, _viewRoot);
                    LaserBeam.name = "LaserSight";
                    LaserBeam.SetActive(false);
                }, ct, onFail: () => LaserBeam = null);
        }

        /// <summary>
        /// 取战斗 HUD prefab 并实例化（ScreenSpace Overlay 根件随离场销毁）。资源缺失/加载失败 → **静默降级**；
        /// 实例化后失败不留在场景（孤儿清理）。
        /// </summary>
        private async UniTask AcquireHudAsync(CancellationToken ct)
        {
            HudLease = await AcquireAsync<GameObject>(BattleHudView.PrefabLocation, "[Battle] HUD prefab 缺失/加载失败，HUD 降级",
                lease =>
                {
                    HudInstance = UnityEngine.Object.Instantiate(lease.Asset);
                    HudInstance.name = "Hud_Battle";
                    HudView = HudInstance.GetComponent<BattleHudView>();
                    ApplyHudTheme();
                }, ct, onFail: () =>
                {
                    if (HudInstance != null) UnityEngine.Object.Destroy(HudInstance);
                    HudInstance = null;
                    HudView = null;
                });
        }

        /// <summary>HUD 状态色注入（P2-3 B4）：provider 已装载 → `UiThemeBinder` 注入；null/资产缺失保持烘焙值并 Warning 一次（本局）。</summary>
        private void ApplyHudTheme()
        {
            if (_themeProvider == null || HudInstance == null) return;
            SafeCall.Invoke(() =>
            {
                UiThemeAsset asset = _themeProvider();
                if (asset == null)
                {
                    Log.Warning("[Battle] 主题资产未就绪——HUD 保持 prefab 烘焙值（状态色不注入）", LogTag.UI);
                    return;
                }
                UiThemeBinder.Apply(HudInstance, UiStyleResolver.FromAsset(asset), "HUD");
            }, "BattleViewAssets.ApplyHudTheme");
        }

        /// <summary>取出生保护壳 prefab 租约（FX 域；实例由保护壳驱动按槽位惰性创建）。资源缺失 → **静默降级**。</summary>
        private async UniTask AcquireProtectionShellAsync(CancellationToken ct)
        {
            ProtectionShellLease = await AcquireAsync<GameObject>(ProtectionShellPrefab,
                "出生保护壳缺失/加载失败，保护特效降级", null, ct);
        }

        /// <summary>取伤害数字底件 prefab（只取租约不实例化——实例由驱动工厂按预算逐条实例化）。资源缺失 → **静默降级**。</summary>
        private async UniTask AcquireDamageNumberAsync(CancellationToken ct)
        {
            DamageNumberLease = await AcquireAsync<GameObject>(DamageNumberPrefab,
                "伤害数字底件缺失/加载失败，飘字降级", null, ct);
        }

        /// <summary>
        /// 取伤害数字逐档材质（黑描边/软影/按档光晕）。**整组注入**：任缺一档 → 整组不注入
        /// （警告），飘字退回底件默认材质——不做部分档位的混合观感。取件序 = 构造参数序（enum 序）。
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
                AssetLease<Material> lease = await AcquireAsync<Material>(locations[i],
                    "伤害数字材质缺失/加载失败，飘字退回默认材质", null, ct);
                if (lease == null) return;                       // 整组不注入（已取件租约已登记，随作用域归还）
                materials[i] = lease.Asset;
            }
            DamageNumberMaterials = new DamageNumberMaterialSet(
                materials[0], materials[1], materials[2], materials[3]);
        }

        /// <summary>取角色片段清单（键→Clip 显式引用的纯配置）。资源缺失 → **静默降级**：驱动回退控制器便利源。</summary>
        private async UniTask AcquireClipManifestAsync(CancellationToken ct)
        {
            ClipManifestLease = await AcquireAsync<AnimationClipManifest>(ClipManifestPath,
                "片段清单缺失/加载失败，动画回退控制器便利源", null, ct);
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
                AssetLease<AudioClip> lease = await AcquireAsync<AudioClip>(location,
                    "音效缺失/加载失败，该音降级", null, ct);
                if (lease != null) _audioLeases.Add(lease);
            }
        }
    }
}
