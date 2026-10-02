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

        /// <summary>对局会话（`ProcedureId.Match` 产出 → `Battle` 消费——跨阶段迁移数据，非服务定位器：
        /// 实例由 Match 阶段创建、随迁移移交所有权，Battle 离场时负责其 Scope 收尾）。</summary>
        public readonly BattleClient BattleClient;

        /// <summary>Account 作用域（与 <see cref="BattleClient"/> 一同移交；Battle 离场 Dispose——关闭序
        /// BattleContext.Dispose → Match Scope → Account Scope，§6.2）。</summary>
        public readonly ClientScope AccountScope;

        /// <summary>测试房意图（`Main` F10 → `Match` 读）：强制本地服 + 房号 Room-Test + 全房免死
        /// （<see cref="LiteSim.SimTestRules.NoDeath"/>）——开发/测试专用，正式包无该入口。</summary>
        public readonly bool TestRoom;

        public ProcedureArgs(Exception error = null, BattleClient battleClient = null, ClientScope accountScope = null,
            bool testRoom = false)
        {
            Error = error;
            BattleClient = battleClient;
            AccountScope = accountScope;
            TestRoom = testRoom;
        }
    }
}
