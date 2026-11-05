namespace LiteGame.Modules.Shared
{
    /// <summary>
    /// 战斗视图资源标识（共享常量面，零引擎）：跨域共用的资源路径**单源**——
    /// 内容段健康检查（Framework）、内存循环探针（App）与战斗视图装配（Battle）读同一份。
    ///
    /// 为什么不放 Core / 不放 Battle：Core 的准入是"跨域契约 + 纯计算"（见 说明 14 章程），常量归共享常量面；
    /// 而单个域（Battle）持有路径会让内容段为了一条 prefab 路径反向认识战斗域类型。
    /// </summary>
    public static class BattleViewPaths
    {
        /// <summary>对局实体视图 prefab（本体住 `Assets/LiteGame/Modules/Battle/Content/Characters/`，收集组须覆盖）。</summary>
        public const string DefaultEntityPrefab = "Assets/LiteGame/Modules/Battle/Content/Characters/Player(Rifle).prefab";
    }
}
