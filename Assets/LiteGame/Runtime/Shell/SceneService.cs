using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace LiteGame
{
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
    public sealed class SceneService
    {
        private SceneHandle _single;                                       // 单场景（切换语义）
        private string _singleLocation;
        private readonly Dictionary<string, SceneHandle> _additives = new Dictionary<string, SceneHandle>(StringComparer.Ordinal);
        private SceneHandle _lastLoad;                                     // 进度读数源：最近一次发起的加载

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
            await UnloadSingleAsync(ct);

            SceneHandle handle = null;
            try
            {
                handle = await LoadHandleAsync(location, LoadSceneMode.Single, ct);
            }
            finally
            {
                // Single 语义已销毁全部旧场景：无论成败，叠加登记都失效（Dispose 释放引用，不再 Unload）
                ClearAdditiveRegistry();
            }

            _single = handle;
            _singleLocation = location;
            Log.Info($"单场景已加载:{location}", "Scene");
        }

        /// <summary>卸载当前单场景；无场景 = no-op（幂等宽容）。</summary>
        public async UniTask UnloadSingleAsync(CancellationToken ct = default)
        {
            if (_single == null) return;

            SceneHandle handle = _single;
            _single = null;
            _singleLocation = null;                            // 先摘引用：重入/失败都不指向半卸场景
            await UnloadHandleAsync(handle, ct);
        }

        // ---- 叠加（并发） ----

        /// <summary>叠加加载场景并激活；同 location 已加载 = Warning + no-op。</summary>
        public async UniTask LoadAdditiveAsync(string location, CancellationToken ct = default)
        {
            ValidateLocation(location);
            if (IsLoaded(location))
            {
                Log.Warning($"叠加场景已加载，忽略重复请求:{location}", "Scene");
                return;
            }

            SceneHandle handle = await LoadHandleAsync(location, LoadSceneMode.Additive, ct);
            _additives[location] = handle;
            Log.Info($"叠加场景已加载:{location}（当前叠加数 {_additives.Count}）", "Scene");
        }

        /// <summary>卸载指定叠加场景；未加载 = no-op（幂等宽容）。</summary>
        public async UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default)
        {
            ValidateLocation(location);
            if (!_additives.TryGetValue(location, out SceneHandle handle)) return;

            _additives.Remove(location);                       // 先摘引用：重入/失败都不指向半卸场景
            await UnloadHandleAsync(handle, ct);
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
            int released = 0;
            if (_single != null)
            {
                _single.Dispose();                              // 释放引用（不 Unload——退出路径）
                _single = null;
                _singleLocation = null;
                released++;
            }

            released += _additives.Count;
            if (_additives.Count > 0)
            {
                foreach (var kv in _additives) kv.Value.Dispose();
                _additives.Clear();
            }

            if (released > 0) Log.Info($"宿主关闭释放 {released} 个场景句柄（退出路径不卸场景，仅归还引用）", "Scene");
        }

        // ---- 内部 ----

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
            foreach (var kv in _additives) kv.Value.Dispose();  // Unity 已销毁其场景——只释放引用
            _additives.Clear();
            Log.Info($"单场景切换清理 {count} 个叠加登记（Unity Single 语义已销毁其场景）", "Scene");
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrEmpty(location)) throw new ArgumentNullException(nameof(location));
        }
    }
}
