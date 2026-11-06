using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>
    /// Meta HTTP 契约的**线形状**（双端唯一真相）：客户端 `LiteGame.App` 编译它。
    /// </summary>
    public sealed class MetaError
    {
        /// <summary>稳定机器可读错误码（客户端按它分支，不解析自由文本）。</summary>
        public string Code { get; set; }

        /// <summary>本地化消息键（客户端按键查表）。</summary>
        public string MessageKey { get; set; }

        /// <summary>非敏感参数（字段名/上限值等；绝不放令牌、字段原文或凭据）。</summary>
        public List<string> Args { get; set; }
    }
}
