using System;

namespace MetaServer
{
    /// <summary>
    /// 构建哈希锚点（形态沿用 <c>RoomServer.ServerHost.ServerBuildHash</c>）。
    ///
    /// **占位实现，非交付形态**：服务端总设计 §P0-5 要求 buildHash 是"代码和协议构建摘要"，
    /// 由 scripts/codegen/gen-build-hash.py 在构建期生成。MetaServer 的生成接线随 R2/G3 宿主扩展
    /// （与 RoomServer.Host 拆分同批）接入，届时替换本类。
    ///
    /// 本批不伪造一个看起来权威的哈希值：显式标注为未接线，避免下游误当作可用的版本身份
    /// （§20 完成定义第 1 条：不得把设计写成已实现）。
    /// </summary>
    public static class BuildHash
    {
        /// <summary>未接线标记。R2/G3 接入 gen-build-hash.py 后替换。</summary>
        public const string Value = "meta-0.0.0-unwired";
    }
}
