# 任务评审：全量 Python P2

> 运行 full-python-p2 · 创建于 2026-10-21

## 评审输入

- 方案：`plan.md`
- 任务清单：`tasks.md`
- 设计权威：`Docs/设计文档/架构/脚本形态统一专项设计.md` v0.2 §6（P2）
- 评审范围：`_local_cdn`、gen-candidate、publish-candidate、serve-cdn、restore-check；P3–P5 不在范围。
- 评审依据：读取五个 PS 全文、现有 restore 基线（PASS，.NET 9 WARN）、清单/构建器/CI/文档引用面、Python `cryptography/openpyxl` 可用性。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 配置 | T1 | 是 | 合法/相对/缺文件/缺字段；serve/publish 共用 |
| AC2 签名 | T2 | 是 | 规范 manifest/转义/前缀/排序/空目录/XML 私钥/锚验签 |
| AC3 发布 | T3 | 是 | 清理/布局/bundle/信封/失败透传 |
| AC4 服务 | T4 | 是 | 配置覆盖/HTTP GET/缺目录 |
| AC5 环境 | T5 | 是 | repo PASS/空项目 blocking/cryptography |
| AC6 注册面 | T6 | 是 | 四 command/toolchain fallback/build-player/文档/旧 PS |
| AC7 门禁 | T7 | 是 | Python 全测/restore/check/docs/Doc/L1/BuildHash |
| AC8 范围 | T7 | 是 | 不真实发布/云侧；不改 Unity/共享源集 |

## 检查项

- [x] 任务粒度可独立执行与验证：五个脚本各自测试；T6/T7 收口。
- [x] 依赖关系无循环：T1 → T3/T4；T2 → T3；T5 独立；T6/T7 收口。
- [x] 每项任务有明确验收与验证命令。
- [x] 测试策略覆盖失败路径：空目录/缺字段/缺目录/空项目/HTTP GET/独立验签。
- [x] 通用性与扩展性：`_common`/`_local_cdn` 复用；签名逻辑单处。
- [x] 无硬编码/特判式补丁：测试 key/anchors/目录/端口均临时生成；不读真实私钥。
- [x] 回滚与风险处理：保留旧 PS 一版可回退；无数据迁移。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 次要 | .NET XML RSA 私钥需转换为 `cryptography` 数字；测试不能依赖真实签名机 | T2 测试用临时生成 RSA 并写等价 XML + anchors；生产仍读默认仓库外私钥 | 已明确 |
| R-2 | 次要 | restore 当前有 `.NET SDK 9` WARN，不能断言“零 WARN” | T5 只断言 repo PASS 与关键 OK/无 MISS；WARN 允许 | 已明确 |
| R-3 | 次要 | serve 测试若用固定端口可能冲突/挂起 | T4 用空闲端口 + 轮询 HTTP + terminate；超时失败 | 已明确 |
| R-4 | 信息 | publish 为测试新增 `--key-path/--anchors-cs/--key-id` 可选参数 | 默认行为与 PS 一致；仅测试/自动化显式传 | 已明确 |
| R-5 | 信息 | 规范 manifest 的排序需与 PS `Sort-Object Path` 对齐 | T2 用 path casefold 稳定排序；测试固定路径顺序 | 已明确 |

无阻塞问题。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
