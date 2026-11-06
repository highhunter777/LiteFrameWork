namespace LiteSim
{
    /// <summary>
    /// 武器槽状态机（《游戏业务系统总设计》§5.2）。P0 只冻结契约与枚举值；
    /// 状态推进（射速节拍/换弹/切枪）归 P1 WeaponSystem——本枚举同时是线上私有快照的 state 字段取值域。
    /// </summary>
    public enum WeaponSlotState : byte
    {
        Unequipped = 0,
        Ready = 1,
        Reloading = 2,
        Switching = 3,
    }
}
