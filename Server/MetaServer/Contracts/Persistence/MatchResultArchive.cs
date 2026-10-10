using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 结算提交的单玩家条目（《上云测试专项设计》§4 裁决点① "结算载荷补 SeatAccountIds"）：
    /// 账号映射随提交载荷携带——Meta 按账号可查（裁决点③）；RoomServer 侧由宿主从会话取得。
    /// </summary>
    public sealed record MatchResultPlayerEntry(string PlayerId, string AccountId, int Kills, int Deaths);

    /// <summary>
    /// 一局对局结果的**归档记录**（供 <see cref="IMatchResultArchive"/> 落库；《上云测试专项设计》
    /// §4 裁决点①③，由 Profile 结算用例从提交载荷映射——归档端口不与线 DTO 耦合）。
    /// **幂等键 = MatchId**（一局一条，唯一性由 _id 承载）。
    ///
    /// 形状边界（落库前拒绝，一次报全——与 <c>SampleSettlementCommand.Validate</c> 同款口径）：
    /// 字段非空且 ≤<see cref="MaxFieldLength"/>、玩家条目 1..<see cref="MaxPlayers"/>、
    /// 击杀/死亡/帧号非负、PlayerId 不重复、AccountId 非空（结算归属账号——缺账号的提交被
    /// 如实拒绝，不静默落一条无主账目）。
    /// </summary>
    public sealed record MatchResultArchiveEntry(
        string MatchId, long Seed, int FinalFrame, int EndReason, int GameplayEndReason, long WinnerEntityId,
        IReadOnlyList<MatchResultPlayerEntry> Players)
    {
        /// <summary>单字段长度上界（对齐服务端解析前限长口径 §P0-3）。</summary>
        public const int MaxFieldLength = 64;

        /// <summary>单局玩家条目上限（对齐房间容量口径）。</summary>
        public const int MaxPlayers = 64;

        /// <summary>形状边界（落库前拒绝）。返回全部违规项；空列表 = 通过。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            ValidateField(errors, nameof(MatchId), MatchId);
            if (Seed < 0) errors.Add("Seed 不得为负：" + Seed);
            if (FinalFrame < 0) errors.Add("FinalFrame 不得为负：" + FinalFrame);
            if (WinnerEntityId < 0) errors.Add("WinnerEntityId 不得为负：" + WinnerEntityId);

            if (Players == null || Players.Count == 0)
            {
                errors.Add("Players 不得为空");
            }
            else if (Players.Count > MaxPlayers)
            {
                errors.Add("Players 数量不得超 " + MaxPlayers + "，实际：" + Players.Count);
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < Players.Count; i++)
                {
                    MatchResultPlayerEntry p = Players[i];
                    if (p == null)
                    {
                        errors.Add("Players[" + i + "] 不得为空");
                        continue;
                    }
                    ValidateField(errors, "Players[" + i + "].PlayerId", p.PlayerId);
                    ValidateField(errors, "Players[" + i + "].AccountId", p.AccountId);
                    if (p.Kills < 0) errors.Add("Players[" + i + "].Kills 不得为负：" + p.Kills);
                    if (p.Deaths < 0) errors.Add("Players[" + i + "].Deaths 不得为负：" + p.Deaths);
                    if (!string.IsNullOrEmpty(p.PlayerId) && !seen.Add(p.PlayerId))
                        errors.Add("Players[" + i + "].PlayerId 重复：" + p.PlayerId);
                }
            }
            return errors;
        }

        private static void ValidateField(List<string> errors, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(name + " 不得为空");
            }
            else if (value.Length > MaxFieldLength)
            {
                errors.Add(name + " 长度不得超 " + MaxFieldLength + "，实际：" + value.Length);
            }
        }
    }

    /// <summary>首次落库快照（重复提交**原样返回**这一份，不重新计算——同账本口径）。</summary>
    public sealed record StoredMatchResult(string MatchId, DateTime FinishedUtc);

    /// <summary>账号维度查询视图（《上云测试》§4 裁决点③：matchId/击杀/结束原因/时间）。</summary>
    public sealed record AccountMatchResult(
        string MatchId, int Kills, int Deaths, int EndReason, int GameplayEndReason, DateTime FinishedUtc);

    /// <summary>落库结果（类型化两态；存储不可用以 <see cref="SettlementStoreUnavailableException"/> 表达）。</summary>
    public abstract record MatchResultStoreOutcome
    {
        /// <summary>首次落库成功。</summary>
        public sealed record Stored(StoredMatchResult Record) : MatchResultStoreOutcome;

        /// <summary>重复提交：携带**首次**落库的快照。</summary>
        public sealed record Duplicate(StoredMatchResult Record) : MatchResultStoreOutcome;
    }

    /// <summary>
    /// 对局结果归档端口（Meta 专项 §11.3 "Match Archive"；《上云测试》批A 的落库与可查面）。
    ///
    /// 实现约束（L1 以测试替身验证**语义**，L3 以真实存储验证**持久性与并发兜底**）：
    /// 1. **唯一性是幂等的最终实现**（一局一条，MatchId 唯一）——重复提交命中首次记录并返回原快照，
    ///    不得二次写入、不得报错给调用方；
    /// 2. <c>FinishedUtc</c> 由**存储端赋值**（服务端时钟），调用方不播种；
    /// 3. 存储不可达/提交失败抛 <see cref="SettlementStoreUnavailableException"/>（未确认语义，
    ///    重试安全——双维度幂等保证重试拿回同一份首次结果）。
    /// </summary>
    public interface IMatchResultArchive
    {
        /// <summary>落库一条对局结果（幂等键 = MatchId）。</summary>
        ValueTask<MatchResultStoreOutcome> StoreAsync(MatchResultArchiveEntry entry, CancellationToken ct);

        /// <summary>按账号列最近 <paramref name="limit"/> 局（完成时刻倒序）。</summary>
        ValueTask<IReadOnlyList<AccountMatchResult>> ListByAccountAsync(string accountId, int limit, CancellationToken ct);
    }
}
