using LiteGameNext.Features.Battle.Hud;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>HUD 数据面：10 类读数（生命/弹药/备弹换弹/准心/计分/团队分/阵亡复活/保护/排行/击杀流/网络）。</summary>
    public sealed class ClientNextBattleHudModelTests
    {
        [Test]
        public void 生命_钳制在零到上限()
        {
            var hud = new BattleHudModel();
            hud.SetVitals(150, 100, 30);
            Assert.AreEqual(100, hud.Health, "超过上限应钳制");
            hud.SetVitals(-5, 100, 30);
            Assert.AreEqual(0, hud.Health, "低于零应钳制");
        }

        [Test]
        public void 弹药_弹匣备弹换弹三读数()
        {
            var hud = new BattleHudModel();
            hud.SetAmmoDetail(7, 120, true);
            Assert.AreEqual(7, hud.Ammo);
            Assert.AreEqual(120, hud.ReserveAmmo);
            Assert.IsTrue(hud.IsReloading);
        }

        [Test]
        public void 准心_开镜形态与散布()
        {
            var hud = new BattleHudModel();
            hud.SetAim(true, 4.8f);
            Assert.IsTrue(hud.IsAiming);
            Assert.AreEqual(4.8f, hud.Spread, 0.001f);
            hud.SetAim(false, -1f);
            Assert.AreEqual(0f, hud.Spread, 0.001f, "负散布应为零");
        }

        [Test]
        public void 计分_团队分缺省取击杀数()
        {
            var hud = new BattleHudModel();
            hud.SetScore(3, 1);
            Assert.AreEqual(3, hud.Kills);
            Assert.AreEqual(1, hud.Deaths);
            Assert.AreEqual(3, hud.TeamScore, "非团队模式团队分取本地击杀");
            hud.SetScore(3, 1, 42);
            Assert.AreEqual(42, hud.TeamScore);
        }

        [Test]
        public void 阵亡复活保护_三态可读()
        {
            var hud = new BattleHudModel();
            hud.SetLifeState(true, 3.5f, true);
            Assert.IsTrue(hud.IsDead);
            Assert.AreEqual(3.5f, hud.RespawnSeconds, 0.001f);
            Assert.IsTrue(hud.IsProtected);
        }

        [Test]
        public void 网络读数_丢包钳制在千分比()
        {
            var hud = new BattleHudModel();
            hud.SetNetwork(45, 2000);
            Assert.AreEqual(45, hud.RttMs);
            Assert.AreEqual(1000, hud.LossPermille, "丢包应钳制到 1000‰");
        }

        [Test]
        public void 排行榜_最多八行且保持顺序()
        {
            var hud = new BattleHudModel();
            var rows = new RankRow[10];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new RankRow(i + 1, "P" + (i + 1), 10 - i, i == 0);
            hud.SetRankboard(rows);
            Assert.AreEqual(8, hud.Rankboard.Count, "排行最多 8 行");
            Assert.AreEqual("P1", hud.Rankboard[0].Name);
            Assert.IsTrue(hud.Rankboard[0].IsLocal);
        }

        [Test]
        public void 击杀流_最新在前且最多五行()
        {
            var hud = new BattleHudModel();
            for (int i = 0; i < 7; i++)
                hud.PushKillFeed(new KillFeedRow("K" + i, "V" + i, 1, i == 6));
            Assert.AreEqual(5, hud.KillFeed.Count, "击杀流最多 5 行");
            Assert.AreEqual("K6", hud.KillFeed[0].Killer, "最新应在前");
            Assert.IsTrue(hud.KillFeed[0].LocalInvolved);
        }

        [Test]
        public void 换局重置_清空全部读数()
        {
            var hud = new BattleHudModel();
            hud.SetVitals(50, 100, 5);
            hud.SetAmmoDetail(1, 2, true);
            hud.SetScore(3, 2, 9);
            hud.SetNetwork(100, 50);
            hud.PushKillFeed(new KillFeedRow("a", "b", 1, false));
            hud.Reset();
            Assert.AreEqual(100, hud.Health);
            Assert.AreEqual(0, hud.Ammo);
            Assert.AreEqual(0, hud.Kills);
            Assert.AreEqual(0, hud.RttMs);
            Assert.AreEqual(0, hud.KillFeed.Count);
            Assert.AreEqual(0, hud.Rankboard.Count);
        }
    }
}
