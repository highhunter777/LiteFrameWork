using LiteGame.View.DamageNumbers;

namespace LiteGame.View
{
    /// <summary>
    /// 样式解析单源接口（《命中反馈与伤害数字专项设计》§4.2）：**初版常量表实现**，样式编辑器批
    /// （《UI编辑器工具专项设计》§3）落地后换 token 来源——接口不变，消费者零改动。
    /// </summary>
    public interface IDamageNumberStyleResolver
    {
        /// <summary>值/角色/暴击 → 样式档。</summary>
        DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit);
    }

}
