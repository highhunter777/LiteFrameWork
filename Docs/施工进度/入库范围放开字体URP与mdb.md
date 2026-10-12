# 入库范围放开：字体 / URP 管线资产 / `.mdb`（2026-10-13）

用户点单（2026-10-13）：「`*.mdb` 可以入库，URP 管线资产，字体也入库」。本批把三类原被 `.gitignore` 排除的内容改为入库，并收口两处随之失真的门禁与文档。

## 状态

**已完成**（`.gitignore` 语义实测 + `restore-check` 实跑 + 提交号见 §验证证据）。

## 交付

| 面 | 落点 |
|---|---|
| `.gitignore`：`*.mdb` 放开 | 删除 `*.mdb` 一行，改为注记。**根因**：DOTween 两个 `.mdb.meta` 早在 `c3e1082` 入库，而实体被忽略 ⇒ meta 永远是孤儿 ⇒ Unity 一开就删它并刷 `A missing` 警告（[非作者机恢复与验证记录](../非作者机恢复与验证记录.md) §1.2(b) 的原始登记）。`*.pdb` 维持忽略（其 meta 未入库，无同型孤儿） |
| `.gitignore`：URP 管线资产放开 | 删除 `Assets/Settings/`、`Assets/Settings.meta`、`Assets/UniversalRenderPipelineGlobalSettings.asset(.meta)` 四条，集中为「URP 管线资产」段。**理由**：`ProjectSettings/GraphicsSettings.asset`（`m_CustomRenderPipeline` + `m_SRPDefaultSettings`）与 `QualitySettings.asset`（三档 `customRenderPipeline`）均已入库并按 GUID 引用这批资产——不分发则干净克隆解析不到、工程退回 Built-in 兜底（同记录 §2） |
| `.gitignore`：字体放开（部分） | 删除 `Assets/ShareResource/Fonts/` 与 `Fonts.meta` 两条，改为只排除 `11月金楷系列/`（＋其 meta）。**入库**：NotoSansSC SDF ×2 + `NotoSansSC-OFL.txt`（SIL OFL 许可）共 70MB——75 处文本引用的视觉单一来源、7 个 EditMode 用例按真实字体加载。**不入库**：金楷系列 5 ttf（110MB、全仓零引用、再分发许可未核实——迁移批一「金楷维持不入库」裁决不变） |
| `.gitignore`：死规则与重复清理 | 删 `Assets/LiteGame/Fonts/` 两条（该目录 2026-10-11 迁移批一已迁至 `ShareResource/Fonts`，规则无对应路径）；删 `Assets/Animator/`、`Assets/ShareResource/Characters/` 各一份重复条目（原各出现两次） |
| `scripts/gate/restore-check.ps1` | 字体检查项 `Assets/LiteGame/Fonts`（迁移后即失效的路径）→ `Assets/ShareResource/Fonts/NotoSansSC-Regular SDF.asset`，并**由 WARN 升为 blocking**（已入库资产缺失＝检出不全）；新增 URP 资产 blocking 检查；场景检查项 `Test.unity`（`58fbfd3` 已删）→ `TrainingGround.unity`，修掉「非作者机必得误导性 `Assets/Scenes missing`」（同记录 §1.3 原始登记） |
| 文档回写 | [环境恢复指南](../环境恢复指南.md) §2/§3（三类移入「已随库分发」，字体行改指金楷）；[非作者机恢复与验证记录](../非作者机恢复与验证记录.md) §0/§1.2(b)/§1.3/§2/§2.1/§3.4/§5 逐条标注消解；[修改指南](../修改指南.md) §3 环境可复现行；[AGENTS.md](../../AGENTS.md) 资源规范加例外条目 |

## 验证证据

| 项 | 结果 |
|---|---|
| `git check-ignore` 正例 | URP 4 路径、NotoSansSC 4 路径、DOTween 2 `.mdb`：**全部不再被排除** |
| `git check-ignore` 反例 | 金楷 ttf 与目录 meta、`SampleContent`、`Assets/Art`、`Assets/Synty`、`Assets/TextMesh Pro`、`Assets/StreamingAssets`、`Builds/**/*.mdb`：**仍被排除**（未误伤） |
| `restore-check.ps1` | **PASSED**（新增字体/URP 两条 `[OK]`；场景行 `[OK]`；唯一 WARN = 本机 .NET 9，与改动无关） |
| 大文件风险 | 最大单件 36MB（`NotoSansSC-Regular SDF.asset`），**无文件超 GitHub 100MB 硬限**；本批入库总量约 70MB |
| BuildHash | 不涉及（三类均不在 `SHARED_TARGETS`/`SHARED_DATA_TARGETS`/`META_TARGETS`） |

## 遗留（如实）

- **金楷系列仍不入库**：110MB、全仓零引用、字体名疑似游戏客户端提取，仓库既有文档自标「商用前核对许可、字体索赔案例常见」——再分发风险未经核实，故不随本批进公开仓库。将来要用，加/删一行规则即可。
- **EditMode 复跑未做**：字体入库后，[非作者机恢复与验证记录](../非作者机恢复与验证记录.md) §3.2 中「缺字体」归因的 10 个失败预期转通过——**尚未复跑验证**。PlayMode 首跑同理（§3.4）。
- **未同步的另一项**：该记录 §1.2(a)（`restore-check` 未校验 `toolchain.json` 的 `edition`/`revision`）与 §1.4（Odin 是否降为 WARN）**本批未动**，仍挂 Owner 裁决。
