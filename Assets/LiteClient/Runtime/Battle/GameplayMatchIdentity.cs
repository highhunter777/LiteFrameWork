using LiteFramework;
using LiteSim;

namespace LiteClient
{
    /// <summary>
    /// 本局玩法数据身份实现：基摘要来自本地装载产物，规则位按本局实例计算。
    /// 唯一允许直接调用 <see cref="CombatConfigDigest"/> 的运行时件（身份守卫允许面）。
    /// </summary>
    public readonly struct GameplayMatchIdentity : IMatchIdentity
    {
        public uint ConfigDigestBase { get; }

        public GameplayMatchIdentity(uint configDigestBase)
        {
            ConfigDigestBase = configDigestBase;
        }

        public uint Compute(bool noDeath, bool infiniteAmmo)
            => CombatConfigDigest.WithTestRules(ConfigDigestBase, noDeath, infiniteAmmo);
    }
}
