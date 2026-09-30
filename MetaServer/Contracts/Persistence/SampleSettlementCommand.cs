using System.Collections.Generic;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 简单测试命令（《框架先行建设与业务接入专项设计》样例⑤：
    /// "简单测试命令 → 实际持久化确认 → 重复请求/故障 → 进程恢复"）。
    ///
    /// 刻意取结算形状（operationId + (playerId, matchId, settlementType) + revision CAS + 增量）：
    /// 这是服务端总设计 §11.3 钦定的唯一键形状，也是排空第 4 步"刷新 Outbox 到持久介质"
    /// 要消费的接缝。**非业务模型**——具体库存/奖励语义不在此冻结（《框架先行》§5-5），
    /// Auth/Lobby/Profile 归 G3（Meta 专项 §15）。
    /// </summary>
    public sealed record SampleSettlementCommand(
        string OperationId, string PlayerId, string MatchId, string SettlementType,
        long ExpectedRevision, long Delta)
    {
        /// <summary>单字段长度上界——服务端侧"解析后限制长度"（§P0-3）的同款纪律。</summary>
        public const int MaxFieldLength = 64;

        /// <summary>
        /// 形状边界（落库前拒绝）。返回全部违规项、一次报全——与 <c>MetaConfig.Validate</c> 同款口径。
        /// 空列表 = 通过。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            ValidateField(errors, nameof(OperationId), OperationId);
            ValidateField(errors, nameof(PlayerId), PlayerId);
            ValidateField(errors, nameof(MatchId), MatchId);
            ValidateField(errors, nameof(SettlementType), SettlementType);

            if (ExpectedRevision < 0)
            {
                errors.Add("ExpectedRevision 不得为负：" + ExpectedRevision);
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
}
