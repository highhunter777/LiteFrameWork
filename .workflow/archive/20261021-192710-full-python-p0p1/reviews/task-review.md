# 任务评审：全量 Python P0/P1

> 运行 full-python-p0p1 · 创建于 2026-10-21

## 评审输入

- 方案：`plan.md`
- 任务清单：`tasks.md`
- 设计权威：`Docs/设计文档/架构/脚本形态统一专项设计.md` v0.2 §6（P0–P5）
- 评审范围：本次运行只覆盖 P0 地基 + P1（hooks / L0）；P2–P5 明确不在范围。
- 评审依据：实际读取 hook 脚本与两个客户端配置、L0 全文、CI/构建器/清单/文档引用面；确认两个 P1 脚本无 Unity/云侧依赖，调用面均在仓库内。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 地基 | T1 | 是 | `_common.py` + `test_common.py`；只标准库 |
| AC2 Hook | T2 | 是 | 脚本 + 双客户端配置 + 四类测试 + 删旧 PS |
| AC3 L0 | T3 | 是 | 脚本 + 四类违规夹具 + 删旧 PS + 注册面 |
| AC4 注册面 | T3、T4 | 是 | 清单/fallback/build-player/CI 在 T3；文档/AGENTS/skills 在 T4 |
| AC5 旧路径清零 | T4、T5 | 是 | grep 旧路径（历史施工卷除外）+ 文件不存在断言 |
| AC6 行为等价 | T2、T3、T5 | 是 | hook 消息逐条；L0 检查项/排除/退出码；L1 |
| AC7 门禁 | T5 | 是 | Python 测试、L0、toolchain、docs、Doc、L1、BuildHash |
| AC8 范围 | T5 | 是 | 只改 scripts/Docs/AGENTS/CI/hook/构建器命令 |

## 检查项

- [x] 任务粒度可独立执行与验证：T1/T2/T3 各自可单独跑测试；T4/T5 收口。
- [x] 依赖关系无循环，顺序合理：T1 → T2/T3 → T4 → T5。
- [x] 每项任务有明确验收标准与验证方式。
- [x] 测试策略足以覆盖验收标准：L0 四类违规夹具、hook 四类输入、注册面 check。
- [x] 通用性与扩展性要求已落到任务：`_common.py` 只放四个原语；后续 P2–P5 复用。
- [x] 不存在硬编码、特判式补丁或范围外改动：L0 排除名单单处；不迁移 P2+ 脚本。
- [x] 回滚与风险处理已安排：恢复旧 PS/配置即可；`_common.py` 无消费者可删。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 次要 | hook 位于 `scripts/hooks/`，若直接 `import _common` 会因 sys.path 不含 `scripts/` 失败 | T2 实现时加 `sys.path.insert(0, scripts_dir)`；测试覆盖直接执行 | 已明确 |
| R-2 | 次要 | L0 删除后，CI、`build-player.ps1`、清单、`toolchain.py` fallback 必须同批切换，否则运行期断链 | T3 文件清单已列全部注册面；T5 再跑 `check` 与 L0 | 已明确 |
| R-3 | 次要 | `toolchain.py check` 只校验路径存在，不校验 CI 命令的参数风格（`-ProjectPath` vs `--project-path`） | T3 同批改 CI/build-player 命令；T5 grep 旧参数 | 已明确 |
| R-4 | 信息 | 历史施工卷仍会命中旧 `.ps1` 路径，不能当现行引用 | T4 grep 明确排除 `Docs/施工进度` 与归档；设计/进度只回写现状 | 已明确 |

无阻塞问题。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
