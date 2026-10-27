using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// A3 生命周期守卫：HUD 取件成功但**未 Attach 即离场**（房间未开局 / 场景加载或相机门失败）时，
    /// `BattleViewAssets.ClearReferences` 必须兜底销毁已实例化的 HUD 根件——否则残留全屏 Canvas。
    /// 该路径在拆分前由 `BattleViewRig.Dispose` 的无条件 `_hudInstance` 销毁覆盖。
    /// </summary>
    public sealed class BattleViewAssetsEditModeTests
    {
        private sealed class FakeContent : IContentService
        {
            public GameObject HudPrefab;
            public int Acquires;

            public UniTask InitializeAsync(CancellationToken ct = default) => UniTask.CompletedTask;

            public IReadOnlyList<string> ListAssetPathsByTag(string tag) => Array.Empty<string>();

            public UniTask<AssetLease<T>> AcquireAsync<T>(string location,
                ContentGeneration generation = default, CancellationToken ct = default) where T : class
            {
                Acquires++;
                if (typeof(T) == typeof(GameObject) && location == BattleHudView.PrefabLocation)
                    return UniTask.FromResult(new AssetLease<T>(location, HudPrefab as T, _ => { }));
                throw new InvalidOperationException($"测试替身只提供 HUD prefab:{location}");
            }

            public UniTask ShutdownAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        }

        [Test]
        public void 取件成功但未Attach_离场销毁HUD实例()
        {
            var hudPrefab = new GameObject("HudPrefab");
            var viewRoot = new GameObject("[BattleView]");
            try
            {
                hudPrefab.AddComponent<BattleHudView>();
                var content = new FakeContent { HudPrefab = hudPrefab };
                using (var account = new ClientScope("Account"))
                {
                    var assets = new BattleViewAssets(content, account, viewRoot.transform, themeProvider: null);
                    assets.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

                    GameObject instance = assets.HudInstance;
                    Assert.IsNotNull(instance, "HUD 取件成功后应实例化根件");
                    Assert.Greater(content.Acquires, 0);

                    assets.ClearReferences();                    // 未 Attach：HUD 装配器未接管实例

                    Assert.IsTrue(instance == null, "未 Attach 路径也必须销毁 HUD 实例（防全屏 Canvas 残留）");
                    Assert.IsNull(assets.HudInstance);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewRoot);
                UnityEngine.Object.DestroyImmediate(hudPrefab);
            }
        }
    }
}
