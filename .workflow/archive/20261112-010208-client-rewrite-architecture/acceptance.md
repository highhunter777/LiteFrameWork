# 验收标准确认：客户端架构重写（不继承旧设计）

> 运行 client-rewrite-architecture · 创建于 2026-11-11
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | 新架构设计文档落地：`Docs/设计文档/架构/客户端重写架构总设计.md`，含旧设计废除清单、目标程序集/目录/生命周期/UI/战斗边界、迁移阶段、守卫；设计索引登记、旧文档处置清单明确；独立子代理评审 PASS；Doc 门禁通过。 | 文档存在 + 评审记录 PASS + `python scripts/gate/test.py --suite Doc` | 任务拆解后回填 |
| AC2 | 新树可运行：`BootNext.unity` 在 60s 内经 `GameEntryNext` → `ClientApp` → `UiRuntime` 打开占位页/LoginPage/ErrorPage；新路径静态扫描 0 处旧机制 token（17 段、`ClientHost`、`ServiceContainer`、`PageServices`、`PageIds`、`UIPageCatalog`、`UIBindIndex`）；新增/相关 EditMode 用例全绿。 | PlayMode/EditMode 运行 + 静态扫描脚本 + L2 filter | 任务拆解后回填 |
| AC3 | 短链路达标：新增页面 = 2 个代码文件（页面类 + 功能打开调用点）+ 1 prefab、0 处中央登记、≤1 次本地化/内容生成；改按钮 = 1 代码文件；新增功能 = 功能目录外 ≤1 行（参与流程 ≤2 行）；新增设置 = ≤2 代码文件 + 1 prefab + 可选 1 条本地化行；冷启动子代理 ≤2 文件且 0 反问。 | 实证文件清单 + 冷启动子代理记录 + G4 测试 | 任务拆解后回填 |

## 提审记录

- 提审时间：2026-11-11 23:03（Asia/Hong_Kong）
- 提审方式：对话中列出关键架构决策与 AC1–AC3，结构化提问确认范围/迁移/本轮验收/UI 工具链
- 用户原话或选择：
  - 范围：按推荐——客户端产品 + 客户端框架；Sim/Net/Sync/服务端/表/美术不动。
  - 迁移：**原地替换（strangler）**——新代码在 `Assets/LiteGame/Next` + `Packages/com.litegame.framework/Next`，旧目录原位保留，逐功能替换。
  - 本轮验收：**AC1–AC3**（架构文档 + 可运行骨架 + 短链路实测）；P1/P2/P3 后续。
  - UI 工具链：不迁移旧 UI 工作台/页面登记/绑定生成链，只留轻量资产校验。

## 用户确认

- 确认结论: 通过
- 确认人: user
- 确认时间: 2026-11-11 23:03（Asia/Hong_Kong）
- 变更要求：迁移形态按用户选择改为原地 strangler；`plan.md` 已把根路径改为 `Assets/LiteGame/Next`，P3b 保留 `Next` 并提升；其余 AC1–AC3 不变。

## 变更记录

- 2026-11-11：用户把迁移形态从“独立新树并行”改为“原地 strangler”。只影响迁移路径/切换步骤，不改变 AC1–AC3 的判定口径；已同步 plan.md 的 A5、候选方案表、目录树、P0/P3b、G2/G3、hot-boundary 接线与 AC5 措辞。
