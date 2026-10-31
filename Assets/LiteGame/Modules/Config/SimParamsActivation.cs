using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// 判定参数覆写在**进房时点**的激活解析（纯函数：零引擎、无静态可变状态）。
    ///
    /// **为什么需要它**：覆写只服务进程内本地服的调参试验——门（<see cref="SimParamsGate"/>）必须在
    /// "真的开一局本地测试房"那一刻求值，而不是装配期（装配期测试模式尚未开启，门恒假）。
    /// 本类把"门 + 合并"收成一个求值点：装配根每会话调用 <see cref="Resolve"/> 一次，结果同时用于
    /// 客户端预测读口（<see cref="CombatConfig.Publish"/>）与本地服房间数值——两端因此逐字段同值。
    ///
    /// 非本地测试房（联机/云端）<paramref name="applyOverrides"/> 恒 false ⇒ 结果就是表值：
    /// 覆写不会进入真服，也不会进任何摘要（`CombatConfigDigest` 只看判定面表字节与本局规则位）。
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
