using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 候选 lua/ 段只读来源（H4-3）：已确认候选且整套脚本可读时，LuaPreloader 用候选字节
    /// 替换 YooAsset tag lua 清单；无候选时保持原路径。
    ///
    /// key = require 缓存键（lua/UI/Page_Login.lua → UI/Page_Login）；全量契约：任何一张
    /// .lua 读不到/为空 → false，不允许 partial overlay。
    /// </summary>
    public interface ICandidateLuaSource
    {
        bool IsActive { get; }
        bool TryLoadAll(out IReadOnlyDictionary<string, byte[]> scripts, out string problem);
    }
}
