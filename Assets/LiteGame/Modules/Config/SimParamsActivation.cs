using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 判定参数覆写在**进房时点**的激活解析（纯函数：零引擎、无静态可变状态）。
    /// </summary>
    public static class SimParamsActivation
    {
        /// <summary>
        /// 解析本局生效数值：门关 / 无覆写 / 覆写对象缺省 ⇒ 表值原样；门开且有覆写 ⇒ 只重建判定几何 13 列。
        /// </summary>
        /// <param name="applyOverrides">门结果（<see cref="SimParamsGate.Evaluate"/>：本地测试模式 ∧ 本地测试房）。</param>
        /// <param name="table">表装载值（<c>IConfigService.TableCombatValues</c>，**不含覆写**）。</param>
        /// <param name="overrides">覆写实例（null = 该装配没有调参面，按无覆写处理）。</param>
        public static CombatValues Resolve(bool applyOverrides, in CombatValues table, SimParamsOverrides overrides)
        {
            if (!applyOverrides || overrides == null || !overrides.HasAny) return table;
            return overrides.TryApply(table, out CombatValues merged) ? merged : table;
        }
    }
}
