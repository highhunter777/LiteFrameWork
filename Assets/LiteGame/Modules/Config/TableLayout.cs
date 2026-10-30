using System;
using System.Collections.Generic;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 表数据目录布局单源（客户端与服务端**源链接共编**——RoomServer.csproj/测试工程 Compile Include 本文件）：
    /// 领域分类目录（《资源收集目录专项设计》§5.1）下的表清单、判定面清单与位置。
    ///
    /// **单源纪律**：gen_luban.py 解析本文件的 <see cref="Files"/> 做 bin 产物的分域归位（生成器引用运行时常量）；
    /// 客户端 <see cref="ConfigService"/> 与服务端 <see cref="RoomServer.ServerTableLoader"/> 的装载路径都从本类取——
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
            "UI/tbltext",               // LText 文案表（key→zh/en；BindLocale 数据源——表现层，进 BuildHash 表现层排除）。
                                       // 表源 #ltext.xlsx：Luban 生成名派生自输入文件名，而 #text.xlsx 是其 L10N 保留名、不可用
            "UI/tbcrosshair",          // 准心调参表（按武器一行：静态满张/连发张开/后坐——纯 View 表现域，
                                       // 不进判定面摘要；消费＝App 装配层投影 CrosshairFeelCatalog）
            "Content/tbcontententry",
            "Strategy/tbstrategy",
            "Combat/tbcombatnum",      // 玩法数值（单行表；装载后产出 CombatValues 实例并发布读口——两端同源）
            "Combat/tbweapon",         // 武器表（多行；装载后回填 WeaponConfig——Sim 武器系统消费）
            "Animation/tbanimationprofile", // 动画 Profile 登记行（多族共存；装载后投影为 Core 行并发布读口）
            "Map/tbmap",               // 地图表（烘焙产物：障碍/出生/边界；判定面域——两端同源）
        };

        /// <summary>
        /// 判定面域（**玩法数据身份**——`CombatConfigDigest` 覆盖的表域单源）：
        /// 域内全部表进运行期摘要（见 <see cref="JudgmentFiles"/>），新增表**落域即入面**——不依赖第二份人工清单。
        /// 判据 = 该域的表参与判定（两端必须同值）：Combat（数值/武器）、Movement（移动数值）、Entity（实体定义）、
        /// Item（道具定义）、Map（地图几何——移动碰撞 + 射击遮挡）。**不计入**：UI/Content/Strategy/Animation——
        /// 表现与内容面（单端可变，热更面；`tbanimationprofile` 亦在此列——动画 Profile 属表现面，单端热更不换联机身份）。
        /// </summary>
        public static readonly string[] JudgmentDomains = { "Combat", "Movement", "Entity", "Item", "Map" };

        /// <summary>
        /// 判定面表清单（<see cref="Files"/> 声明序派生——**顺序即摘要规范的一部分**，两端同一份）：
        /// 装载后按 <see cref="ComputeGameplayDigestBase"/> 复算玩法数据身份（服务端出 `StartGame.ConfigHash`、
        /// 客户端装载后比对）。取数归各自 IO（客户端装载候选缓存 / 服务端直读盘），清单与顺序归本处。
        /// </summary>
        public static readonly string[] JudgmentFiles = DeriveJudgmentFiles();

        private static string[] DeriveJudgmentFiles()
        {
            var files = new List<string>();
            // 以 Files 的声明序遍历（不是以 JudgmentDomains 的序）——摘要顺序随表清单走，新增表只改 Files。
            foreach (string file in Files)
            {
                string domain = file.Substring(0, file.IndexOf('/'));
                for (int d = 0; d < JudgmentDomains.Length; d++)
                    if (string.Equals(domain, JudgmentDomains[d], StringComparison.Ordinal))
                    {
                        files.Add(file);
                        break;
                    }
            }
            if (files.Count == 0)
                throw new InvalidOperationException(
                    "判定面表清单为空——JudgmentDomains 与 Files 的领域名对不上（玩法数据身份摘要会退化为空集）");
            return files.ToArray();
        }

        /// <summary>
        /// 判定面表字节 → 玩法数据身份**基摘要**（两端共用：算法/覆盖面单源 = <c>CombatConfigDigest</c>）。
        /// 入参 = 装载缓存（**裸表名 → .bytes 字节**，两端同形：客户端 <see cref="ConfigService.LoadAsync"/>
        /// 预取缓存、服务端 `ServerTableLoader.LoadTableBytes` 预读缓存）——两端同一份文件必得同一摘要。
        /// 缺字节抛（清单/取数口径漂移＝静默退化的入口，宁 fail-fast）。
        /// </summary>
        public static uint ComputeGameplayDigestBase(IReadOnlyDictionary<string, byte[]> bytesByTableName)
        {
            if (bytesByTableName == null) throw new ArgumentNullException(nameof(bytesByTableName));
            string[] files = JudgmentFiles;
            var tables = new LiteSim.CombatConfigDigest.Entry[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                string name = TableName(files[i]);
                if (!bytesByTableName.TryGetValue(name, out byte[] bytes) || bytes == null)
                    throw new InvalidOperationException(
                        $"判定面表缺字节：{files[i]}——装载缓存的键/清单口径漂移（键应为 TableName 派生的裸表名）");
                tables[i] = new LiteSim.CombatConfigDigest.Entry(files[i], bytes);
            }
            return LiteSim.CombatConfigDigest.ComputeBase(tables);
        }

        /// <summary>表数据文件完整 location（<see cref="DataDir"/> + file + ".bytes" 拼接口径单源）——
        /// 装载（<see cref="ConfigService"/>）、服务端直读与编辑器产物核对共用，禁第二份拼接。</summary>
        public static string TableDataPath(string file) => DataDir + file + ".bytes";

        /// <summary>
        /// Luban 装载键（**裸表名**——生成物 <c>cfg.Tables</c> 构造按裸名逐表回调 loader，
        /// 见 Generated/Tables.cs）：从 <see cref="Files"/> 的领域相对路径取末段单源派生。
        /// 装载缓存的键一律用它（领域路径只作位置）；键/位置两侧对不上即 "key not present"。</summary>
        public static string TableName(string file)
        {
            int slash = file.LastIndexOf('/');
            return slash < 0 ? file : file.Substring(slash + 1);
        }
    }
}
