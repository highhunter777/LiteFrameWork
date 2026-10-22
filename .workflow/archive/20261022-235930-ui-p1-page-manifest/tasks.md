# 实施任务清单：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/8（取消 2，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 页面清单模型、状态词表、存储与 schema | - | 取消 | UiPageManifest/UiPageManifestEntry/UiPageStatus 落地；UiPageManifestStore 走 UiDeterministicJson；UiPageManifest.schema.json；确定性（无时间戳/稳定排序/LF/无 BOM） | unity-tests --filter UiPageManifestEditModeTests | - |
| T2 | 对账同步：tbuiform + 已有清单 + 契约存在性 | T1 | 取消 | Sync 保留维护字段（owner/status/designRef/deviceProfiles/notes）、刷新身份字段；新增缺失条目；孤儿（Draft 保留/非 Draft 告警）、重复 formId/重名、非法状态、prefab 缺失（Warning）；表读失败不伪造；负例不写盘；sourceHash | unity-tests --filter UiPageManifestEditModeTests | - |
| T3 | 页面清单模型、状态词表、存储与 schema | - | 完成 | UiPageManifest/UiPageManifestEntry/UiPageStatus 落地；UiPageManifestStore 走 UiDeterministicJson；UiPageManifest.schema.json；确定性（无时间戳/稳定排序/LF/无 BOM） | unity-tests --filter UiPageManifestEditModeTests | UiPageManifest/UiPageManifestEntry/UiPageFormEntry/UiPageStatus 落地；Store 走 UiDeterministicJson；UiPageManifest.schema.js… |
| T4 | 对账同步：tbuiform + 已有清单 + 契约存在性 | T3 | 完成 | Sync 保留维护字段（owner/status/designRef/deviceProfiles/notes）、刷新身份字段；新增缺失条目；孤儿（Draft 保留/非 Draft 告警）、重复 formId/重名、非法状态、prefab 缺失（Warning）；表读失败不伪造；负例不写盘；sourceHash | unity-tests --filter UiPageManifestEditModeTests | Sync 按页面(prefab)分组、forms[] 承载多行；保留维护字段、刷新身份字段；重复 formId/非法状态 Error、孤儿 Draft 保留/非 Draft 告警、多行 lua 告警；表读失败经 TryListPages … |
| T5 | 编辑 API：草稿/状态流转/维护字段 | T3 | 完成 | EnsureDraft 幂等创建草稿（未登记页）；SetStatus 按五态迁移表校验（同值无操作、非法迁移报错）；SetEditorial 更新 owner/designRef/deviceProfiles/notes；纯内存 + 显式保存 | unity-tests --filter UiPageManifestEditModeTests | EnsureDraft 幂等（校验 Assets/ .prefab 存在、Page_ 前缀）；SetStatus 按五态迁移表（同值无操作、非法迁移/未知页报错）；SetEditorial 更新维护字段并重算指纹 |
| T6 | 13 个真实页面基线落盘 | T3, T4, T5 | 完成 | 对真实 tbuiform 生成 UiPageManifest.json（13 条、Registered/art.ui、0 错误）；清单入库供工作台/评审读取 | unity-tests --filter UiPageManifestEditModeTests（真实目录用例） | 真实 tbuiform 生成基线：pages=12 forms=13 warnings=0 hash=sha256:bfbc67c7…（6,235 B，入库） |
| T7 | 先行测试与负例覆盖 | T3, T4, T5 | 完成 | UiPageManifestEditModeTests 覆盖首次生成/保留维护字段/新增/孤儿/重复/非法状态/确定性/sourceHash/写盘读回/EnsureDraft 幂等/SetStatus 迁移，先红后绿 | wf verify --red + 定向 unity-tests | 先红：page-manifest-red 12/14 失败（断言失败）；实现后 15/15 全绿；回归 UiPreview 5/5、UiWidgetLibrary 6/6、WidgetSpec 3/3、WidgetManifest 11/… |
| T8 | 门禁、文档冻结与回写 | T4, T5, T6, T7 | 完成 | 定向 Unity 用例、L1、doc-lint、L2 meta 通过；设计 §8.5 + 总设计 §5.1/协作规范 §5.3 状态表述统一 + README schema；进度卷第 3 章；审计挂账 | L1+doc+l2-meta | L1 PASS 1819；doc-lint OK 0 violation；L2 meta PASS（14192）；定向 15/15 + 回归 7 套件；设计 §8.5 + 总设计 §5.1/协作规范 §5.3 五态统一 + README … |

## 任务详情

### T1 · 页面清单模型、状态词表、存储与 schema

- 文件: Assets/LiteGame/Editor/UiTools/Pages/UiPageManifest.cs, Assets/LiteGame/Editor/UiTools/Pages/UiPageStatus.cs, Assets/LiteGame/Editor/UiTools/Pages/UiPageManifestJson.cs, Docs/设计文档/客户端/UI系统/UiPageManifest.schema.json

- 状态理由: 路径应为现有 Page/ 目录而非 Pages/，重建任务

### T2 · 对账同步：tbuiform + 已有清单 + 契约存在性

- 文件: Assets/LiteGame/Editor/UiTools/Pages/UiPageManifestSync.cs

- 状态理由: 路径应为现有 Page/ 目录而非 Pages/，重建任务

### T3 · 页面清单模型、状态词表、存储与 schema

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageManifest.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageStatus.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageManifestJson.cs, Docs/设计文档/客户端/UI系统/UiPageManifest.schema.json

### T4 · 对账同步：tbuiform + 已有清单 + 契约存在性

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageManifestSync.cs

### T5 · 编辑 API：草稿/状态流转/维护字段

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageManifestEditor.cs

### T6 · 13 个真实页面基线落盘

- 文件: Assets/LiteGame/Editor/UiTools/UiPageManifest.json

### T7 · 先行测试与负例覆盖

- 文件: Assets/Tests/EditMode/UiPageManifestEditModeTests.cs

### T8 · 门禁、文档冻结与回写

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/UI系统总设计.md, Docs/设计文档/客户端/UI系统/UI美术协作与交接规范.md, Docs/设计文档/客户端/UI系统/README.md, Docs/施工进度/UI系统线.md, Docs/审计/代码质量审计-客户端.md, .workflow

