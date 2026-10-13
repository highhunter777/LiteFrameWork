# 上云批C-③：联机入口编排 + Bridge.meta 门面（接手 WIP 收尾）

> 依据：《上云测试专项设计》§2 客户端闭环（游客登录 → 房间列表 → Join Ticket → 现有 Join 管道）与 §3 批C；《上云测试页面层级设计》§10 待接线口径
> 范围：MetaEntryService 编排件、Bridge.meta Lua 门面、ProductBridges 装订单源、ProcedureArgs.MetaJoin 联机路径、-meta.*/-envelope.key 注入、装配面接线、定向测试
> 状态：**已完成（附验证证据）**——2026-10-13（接手并行线 WIP：补编译阻断修复、命名归位与收尾验证后转绿）
> 不含：五页 Lua 接线与文案键启用（页面接线批 C-④）；批C-② 与 uitools 单源化的补记（挂批C-⑤ 收尾批）

## 交付物

| 项 | 落点 |
| --- | --- |
| 联机入口编排 | `Assets/LiteGame/App/Meta/MetaEntryService.cs`——游客登录（缓存账号/令牌，令牌不出服务面）、房间列表、换票进房（`Request(Match, MetaJoin)` 走现有 Join 管道）、最近战绩；缺 `-meta.url` = `IsConfigured=false` 显性拒绝（fail-closed 不回落本地）；设备标识经 `Func<string>` 注入（本体零引擎依赖，EditMode 直测） |
| Lua 门面 | `Assets/LiteGame/App/Meta/MetaBridge.cs`——`Bridge.meta`：GuestLogin/ListRooms/QuickEnter/EnterRoom/ListRecords + IsConfigured/AccountId/ServerUrl；回调恰一次（主线程续延）、失败只回稳定码/Network/InvalidResponse（不含原文）；行字段手工展开 LuaTable（令牌/票据不出桥） |
| 装订单源 | `Assets/LiteGame/App/ProductBridges.cs`——两条 env 重建路（Preload 首建 / DevReload 重建）唯一装订口：框架四门面 + meta；`ProcedurePreload.cs`/`Editor/DevReload.cs` 换指本口 |
| 联机进房参数 | `Assets/LiteGame/App/Procedure/MetaJoin.cs`（`rename_asset` 保 GUID）+ `ProcedureArgs.MetaJoin` + `ProcedureMatch` 联机路径放行（票据非空即走；正式构建的测试身份门禁保留） |
| 信封/基址注入 | `ProcedureMatch.CreateTransportForFactory(testRoom, deploy)`——`-envelope.key` 优先（非法键 fail-fast 抛），空回落 `LITENET_SECURE_ENVELOPE_KEY` 既有环境变量口 |
| 装配接线 | `ContainerModule`：deploy 单次快照 → UnityMetaClient（未配置置空）→ `MetaBridge.Bind` → `BindMachine(fsm)` |
| 测试可见性 | `Assets/LiteGame/App/AssemblyInfo.cs`——`InternalsVisibleTo("LiteGame.EditModeTests")`（先例：LiteSim.Core→LiteSim.Core.Tests、RoomServer→LiteNet.Tests） |
| 定向测试 | `Assets/Tests/EditMode/MetaEntryEditModeTests.cs`——9 例：fail-closed 零 Request／登录门禁／换票地址解析并精确移交 Match／地址不可解析零 Request／流程机未绑定显性失败／门面回调契约（恰一次+稳定码）／行形状手工展开／信封注入优先+非法键 fail-fast |

## 接手修复（WIP 交接差异处置）

| # | 问题 | 处置 |
| --- | --- | --- |
| ① | R12 红：`App/Meta/MetaBridge.cs:5`、`App/ProductBridges.cs:1` 的 `using XLua` 落在边界目录外（L1 纪律扫描实测） | 本仓首次 `lint-allow R12` 行内豁免 + 写明理由——门面本体须引用 App 侧编排件（LuaBridge 不引用 App，依赖方向不可反），本体只做 env 装订/行展开 |
| ② | 编译阻断：`MetaEntryEditModeTests` CS0117——`ProcedureMatch.CreateTransportForFactory` 为 internal 而测试程序集无 IVT，**该测试从未编译过** | 补 `AssemblyInfo.cs` IVT，重编译 0 错误 |
| ③ | 命名遗留（裁决 B〔`5fcd2bd`〕之后新写）：`CloudJoin` 类型/文件与"云路径"措辞 | `CloudJoin→MetaJoin` 全量归位（类型/文件/字段/参数名），"云路径→联机路径"，引用 6 文件清扫 |
| ④ | 接线纪律：`context.Put(metaEntry)` 无容器解析点 | 退役容器键——消费者＝`Bridge.meta` 门面（`MetaBridge.Bind` 直接装订），注释注明 |

## 证据

| 项 | 命令/方式 | 结果 |
| --- | --- | --- |
| 重编译 | unity-pipeline `recompile` → `recompile_status` | `completed / failed:false / errors:[]` |
| 定向 EditMode（实跑） | unity-pipeline `run_tests --mode editor --filter <fixture>` | MetaEntry **9/9** · LTextWiring **5/5** · AppLifetime **3/3** · ReleaseLayout **6/6** |
| L1 门禁 | `scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` | **1636/1636**（含纪律扫描零违规——lint-allow 生效实证） |
| BuildHash | `scripts/codegen/gen-build-hash.py --check` | 双常量 OK（本批不进源集，无需重录） |

## 边界与备注（如实）

- ~~**页面接线批（批C-④）挂账**：五页 Lua 骨架现为空实现——`Bridge.meta` 调用点、页面跳转（Login→Lobby→Room→Result）与文案键启用（LText 链已就绪，`SetTextKey` 现可用）归批 C-④；`MetaBridge` 类头已挂批号。~~ **已交付（批C-④，2026-10-13）**：见[上云批C页面接线](上云批C页面接线.md)——五页接线 + 跳转链 + 文案键启用；控件/列表协议挂批C-⑥。
- 批C-②（`5e8634a` BindLocale 生产装配）与 uitools 表清单单源化（`5e7fe22`）尚无施工记录/索引行——按其提交自述挂批C-⑤ 收尾批补记。
- 存量债同型（如实）：`MetaBridge.s_meta` 静态装订沿用 `Bridge.Data` 静态定位器范式（《代码质量审计-客户端》[中] 债同族）；登录态跨 env 重建存活为有意语义。
- 未跑 L2 全量（EditMode 全量/PlayMode 归夜间流水线）；本批为 C# 装配面 + EditMode 定向，未触碰序列化资产与 Lua。
- 提交面排除：`ProjectSettings/GraphicsSettings.asset`（本机编辑器序列化版本迁移噪声，与本批无关）。
