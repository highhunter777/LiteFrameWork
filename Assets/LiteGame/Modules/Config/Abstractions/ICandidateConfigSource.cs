using System.Collections.Generic;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 候选 config/ 段只读来源（《热更与内容发布专项设计》§8/§10；H4 批次）。
    ///
    /// **职责边界**：只回答"已确认候选是否带 config 段"以及"全量表字节"，不做表反序列化、
    /// 不做校验、不写盘——建表与语义校验归 ConfigService/ConfigTableValidator。
    ///
    /// **全量契约**：config/ 段一旦存在，必须是 TableLayout.Files 全量；缺一张即失败。
    /// 这是 cfg.Tables 全表构造的硬前提（混用候选/内置字节会产生跨表不一致快照）。
    /// </summary>
    public interface ICandidateConfigSource
    {
        /// <summary>已确认候选（ReleaseId != builtin）且候选根下存在 config/ 目录。未确认时恒 false。</summary>
        bool HasConfigSegment { get; }

        /// <summary>
        /// 读取全量表字节。key = TableLayout.TableName(file)（Luban loader 的裸表名），
        /// value = 原始 .bytes。任何一张缺失/为空 → false 并给出可诊断 reason。
        /// </summary>
        bool TryReadAll(out IReadOnlyDictionary<string, byte[]> tables, out string problem);
    }
}
