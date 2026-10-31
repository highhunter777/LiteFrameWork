# 验收标准确认：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 运行 sync-and-sim-params · 创建于 2026-10-30T19:40:29+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| B1 | `Assets/LiteSync/` 存在，asmdef（refs LiteNet+LiteSim、noEngineReferences、Protobuf 预编译）与双轨 csproj 齐备 | 文件/asmdef/csproj 检查 + Unity 编译 0 错 | 待拆解 |
| B2 | `LiteNet.asmdef`/`LiteNet.csproj` 不引 `LiteSim`；`Assets/LiteNet/**` 的 `using LiteSim` 零命中 | grep 断言 + 编译 | 待拆解 |
| B3 | 15 件文件按 Shared/Snapshot/Input/Events/Client 搬迁到位，`.meta` 随迁无 GUID 重建；LiteNet 保留项与清单一致 | `git status` 成对 + 文件清单核对 | 待拆解 |
| B4 | `SHARED_TARGETS` 含 `LiteSync/{Shared,Snapshot,Input,Events}`、不含 `Client`；BuildHash 路径/命名空间不变；`--check-shared` 通过并记录新哈希 | `gen-build-hash.py --check-shared` | 待拆解 |
| B5 | `Allowed` 删除 `LiteNet -> LiteSim`、新增 LiteSync 条目；`ClientRoots` 含 LiteSync；两条硬边生效且有植入违规负例 | `ClientAsmdefAllowlistTests`/`ArchitectureGuardTests` | 待拆解 |
| B6 | `WeakNet/*` 移至 Dev；LiteNet 只留两个接口；WeakNet 用例改引用后仍绿 | grep + `Tests/LiteNet.Tests` | 待拆解 |
| B7 | `DiagTrace` 保留并登记理由；Dev 侧只经 `DebugKernel` 读 | `DebugKernelEditModeTests` + 文档登记 | 待拆解 |
| B8 | L1 全量绿；两端重建后联机冒烟通过、BuildHash 一致 | `gate/test.py --suite L1` + 联测 | 待拆解 |
| B9 | `CombatValues` 11 字段/9 参数；13 列从 `Combat/tbcombatgeom` 装载；未发布 `Loaded` 抛；`Geometry` 展示兜底；`Loaded` 时序；候选表校验；10 处构造点更新 | 定向 EditMode/L1 + `ResetForTests` + 坏表用例 | 待拆解 |
| B10 | `Assets/**`、`Server/**`、`Tests/**` 旧几何名零命中；`ResolveHead*`/override 字段零命中；消费者迁移；判定/烘焙不读 `Geometry`；SimStep/9 System 签名不变 | grep 守卫（含负例）+ 编译 | 待拆解 |
| B11 | `#combatgeom` 接入 `__tables__` + `TableLayout.Files("Combat/tbcombatgeom")`；`JudgmentFiles` 自动包含；调用点全更新 | 表/配置检查 + `--check-shared` | 待拆解 |
| B12 | 五件 `.g.cs` 不存在；五个烘焙器改产表行/`.bin`；写入源只来自 prefab/anchor/曲线；SimMapBaker 读 CC 并断言表 row ≡ 入库 CC 夹具；`restore-check` 前置；测试迁移；注释清理 0 warning；`WriteGenerated` 删除 | 文件/测试 + Unity 0 warning + 烘焙摘要 | 待拆解 |
| B13 | `Combat/tbcombatgeom` 进 JudgmentFiles ⇒ 表字节进摘要；联机摘要 = 表字节 + 入口规则位；本地按生效值；两端不等拒局 | `CombatConfigDigest`/`GameplayDigestBase` 用例 | 待拆解 |
| B14 | 13 个几何值与表加载结果逐字段精确相等（0 容差）；固定输入 checksum/回放与黄金样本一致 | 定向 L1/EditMode + 黄金样本 | 待拆解 |
| B15 | `AoiSettings` 服务端真实注入（全开/30m/15m）；`AoiFilter` 无静态缓存；`SnapshotHz` 与 AOI 分离，客户端从 JoinAck 取 | 服务端配置注入 + EditMode/Play 用例 | 待拆解 |
| B16 | LiteSim 无 `RootMotion*`；`rootmotion.bin` 在 GameData/Config/runtime；`RootMotionCatalog.Load` 在 Battle/Arena | 文件/asmdef 检查 | 待拆解 |
| B17 | `ContentIdentity` 不引 LiteSim；绑定用 `RootMotionCatalog.Digest`；不新增名称字段 | `AppComposition` 检查 + 摘要用例 | 待拆解 |
| B18 | `.bin` 不进 SHARED_TARGETS；`--check-shared` 通过；无 `RootMotionData.g.cs`；“已烘焙未接线”登记 | `--check-shared` + 文档 | 待拆解 |
| B19 | 13 项滑杆；`SimParamsOverrides` dev-only；单门 `SimParamsGate.Evaluate` + 单合并点（ConfigService + ServerTableLoader）；联机不读覆盖；删除四个几何覆写项；Damage/Rpm 保持现状 | provider/门/合并点用例 + grep | 待拆解 |
| B20 | 本地下一局生效；对局中提示；四条断言（哨兵/无覆盖摘要、双 checksum、联机表值、GM 真实操作禁用） | EditMode/Play + 真实操作验证 | 待拆解 |
| B21 | 编辑器/GM 导出同一 `#combatgeom.xlsx`；自动 gen.bat + check-shared + diff + 备份；`.bin` 导出显示路径/摘要 | 工具操作 + 证据 | 待拆解 |
| B22 | 静态可变扫描根含 LiteNet/LiteSync；无新增 public static 可变字段 | `StaticMutableAllowlistTests` | 待拆解 |
| B23 | `sync-and-sim` 命令组通过：L1、LiteSync/LiteSim 定向、`--check-shared`、L2、两端联测 | `wf verify` + 证据 | 待拆解 |
| B24 | 文档回写 3 份设计 + 2 份进度卷，索引登记，`doc-lint` 全过 | `doc-lint.py` + 文档检查 | 待拆解 |

## 提审记录

- 提审时间：2026-10-31T16:56:00+08:00
- 提审方式：对话分组列出 B1~B24，并落盘本文件；结构化提问选择
- 用户原话或选择：确认全部 24 条

## 用户确认

- 确认结论: 通过
- 确认人: 用户
- 确认时间: 2026-10-31T16:58:49+08:00
- 变更要求：无

## 变更记录

- 2026-10-31：初版。用户已裁决 P-3 嵌套 `CombatGeometry`、P-6 扩范围全迁、P-9 B 先且独占 `BakeTargets`；方案经 9 轮 dsh 独立评审，最终 PASS。
