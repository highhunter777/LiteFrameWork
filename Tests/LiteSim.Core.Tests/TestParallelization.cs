using Xunit;

// LiteSim 用例串行执行的既有纪律（历史动因：SimTestRules 全局静态曾被用例写、Damage/WeaponSystem 读，
// 并行窗口污染确定性长跑——实证确定性_中途快照CopyTo 在车道环境约 1/6 偶发红）。测试规则已收敛为
// 每局注入持有件（MatchTestRules——静态污染面已拆除），但本程序集仍保留串行：确定性长跑对
// CPU 车道亲和敏感的其余诱因未逐一排除，放宽并行的收益（秒级）不值得冒回归风险。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
