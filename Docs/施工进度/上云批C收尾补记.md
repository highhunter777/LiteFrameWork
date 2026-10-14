# 上云批C-⑤：收尾补记——批C-② BindLocale 生产装配 + uitools 表清单单源化

> 依据：《上云测试专项设计》§2/§3 批C（收尾批）；《上云测试页面层级设计》§10 本地化装配与待接线口径；《UI框架总设计》§9 文本链；红线 5（单源纪律——"必须与 X 一致"只靠注释即违规）
> 范围：两件已落地提交（`5e8634a` 批C-② / `5e7fe22` uitools）的施工记录与索引行补记＋《代码质量审计-客户端》修复登记回写＋批C 系列挂账面收口
> 状态：**已完成（附验证证据）**——2026-10-14。本批为**补记批**：两提交的代码事实先于记录在库（批C-③ 接手时按其提交自述挂账"尚无施工记录/索引行"），记录按提交事实与本批复跑证据补写，不改任何代码与资产

## 交付物一：批C-② BindLocale 生产装配 + Bridge.text 门面（提交 `5e8634a`，2026-10-13 19:47）

`SetTextKey` 由"必抛"转正的生产装配链——批C-④ 五页文案键启用与批C-⑥ 行文案随语言整表重绑的共同底座。前置提交 `80e6fd66`（同日 16:19）先落表源（`#ltext.xlsx` → `Tbltext` 生成物，`TableLayout.Files` 加 `UI/tbltext`）。

| 项 | 落点 |
| --- | --- |
| 可选能力接口 | `Assets/LiteGame/UI/ILocaleBound.cs`（新）——《UI框架总设计》§5.1 口径；`LuaBehaviourAdapter`（路径 A）与 `UIBindBase`（路径 B）实现 |
| 注入时机 | `UIForm`——两个 OnInit 调用点后注入（索引已建、OnShow 前；换表重建索引后补注入）；OnHide/UnbindAll 的订阅清理归索引既有面；`ApplyLocale` 经 SafeCall 防护 |
| 装配缝 | `UIService` 增 `localeProvider` 可选参数（null＝测试/工具装配不注入——此时 SetTextKey 由索引抛，装配错误 fail-fast）；`UIForm.LocaleProvider` |
| 生产装配 | `UiShellModule`——`LTextCatalog.Build(config.Tables)`（tbltext 行 → 目录，zh 列＝`SourceLocale`、en 列＝`EnglishLocale` 映射单源）→ `LocalizationService`（缺 key 开发期显 `[key]` + 缺 key 日志）懒建单实例（与 UIFormCatalog 懒投影同判据） |
| 常量与注释 | `LocalizationCatalog` 增 `EnglishLocale`；两处框架注释的文本链描述同步实际表源 |
| Lua 门面 | `Bridge` 白名单三件→四件：`text`（`Bridge.Data.cs`）——`Get/Format` 四参（Toast/动态拼接）、`SetLocale/Locale`（设置页语言切换入口）；**控件文案仍走 `self.ui:SetTextKey`**（语言切换自动刷新） |
| 路径 B 写入口 | `UIBindBase` 补 `SetTextKey/SetTextKeyPlural/UnbindTextKey`——与 Lua shim 同面 |
| 定向测试 | `Assets/Tests/EditMode/LTextWiringEditModeTests.cs`（新，5 例）：真表投射 41 键／表源 key 命名全过 LTextKey／服务行为（Get/Format/切语言/缺 key 显 `[key]`）／路径 B 注入与 Empty 守卫／路径 A 契约钉 |

## 交付物二：uitools 表清单单源化（提交 `5e7fe22`，2026-10-13 19:33）

《代码质量审计-客户端》§二[中]「表清单第二份，单源纪律只靠注释维持」的根治批——审计修法三选一取**派生**（由 `TableLayout.Files` 驱动，而非守卫测试）：

| 项 | 落点 |
| --- | --- |
| 清单派生 | `Assets/LiteGame/Editor/UiTools/Validation/Rules/AssetTablePathRule.cs`——手写九表 `(file, type)` 数组退役，改 `BuildTables()` 按 `TableLayout.Files` 驱动：领域相对名 → `TableLayout.TableName` 取裸表名 → `cfg.<Pascal>` 表类名约定解析；**解析失败 fail-fast**（布局与生成物漂移当场暴露，不静默跳过） |
| 目录与拼接口径 | `TableDataDir` → 引 `TableLayout.DataDir` 单源（审计条目的第二半：原第二份少尾斜杠）；装载/核对两处路径拼接改走 `TableLayout.TableDataPath`——装载/服务端/编辑器/门禁四面同源（`GmConfigDiagnosticsPage` 先例） |
| 新表接入成本 | 单源化后新表入清单**零登记成本、不可能漏**——恰由本批场景实证（见下）；UI 工具链"新表接入第五件"（步 5 遗留登记）随之退役 |

**漂移窗口事实链（补记时按 git 取证）**：`80e6fd66`（16:19）把 `tbltext` 加入 `TableLayout.Files`（9→10 表）而手写清单未跟——审计预测的"新表只入单源即在此漏登记、该表静默不在门禁罩内"**已真实发生**，窗口至 `5e7fe22`（19:33）约 3 小时（tbltext 为 key→zh/en 文案表、无路径列，窗口内无实际漏检面——但漂移形态实证，单源化消解的是整类而非补一行）。

## 本批本体：记录/索引/登记回写

| 面 | 处置 |
| --- | --- |
| 施工记录 | 本文件（批C-② 与 uitools 两段补记——批C 系列 ②③④⑤⑥ 记录自此全齐） |
| 索引行 | `施工进度/README.md` 补一行（此前缺口即本批挂账来源） |
| 审计修复登记 | 《代码质量审计-客户端》修复登记表加行（§二「表清单第二份」→ 已修复，已提交 `5e7fe22`）＋条目就地注记（登记表口径：不删除条目） |
| 挂账收口 | [上云批C入口编排](上云批C入口编排.md) §边界"挂批C-⑤ 补记"行、《上云测试专项设计》进展行（批C-④/⑤/⑥ 交付事实＋剩余面收正）、[UI工具链](UI工具链.md) 步 5 遗留"表清单显式登记"条目 |

## 验证证据

| 项 | 方式 | 结果 |
| --- | --- | --- |
| 本批复跑（2026-10-14，TestRunnerApi 定向 EditMode） | 两 fixture 定向执行 | **LTextWiring 5/5 · UiValidation 14/14，overall Passed**（12.2s）——UiValidation 含门禁"真实资产面三规则零 Error"用例＝单源派生装载面（`BuildTables()` 解析 10 表 + 真实表装载提取）实跑核证 |
| 提交时点证据（按提交自述） | `5e8634a` | 重编译 0 错 · **L1 1636/1636** · EditMode 新 5 例 + LTextBind 9 例与 CombatNumbers 2 例回归绿 |
| 提交时点证据（按提交自述） | `5e7fe22` | 重编译 0 错误 · UiValidation 真实表装载提取零悬空、全量门禁 Warning 计数不变。**注**：提交自述"20/20"与本文件事实不符——该 fixture 按 `[Test]` 计 14 例（`071c391f` 起未变、`5e7fe22` 未动测试文件），以本批复跑 **14/14** 为准；提交历史不改写 |
| 门禁面 | 本批零代码/资产改动 | 编译与 L1 基线随上一代码批（批 E：重编译 0 错 · L1 1642/0）不动；本批门禁＝Doc lane：**doc-lint OK（0 violation / 0 warning）** |

## 边界与备注（如实）

- 本批为纯文档补记：两提交的代码早已在库且各有提交时点验证；本批不重复跑 L1/L2 全量（EditMode 全量/PlayMode 归夜间流水线）；
- 端到端真联机手测（`-meta.url` + 真 Meta + 真房服 + 真 KCP/信封，含 `Bridge.text` 语言切换）需部署环境，归《上云测试专项设计》§6 第 1–3 项与批B 云侧执行；
- 批C 系列至此全部落记录；上云专项剩余面＝批B **云侧执行**（安全组+白名单+证书，§6 第 1 项）与 §6 第 2–3 项（端到端手工验收——需云主机与真客户端）。
