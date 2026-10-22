# 代码评审：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

> 运行 ui-p1-page-manifest · 创建于 2026-10-22T23:28:19+08:00

## 变更范围

- 对照任务与方案：T3（模型/词表/存储/schema）→ AC1；T4（对账）→ AC2；T5（编辑 API）→ AC3；T6（基线）→ AC3；T7（先行测试）→ AC1–AC3；T8（门禁/文档）→ AC4/AC5。T1/T2 因路径误写取消、由 T3/T4 承接（同一范围）。
- 变更文件/模块：
  - 编辑器：`Assets/LiteGame/Editor/UiTools/Page/{UiPageManifest,UiPageStatus,UiPageManifestJson,UiPageManifestHash,UiPageManifestSync,UiPageManifestEditor}.cs`（新增）；`UiPageScaffold.cs`（新增 `TryListPages`，`ListPages` 委托保留旧行为）。
  - 数据：`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`（基线，12 页/13 行）。
  - 测试：`Assets/Tests/EditMode/UiPageManifestEditModeTests.cs`（15 例）。
  - 文档：`UI编辑器工具链专项设计.md` §8.5、`UiPageManifest.schema.json`（新增）、`UI系统总设计.md` §5.1、`UI美术协作与交接规范.md` §5.3、`UI系统/README.md`、`Docs/施工进度/UI系统线.md` 第 3 章、`Docs/施工进度/README.md`、`Docs/审计/代码质量审计-客户端.md`。
  - 工装：`.workflow/config.json`（新增 `page-manifest-editmode` / `page-manifest-red`）。

## 正确性与边界

- 主路径：`UiPageManifestSync` 读 `tbuiform`（`TryListPages`）→ 按 prefab 分组 → 已有条目刷新身份字段/保留维护字段 → 校验 → 排序 → `sourceHash` → 写盘；`UiPageManifestEditor` 提供草稿/状态/维护字段的纯内存编辑。
- 边界与错误路径（均有用例）：表读失败（显式错误不写盘）、重复 formId、非法状态（不写盘）、清单有而表无的 Draft/非 Draft、多行 lua 不一致、prefab 缺失、输出路径不在 `Assets/`、未知页面、非法状态迁移、草稿重复创建。
- 回归风险：`UiPageScaffold.ListPages` 行为保持不变（委托 `TryListPages`，失败仍返回空 + warning）；`UiPreview` 5/5 覆盖页面选择器；其余 UI 套件全绿。未改 prefab/场景/表。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：是。15 例覆盖首次生成、同页多行合并、多行 lua 告警、保留维护字段、新增条目、孤儿两种状态、重复 formId、非法状态不写盘、确定性、指纹敏感、写盘读回与路径边界、草稿幂等、状态迁移（合法/非法/同值/未知页）、真实 12 页/13 行基线。
- 是否存在迎合实现的测试：否。断言基于表投影与磁盘事实；先红证据（12/14 失败）证明先红后绿；额外补了两条真实场景用例（多行合并、lua 不一致）。

## 通用性与扩展性

- 根因与复用边界：页面身份（表）与维护态（Owner/状态/设计稿）此前无共同载体；本批用「页面唯一 + forms[]」把两者收进同一注册表。复用 `UiPageScaffold`（表读面）、`UiContractPaths.ForPage`（契约路径）、`UiPageContract.DefaultCacheStrategy`（缓存默认）、`UiDeterministicJson`（序列化口径）、`UiWidgetManifest` 的清单模式。
- 扩展点与接入方式：新增维护字段走 schema 次版本并在 Sync 保留；状态新增扩枚举 + 迁移表 + 设计 §8.5；工具（起页/登记）经 `UiPageManifestEditor` 更新，不直改 JSON。
- 硬编码、特判、魔法值：页面枚举来自表；状态/迁移是枚举表；默认 Owner/默认状态是单一常量；无按页面名分支；`Tmp_Dialog` 两行由通用分组逻辑处理（非特判）。
- 重复逻辑与过度抽象：清单存储/指纹与 P1-2 同构但类型不同，未强行抽象共用基类（JSON 口径已共用 `UiDeterministicJson`）；`UiPageManifestHash` 只做指纹，未混入对账逻辑。
- 相关场景复用与回归影响：`TryListPages` 是表读面的扩展而非替代；`ListPages` 调用方零改动。
- 命名、结构与复杂度：6 个文件按模型/词表/存储/指纹/对账/编辑拆分，单文件最大约 260 行，无 >400 行多职责文件。

## 安全与性能

- 安全：无网络/权限面；写盘限定 `Assets/`；错误信息不含敏感数据；状态迁移受表约束，避免「随手改 JSON」式降级。
- 性能与资源：对账读表（Python helper）+ 13 行投影 + 12 次 `File.Exists`，编辑器一次调用亚秒级；无缓存需求。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 高（阻塞） | `UiPageManifestSync` 初版模型 | 初版按 `tbuiform` 行建条目（formId/layer 标量），真实表中 `Tmp_Dialog` 登记 201/206 两行 ⇒ 被误判「重名页面」、真实基线用例红 | 条目改为按页面（prefab）唯一 + `forms[]` 承载多行；新增「同页多行合并」「多行 lua 不一致」用例；设计 §8.5 同步 | 已修复 |
| C-2 | 中 | `UiPageScaffold.ListPages` | 表读失败返回空表（仅 warning），对账会把「读失败」当「空表」⇒ 清单全孤儿 | 新增 `TryListPages(out rows, out error)`；对账用它显式报错不写盘；`ListPages` 委托保旧行为 | 已修复 |
| C-3 | 低 | `UiPageManifestSync` | `existing?.Pages ?? (IReadOnlyList<…>)Array.Empty<…>()` 类型不匹配（编译期） | 改为先赋 `IReadOnlyList` 局部再判空 | 已修复 |
| C-7 | 中 | `UiPageManifestSync.Sync` | 基线入库后，显式行对账（`Sync(rows, null)`）仍读磁盘已有清单 ⇒ 夹具用例读到真实 12 页（`Expected: 2 / But was: 14`、`Single()` 抛多元素）；单跑绿、全量 verify 才红 | 只有 `rows == null`（默认模式）才 `LoadIfExists`；显式行时 `existing` 严格按入参 | 已修复 |
| C-4 | 低（延后） | `UiContractGenerator.GenerateRegistered` | 多行页面（Tmp_Dialog）的契约 `page.formId` 取首个匹配行，契约未标注多行 | 已在设计 §8.5 与审计表登记；P1-4/P1-5 明确「契约主行 + manifest 选择」或契约带 `formIds[]` | 已登记 |
| C-5 | 信息（设计约束） | `UiPageManifestEditor.EnsureDraft` | 草稿只接受 `Page_*`（`Hud_*` 不走 tbuiform、不在页面清单范围） | 由设计边界决定；错误信息明确 | 已接受 |
| C-6 | 信息（设计约束） | 基线数据 | 13 行全部 `Registered`，未自动标 `Wired` | `Wired` 属工程接线/评审动作；进度卷边界已写明 | 已接受 |

无其他阻塞问题。检查过的风险面：多行登记、表读失败、状态迁移、孤儿条目、外部手改（`sourceHash`）、磁盘写入边界。

## 复测记录

- 修复后执行的命令与结果（最终代码状态）：
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-page-manifest verify` → **PASS 16/16**（含新增 `page-manifest-editmode`；首轮 15/16 暴露「显式行误读磁盘基线」C-7，修复后复跑 16/16）。
  - 定向夹具：`UiPageManifest` **15/15** · 回归 `UiPreview` 5/5 · `UiWidgetLibrary` 6/6 · `WidgetSpec` 3/3 · `UiWidgetManifest` 11/11 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiStaticText` 6/6。
  - `python scripts/gate/doc-lint.py` → OK（0 violation）；L2 meta 扫描 14192 个 `.meta` 全合法。
- 验证证据：evidence/verify.json

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: DeepSeek Harness（本批执行代理，独立复核 diff、真实对账结果与命令输出）
- 评审时间: 2026-10-22T23:58:00+08:00
