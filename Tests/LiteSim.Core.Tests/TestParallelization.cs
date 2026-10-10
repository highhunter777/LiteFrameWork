using Xunit;

// Sim 用例共享 SimTestRules 全局静态（NoDeath/InfiniteAmmo 由用例写、DamageSystem/WeaponSystem 读）——
// 集合并行下"写方 try/finally 的 flag 窗口"会污染并行读者的 3000 帧确定性运行
// （实证：确定性_中途快照CopyTo 在车道环境约 1/6 偶发红；隔离跑恒绿）。
// 本项目内**串行执行集合**（秒级时间成本换确定性）；根治路径（标志位入世界/房间实例）
// 随测试模式规则表化另行评估，不在本处预动。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
