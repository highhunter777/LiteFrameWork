using System.Collections.Generic;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 候选 config/ 段只读来源（《热更与内容发布专项设计》§8/§10）。
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
