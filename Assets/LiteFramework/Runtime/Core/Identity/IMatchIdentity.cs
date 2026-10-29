namespace LiteFramework
{
    /// <summary>
    /// 本局玩法数据身份口：基摘要由装配层提供，规则位由对局上下文按本局实例计算。
    /// 实现方是唯一允许直接调用 CombatConfigDigest 的运行时件。
    /// </summary>
    public interface IMatchIdentity
    {
        uint ConfigDigestBase { get; }
        uint Compute(bool noDeath, bool infiniteAmmo);
    }
}
