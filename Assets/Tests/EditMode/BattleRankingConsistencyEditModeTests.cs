using System.Collections.Generic;
using LiteNet.Proto;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 排行口径一致性：局内排行榜（<see cref="BattleRankboardDriver"/>，每帧从活世界读）
    /// 与结算页（<see cref="ResultRanking"/>，从冻结结果读）必须给出**同一条全序与同一套名次**——
    /// 两处共用 <see cref="BattleRankOrder"/>；本用例用同一批玩家从两条入口各算一次并逐格比对，
    /// 防"各写一份排序"在同分边界漂移。
    /// </summary>
    public sealed class BattleRankingConsistencyEditModeTests : UnityTestBase
    {
        private sealed class Board
        {
            public readonly GameObject[] CellRoots = new GameObject[BattleRankboardDriver.CellCount];
            public readonly TextMeshProUGUI[] Names = new TextMeshProUGUI[BattleRankboardDriver.CellCount];
            public readonly TextMeshProUGUI[] Kills = new TextMeshProUGUI[BattleRankboardDriver.CellCount];
            public readonly GameObject[] Highlights = new GameObject[BattleRankboardDriver.CellCount];
        }

        private Board BuildBoard()
        {
            var board = new Board();
            for (int i = 0; i < BattleRankboardDriver.CellCount; i++)
            {
                GameObject cell = Scope.CreateGameObject("Cot_RankCell" + (i + 1));
                GameObject nameGo = Scope.CreateGameObject("Txt_Name", typeof(RectTransform), typeof(TextMeshProUGUI));
                GameObject killGo = Scope.CreateGameObject("Txt_Kills", typeof(RectTransform), typeof(TextMeshProUGUI));
                GameObject plate = Scope.CreateGameObject("Image_Highlight", typeof(RectTransform));
                nameGo.transform.SetParent(cell.transform, false);
                killGo.transform.SetParent(cell.transform, false);
                plate.transform.SetParent(cell.transform, false);
                plate.SetActive(false);
                board.CellRoots[i] = cell;
                board.Names[i] = nameGo.GetComponent<TextMeshProUGUI>();
                board.Kills[i] = killGo.GetComponent<TextMeshProUGUI>();
                board.Highlights[i] = plate;
            }
            return board;
        }

        private static long SpawnPlayer(SimWorldState world, int kills, int deaths = 0)
            => world.Spawn(new EntitySlot
            {
                Hp = 100,
                Pos = new SimVector3(0f, 0f, 0f),
                Flags = EntityFlags.Player,
                Kills = kills,
                Deaths = deaths,
            }, out _);

        private static List<PlayerMatchResult> FrozenRows(SimWorldState world)
        {
            var rows = new List<PlayerMatchResult>();
            EntitySlot[] entities = world.Entities;
            for (int slot = 0; slot < entities.Length; slot++)
            {
                ref EntitySlot e = ref entities[slot];
                if (!PlayerIdentity.IsPlayer(in e)) continue;
                rows.Add(new PlayerMatchResult
                {
                    PlayerId = rows.Count,
                    EntityId = e.Id,
                    Kills = e.Kills,
                    Deaths = e.Deaths,
                });
            }
            return rows;
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 局内排行榜与结算排行_同全序同名次()
        {
            var world = new SimWorldState { RngState = 7UL };
            long localId = SpawnPlayer(world, 2);      // 同分：EntityId 升序
            SpawnPlayer(world, 2);
            SpawnPlayer(world, 5);                     // 第一
            SpawnPlayer(world, 1);                     // 末位
            Board board = BuildBoard();
            var driver = new BattleRankboardDriver(new LiteGame.BattleReadModel(() => world, () => localId),
                () => localId, board.CellRoots, board.Names, board.Kills, board.Highlights);

            driver.Tick();
            ResultRanking.Row[] rows = ResultRanking.Build(FrozenRows(world));

            Assert.AreEqual(rows.Length, 4, "四名参赛玩家");
            // 独立期望序列：不调用 BattleRankOrder，钉死 EntityId 并列时的顺序（id1→id2，击杀同分）。
            Assert.AreEqual(5, rows[0].Kills);
            Assert.AreEqual(2, rows[1].Kills);
            Assert.AreEqual(2, rows[2].Kills);
            Assert.AreEqual(1, rows[3].Kills);
            Assert.AreEqual(2, rows[0].PlayerId);   // 玩家3（第三只 spawn、击杀 5）
            Assert.AreEqual(0, rows[1].PlayerId);   // 玩家1（id1，击杀 2）
            Assert.AreEqual(1, rows[2].PlayerId);   // 玩家2（id2，击杀 2）
            Assert.AreEqual(3, rows[3].PlayerId);   // 玩家4（击杀 1）
            Assert.AreEqual("玩家3", rows[0].Name);
            Assert.AreEqual("玩家1", rows[1].Name);
            Assert.AreEqual("玩家2", rows[2].Name);
            Assert.AreEqual("玩家4", rows[3].Name);
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(rows[i].Kills.ToString(), board.Kills[i].text,
                    $"第 {i + 1} 格的击杀与结算行一致（同一条全序）");
                Assert.AreEqual(rows[i].Name, board.Names[i].text,
                    $"第 {i + 1} 格的名字与结算行一致（身份序号单源）");
            }
            Assert.AreEqual(1, rows[0].Rank, "击杀最多者名次 1");
            Assert.AreEqual(2, rows[1].Rank, "同击杀两人并列名次 2");
            Assert.AreEqual(2, rows[2].Rank);
            Assert.AreEqual(4, rows[3].Rank, "末位名次 = 1 + 严格更高的三人");

            driver.Dispose();
        }
    }
}
