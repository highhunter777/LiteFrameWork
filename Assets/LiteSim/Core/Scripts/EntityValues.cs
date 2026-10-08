namespace LiteSim
{
    /// <summary>实体定义数值。出生生命由实体表提供，装载后投影到固定对局配置。</summary>
    public struct EntityValues
    {
        public int InitialHp;

        public EntityValues(int initialHp)
        {
            InitialHp = initialHp;
        }

        /// <summary>默认角色定义的兜底值，必须与 tbentityconfig 的 id=1 行一致。</summary>
        public static EntityValues Default => new EntityValues(100);
    }
}
