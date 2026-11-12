using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.Modules.Config;
using LiteGameNext.Platform.Config;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>玩法配置摘要（红线 1 客户端复算面）：单源取表、同表同值、改字节即变、缺表即抛。</summary>
    public sealed class ClientNextConfigDigestTests
    {
        private static LiteGameNext.Platform.Config.ConfigService LoadReal()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) =>
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
                    "Assets/GameData/Config/" + file + ".bytes");
                return UniTask.FromResult(asset?.bytes);
            });
            service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service;
        }

        [Test]
        public void 摘要_判定面清单来自共享单源()
        {
            Assert.Greater(TableLayout.JudgmentFiles.Length, 0, "判定面清单不应为空");
            CollectionAssert.Contains(TableLayout.JudgmentFiles, "Combat/tbweapon");
            CollectionAssert.Contains(TableLayout.JudgmentFiles, "Map/tbmap");
        }

        [Test]
        public void 真实表_可复算摘要且两次一致()
        {
            LiteGameNext.Platform.Config.ConfigService service;
            try
            {
                service = LoadReal();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            uint first = service.ComputeGameplayDigestBase();
            uint second = service.ComputeGameplayDigestBase();
            Assert.AreEqual(first, second, "同表集两次复算应同值");
            Assert.AreNotEqual(0u, first, "摘要不应为零");
        }

        [Test]
        public void 改一个字节_摘要即变()
        {
            var files = TableLayout.JudgmentFiles;
            var entries = new List<CombatConfigDigest.Entry>();
            foreach (string file in files)
                entries.Add(new CombatConfigDigest.Entry(file, new byte[] { 1, 2, 3 }));
            uint before = CombatConfigDigest.ComputeBase(entries);

            entries[0] = new CombatConfigDigest.Entry(files[0], new byte[] { 1, 2, 4 });
            uint after = CombatConfigDigest.ComputeBase(entries);

            Assert.AreNotEqual(before, after, "改一个字节摘要必须变");
        }

        [Test]
        public void 规则位不同_终值摘要不同()
        {
            const uint baseDigest = 0x12345678u;
            uint plain = CombatConfigDigest.WithTestRules(baseDigest, noDeath: false, infiniteAmmo: false);
            uint noDeath = CombatConfigDigest.WithTestRules(baseDigest, noDeath: true, infiniteAmmo: false);
            uint infinite = CombatConfigDigest.WithTestRules(baseDigest, noDeath: false, infiniteAmmo: true);
            Assert.AreNotEqual(plain, noDeath);
            Assert.AreNotEqual(plain, infinite);
            Assert.AreNotEqual(noDeath, infinite);
        }

        [Test]
        public void 未装载_复算即抛()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService(
                (file, ct) => UniTask.FromResult<byte[]>(null));
            Assert.Throws<LiteGameNext.Platform.Config.InvalidDataException>(
                () => service.ComputeGameplayDigestBase());
        }
    }
}
