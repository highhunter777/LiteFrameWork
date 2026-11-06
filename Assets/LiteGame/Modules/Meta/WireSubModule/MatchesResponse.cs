using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>战绩响应：<c>{ matches: [ ... ] }</c>。</summary>
    public sealed class MatchesResponse
    {
        public List<MatchRecordInfo> Matches { get; set; }
    }
}
