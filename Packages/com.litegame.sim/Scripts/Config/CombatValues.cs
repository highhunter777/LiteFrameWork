namespace LiteSim
{
    /// <summary>
    /// 玩法数值实例（**技术债 #1 根治面**）：装载面 8 字段从"全局可变静态 + LoadFrom 回填" 改为**按值传递的实例**——装载链（服务端 `ServerTableLoader`／客户端 `ConfigS。）。
    /// </summary>
    public struct CombatValues
    {
        public float MoveSpeed;
        /// <summary>tbmovementconfig.gravity 投影；本局固定值，不在 combatnum 重复存储。</summary>
        public float Gravity;
        public float HitscanRange;
        public int BaseDamage;
        public int DamageSpread;
        /// <summary>tbentityconfig.initial_hp 投影；本局默认角色的出生生命。</summary>
        public int EntityHp;

        /// <summary>进房初值的免死规则（测试房；运行态经 <see cref="MatchTestRules"/> 注入——见该类注释）。
        /// 进联机身份摘要（终值规则位）与重放装载值；常规对局恒 false。</summary>
        public bool NoDeath;

        /// <summary>进房初值的无限子弹规则（测试房；同上）。常规对局恒 false。</summary>
        public bool InfiniteAmmo;

        /// <summary>
        /// 瞄准态（ADS）移动上限（m/s），表值 tb_combatnum.aim_move_speed。
        /// </summary>
        public float AimMoveSpeed;

        /// <summary>
        /// 射击窗长（逻辑帧数）＝开火态时间，表值 tb_combatnum.fire_stance_frames。
        /// </summary>
        public int FireStanceFrames;

        /// <summary>判定几何（`#combatgeom` 13 列投影）；经本实例随 `in CombatValues` 传递。</summary>
        public CombatGeometry Geometry;

        /// <summary>构造固定对局数值；重力与出生生命由各自所属表装配（测试规则初值恒 false——
        /// 测试房由入口经 <see cref="MatchTestRules"/> 叠加）。</summary>
        public CombatValues(float moveSpeed, float gravity, float hitscanRange,
            int baseDamage, int damageSpread, int entityHp,
            float aimMoveSpeed, int fireStanceFrames, in CombatGeometry geometry)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
            AimMoveSpeed = aimMoveSpeed;
            FireStanceFrames = fireStanceFrames;
            Geometry = geometry;
            NoDeath = false;
            InfiniteAmmo = false;
        }

        /// <summary>展示/测试兜底值（非权威）；运行期权威值来自表装载。</summary>
        public static CombatValues Default => new CombatValues(5f, MovementValues.Default.Gravity,
            100f, 25, 1, EntityValues.Default.InitialHp, 2.5f, 60, CombatGeometry.Default);
    }
}
