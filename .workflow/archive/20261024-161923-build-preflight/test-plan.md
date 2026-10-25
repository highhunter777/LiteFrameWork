# 测试计划：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24

## 测试策略

- Python unittest（`scripts/tests/test_build_plan.py`），覆盖纯分类内核、漂移解析、CLI 契约与只读行为。
- 不依赖 Unity、不依赖真实 git 历史：分类用 `--changed-file`/纯函数；git 采集用真实仓库的 `--base HEAD` 只读用例。
- AC 映射：AC1/AC3/AC7/AC9 由分类与矩阵用例覆盖；AC2/AC4 由 shared/unknown/漂移用例覆盖；AC5 由 JSON 键集用例；AC6 由只读 + 清单/文档门禁；AC8 由全量门禁。

## 用例清单

1. 无改动 → 不需要出包（AC1）。
2. shared 目录/源链接件/`MapTableProjection` → 重算哈希 + 两端 + 发布（AC2/AC4）。
3. proto/Luban → 生成 + 两端 + 内容 + 发布（AC3）。
4. 服务端宿主/Meta/房间内核 → 重建/发布；房间内核不改客户端（AC3/AC7）。
5. 客户端 C#/内容资源（Art/Animation/Lua/Prefab/Scene/Config）/Editor/文档脚本/配置发布（AC3/AC7）。
6. 未知路径保守升级 + `unknownPaths`（AC4）。
7. 规则优先级冲突（LiteSim Editor、Lua、Editor 代码）。
8. 矩阵 6 行固定用例（AC7）。
9. `--json` schema 版本与键集（AC5）。
10. 路径归一/去重（AC9）。
11. 漂移真实 csproj 已知例外 + 构造文本注入未知单文件/通配（AC4）。
12. CLI help/base 失败/`--changed-file`/跨仓库 fail-fast/只读工作树（AC6/AC9）。

## 预期失败证据

- `wf verify --red` → `evidence/red.json`：`build-plan-red` exit=1（686 ms）。
- 骨架阶段：25 failures + 1 error（首轮骨架）（空 findings 导致一处 IndexError，非导入/环境故障）；实现后应全绿。
- 红因：分类/动作/JSON/漂移尚未实现，断言失败。

## 运行方式

```bash
python -m unittest discover -s scripts/tests -p test_build_plan.py -v
python scripts/build/build-plan.py --base HEAD --json
python scripts/toolchain.py check
python scripts/gate/test.py --lane L1 --profile PullRequest
```
