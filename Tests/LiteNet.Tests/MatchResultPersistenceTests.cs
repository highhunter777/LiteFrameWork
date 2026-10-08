using System;
using System.IO;
using LiteSim;
using LiteTesting;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MatchResultPersistenceTests
    {
        [Fact]
        public void FrozenScoresAndWinnerSurviveJournalRestartAndCompaction()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litegame_match_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "settlement.jsonl");
            try
            {
                var result = new MatchResultSummary("match-rules", 42, 120, ShutdownReason.KillLimit,
                    new[] { 0, 1 }, 65536L, MatchEndReason.KillLimit,
                    new[] { new PlayerMatchResult(0, 65536L, 3, 1), new PlayerMatchResult(1, 65537L, 1, 3) });
                using (var outbox = FileSettlementOutbox.Open(path, 2))
                {
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(result));
                    Assert.Equal(SettlementOutboxResult.Duplicate, outbox.Enqueue(result));
                    Assert.Equal(SettlementOutboxResult.Appended, outbox.Enqueue(
                        new MatchResultSummary("completed", 1, 1, ShutdownReason.Operator, Array.Empty<int>())));
                    Assert.True(outbox.TryMarkCompleted("completed"));
                    outbox.Flush(); // 压实也必须保留新结果字段
                }
                using (var reopened = FileSettlementOutbox.Open(path, 2))
                {
                    MatchResultSummary saved = Assert.Single(reopened.ListPending());
                    Assert.Equal(65536L, saved.WinnerEntityId);
                    Assert.Equal(MatchEndReason.KillLimit, saved.GameplayEndReason);
                    Assert.Equal(3, saved.Players[0].Kills);
                    Assert.Equal(3, saved.Players[1].Deaths);
                    Assert.Equal(new[] { 0, 1 }, saved.SeatPlayerIds);
                    Assert.Equal(SettlementOutboxResult.Duplicate, reopened.Enqueue(result));
                }
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void OldJournalRowsRemainReadable()
        {
            string path = Path.Combine(Path.GetTempPath(), "litegame_old_match_" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                File.WriteAllText(path, "{\"Kind\":\"s\",\"MatchId\":\"old\",\"Seed\":42,\"FinalFrame\":100,"
                    + "\"EndReason\":1,\"SeatPlayerIds\":[0,1]}\n");
                using var outbox = FileSettlementOutbox.Open(path, 2);
                MatchResultSummary saved = Assert.Single(outbox.ListPending());
                Assert.Equal("old", saved.MatchId);
                Assert.Empty(saved.Players);
                Assert.Equal(0L, saved.WinnerEntityId);
                Assert.Equal(MatchEndReason.None, saved.GameplayEndReason);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void NullPlayerInCorruptRowDoesNotPreventValidResultRecovery()
        {
            string path = Path.Combine(Path.GetTempPath(), "litegame_corrupt_match_" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                File.WriteAllText(path, "{\"MatchId\":\"broken\",\"Players\":[null]}\n{\"MatchId\":\"valid\"}\n");
                using var outbox = FileSettlementOutbox.Open(path, 2);
                Assert.Equal(1, outbox.SkippedCorruptLines);
                Assert.Equal("valid", Assert.Single(outbox.ListPending()).MatchId);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
