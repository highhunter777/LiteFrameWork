using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;
using LiteClient;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 样例①（真实资产包段）的素材与收集组构建器（幂等；《样例①真实资源包段-运行手册》第 1 步）。
    ///
    /// 两步：
    /// ① **生成标记素材**（入库口径：共享资源目录、只收纳必要配套）：
    ///    `Assets/ShareResource/SampleContent/CandidateMarkerTexture.png`（2×2 纯色）+ 引用它的
    ///    `CandidateMarker.prefab`（SpriteRenderer）——Prefab 的唯一依赖是同目录纹理，无字体/TMP/
    ///    角色等外部依赖。
    /// ② **登记收集组**：`ShareResource`（CollectPath=`Assets/ShareResource` 整目录，PackSeparately
    ///    ——与 UIScreens 同款：单资源单包）——经 <see cref="BundleCollectorSettingData"/> **代码登记**
    ///    并 SaveFile（不手改 .asset）。
    ///
    /// **为什么这样能作证据**：内置包（StreamingAssets/yoo）在本收集组登记**之前**构建，因此不含标记
    /// 素材；候选资产包（本组参与构建）含它——资源入口探针（<see cref="AssetsHealthProbe"/>）能加载出
    /// `CandidateMarker.prefab`，即证明资源来自**候选包根**而非内置包。
    /// </summary>
    public static class ContentSampleBuilder
    {
        private const string ShareResourceRoot = "Assets/ShareResource";
        private const string GroupName = "ShareResource";

        [MenuItem("LiteGame/Resource/样例内容：生成标记素材与收集组")]
        public static void BuildFromMenu()
        {
            GenerateMarkerAssets();
            RegisterCollectGroup();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ContentSample] 标记素材与收集组就绪（样例①真实资产包段）");
        }

        /// <summary>生成标记纹理与引用它的 Prefab（已存在即跳过——幂等）。</summary>
        public static void GenerateMarkerAssets()
        {
            EnsureFolder(ShareResourceRoot);
            EnsureFolder(ContentSampleAssets.Root);

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(ContentSampleAssets.MarkerTextureLocation) == null)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color32[4];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0xF2, 0x6B, 0x21, 0xFF);
                    texture.SetPixels32(pixels);
                    texture.Apply();
                    File.WriteAllBytes(ContentSampleAssets.MarkerTextureLocation, texture.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                AssetDatabase.ImportAsset(ContentSampleAssets.MarkerTextureLocation);

                // 纹理按 Sprite 导入（Prefab 依赖它作为 Sprite 资源——同目录、同收集组）
                var importer = AssetImporter.GetAtPath(ContentSampleAssets.MarkerTextureLocation) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(ContentSampleAssets.MarkerPrefabLocation) != null) return;

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ContentSampleAssets.MarkerTextureLocation);
            if (sprite == null)
                throw new InvalidOperationException(
                    $"标记纹理未能作为 Sprite 载入：{ContentSampleAssets.MarkerTextureLocation}（检查纹理导入设置）");

            var go = new GameObject("CandidateMarker", typeof(SpriteRenderer));
            try
            {
                go.GetComponent<SpriteRenderer>().sprite = sprite;
                PrefabUtility.SaveAsPrefabAsset(go, ContentSampleAssets.MarkerPrefabLocation);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>登记收集组（幂等）：`ShareResource` → `Assets/ShareResource` 整目录、PackSeparately。</summary>
        public static void RegisterCollectGroup()
        {
            if (!BundleCollectorSettingData.HasSettingAsset())
                throw new InvalidOperationException("BundleCollectorSetting.asset 不存在——无法登记收集组");

            BundleCollectorPackage package = FindPackage(AssetService.DefaultPackageName);
            if (package == null)
                package = BundleCollectorSettingData.CreatePackage(AssetService.DefaultPackageName);

            BundleCollectorGroup group = null;
            foreach (BundleCollectorGroup g in package.Groups)
            {
                if (g.GroupName == GroupName) { group = g; break; }
            }
            if (group == null)
            {
                group = BundleCollectorSettingData.CreateGroup(package, GroupName);
                group.GroupDesc = "共享资源目录（Assets/ShareResource）——样例①真实资产包段标记素材在此";
                group.ActiveRuleName = nameof(EnableGroup);
            }

            bool hasCollector = false;
            foreach (BundleCollector c in group.Collectors)
            {
                if (c.CollectPath == ShareResourceRoot) { hasCollector = true; break; }
            }
            if (!hasCollector)
            {
                BundleCollectorSettingData.CreateCollector(group, new BundleCollector
                {
                    CollectPath = ShareResourceRoot,
                    CollectorType = ECollectorType.MainAssetCollector,
                    AddressRuleName = nameof(AddressByFileName),
                    PackRuleName = nameof(PackSeparately),
                    FilterRuleName = nameof(CollectAll),
                });
            }

            BundleCollectorSettingData.ModifyGroup(package, group);
            BundleCollectorSettingData.SaveFile();

            // 忽略规则核对（LiteGameIgnoreRule 放行本目录的 prefab/png；若被忽略会静默少收——显式报告）
            IAssetIgnoreRule ignoreRule = BundleCollectorSettingData.GetAssetIgnoreRuleInstance(package.IgnoreRuleName);
            if (ignoreRule != null && ignoreRule.IsIgnoreAsset(new EditorAssetInfo(ContentSampleAssets.MarkerPrefabLocation)))
                Debug.LogWarning($"[ContentSample] 标记素材被忽略规则 {package.IgnoreRuleName} 排除——收集组不会收录它");
        }

        private static BundleCollectorPackage FindPackage(string packageName)
        {
            BundleCollectorSetting setting = BundleCollectorSettingData.Setting;
            if (setting == null) return null;
            foreach (BundleCollectorPackage p in setting.Packages)
            {
                if (p.PackageName == packageName) return p;
            }
            return null;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                throw new InvalidOperationException($"非法资源目录：{assetFolder}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}