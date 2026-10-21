# 代码评审：全量 Python P4

> 运行 full-python-p4 · 创建于 2026-10-21

## 变更范围

- 新增 `build-player.py`、`player-smoke.py` 与离线测试；删除两 PS。
- 清单/CI/toolchain build/工具索引/现行文档/skills 切 Python。
- 范围：scripts/Docs/AGENTS/.github；无 Assets/Server 改动。

## 正确性与边界

- build-player：门禁/Profile/Pipeline target+backend+scenes/deploy.args/bundles/dry-run/confirm/轮询/产物新鲜度逐段移植；Pipeline Result 从 tab-field 3 提取；eval_file 写 UTF-8 无 BOM。
- player-smoke：清旧进程/log、启动 exe、literal 标记、MemLoop/InputModal 断言、超时/早退处理。
- 离线验证：help、缺 profile、缺 exe；完整构建/冒烟未跑，不宣称通过。
- 注册面：CI/清单/toolchain/索引/文档一致。

## 测试充分性

- 红测 5；最终 Python 91/91；L1 1801、Doc 0/0、L2 meta/restore/L0。
- 未覆盖：真实 Pipeline 输出、完整 IL2CPP 构建、真实 Player.log/标记/内存断言；机器侧待办。
- 测试未迎合实现：缺 profile/exe 走真实失败路径。

## 通用性与扩展性

- 复用 `_common`；Pipeline 封装集中 build-player；smoke 标记/断言集中；Profile 仍是唯一配置源。
- 未引入构建框架/第二套参数表；P5 可复用相同迁移模式。
- 未硬编码 Unity 路径（build 用 `unity` CLI）；smoke 从 ProjectSettings 推导 Player.log。

## 安全与性能

- 构建链保持 restore/L0/buildHash 门禁不可跳过；产物新鲜度防静默 no-op。
- smoke 终止 Player 进程；超时/早退有界；日志只读。
- 未真实构建，性能/耗时未测。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 信息 | build/smoke | 完整 IL2CPP/Player 未跑 | 本机无 Unity/Player；机器侧验收待办 | 遗留（非代码阻塞） |
| C-2 | 次要 | test_toolchain | 批量替换误把“旧 PS 不存在”断言改成 `.py` 不存在 | 恢复 `.ps1` 断言；91/91 | 已修复 |
| C-3 | 信息 | 文档 | Python 行号引用随迁移失效 | 去掉行号只留路径；doc-lint 0/0 | 已修复 |
| C-4 | 信息 | smoke | 依赖 Windows `taskkill`/Player.log 路径 | 与 PS 平台约束一致；非 Windows 不在验收范围 | 接受 |

无阻塞代码问题；完整构建/冒烟是明确机器侧待办。

## 复测记录

- `workflow.py verify` → 9/9 PASS（L1 1801、Doc、restore、L0、Python 91/91、check 0 error/1 warning、docs current、Doc lane、L2 meta）。
- 附加：build/smoke help/缺 profile/exe；CI YAML OK；BuildHash OK。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
