using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using UnityEngine;
#if !UNITY_EDITOR
using System.Collections.Generic;
using YooAsset;
#endif

namespace LiteGameNext.Platform.Content
{
    /// <summary>新客户端内容装载门面：编辑器按 AssetDatabase 定位，Player 按 YooAsset location 装载；缺资源一律显性失败。</summary>
    public sealed class NextContentService
    {
        private const string NextRoot = "Assets/LiteGame/Next";
        private const string PackageName = "DefaultPackage";

        private bool _initialized;
#if !UNITY_EDITOR
        private readonly Dictionary<GameObject, AssetHandle> _handles = new Dictionary<GameObject, AssetHandle>();
        private readonly List<SceneHandle> _scenes = new List<SceneHandle>();
        private ResourcePackage _package;
#endif

        /// <summary>初始化内容包；编辑器侧无需初始化，Player 侧建立内置 Offline 包。</summary>
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            if (_initialized) return;
            ct.ThrowIfCancellationRequested();
#if UNITY_EDITOR
            await UniTask.CompletedTask;
#else
            YooAssets.Initialize();
            ResourcePackage package = YooAssets.CreatePackage(PackageName);
            var options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
            };
            await NextAssetAwait.Wait(package.InitializePackageAsync(options), ct);
            RequestPackageVersionOperation versionOp = package.RequestPackageVersionAsync();
            await NextAssetAwait.Wait(versionOp, ct);
            LoadPackageManifestOperation manifestOp = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(versionOp.PackageVersion, 60));
            await NextAssetAwait.Wait(manifestOp, ct);
            _package = package;
            Log.Info("[Asset] ready package=" + PackageName);
#endif
            _initialized = true;
        }

        /// <summary>按 prefab 文件名（不含扩展名）装载并实例化，实例保持非激活交给调用方注入依赖。</summary>
        public async UniTask<GameObject> InstantiateAsync(string address, CancellationToken ct)
        {
            ValidateAddress(address);
            ct.ThrowIfCancellationRequested();
#if UNITY_EDITOR
            GameObject prefab = LoadEditorPrefab(AddressKey(address));
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + "(Page)";
            instance.SetActive(false);
            return instance;
#else
            // Player：包未开启 addressable（EnableAddressable=0），按资源路径加载。
            AssetHandle handle = RequirePackage().LoadAssetAsync<GameObject>(address);
            await NextAssetAwait.Wait(handle, ct);
            GameObject go = Track(handle, address, "(Page)");
            go.SetActive(false);
            return go;
#endif
        }

        /// <summary>同步装载（战斗世界用；保持 Func&lt;BattleWorldView&gt; 契约，不改 BattleFeature）。</summary>
        public GameObject InstantiateSync(string address)
        {
            ValidateAddress(address);
#if UNITY_EDITOR
            GameObject prefab = LoadEditorPrefab(AddressKey(address));
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + "(Instance)";
            return instance;
#else
            AssetHandle handle = RequirePackage().LoadAssetSync<GameObject>(address);
            handle.WaitForAsyncComplete();
            return Track(handle, address, "(Instance)");
#endif
        }

        /// <summary>加载地图场景（Additive）；编辑器走 SceneManager，Player 走 YooAsset 场景句柄。</summary>
        public async UniTask<bool> LoadSceneAsync(string scenePath, CancellationToken ct)
        {
            ValidateAddress(scenePath);
            ct.ThrowIfCancellationRequested();
#if UNITY_EDITOR
            UnityEngine.AsyncOperation op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                scenePath, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (op != null && !op.isDone) await UniTask.Yield();
            return UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath).IsValid();
#else
            ResourcePackage package = RequirePackage();
            SceneHandle handle = package.LoadSceneAsync(scenePath,
                UnityEngine.SceneManagement.LoadSceneMode.Additive);
            await NextAssetAwait.Wait(handle, ct);
            handle.ActivateScene();
            _scenes.Add(handle);
            return true;
#endif
        }

        /// <summary>卸载全部本服务加载的场景（逆序）。</summary>
        public async UniTask UnloadScenesAsync(CancellationToken ct)
        {
#if UNITY_EDITOR
            await UniTask.CompletedTask;
#else
            for (int i = _scenes.Count - 1; i >= 0; i--)
            {
                SceneHandle handle = _scenes[i];
                await NextAssetAwait.Wait(handle.UnloadSceneAsync(), ct);
            }
            _scenes.Clear();
#endif
        }

        /// <summary>销毁实例并归还其内容句柄；编辑器侧只销毁实例。</summary>
        public void Release(GameObject instance)
        {
            if (instance == null) return;
#if !UNITY_EDITOR
            if (_handles.TryGetValue(instance, out AssetHandle handle))
            {
                _handles.Remove(instance);
                handle.Release();
            }
#endif
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }

        /// <summary>关闭：归还全部剩余句柄并清空登记表。</summary>
        public UniTask ShutdownAsync(CancellationToken ct)
        {
#if !UNITY_EDITOR
            for (int i = _scenes.Count - 1; i >= 0; i--) _scenes[i].UnloadSceneAsync();
            _scenes.Clear();
            foreach (KeyValuePair<GameObject, AssetHandle> pair in _handles) pair.Value.Release();
            _handles.Clear();
            _package = null;
#endif
            _initialized = false;
            return UniTask.CompletedTask;
        }

        /// <summary>把完整资源路径或纯文件名统一归一化成不含扩展名的文件名。</summary>
        public static string AddressKey(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return address;
            string leaf = address.Replace('\\', '/');
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0) leaf = leaf.Substring(slash + 1);
            if (leaf.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                leaf = leaf.Substring(0, leaf.Length - ".prefab".Length);
            return leaf;
        }

        private static void ValidateAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("内容地址为空", nameof(address));
        }

#if UNITY_EDITOR
        private static GameObject LoadEditorPrefab(string address)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(address);
            string[] guids = UnityEditor.AssetDatabase.FindAssets(name + " t:Prefab", new[] { NextRoot });
            string path = null;
            for (int i = 0; i < guids.Length; i++)
            {
                string candidate = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(candidate), name,
                        StringComparison.Ordinal)) continue;
                if (path != null)
                    throw new InvalidOperationException("内容地址歧义：" + address + "（" + path + " / " + candidate + "）");
                path = candidate;
            }
            if (path == null)
                throw new InvalidOperationException("页面 prefab 不存在：" + address);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("页面 prefab 无法加载：" + path);
            return prefab;
        }
#else
        private ResourcePackage RequirePackage()
        {
            if (_package == null)
                throw new InvalidOperationException("内容服务未初始化：先 InitializeAsync");
            return _package;
        }

        private GameObject Track(AssetHandle handle, string address, string suffix)
        {
            GameObject asset = handle.GetAssetObject<GameObject>();
            if (asset == null)
            {
                handle.Release();
                throw new InvalidOperationException("页面 prefab 加载失败：" + address);
            }
            GameObject go = UnityEngine.Object.Instantiate(asset);
            go.name = asset.name + suffix;
            _handles[go] = handle;
            return go;
        }
#endif
    }
}
