using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace LiteClient
{
    /// <summary>
    /// 场景句柄的租约包装（《客户端总设计》§8.2 四件之一的 `SceneLease`）。
    ///
    /// **为什么是包装而不是直接登记 <see cref="SceneHandle"/>**：<see cref="ClientScope.Register{T}"/>
    /// 要求 <c>T : IDisposable</c>，而 <see cref="SceneHandle"/> 的 <c>Dispose</c> **只释放 YooAsset 引用、
    /// 不卸场景**。卸载路径要的是"先 <c>UnloadSceneAsync</c> 再 Dispose"——借租约把这两步绑成一个
    /// 可登记对象，作用域的 LIFO 收尾即"卸场景 + 释放资源"（§6.2 Scene 行退出动作）。
    ///
    /// **正常卸载路径不用它**：卸载走 <see cref="SceneService.UnloadSingleAsync"/> /
    /// <see cref="SceneService.UnloadAdditiveAsync"/> 的显式 <c>UnloadHandleAsync</c>（因为要 await
    /// 卸载完成）；本租约服务**宿主关闭路径**（<see cref="SceneService.ReleaseAll"/>，退出不卸场景、
    /// 只归还引用）与作用域的登记语义。两条路径都幂等（<see cref="AssetLease{T}"/> 同款守卫）。
    /// </summary>
    internal sealed class SceneLease : IDisposable
    {
        private readonly SceneHandle _handle;
        private bool _released;

        public SceneLease(SceneHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _handle.Dispose();
        }
    }

    /// <summary>
    /// 场景加载薄壳（DI 单例，ProcedureLaunch 注册）。**只提供机制，切换决策归 Procedure**——
    /// 业务禁止裸调 AssetService.LoadSceneAsync（设计方案 §1.3 场景行）。
    /// 加载方式（两种，语义各自钉死）：
    /// ① **单场景** `LoadSingleAsync/UnloadSingleAsync`——切换语义（先卸后载）；Unity Single 模式会销毁全部已开场景，
    ///    故切换成功/失败后**叠加登记一律随之失效**（句柄 Dispose 释放 YooAsset 引用，不再 Unload——场景已不在）；
    /// ② **叠加** `LoadAdditiveAsync/UnloadAdditiveAsync`——并发多场景（关卡叠加/UI 叠加）；
    ///    同 location 重复加载 = Warning + no-op（幂等宽容，同事件注销口径）；卸载未加载 = no-op。
    /// 契约：location 为场景资源完整路径；失败抛 InvalidOperationException（含 location，fail-fast 由流程 Fail() 接）；
    /// 句柄全由本类持有不外泄（业务拿不到 SceneHandle，无泄漏窗口）；主线程 only；加载并发不设守卫（并发语义是调用方的策略）；
    /// **叠加场景的 AudioListener/Camera 冲突由场景制作者处理**（机制壳不代管策略——实测叠加后会出现
    /// "multiple audio listeners" 警告，属被叠加场景自带监听器所致）。
    /// </summary>
    public sealed class SceneService : ISceneService
    {
        private SceneHandle _single;                                       // 单场景（切换语义）
        private ClientScope _singleScope;                                  // 单场景作用域（§6.2 Scene 行）
        private string _singleLocation;
        private readonly Dictionary<string, SceneHandle> _additives = new Dictionary<string, SceneHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, ClientScope> _additiveScopes = new Dictionary<string, ClientScope>(StringComparer.Ordinal);   // 每个叠加场景一枚作用域
        private SceneHandle _lastLoad;                                     // 进度读数源：最近一次发起的加载
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        /// <summary>
        /// 宿主代次：<see cref="ReleaseAll"/> 时递增。在途加载据此判定"我已过期"——
        /// 底层加载不可中途取消时，迟到的结果必须**释放自己的租约**而不是回写已释放的服务
        /// （《动画模块专项设计》§9 同款纪律；《客户端总设计》§5"旧回调不能修改新实例"）。
        /// </summary>
        private int _generation;

        /// <summary>服务是否已释放（ReleaseAll 后不再接受加载请求）。</summary>
        public bool IsReleased { get; private set; }

        /// <summary>因宿主已释放而被丢弃的迟到加载结果数（诊断：应恒为 0，非 0 说明有加载跨了关闭）。</summary>
        public int DiscardedLateLoads { get; private set; }

        /// <summary>最近一次加载操作的进度（0~1；无加载为 0）。</summary>
        public float Progress => _lastLoad != null ? _lastLoad.Progress : 0f;

        /// <summary>当前单场景名（未加载为 null）。</summary>
        public string SingleSceneName => _single?.SceneName;

        /// <summary>叠加场景数量。</summary>
        public int AdditiveCount => _additives.Count;

        /// <summary>已加载叠加场景的 location 只读视图（诊断/面板用；句柄仍不外泄）。</summary>
        public IReadOnlyCollection<string> AdditiveLocations => _additives.Keys;

        /// <summary>该 location 是否已加载（单场景或叠加任一）。</summary>
        public bool IsLoaded(string location)
            => !string.IsNullOrEmpty(location)
               && (_singleLocation == location || _additives.ContainsKey(location));

        // ---- 单场景（切换） ----

        /// <summary>加载单场景并激活（先卸旧单场景；叠加登记随之清理——见类注释①）。</summary>
        public async UniTask LoadSingleAsync(string location, CancellationToken ct = default)
        {
            ValidateLocation(location);
            ThrowIfReleased(nameof(LoadSingleAsync));

            int generation = _generation;
            await UnloadSingleAsync(ct);

            SceneHandle handle = null;
            try
            {
                handle = await LoadHandleAsync(location, LoadSceneMode.Single, ct);
            }
            catch
            {
                // 失败路径也要清理叠加登记（Single 语义可能已销毁旧场景）——见 finally 的说明
                ClearAdditiveRegistry();
                throw;
            }

            // Single 语义已销毁全部旧场景：无论成败，叠加登记都失效（Dispose 释放引用，不再 Unload）
            ClearAdditiveRegistry();

            // 迟到检查（§9）：等待期间宿主可能已 ReleaseAll——此时不得回写已释放的服务
            if (generation != _generation || IsReleased)
            {
                handle.Dispose();
                DiscardedLateLoads++;
                Log.Warning($"单场景加载完成但宿主已释放，结果就地丢弃:{location}", "Scene");
                return;
            }

            _single = handle;
            _singleLocation = location;
            // 场景作用域（§6.2"Scene：场景加载到卸载"）：句柄登记进作用域后，
            // 卸载/释放路径**只 Dispose 作用域**就完成了"卸场景 + 释放引用"两件事——
            // 顺序与容错由 ClientScope 保证（先 UnloadSceneAsync 再 Dispose，见 SceneLease 的 LIFO 语义）。
            _singleScope = new ClientScope($"Scene.Single[{location}]");
            _singleScope.Register(new SceneLease(handle));
            Log.Info($"单场景已加载:{location}", "Scene");
        }

        /// <summary>卸载当前单场景；无场景 = no-op（幂等宽容）。</summary>
        public async UniTask UnloadSingleAsync(CancellationToken ct = default)
        {
            if (_single == null) return;

            SceneHandle handle = _single;
            ClientScope scope = _singleScope;
            _single = null;
            _singleScope = null;
            _singleLocation = null;                            // 先摘引用：重入/失败都不指向半卸场景
            await UnloadHandleAsync(handle, ct);
            scope?.Dispose();                                  // 作用域收尾：登记项已由上面的显式卸载归还，此处保幂等与容错聚合
        }

        // ---- 叠加（并发） ----

        /// <summary>叠加加载场景并激活；同 location 已加载 = Warning + no-op。
        /// **同名场景已在 SceneManager 在场（预置场景开发形态）= 不再叠加**：编辑器里开着目标场景
        /// 进 Play 时，再叠加会得到 Unity 的**第二份同名实例**（相机/监听器/场景对象全部翻倍）。
        /// 命中 → 跳过加载，登记为**领养场景**（句柄 = null，属编辑器会话，本服务不卸不释放）；
        /// `IsLoaded` 从此返回 true（后续重进走幂等早退），`UnloadAdditiveAsync` 对领养实例 = no-op 警告。</summary>
        public async UniTask LoadAdditiveAsync(string location, CancellationToken ct = default)
        {
            ValidateLocation(location);
            ThrowIfReleased(nameof(LoadAdditiveAsync));
            if (IsLoaded(location))
            {
                Log.Warning($"叠加场景已加载，忽略重复请求:{location}", "Scene");
                return;
            }
            if (IsLoadedExternally(location))
            {
                _additives[location] = null;              // 领养登记：无句柄（场景归编辑器会话所有）
                Log.Warning($"同名场景已在场（预置场景开发形态），跳过叠加加载并登记:{location}", "Scene");
                return;
            }

            int generation = _generation;
            SceneHandle handle = await LoadHandleAsync(location, LoadSceneMode.Additive, ct);

            // 迟到检查（§9）：等待期间宿主已释放 / 已发生单场景切换清理 → 就地释放引用
            if (generation != _generation || IsReleased || _additives.ContainsKey(location))
            {
                handle.Dispose();
                DiscardedLateLoads++;
                Log.Warning($"叠加场景加载完成但宿主已释放或登记已变更，结果就地丢弃:{location}", "Scene");
                return;
            }

            _additives[location] = handle;
            var additiveScope = new ClientScope($"Scene.Additive[{location}]");
            additiveScope.Register(new SceneLease(handle));
            _additiveScopes[location] = additiveScope;
            Log.Info($"叠加场景已加载:{location}（当前叠加数 {_additives.Count}）", "Scene");
        }

        /// <summary>卸载指定叠加场景；未加载 = no-op（幂等宽容）。**领养实例 = no-op 警告**：
        /// 场景归编辑器会话所有，本服务只摘自己的登记、不卸载它。</summary>
        public async UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default)
        {
            ValidateLocation(location);
            if (!_additives.TryGetValue(location, out SceneHandle handle)) return;

            _additives.Remove(location);                       // 先摘引用：重入/失败都不指向半卸场景
            _additiveScopes.Remove(location, out ClientScope scope);
            if (handle == null)
            {
                Log.Warning($"叠加场景为领养实例（预置场景开发形态），只摘登记、不执行卸载:{location}", "Scene");
                return;
            }
            await UnloadHandleAsync(handle, ct);
            scope?.Dispose();                                  // 作用域收尾（同单场景）
            Log.Info($"叠加场景已卸载:{location}（余 {_additives.Count}）", "Scene");
        }

        // ---- 释放（宿主关闭面） ----

        /// <summary>
        /// 释放全部在持场景句柄（单场景 + 全部叠加），**不执行场景卸载**——本方法只用于宿主关闭路径
        /// （Container 模块 ShutdownAsync 逆序调用，§6.2 Scene 域"释放资源租约"退出动作）：
        /// 进程已在退出，卸场景无意义，但 YooAsset 引用必须归零（编辑器 Domain-Reload-Off 的重复 Play、
        /// 退出复跑不能累积半开句柄）。幂等；正常路径的单/叠加卸载不经过本方法。
        /// </summary>
        public void ReleaseAll()
        {
            if (IsReleased) return;                             // 幂等
            IsReleased = true;
            _generation++;                                      // 使全部在途加载过期（迟到结果就地释放）

            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            _lifetime.Dispose();

            int released = 0;
            if (_single != null)
            {
                _single.Dispose();                              // 释放引用（不 Unload——退出路径）
                _single = null;
                _singleLocation = null;
                released++;
            }
            _singleScope?.Dispose();                            // 场景作用域同步收尾（幂等）
            _singleScope = null;

            released += _additives.Count;
            if (_additives.Count > 0)
            {
                foreach (var kv in _additives) kv.Value?.Dispose();   // 领养实例（null 句柄）只清登记
                _additives.Clear();
            }
            foreach (var kv in _additiveScopes) kv.Value.Dispose();
            _additiveScopes.Clear();

            _lastLoad = null;                                   // 进度读数源失效

            if (released > 0) Log.Info($"宿主关闭释放 {released} 个场景句柄（退出路径不卸场景，仅归还引用）", "Scene");
        }

        /// <summary>宿主代次取消令牌（服务生命周期）——调用方与自己的 ct 链接，即可获得"宿主关闭级联"。
        /// 已释放时返回已取消令牌。</summary>
        public CancellationToken LifetimeToken => _lifetime.Token;

        // ---- 内部 ----

        private void ThrowIfReleased(string operation)
        {
            if (IsReleased)
                throw new ObjectDisposedException(nameof(SceneService), $"宿主已释放，拒绝 {operation}");
        }

        private async UniTask<SceneHandle> LoadHandleAsync(string location, LoadSceneMode mode, CancellationToken ct)
        {
            SceneHandle handle = AssetService.Package.LoadSceneAsync(location, mode);
            _lastLoad = handle;
            try
            {
                await handle.AsUniTask(ct);
            }
            catch (Exception ex)
            {
                handle.Dispose();                              // 失败句柄立即释放，不留半截加载
                throw new InvalidOperationException($"场景加载失败:{location}", ex);
            }

            handle.ActivateScene();
            return handle;
        }

        private static async UniTask UnloadHandleAsync(SceneHandle handle, CancellationToken ct)
        {
            await handle.UnloadSceneAsync().AsUniTask(ct);
            handle.Dispose();
        }

        private void ClearAdditiveRegistry()
        {
            if (_additives.Count == 0) return;

            int count = _additives.Count;
            foreach (var kv in _additives) kv.Value?.Dispose();  // Unity 已销毁其场景——只释放引用（领养实例 = null 只清登记）
            foreach (var kv in _additiveScopes) kv.Value.Dispose();   // 作用域同步收尾（幂等）
            _additives.Clear();
            _additiveScopes.Clear();
            Log.Info($"单场景切换清理 {count} 个叠加登记（Unity Single 语义已销毁其场景）", "Scene");
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) throw new ArgumentNullException(nameof(location));
        }

        /// <summary>SceneManager 里是否已有同 path 的已加载场景（本服务之外持有的——编辑器预置场景开发形态）。
        /// 在发起 YooAsset 加载**之前**调用：本服务自己加载的场景此刻还不在 SceneManager 里，
        /// 命中的一定是外部（编辑器会话）实例。</summary>
        private static bool IsLoadedExternally(string location)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.path == location) return true;
            }
            return false;
        }
    }
}
