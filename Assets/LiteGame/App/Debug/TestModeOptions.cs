#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame
{
    /// <summary>
    /// 测试模式开关的**统一描述表**（开发/编辑器/开发包专用；release 整段剥离）。
    /// 单一写点：GM 面板渲染、运行时快照、DevStartupConfig 进房初值都经 <see cref="Get"/>/<see cref="Set"/>——
    /// 新增一个开关 = 本表加一行 + 运行时字段（+ 必要时的消费者读取），面板零改动；
    /// 显示名只有这里一份（面板勾选框与状态行不再各写一套）。
    ///
    /// 分组：<see cref="Group.Startup"/> = 进房生效（DevStartupConfig 序列化承载初值）；
    /// <see cref="Group.Live"/> = 局内可调（GM 面板现场开合，即时生效）。
    /// 副作用（如无限子弹要同步 Sim 静态、受测试模式门约束）一律在 <see cref="Set"/> 内收口。
    /// **默认值与 <see cref="TestModeRuntime"/> 字段初始值同口径**（字段初始值是"未套默认前的初态"）。
    /// </summary>
    public static class TestModeOptions
    {
        /// <summary>开关标识（类型安全——面板/快照不写字符串）。</summary>
        public enum Id
        {
            NoDeath,
            InfiniteAmmo,
            LaserSight,
            DamageNumbers,
            AutoHeadshot,
            DrawHeadshotDebug,
            TeleportEnabled,
            BotFrozen,
            BotInfiniteHp,
        }

        /// <summary>生效方式分组。</summary>
        public enum Group
        {
            /// <summary>进房生效：进入测试模式时从场景 DevStartupConfig 快照进运行时。</summary>
            Startup,
            /// <summary>局内可调：GM 面板现场开合，即时生效。</summary>
            Live,
        }

        /// <summary>选项描述（面板按 <see cref="All"/> 顺序渲染，不硬编码清单）。</summary>
        public readonly struct Info
        {
            public readonly Id Id;
            public readonly string Label;
            public readonly Group Group;
            public readonly bool Default;

            public Info(Id id, string label, Group group, bool @default)
            {
                Id = id;
                Label = label;
                Group = group;
                Default = @default;
            }
        }

        /// <summary>选项清单（顺序即面板显示顺序）。</summary>
        public static readonly Info[] All =
        {
            new Info(Id.NoDeath, "全房免死（Hp 保底 1、目标不消失）", Group.Startup, true),
            new Info(Id.InfiniteAmmo, "无限子弹（开火不扣弹匣、空仓也可开火、不触发末发自动换弹；Sim 规则两端同源）", Group.Live, false),
            new Info(Id.LaserSight, "瞄准激光（测试房内显示；非测试模式按武器支持位）", Group.Live, true),
            new Info(Id.DamageNumbers, "伤害数字（测试房内飘字；非测试模式恒显）", Group.Live, true),
            new Info(Id.AutoHeadshot, "自动爆头（替换输入源：锁最近敌人头部带 + 持续开火，端到端验爆头链）", Group.Live, false),
            new Info(Id.DrawHeadshotDebug, "爆头区域可视化（服务端判定圆柱 + 爆头带）", Group.Live, false),
            new Info(Id.TeleportEnabled, "定点传送（T 键 / 面板按钮 → 准心点）", Group.Startup, true),
            new Info(Id.BotFrozen, "bot 冻结（权威侧位置每帧回写）", Group.Startup, false),
            new Info(Id.BotInfiniteHp, "bot 无限生命（权威侧每帧回满——假人永不死；命中/飘字反馈照常）", Group.Live, false),
        };

        /// <summary>读当前值（运行时快照）。</summary>
        public static bool Get(Id id)
        {
            switch (id)
            {
                case Id.NoDeath: return TestModeRuntime.NoDeath;
                case Id.InfiniteAmmo: return TestModeRuntime.InfiniteAmmo;
                case Id.LaserSight: return TestModeRuntime.LaserSight;
                case Id.DamageNumbers: return TestModeRuntime.DamageNumbers;
                case Id.AutoHeadshot: return TestModeRuntime.AutoHeadshot;
                case Id.DrawHeadshotDebug: return TestModeRuntime.DrawHeadshotDebug;
                case Id.TeleportEnabled: return TestModeRuntime.TeleportEnabled;
                case Id.BotFrozen: return TestModeRuntime.BotFrozen;
                case Id.BotInfiniteHp: return TestModeRuntime.BotInfiniteHp;
                default: return false;
            }
        }

        /// <summary>写当前值（**单一写点**：副作用在此收口）。</summary>
        public static void Set(Id id, bool value)
        {
            switch (id)
            {
                case Id.NoDeath: TestModeRuntime.NoDeath = value; break;
                case Id.InfiniteAmmo: TestModeRuntime.InfiniteAmmo = value; break;
                case Id.LaserSight: TestModeRuntime.LaserSight = value; break;
                case Id.DamageNumbers: TestModeRuntime.DamageNumbers = value; break;
                case Id.AutoHeadshot: TestModeRuntime.AutoHeadshot = value; break;
                case Id.DrawHeadshotDebug: TestModeRuntime.DrawHeadshotDebug = value; break;
                case Id.TeleportEnabled: TestModeRuntime.TeleportEnabled = value; break;
                case Id.BotFrozen: TestModeRuntime.BotFrozen = value; break;
                case Id.BotInfiniteHp: TestModeRuntime.BotInfiniteHp = value; break;
            }

            // 副作用：无限子弹写**当前活动局**的规则持有件（客户端预测与本地服同实例——同进程两端同值；
            // 面板写值落在渲染帧之间，不会在 tick 中途换口径）。非测试模式一律不武装；无活动局（未进对局）
            // 时只记 TestModeRuntime 初值，下次进房生效。
            if (id == Id.InfiniteAmmo && TestModeRuntime.Active && LiteSim.SimTestRules.ActiveMatch != null)
                LiteSim.SimTestRules.ActiveMatch.InfiniteAmmo = value;

            // bot 无限生命（Live）：同步写宿主钩子（本地服宿主循环读取——同帧起效；进房镜像兜底在
            // ProcedureMatch）。非测试模式一律不武装（同 InfiniteAmmo 口径）。
            if (id == Id.BotInfiniteHp)
                LiteSim.SimTestRules.BotInfiniteHp = TestModeRuntime.Active && value;
        }

        /// <summary>套本表默认口径（<see cref="TestModeRuntime.ApplyDefaults"/> 的数据源）。</summary>
        public static void ApplyDefaults()
        {
            for (int i = 0; i < All.Length; i++) Set(All[i].Id, All[i].Default);
        }
    }
}
#endif