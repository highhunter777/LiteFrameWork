using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 局内排行榜驱动的 L2 EditMode 覆盖（排行榜批——FFA "比分"＝击杀）：镜像与本地 Id 构造注入，
    /// 确定性驱动。钉住：全序排序（Kills 降序 → EntityId 升序，第一名在最左）、同分不抖、脏位（值不变不写）、
    /// 自己格高亮唯一、人数 &lt; 8 尾部格隐藏、非参赛实体不计入、`Txt_Score` 仅 Team 模式显示、Dispose 格归位。
    /// 真场景观感归 PlayMode/手测；prefab 结构（8 格预置）另由结构守卫。
    /// </summary>
    public sealed class BattleRankboardEditModeTests : UnityTestBase
    {
        private static readonly int Cells = BattleRankboardDriver.CellCount;

        private sealed class Board
        {
            public readonly GameObject[] CellRoots = new GameObject[Cells];      // 格根＝单元（隐藏/显示只关它）
            public readonly TextMeshProUGUI[] Names = new TextMeshProUGUI[Cells];
            public readonly TextMeshProUGUI[] Kills = new TextMeshProUGUI[Cells];
            public readonly GameObject[] Highlights = new GameObject[Cells];
            public TextMeshProUGUI TeamScore;
        }

        private Board BuildBoard()
        {
            var board = new Board();
            for (int i = 0; i < Cells; i++)
            {
                GameObject cell = Scope.CreateGameObject("Cot_RankCell" + (i + 1));   // 格根（单元）
                GameObject nameGo = Scope.CreateGameObject("Txt_Name", typeof(RectTransform), typeof(TextMeshProUGUI));
                GameObject killGo = Scope.CreateGameObject("Txt_Kills", typeof(RectTransform), typeof(TextMeshProUGUI));
                GameObject plate = Scope.CreateGameObject("Image_Highlight", typeof(RectTransform));
                nameGo.transform.SetParent(cell.transform, false);     // 子件挂格根下（保持激活）
                killGo.transform.SetParent(cell.transform, false);
                plate.transform.SetParent(cell.transform, false);
                plate.SetActive(false);                                // 高亮件仍按位开关
                board.CellRoots[i] = cell;
                board.Names[i] = nameGo.GetComponent<TextMeshProUGUI>();
                board.Kills[i] = killGo.GetComponent<TextMeshProUGUI>();
                board.Highlights[i] = plate;
            }
            GameObject scoreGo = Scope.CreateGameObject("TeamScore", typeof(RectTransform), typeof(TextMeshProUGUI));
            board.TeamScore = scoreGo.GetComponent<TextMeshProUGUI>();
            scoreGo.SetActive(false);
            return board;
        }

        private static BattleRankboardDriver BuildDriver(Board board, SimWorldState world, long selfId)
            => new BattleRankboardDriver(new LiteGame.BattleReadModel(() => world, () => selfId),
                () => selfId, board.CellRoots, board.Names, board.Kills, board.Highlights, board.TeamScore);

        /// <summary>生成一个参赛玩家（<see cref="EntityFlags.Player"/>），返回实体 Id（生成序 = Id 升序）。</summary>
        private static long SpawnPlayer(SimWorldState world, int kills)
            => world.Spawn(new EntitySlot
            {
                Hp = 100,
                Pos = new SimVector3(0f, 0f, 0f),
                Flags = EntityFlags.Player,
                Kills = kills,
            }, out _);

        [Test]
        [Category(TestCategory.Contract)]
        public void 排行_击杀降序_第一名在最左_名字为身份序号()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 1);      // 身份序号 1
            SpawnPlayer(world, 5);                 // 身份序号 2
            SpawnPlayer(world, 3);                 // 身份序号 3
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();

            Assert.AreEqual("5", board.Kills[0].text, "第一名（击杀最多）在最左");
            Assert.AreEqual("3", board.Kills[1].text);
            Assert.AreEqual("1", board.Kills[2].text);
            // 名字 = 身份序号（按 EntityId 升序）——不随击杀变化（占位口径，真名字归 G3）
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "2", board.Names[0].text, "B 的身份序号 2（排第一）");
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "1", board.Names[2].text, "A 的身份序号 1（排第三）");
            Assert.AreEqual(3, ActiveCells(board), "仅 3 格在显");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 排行_同击杀按EntityId升序_两次Tick不抖()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 2);
            SpawnPlayer(world, 2);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "1", board.Names[0].text, "同分 → EntityId 升序（A 在前）");
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "2", board.Names[1].text);

            driver.Tick();                                  // 数据未变 —— 不写
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "1", board.Names[0].text);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 脏位_击杀分布不变不重写()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 1);
            SpawnPlayer(world, 2);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();
            board.Kills[1].text = "SENTINEL";              // 人为改值（模拟外部写）
            driver.Tick();                                 // 击杀分布未变 —— 应跳过写
            Assert.AreEqual("SENTINEL", board.Kills[1].text, "值未变不重写（脏位）");
            Assert.AreEqual(PlayerIdentity.PlaceholderNamePrefix + "1", board.Names[1].text, "确实跑到了该格（非整体跳过）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 自己格高亮_唯一()
        {
            var world = new SimWorldState { RngState = 1UL };
            SpawnPlayer(world, 1);
            long idB = SpawnPlayer(world, 2);              // 本地玩家 = 击杀最多者（排第一）
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idB);

            driver.Tick();

            int active = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (board.Highlights[i].activeSelf)
                {
                    active++;
                    Assert.AreEqual("2", board.Kills[i].text, "高亮的恰是本地玩家那一格（击杀最多 → 最左）");
                }
            }
            Assert.AreEqual(1, active, "高亮件恰好一个点亮");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 人数少于八格_尾部格隐藏()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 0);
            SpawnPlayer(world, 0);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();

            Assert.IsTrue(board.CellRoots[1].activeSelf, "第 2 格在显（单元级）");
            Assert.IsTrue(board.Names[1].gameObject.activeSelf, "格内子件保持激活（隐藏只关格根）");
            for (int i = 2; i < Cells; i++)
            {
                Assert.IsFalse(board.CellRoots[i].activeSelf, $"第 {i + 1} 格应隐藏（人数 2；关格根）");
                Assert.IsTrue(board.Names[i].gameObject.activeSelf, "格内子件不被驱动关掉（编辑器可直接调）");
                Assert.IsFalse(board.Highlights[i].activeSelf);
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 非参赛实体_不计入排行()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 1);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Flags = 0u, Kills = 99 }, out _);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();

            Assert.AreEqual("1", board.Kills[0].text, "非参赛实体（无 Player 标志）不进排行");
            Assert.IsFalse(board.CellRoots[1].activeSelf, "仅 1 人 → 第 2 格隐藏（关格根）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 团队比分_FFA隐藏_Team模式才显示()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 0);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);

            driver.Tick();
            Assert.IsFalse(board.TeamScore.gameObject.activeSelf, "FFA（Team==0）隐藏——不显示恒 0 假数据");

            world.Match.Team = 1;                          // 团队模式（未做，面预留）
            world.Match.Score = 7;
            driver.Tick();
            Assert.IsTrue(board.TeamScore.gameObject.activeSelf, "Team 模式显示");
            Assert.AreEqual("7", board.TeamScore.text);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Dispose_格归位不残留()
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = SpawnPlayer(world, 3);
            Board board = BuildBoard();
            BattleRankboardDriver driver = BuildDriver(board, world, idA);
            driver.Tick();
            Assert.IsTrue(board.Highlights[0].activeSelf, "本地玩家格已点亮");

            driver.Dispose();

            for (int i = 0; i < Cells; i++)
            {
                Assert.IsFalse(board.Highlights[i].activeSelf, "离场高亮归零");
                Assert.IsFalse(board.CellRoots[i].activeSelf, "离场格隐藏（关格根）");
            }
            driver.Dispose();                              // 幂等
        }

        // ---- assertion helper ----

        private static int ActiveCells(Board board)
        {
            int n = 0;
            for (int i = 0; i < Cells; i++)
                if (board.CellRoots[i].activeSelf) n++;      // 单元级＝格根
            return n;
        }
    }
}
