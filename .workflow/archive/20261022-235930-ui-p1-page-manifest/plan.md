# 方案：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

> 运行 ui-p1-page-manifest · 创建于 2026-10-22T23:28:19+08:00

## 背景与目标

- 设计依据：《UI 系统总设计》§5.1（「`UiPageManifest`（工具生成，不进运行时）记录页面 Owner、契约路径、设计稿链接、设备档、状态」）、§10.3（编辑器页面清单不进包）；《UI 编辑器工具链专项设计》§3.1（起页生成草稿条目：Owner=美术、状态=草案）、§3.6（登记后更新状态：草案→已登记/已接线）、§8.3（生成物落点 `Assets/LiteGame/Editor/UiTools/UiPageManifest.json`）；《UI 美术协作与交接规范》§5.3（页面清单记录状态，工具与评审更新）。
- 现状：`tbuiform` 是运行时页面目录（五列）；`UiPageScaffold.ListPages()` 已能读表；页面契约（P1-1）与控件清单（P1-2）已落地；**但没有页面清单**——Owner/设计稿/设备档/状态无处记录，起页草稿页（未登记表）也无处登记。
- 目标（可观察）：一条命令把 `tbuiform` 行 + 已有清单 + 契约存在性对账成确定性 `UiPageManifest.json`（保留作者维护字段、新增缺失条目、报告孤儿/非法状态）；提供草稿条目创建与状态流转 API（带合法迁移校验）；13 个真实页面生成基线；正负例测试齐；口径写入设计文档。

## 需求澄清与假设

- 需要确认的问题：无（沿用前两批裁决方式：机制 + 真实页面基线一起交付）。
- 已确认答复（2026-10-22，用户）：接着做 P1-3（P1-2 选择的「机制 + 内容补全」口径延续到本批）。
- 采用假设：
  - **清单是「工具维护的注册表」而非纯生成物**：起页可在表行之前产生草稿条目（设计 §3.1），Owner/设计稿/设备档/状态是作者/评审维护数据，必须在对账刷新时**保留**；身份字段（formId/luaPath/layer/fullScreen/cacheStrategy/prefabPath/契约路径）从 `tbuiform` 与契约存在性刷新。
  - **状态词表合并两处设计表述**：总设计 §5.1「草案/评审/冻结」+ 协作规范 §5.3「草案/已登记/已接线/冻结」→ 五态 `Draft/InReview/Registered/Wired/Frozen`，在工具链 §8.5 冻结为单源，两处旧表述改为指向该节。
  - **基线状态**：13 个真实页面已在 `tbuiform` 登记 → 基线状态 `Registered`（`Wired` 属工程接线/评审动作，不在本批自动判定）；Owner 统一 `art.ui`；设计稿/设备档留空待作者维护。
  - **基线入库**：与纯生成物（页面契约、控件清单）不同，页面清单承载维护态数据，**提交基线**（`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`），否则 Owner/状态无法跨人传递。
  - 假设影响与风险：清单 JSON 会被工具改写（不是只读生成物），`sourceHash` 用于发现外部手改；手改检测规则归 P1-4/P1-5。

## 范围与非目标

- 范围内：
  - 模型：`UiPageManifest`（pages[]/warnings[]/sourceHash）+ `UiPageManifestEntry` + `UiPageStatus` 词表。
  - 存储：`UiPageManifestStore`（`UiDeterministicJson` 口径、UTF-8 无 BOM、LF）。
  - 对账：`UiPageManifestSync`（tbuiform + 已有清单 + 契约存在性 → 清单；保留维护字段、新增/孤儿/重复/非法状态报告、负例不写盘）。
  - 编辑 API：`UiPageManifestEditor.EnsureDraft`（起页草稿，幂等）、`SetStatus`（合法迁移校验）、`SetEditorial`（Owner/设计稿/设备档/备注）。
  - schema：`UiPageManifest.schema.json`。
  - 基线：13 个真实页面的 `UiPageManifest.json` 落盘。
  - 测试：`UiPageManifestEditModeTests`（对账/保留/新增/孤儿/重复/状态迁移/草稿幂等/确定性/写盘读回/真实目录基线）。
  - 文档：工具链专项设计 §8.5（字段模型与对账口径 v1.0 冻结）；总设计 §5.1 与协作规范 §5.3 状态表述改为指向 §8.5；README schema 登记；进度卷第 3 章；审计挂账。
- 非目标：`UiRuleExceptions` 与校验规则族（P1-4）、Hub/CLI/CI 接线（P1-5）、起页工具自动写草稿（P1-5 接线；本批只提供 API）、试点页挂 `LTextKeyRef`（P1-6）、运行时读取清单（设计明确禁止）。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（`ListPages()` → `PageRow{id,lua,prefab,layer,fullScreen}`；`PrefabPath` 单源）。
  - `Assets/LiteGame/Editor/UiTools/Contract/UiContractPaths.cs`（契约路径派生 `ForPage(name)`）。
  - `Assets/LiteGame/Editor/UiTools/UiDeterministicJson.cs`（P1-2 抽出的确定性 JSON 口径）。
  - `Assets/LiteClient/UI/UIFormInfo.cs`（`UICacheStrategy` 词表单源）。
- 约束：
  - 生成物禁手改；清单由工具 API 更新，不直改 JSON。
  - 无时间戳、稳定排序、LF、UTF-8 无 BOM；负例不写盘；输出限定 `Assets/`。
  - 不按页面名特判；身份字段来自 `tbuiform`，维护字段来自清单自身。
  - 运行时零依赖（清单在 Editor 目录、不进包）。
  - 禁新增 public static 可变状态。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | 清单＝工具维护注册表；对账时保留维护字段、刷新身份字段 | 支持「草稿先于表行」与状态流转；维护数据不丢 | 需合并语义与迁移校验；手改检测需 `sourceHash` | 推荐 |
| B | 清单＝纯生成物（只从 tbuiform 生成），维护字段放旁路文件 | 生成确定性最纯 | 两份数据源（旁路+清单）必然漂移；起页草稿仍无处落 | 不采用 |
| C | 维护字段挂 prefab 组件 | 单一真相在资产 | 改 13 个页面 prefab；编辑器清单语义（不进运行时）被打破 | 不采用 |

推荐方案与理由：A。设计明确「起页先产生草稿条目、登记后再更新状态」，清单必须承载维护态；身份字段仍以 `tbuiform` 为真相，对账只做投影，避免第二份运行时目录。

## 设计要点

- 数据模型（`UiPageManifest`）：
  - `schemaVersion`、`generator{name,version}`、`pages[]`、`warnings[]`、`sourceHash`。
  - 条目：`name`、`formId`（未登记草稿为 null）、`prefabPath`、`luaPath`、`layer`、`fullScreen`、`cacheStrategy`、`owner`、`status`、`contractPath`、`contractPresent`、`designRef`、`deviceProfiles[]`、`notes`。
- 状态词表（`UiPageStatus`）：`Draft`（草案）/`InReview`（评审）/`Registered`（已登记）/`Wired`（已接线）/`Frozen`（冻结）。
- 合法迁移：`Draft→InReview|Registered`；`InReview→Draft|Registered|Frozen`；`Registered→Wired|InReview|Frozen`；`Wired→Frozen|Registered`；`Frozen→InReview`（解冻须评审）；同值＝无操作。
- 关键流程（`Sync`）：读已有清单（无则空）→ 读 `tbuiform` 行 → 逐行对账：已有条目刷新身份字段并保留 `owner/status/designRef/deviceProfiles/notes`；缺失则新建（`status=Registered`、`owner=art.ui`）；已有条目不在表内且 `status=Draft` 保留，非 `Draft` 记 Warning（孤儿/表行缺失）→ 校验（重复 formId、名字/prefab 不一致、非法状态、缺失 prefab）→ 稳定排序（按 `name`）→ `sourceHash` → 写盘。
- `sourceHash` 输入：每页 `name|formId|prefabPath|luaPath|layer|fullScreen|cacheStrategy|owner|status|contractPath|contractPresent|designRef|deviceProfiles|notes`；不含 `warnings`/生成器版本/时间戳。
- 失败与边界：清单不存在＝首次生成；JSON 不可读/非法状态 → 显式错误不覆盖；`tbuiform` 读取失败 → 错误不写盘（不伪造空表）；重复 `formId`/重名 → Error；prefab 缺失 → Warning（条目保留供清理）；输出路径不在 `Assets/` → Error。
- 编辑 API（纯内存 + 显式保存）：`EnsureDraft(prefabPath, owner)` 幂等创建草稿；`SetStatus(name, status)` 校验迁移；`SetEditorial(name, owner, designRef, deviceProfiles, notes)` 更新维护字段。

## 通用性与扩展性

- 根因/共性问题：页面身份（表）与页面维护态（Owner/状态/设计稿）此前无共同载体；草稿页在登记前不可见。
- 复用与扩展点：`UiPageScaffold.ListPages()`（表读面单源）、`UiContractPaths.ForPage`（契约路径单源）、`UICacheStrategy`（缓存词表单源）、`UiDeterministicJson`（序列化口径单源）、`UiWidgetManifest` 的清单模式（模型/存储/schema/负例）。
- 避免硬编码/特判：页面枚举来自表；状态/迁移是枚举表；不按页面名分支；维护字段默认值集中（`art.ui`/`Registered`）。
- 扩展方式：新增维护字段走 schema 次版本并在 Sync 中保留；状态新增扩枚举 + 迁移表 + 设计 §8.5。

## 测试策略

- 层级：Unity EditMode（真实 `tbuiform` 读面、真实 prefab/契约路径、真实 `AssetDatabase`）。
- 用例：首次生成（真实 13 行）；对账保留维护字段；新增表行 → 新增条目；孤儿条目（Draft 保留/非 Draft 告警）；重复 formId/重名 Error；非法状态 Error 不写盘；prefab 缺失 Warning；确定性（逐字节、无 `generatedAt`）；`sourceHash` 对维护字段敏感；写盘读回一致 + 输出路径边界；`EnsureDraft` 幂等；`SetStatus` 合法/非法迁移。
- 回归：P1-1/P1-2 四套件 + `UiWidgetLibrary`。
- 先红后绿：骨架（Sync 返回空清单、Editor API 空实现）→ 断言失败 → 实现至绿。

## 验收标准

- [ ] AC1：`UiPageManifest` 模型/状态词表/存储/schema 落地；确定性 JSON、`sourceHash`、负例不写盘、输出限定 `Assets/`。
- [ ] AC2：对账保留维护字段、刷新身份字段、报告孤儿/重复/非法状态；`tbuiform` 读失败不伪造。
- [ ] AC3：13 个真实页面基线落盘（`Registered`/`art.ui`），真实目录对账 0 错误；`EnsureDraft`/`SetStatus`/`SetEditorial` 可用且迁移受校验。
- [ ] AC4：定向 EditMode 全绿；L1、doc-lint、L2 meta 通过；无 prefab/场景改动。
- [ ] AC5：设计 §8.5 + 状态表述统一 + README schema 登记；进度卷回写；审计挂账。

## 风险与回滚

- 风险：清单是可变注册表，手改/外部写入可能绕过工具——`sourceHash` 与「对账前后比较」可发现漂移（手改检测规则归 P1-4）；真实表读取依赖 Python/openpyxl（沿用既有 `_uiform_row.py` 链，失败显式报错）。
- 监控：定向用例、`doc-lint`、L1、清单 `warnings[]`。
- 回滚：删除清单与新增代码/文档即可；无 prefab/表结构/协议/BuildHash 面。
