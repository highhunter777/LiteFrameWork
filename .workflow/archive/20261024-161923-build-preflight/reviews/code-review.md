# 代码评审（独立子代理）：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 变更范围

- 新增 `scripts/build/build-plan.py`（只读 diff→双口径计划）、`scripts/tests/test_build_plan.py`（42 例）。
- `scripts/gate/toolchain.json` 新增 `build.plan`；工具索引重生成。
- 文档回写：`05-验证与门禁`、`11-联机测试流程`、施工进度第 34 章、客户端审计两条。
- 不修改 `build-player.py`、不改 BuildHash 源集/CI。

## 正确性与边界

- 两轮代码评审提出的问题已修：共享源链接删除/重命名、按工程漂移对账、未登记通配文本警告、`Assets/**/*.csproj` 双端重建、`.sln/.slnx`、`--changed-file` 忽略过滤、忽略计数/未跟踪用例。
- 最终独立复评：42/42 测试通过；`--base HEAD` exit 0；`toolchain check` OK；BuildHash 不变；AC1-AC9 均可判定。
- 边界如实：`MapTableProjection.cs` 仍为已知漂移（另批补登记）；preflight 只读 advisory、未接 build-player/CI。

## 测试充分性

- 42 例覆盖矩阵 6 行、双口径/去重、优先级冲突、shared 前缀与 Editor 排除、`.meta` 跟随、csproj 双端、按工程漂移、构造漂移注入、忽略/未跟踪、JSON、只读工作树、CLI 失败路径。
- 最终复评：测试充分性 PASS；剩余 minor（默认 drift 并集分支、`_actual_link_sets` 无独立桩用例、目录 `.meta`/LiteNet Diagnostics 噪声）不阻塞。

## 通用性与扩展性

- 通用性 PASS：复用 `gen-build-hash` 源集常量与 `collect()`，规则集中，无第二份源集清单；仅本仓路径约定。
- 扩展性 PASS：`AREA_RULES` + `ACTIONS_ORDER` 双表，新增面只需加 area/规则；增长点已在 minor 中记录。

## 安全与性能

- 只读、零写盘；`git -c core.quotepath=false` 防中文路径转义；不调 Unity/网络；无敏感输出。

## 发现的问题与处理

| 轮次 | 问题 | 处理 | 状态 |
|---|---|---|---|
| 1 | 内容重打包、Packages/deploy 误判、矩阵不可数据驱动 | 修订方案 2：双口径 + 规则补充 + 固定用例 | 已修 |
| 2 | 房间内核客户端重打包、美术资源 unknown、漂移通配、规则重叠 | 修订方案 3-4 | 已修 |
| 3 | meta 吞并服务端、发布动作缺失、漂移跨 csproj | 修订方案 5-6 | 已修 |
| 代码 1 | 共享链接删除/重命名、按工程漂移、文本警告、csproj 误判 | 实际链接并入、按工程注册集、文本渲染、双端 csproj | 已修 |
| 代码 2 | 漂移用例区分度、重命名闭环、AC9 用例、`.sln`/changed-file | 新增 4 例 + 规则修正 | 已修 |

## 复测记录

- 最终复评子代理：独立 Explore 子代理（2026-10-24，第三轮）
- 复评命令与结果：`python -m unittest ... test_build_plan.py -v` → 42/42；`python scripts/build/build-plan.py --base HEAD` → exit 0；`toolchain.py check` → OK；`--check-shared` → `0a998c8ac6c9de89` 不变。
- 复评结论：PASS（0 blocker/major；minor 不阻塞）。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 独立 Explore 子代理（第三轮）
- 评审时间: 2026-10-24
