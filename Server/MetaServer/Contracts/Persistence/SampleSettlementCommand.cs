using System.Collections.Generic;
using MetaServer.Contracts.Validation;

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
        /// <summary>单字段长度上界——**别名**：上限表单源 <see cref="FieldLimits.BusinessKeyBytes"/>
        /// （P2：本记录此前按**字符数**判，与其它五处按 UTF-8 字节判的口径不一致——多字节字符下形同虚设）。</summary>
        public const int MaxFieldLength = FieldLimits.BusinessKeyBytes;

        /// <summary>
        /// 形状边界（落库前拒绝）。返回全部违规项、一次报全——与 <c>MetaConfig.Validate</c> 同款口径。
        /// 空列表 = 通过。
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            FieldValidation.Append(errors, nameof(OperationId), OperationId, MaxFieldLength);
            FieldValidation.Append(errors, nameof(PlayerId), PlayerId, MaxFieldLength);
            FieldValidation.Append(errors, nameof(MatchId), MatchId, MaxFieldLength);
            FieldValidation.Append(errors, nameof(SettlementType), SettlementType, MaxFieldLength);

            if (ExpectedRevision < 0)
            {
                errors.Add("ExpectedRevision 不得为负：" + ExpectedRevision);
            }

            return errors;
        }

    }
}
