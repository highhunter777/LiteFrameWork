namespace LiteClient
{
    /// <summary>
    /// 表数据目录布局单源（客户端与服务端**源链接共编**——RoomServer.csproj/测试工程 Compile Include 本文件）：
    /// 领域分类目录（《资源收集目录专项设计》§5.1）下的表清单与位置。
    ///
    /// **单源纪律**：gen_luban.py 解析本文件的 <see cref="Files"/> 做 bin 产物的分域归位（生成器引用运行时常量）；
    /// 客户端 <see cref="ConfigService"/> 与服务端 <see cref="RoomServer.CombatNumbers"/> 的装载路径都从本类取——
    /// 新增表 = 在 <see cref="Files"/> 加一行"领域/表名"，再跑 gen.bat + gen-build-hash.py。
    /// </summary>
    public static class TableLayout
    {
        /// <summary>表数据收集根目录（YooAsset location 前缀；跨平台恒为 Assets 路径）。</summary>
        public const string DataDir = "Assets/GameData/Config/";

        /// <summary>表数据文件清单（**领域相对路径**：领域子目录/表名，不带扩展名）——
        /// 领域即 <see cref="DataDir"/> 下的一级子目录，YooAsset PackDirectory 下每域自成一包（改一域只重发该域）。</summary>
        public static readonly string[] Files =
        {
            "Item/tbitemconfig",       // 道具表（类型 + 刷新/拾取/携带/使用 + 各类型效果数值）
            "Movement/tbmovementconfig", // 移动数值（单行表；装载后产出 MovementValues 实例并发布读口）
            "Entity/tbentityconfig",   // 实体定义（默认角色出生 HP；装载后投影到固定对局配置）
            "UI/tbuiform",
            "Content/tbcontententry",
            "Strategy/tbstrategy",
            "Combat/tbcombatnum",      // 玩法数值（单行表；装载后产出 CombatValues 实例并发布读口——两端同源）
            "Combat/tbweapon",         // 武器表（多行；装载后回填 WeaponConfig——Sim 武器系统消费）
            "Animation/tbanimationprofile", // 动画 Profile 登记行（多族共存；装载后投影为 Core 行并发布读口）
        };

        /// <summary>表数据文件完整 location（<see cref="DataDir"/> + file + ".bytes" 拼接口径单源）——
        /// 装载（<see cref="ConfigService"/>）、服务端直读与编辑器产物核对共用，禁第二份拼接。</summary>
        public static string TableDataPath(string file) => DataDir + file + ".bytes";
    }
}
