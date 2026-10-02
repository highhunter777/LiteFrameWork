using LiteFramework;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>候选资产包根判定（《样例①真实资源包段-运行手册》判据单源）：
    /// 只有"已确认候选身份 + 候选资产包版本文件已就位"两条齐备才用候选根；
    /// 其余（无身份/内置身份/文件缺失）一律退回内置包根——fail-safe，且不虚构能力。</summary>
    public sealed class CandidateBundleRootResolverEditModeTests
    {
        private const string VersionFile = "content/candidate/bundle/DefaultPackage.version";

        [Test]
        public void 判定_内置身份_即便文件在也退回内置包根()
        {
            bool ready = CandidateBundleRootResolver.IsCandidateBundleReady(
                ContentGeneration.Default.ReleaseId, ReleaseLayout.CandidateBundleRootRelative,
                AssetService.DefaultPackageName, _ => true);

            Assert.IsFalse(ready, "内置身份永远走内置包根");
        }

        [Test]
        public void 判定_无身份或空_退回内置包根()
        {
            Assert.IsFalse(CandidateBundleRootResolver.IsCandidateBundleReady(
                null, ReleaseLayout.CandidateBundleRootRelative, AssetService.DefaultPackageName, _ => true));
            Assert.IsFalse(CandidateBundleRootResolver.IsCandidateBundleReady(
                "", ReleaseLayout.CandidateBundleRootRelative, AssetService.DefaultPackageName, _ => true));
        }

        [Test]
        public void 判定_已确认候选且版本文件就位_用候选根且探测路径正确()
        {
            string probed = null;
            bool ready = CandidateBundleRootResolver.IsCandidateBundleReady(
                "rel-sample-001", ReleaseLayout.CandidateBundleRootRelative, AssetService.DefaultPackageName,
                path => { probed = path; return true; });

            Assert.IsTrue(ready, "两条齐备 → 用候选资产包根");
            Assert.AreEqual(VersionFile, probed, "版本文件探测路径 = 候选 bundle 根 + 包名.version（与 BuiltinFileSystem 应答同约定）");
        }

        [Test]
        public void 判定_有身份但版本文件缺失_退回内置包根()
        {
            bool ready = CandidateBundleRootResolver.IsCandidateBundleReady(
                "rel-sample-001", ReleaseLayout.CandidateBundleRootRelative, AssetService.DefaultPackageName, _ => false);

            Assert.IsFalse(ready, "候选包不完整（缺版本文件）→ 退回内置，不让初始化裸失败");
        }
    }
}