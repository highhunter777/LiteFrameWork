# AGENTS.md——代理与新成员必读入口

> 本文件是**动代码前的必读清单与红线速查**。详细规则与查询表在 `Docs/修改指南.md`，本文件只做收敛索引；两者冲突以 `Docs/修改指南.md` 为准。

## 必读顺序（动代码前）

| 序 | 文档 | 回答什么 |
|---|---|---|
| 1 | 本文件 | 基础原则、红线速查、常用命令 |
| 2 | [Docs/修改指南.md](Docs/修改指南.md) | **§1 代码实现规范（红线，含审计反例）→ §2 修改规范（动手前/改动中/收尾流程）→ §3 领域查询表（"我想改 X 去哪改、改完跑什么"）** |
| 3 | [Docs/代码质量审计-客户端.md](Docs/代码质量审计-客户端.md) · [Docs/代码质量审计-服务端.md](Docs/代码质量审计-服务端.md) | 存量质量债登记表（硬编码/重复/技术债/不统一路径/类设计，含 file:line 与修复状态）——改文件前查该文件有没有已登记债；**新代码禁止产生同型问题** |
| 4 | [Docs/项目说明/README.md](Docs/项目说明/README.md) | 项目是什么、怎么跑、数据怎么流、怎么分层 |
| 5 | [Docs/设计文档/README.md](Docs/设计文档/README.md) | 目标架构裁决（与本指南/代码冲突时以设计文档为准） |
| 6 | [Docs/开发导航/README.md](Docs/开发导航/README.md) | 代码地图、程序集引用图 |

## 基础原则

**代码规范**

- 通用框架：面向通用性和扩展性的框架设计实现。
- 单一职责：代码文件与类定义均保持单一职责。
- 目录清晰：文件目录结构清晰、层次分明。
- 模块聚合：业务模块的代码文件与相关美术资源放在同一模块目录下，并在该目录内划分子目录管理。
- 命名统一：遵循统一的命名规范。
- 消除重复：减少重复实现，优先复用。
- 注释准确：代码注释仅描述现有设计与实现。
- 架构一致：除 ECS 架构外，均遵循面向对象规范。
- 开闭原则：对修改关闭，对扩展开放。
- 禁止硬编码：避免硬编码，使用配置、常量或数据驱动。
- 生产级质量：代码实现需达到生产级标准，注重扩展性与通用性。

**文档规范**

- 设计文档与施工进度文档分开。
- 设计文档仅描述最新设计；进度文档仅保留最新进度。
- 代码完成后，应回写对应文档。

**工具规范**

- PowerShell 脚本统一使用 pwsh。

**资源规范**

- 代码与脚本资源可纳入版本管理。
- 配置资源、预制体模板资源及其直接依赖资源、场景资源及其直接依赖资源可纳入版本管理。
- 第三方付费资源不纳入版本管理。
- 其他资源不纳入版本管理。
- **目录分层（2026-10-11 裁决）**：Assets 顶层只放原始资源；进包资源只放两类收集来源——**模块内使用资源目录**（模块聚合，样板 `LiteGame/Lua`）与**共享资源目录 `Assets/ShareResource`**（其下自建顶层子目录分类）；映射与迁移面见 [Docs/设计文档/client/content/资源收集目录专项设计.md](Docs/设计文档/client/content/资源收集目录专项设计.md) §2/§5。

> 以上原则的落地细则与审计反例见 [Docs/修改指南.md](Docs/修改指南.md) §1 与两份质量登记表。

## 核心红线速查

1. **BuildHash 源集**：改 `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol,Transport/Security}`、两端表数据 → ★ `python scripts/codegen/gen-build-hash.py`（权威清单＝该脚本 TARGETS/DATA_TARGETS）；漏跑＝出包门禁被拦、两端常量不同步时 Join 握手拒进房（2026-10-07 起开发期不校验，守卫在 build-player.ps1 门禁步 --check）。
2. **R11 房间内核纯度**：`Assets/RoomServer/Runtime/` 禁 Console/系统时钟/文件 IO/proto/LiteNet。
3. **R13 数值读口**：机制面禁读静态读口可变值，数值经实例参数传递（consts 豁免）。
4. **Unity 序列化资产**（场景/Prefab/Material/.meta）：一律走 unity-pipeline CLI（`UNITY-GUIDE.md`），禁直改文件。
5. **禁硬编码**：魔法数字/字符串进常量或表；跨层共享常量 C# 单源权威，Lua shim/工具引用它；生成器引用运行时常量；"必须与 X 一致"只靠注释＝违规，要机制化（单源/生成/守卫测试三选一）。
6. **禁新增 public static 可变状态**（豁免名单封闭）；确定性判定路径（伤害/弹药/命中/checksum）禁读任何静态可变值——包括 dev 宏内。
7. **错误契约统一**：已关闭服务再调用抛 `ObjectDisposedException`；程序员错误 fail-fast、内容降级 Log.Warning+默认值；同层姊妹服务同契约。
8. **日志统一 `Log.*` 门面**；临时诊断带 `[Diag]` + 摘除批次号，批次收口必清；热路径禁周期性 Warning。
9. **UI 单写者**：产品 UI 开关唯一走 `UINavigationController`；控件计时统一 `UiAnimationClock`；动效参数走 `UiFx` 收口。
10. **接线纪律**：注册进容器的服务必须有生产消费者；"待接线"必须挂施工进度批次号；禁反射取私有服务（走 `GameEntry.TakeContainer()`）。
11. **类设计**：单一职责；>400 行多职责先给拆分方案；禁 public 可变字段状态袋；禁哈希当身份键；事件回调上接口端口。
12. **文档回写**：代码完成后回写设计文档/施工进度；修了审计登记条目回写"修复登记"表；新问题按格式追加登记。

## Skill 索引（按任务触发，ZCode）

下列流程性规范已抽为通用级 skill（`.agents/skills/`，ZCode/Claude Code 等跨工具标准位），ZCode 会话中按任务自动加载；本文件的红线条目保留（同时服务无 skill 机制的其他工具）。另有 PostToolUse 提醒 hook：脚本单源在 `scripts/hooks/shared-source-reminder.ps1`（客户端中立），由 `.zcode/config.json`（ZCode）与 `.claude/settings.json`（Claude Code）各自挂载，在编辑器触碰共享源集/表/Unity 序列化资产时即时提醒，不依赖模型自觉。

| skill | 触发 |
|---|---|
| `build-hash-sync` | 动共享源集（红线 1）、`.proto`、Luban 表源、表数据；出包前哈希检查 |
| `unity-asset-pipeline` | 场景/Prefab/Material/SO/.meta、Assets 内移动删除、工程设置（红线 4） |
| `directory-move-sync` | 目录搬动（六面同步 + unity-pipeline） |
| `batch-wrapup` | 一批改动收尾/提交前（修改指南 §2.3 必跑 + §2.4 回写） |
| `audit-debt-check` | 动源码文件前查两份质量登记表（修改指南 §2.1 第 3 步） |

## 常用命令

```powershell
# 验证基线（白天代码批，一切改动的底线）
pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest     # L1 纯逻辑（含纪律扫描/表守卫/BuildHash 守卫）
pwsh scripts/gate/l0-dep-scan.ps1                            # 依赖与秘密扫描
pwsh scripts/codegen/toolbox.ps1                             # 生成器工具箱（build-hash/proto/luban/sync-code/refresh-hash 统一入口；形态见专项设计）
python scripts/codegen/gen-build-hash.py                     # ★ 共享源集改动后必跑
python scripts/codegen/gen-build-hash.py --check             # 出包前复算比对
scripts/codegen/gen-proto.ps1 -ProtocPath <protoc.exe>       # ★ 改 .proto 后
Luban/gen.bat                                                # ★ 改表源后（双端重生成）
pwsh scripts/gate/l2-unity-gate.ps1                          # L2 Unity 门禁（夜间）
pwsh scripts/gate/restore-check.ps1                          # 环境可复现检查
```

Unity 交互（改场景/Prefab/跑 PlayMode）一律走 unity-pipeline CLI，见 `UNITY-GUIDE.md`。

## 工作约定

- PowerShell 脚本统一用 `pwsh`。
- 设计文档与施工进度分开：设计文档只描述最新设计，进度文档只保留最新进度。
- 文档与代码不一致：以代码为准，并顺手修文档。
- 目录搬动当批同步：`ScanTargets.cs`（纪律目标）、YooAsset 收集组、`Tests.slnx`/csproj、构建器路径、[Docs/开发导航/代码地图.md](Docs/开发导航/代码地图.md)。
- 资源放置与收集目录规划（打资源包热更）按「基础原则·资源规范」＋ [资源收集目录专项设计](Docs/设计文档/client/content/资源收集目录专项设计.md)；版本管理按既有 `.gitignore` 裁决执行，两者无关。
