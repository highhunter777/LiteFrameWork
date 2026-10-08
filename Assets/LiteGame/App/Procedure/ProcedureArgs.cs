using System;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 流程迁移 payload（通用状态机 `StageMachine&lt;ProcedureId, ProcedureArgs&gt;` 的 `TReq`）：
    /// **每次迁移随参数带**，编译期强类型。所有字段只读；每次 `Request` 覆盖上一次（last-wins 与迁移目标同源）。
    /// </summary>
    public readonly struct ProcedureArgs
    {
        /// <summary>失败原因（`ProcedureId.Error` 阶段读；其余阶段为 null）。</summary>
        public readonly Exception Error;

        /// <summary>Account 会话（`ProcedureId.Match` 产出 → `Battle` 消费——跨阶段移交所有权，
        /// Battle 离场时由 MatchSession/AccountSession 负责其域收尾）。</summary>
        public readonly AccountSession AccountSession;

        /// <summary>测试房意图（`Main` F10 → `Match` 读）：强制本地服 + 房号 Room-Test + 全房免死
        /// （<see cref="LiteSim.SimTestRules.NoDeath"/>）——开发/测试专用，正式包无该入口。</summary>
        public readonly bool TestRoom;

        public ProcedureArgs(Exception error = null, AccountSession accountSession = null,
            bool testRoom = false)
        {
            Error = error;
            AccountSession = accountSession;
            TestRoom = testRoom;
        }
    }
}
