# 实施任务清单：同步层拆分与判定参数化（LiteSync 去耦 + LiteSim 参数装载）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 17/84（取消 67，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | LiteSync 程序集与空壳 | - | 取消 | B1：Assets/LiteSync 存在，asmdef references=[LiteNet,LiteSim]+noEngineReferences+Google.Protobuf 预编译，双轨 csproj 齐备；Unity 重编译 0 error | Unity 重编译；文件/asmdef/csproj 检查 | - |
| T2 | 同步件搬迁与引用收口 | T1 | 取消 | B2/B3/B6/B7：15 件文件+meta 按目录搬迁，LiteNet 零 using LiteSim，WeakNet 移 Dev，DiagTrace 保留登记，引用者/测试改引用后编译绿 | grep using LiteSim 零命中；Unity 重编译；Tests/LiteNet.Tests | - |
| T3 | BuildHash 源集与架构守卫 | T2 | 取消 | B4/B5/B22：SHARED_TARGETS 含 LiteSync/{Shared,Snapshot,Input,Events} 不含 Client；BuildHash 路径/命名空间不变；Allowed 删 LiteNet->LiteSim 加 LiteSync；ClientRoots/StaticMutable 扫描根含 LiteSync；硬边与负例生效 | gen-build-hash.py --check-shared；ClientAsmdefAllowlistTests/ArchitectureGuardTests/StaticMutableAllowlistTests | - |
| T4 | 两端重建与联测 | T3 | 取消 | B8：L1 全量绿；两端重建后联机冒烟通过且 BuildHash 一致 | gate/test.py --suite L1；build-player.py；player-smoke；两端 BuildHash 对账 | - |
| T5 | CombatGeometry 与发布闸门 | T4 | 取消 | B9：CombatValues 11 字段/9 参数；CombatGeometry 13 列；CombatConfig _published 三态与 Geometry 展示兜底；10 处构造点更新；候选表校验坏表拒启动 | CombatGeometry/CombatValues EditMode；ResetForTests；坏表用例；L1 | - |
| T6 | 判定面与外部消费者迁移 | T5 | 取消 | B10：Assets/Server/Tests 旧几何名零命中；ResolveHead*/override 字段零命中；HUD/Dev/Editor/测试读者迁移；判定/烘焙不读 Geometry；SimStep 与 9 System 签名不变 | grep 守卫含负例；Unity 0 error；定向 EditMode | - |
| T7 | 表接入、摘要与值校验 | T6 | 取消 | B11/B13/B14：TableLayout.Files=Combat/tbcombatgeom；__tables__/ConfigService/ServerTableLoader 调用点全更新；表字节进摘要且联机不含覆盖；13 值与装载结果 0 容差 + 黄金样本 | Luban/gen.bat；gen-build-hash --check-shared；CombatConfigDigest/GameplayDigestBase 用例 | - |
| T8 | 烘焙全迁与消费面 | T7 | 取消 | B12：五件 .g.cs 不存在；五烘焙器改产表/数据；SimMapBaker 读 CC 并断言表 row≡入库夹具；Grip 消费面迁移；restore-check 前置；注释 0 warning；WriteGenerated 删除 | 烘焙工具实操；BodyBake/SimMapBaker/WeaponGrip EditMode；Unity 0 warning | - |
| T9 | AOI 与广播拆分 | T8 | 取消 | B15：AoiSettings 服务端真实注入三档；AoiFilter 无静态缓存；SnapshotHz 与 AoiSettings 分离，客户端从 JoinAck 取；SimView 无静态 SimConfig.SnapshotHz 读 | 服务端配置注入；EditMode/Play 用例；grep 静态缓存零命中 | - |
| T10 | RootMotion 数据产物 | T9 | 取消 | B16/B17/B18：LiteSim 无 RootMotion*；rootmotion.bin 在 GameData/Config/runtime；RootMotionCatalog.Load 在 Battle/Arena；ContentIdentity 用字节摘要；.bin 不进 SHARED_TARGETS | RootMotionDataTests；gen-build-hash --check-shared；文档登记 | - |
| T11 | 调参面与导出 | T10 | 取消 | B19/B20/B21：13 项滑杆；单门 SimParamsGate + 单合并点；联机禁用覆盖；四条断言；编辑器/GM 导出到表 + diff/备份/check-shared；.bin 导出显示摘要 | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作 | - |
| T12 | 门禁与文档收口 | T11 | 取消 | B23/B24：sync-and-sim 命令组通过；四份设计文档+两份进度卷回写；索引登记；doc-lint 全过 | wf verify；doc-lint.py；L2 | - |
| T13 | LiteSync 程序集与空壳 | - | 取消 | B1：Assets/LiteSync 存在，asmdef references=[LiteNet,LiteSim]+noEngineReferences+Google.Protobuf 预编译；双轨 csproj 齐备；Unity 与 dotnet 双轨均编译通过 | Unity 重编译 0 error；dotnet build 双轨 csproj（Tests.slnx/LiteNet.csproj/LiteSync.csproj）；文件/asmdef 检查 | - |
| T14 | 15 件搬迁、引用收口与 BuildHash 重生成 | T13 | 取消 | B2/B3/B4/B6/B7：15 件+meta 搬迁，LiteNet 零 using LiteSim/LiteSync，SHARED_TARGETS 增 LiteSync/{Shared,Snapshot,Input,Events}（不含 Client），重生成 BuildHash；WeakNet 移 Dev，DiagTrace 保留登记，引用者/测试编译绿 | grep 零命中；Unity+dotnet 编译；Tests/LiteNet.Tests；DebugKernelEditModeTests；gen-build-hash.py 重生成 + --check-shared | - |
| T15 | 架构守卫与静态可变扫描根 | T14 | 取消 | B5/B22：Allowed 删 LiteNet->LiteSim 加 LiteSync；ClientRoots/StaticMutable 根含 LiteSync；两条硬边与植入违规负例生效；无新增 public static 可变字段 | ClientAsmdefAllowlistTests/ArchitectureGuardTests/StaticMutableAllowlistTests；gen-build-hash --check-shared | - |
| T16 | 两端重建与联测 | T15 | 取消 | B8：L1 全量绿；两端重建后本地测试房+KCP lossy 冒烟通过；两端 BuildHash 一致（留证据） | gate/test.py --suite L1；build-player.py；player-smoke；两端 BuildHash 对账 | - |
| T17 | CombatGeometry 类型与发布闸门 | T16 | 取消 | B9（类型/闸门部分）：CombatValues 11 字段/9 参数；_published 三态；Geometry 展示兜底 DisplayOnly；10 处构造点更新；ResetForTests+[Collection]；Loaded 时序/编辑器未发布不崩 用例；坏表候选校验（表接入在 T18） | CombatGeometry/CombatValues EditMode；ResetForTests；Loaded 三态；dotnet+Unity 编译 | - |
| T18 | 表接入与装载 | T17 | 取消 | B11：__tables__ + TableLayout.Files=Combat/tbcombatgeom；JudgmentFiles 自动包含；ConfigService/ServerTableLoader/测试调用点全更新；坏表在 ValidateCandidate 拒启动 | Luban/gen.bat；gen-build-hash --check-shared；表装载/坏表用例 | - |
| T19 | 判定面与外部消费者迁移 | T18 | 取消 | B10：Assets/Server/Tests 旧几何名零命中；ResolveHead*/override 零命中；HUD/Dev/Editor/测试读者迁移；判定/烘焙不读 Geometry；SimStep 与 9 System 签名不变 | grep 守卫含负例；Unity 0 error；定向 EditMode | - |
| T20 | 摘要与值校验 | T19 | 取消 | B13/B14：Combat/tbcombatgeom 进 JudgmentFiles⇒表字节进摘要；联机摘要不含覆盖；13 值与表装载结果 0 容差；黄金样本/更新流程与测试名落档 | CombatConfigDigest/GameplayDigestBase 用例；黄金样本；gen-build-hash --check-shared | - |
| T21 | Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated | T20 | 取消 | B12（部分）：Body/Head/Muzzle 三件 .g.cs 退役；三烘焙器写表（写入源只来自 prefab/anchor）；SimMapBaker 读 CC 并断言表 row≡入库 CC 夹具；restore-check 前置；WriteGenerated 零引用删除；注释 0 warning | 烘焙工具实操；BodyBake/SimMapBaker EditMode；Unity 0 warning | - |
| T22 | Grip 烘焙与消费面 | T21 | 取消 | B12（Grip）：GripBake.g.cs 退役；WeaponGripBaker 产 grip.bin；WeaponGripCatalog.Load 落 Battle/Characters；WeaponGripConstraint/WeaponAimConstraint/AimPitchPostureDriver/GripTuning/FeatureDebugTunables 迁移；grip.bin 摘要预期 | WeaponGripConstraintEditMode；catalog 用例；烘焙摘要 | - |
| T23 | RootMotion 数据产物 | T22 | 取消 | B16/B17/B18：先落 RootMotionCatalog.Load/rootmotion.bin/Digest/AppComposition 绑定，再删 RootMotionData.g.cs；LiteSim 无 RootMotion*；.bin 不进 SHARED_TARGETS；已烘焙未接线登记 | RootMotionDataTests；gen-build-hash --check-shared；文档登记 | - |
| T24 | AOI 与广播拆分 | T23 | 取消 | B15：AoiSettings 落 LiteSync/Shared；服务端真实配置注入三档；AoiFilter 无静态缓存；SnapshotHz 分离，客户端从 JoinAck 取；SimView 无静态读；ThrottleRadius 系数常量/表单源 | 服务端配置注入；EditMode/Play；grep 静态缓存零命中 | - |
| T25 | 调参面与导出 | T24 | 取消 | B19/B20/B21：13 项滑杆；单门 SimParamsGate.Evaluate+单合并点；联机禁用覆盖；B20 四条断言；编辑器/GM 导出到表+diff/备份/check-shared；.bin 导出显示摘要 | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作 | - |
| T26 | 自动门禁命令组（主代理） | T25 | 取消 | B23（自动部分）：.workflow/config.json 增 sync-and-sim 命令组（L1/LiteSync/LiteSim 定向/check-shared/L2），wf verify 通过；人工联测证据不在本任务 | wf verify；证据列出实际命令 | - |
| T27 | 文档回写与人工联测证据 | T26 | 取消 | B24+B23（人工部分）：3 份设计+2 份进度卷回写、索引登记、doc-lint 全过；两端重建/联测命令与 BuildHash 对账记录归档 | doc-lint.py；人工联测证据 | - |
| T28 | LiteSync 程序集与双轨编译 | - | 取消 | B1：Assets/LiteSync 存在，asmdef refs=[LiteNet,LiteSim]+noEngineReferences+Protobuf 预编译；双轨 csproj 齐备；Unity 与 dotnet 双轨编译通过。执行者：施工子代理（纯 C#/asmdef，不碰门禁脚本） | Unity 0 error；dotnet build LiteSync.csproj/LiteNet.csproj/Tests.slnx；文件/asmdef 检查 | - |
| T29 | 15 件搬迁、引用收口与 BuildHash 重生成 | T28 | 取消 | B2/B3/B4/B6/B7：15 件+.meta 走 unity-pipeline/AssetDatabase.MoveAsset；LiteNet.asmdef/csproj 删 LiteSim；SHARED_TARGETS 增 LiteSync/{Shared,Snapshot,Input,Events}（不含 Client）并重生成 BuildHash；WeakNet 移 Dev；DiagTrace 保留登记。执行者：资产搬迁+gen-build-hash.py 由主代理，C#/测试由施工子代理 | unity-pipeline 回读+git status 成对；grep 零命中；Unity+dotnet 编译；Tests/LiteNet.Tests；DebugKernelEditModeTests；gen-build-hash.py 重生成 + --check-shared | - |
| T30 | 架构守卫与静态可变扫描根 | T29 | 取消 | B5/B22：Allowed 删 LiteNet->LiteSim 加 LiteSync；ClientRoots 含 LiteSync；StaticMutable 根含 LiteNet+LiteSync；两条硬边与植入违规负例生效 | ClientAsmdefAllowlistTests/ArchitectureGuardTests/StaticMutableAllowlistTests；gen-build-hash --check-shared | - |
| T31 | 两端重建与联测（批 6） | T30 | 取消 | B8：L1 全量绿；两端重建后本地测试房+KCP lossy 冒烟通过；两端 BuildHash 一致；证据落盘 | gate/test.py --suite L1；build-player.py；player-smoke；BuildHash 对账 | - |
| T32 | CombatGeometry 类型与发布闸门 | T31 | 取消 | B9（类型/闸门）：11 字段/9 参数；_published 三态；Geometry 展示兜底 DisplayOnly；10 处构造点；ResetForTests+[Collection]；Loaded 时序/编辑器未发布不崩；坏表候选校验（表接入在 T33） | CombatGeometry/CombatValues EditMode；Loaded 三态/时序；dotnet+Unity 编译 | - |
| T33 | 表接入、13 列映射与调用点 | T32 | 取消 | B11+B9（表部分）：__tables__ + TableLayout.Files=Combat/tbcombatgeom；JudgmentFiles 自动包含；BuildCombatValues(...,tbcombatgeom) 13 列映射；ConfigService/ServerTableLoader/测试调用点全更新；坏表 ValidateCandidate 拒启动；改共享源后重生成 BuildHash | Luban/gen.bat；gen-build-hash.py 重生成+--check-shared；表/坏表/映射用例 | - |
| T34 | 判定面与外部消费者迁移 | T33 | 取消 | B10：Assets/Server/Tests 旧几何名零命中；删除 ResolveHead*/override 四几何项；HUD/Dev/Editor/测试读者迁移；判定路径与烘焙写入源不读 Geometry（显示/对比白名单）；SimStep 与 9 System 签名不变；改共享源后重生成 BuildHash | grep 守卫（含白名单/负例）；Unity 0 error；定向 EditMode；gen-build-hash 重生成+check | - |
| T35 | 摘要与值校验 | T34 | 取消 | B13/B14：Combat/tbcombatgeom 进 JudgmentFiles⇒表字节进摘要；联机摘要不含覆盖；13 值与表装载结果 0 容差；黄金样本/更新流程/测试名落档；改共享源后重生成 BuildHash | CombatConfigDigest/GameplayDigestBase 用例；黄金样本；gen-build-hash 重生成+check | - |
| T36 | Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated | T35 | 取消 | B12（部分）：先落表写入器/新行；三件 .g.cs 走 unity-pipeline 删除；三烘焙器写表（源只来自 prefab/anchor）；SimMapBaker 读 CC 并断言表 row≡入库 CC 夹具；restore-check 前置；WriteGenerated 零引用删除；注释 0 warning；改共享源后重生成 BuildHash。执行者：资产删除由主代理，代码/测试由施工子代理 | 烘焙工具实操；BodyBake/SimMapBaker EditMode；Unity 0 warning；gen-build-hash 重生成+check | - |
| T37 | Grip 烘焙与消费面 | T36 | 取消 | B12（Grip）：先落 WeaponGripCatalog/grip.bin 与消费面，再删 GripBake.g.cs（unity-pipeline）；WeaponGripBaker 产 grip.bin；WeaponGripConstraint/WeaponAimConstraint/AimPitchPostureDriver/GripTuning/FeatureDebugTunables 迁移；内容摘要含 grip.bin。执行者：资产删除主代理，代码/测试施工子代理 | WeaponGripConstraintEditMode；catalog 用例；烘焙摘要；Unity 0 error | - |
| T38 | RootMotion 数据产物 | T37 | 取消 | B16/B17/B18：先落 RootMotionCatalog.Load/rootmotion.bin/Digest/AppComposition 绑定，再删 RootMotionData.g.cs（unity-pipeline）；LiteSim 无 RootMotion*；.bin 不进 SHARED_TARGETS；已烘焙未接线登记。执行者：资产删除主代理，代码/测试施工子代理 | RootMotionDataTests；gen-build-hash 重生成+--check-shared；文档登记 | - |
| T39 | AOI 与广播拆分 | T38 | 取消 | B15：AoiSettings 落 LiteSync/Shared；服务端真实配置（deploy/roomserver.windows.json）注入三档；AoiFilter 无静态缓存；SnapshotHz 分离，客户端从 JoinAck 取；SimView 无静态读；ThrottleRadius 为命名常量/表列；改共享源后重生成 BuildHash | 服务端配置注入；LocalServerHandshake/EditMode/Play；grep 零命中；gen-build-hash 重生成+check | - |
| T40 | 调参面与导出 | T39 | 取消 | B19/B20/B21：13 项滑杆；dev 三宏；单门 SimParamsGate.Evaluate 每会话一次 bool；单合并点 ConfigService+ServerTableLoader；联机禁用覆盖；删除四几何覆写、保留 Damage/Rpm；B20 四条断言；导出/diff/备份/check-shared；.bin 摘要；改共享源后重生成 BuildHash | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作；gen-build-hash 重生成+check | - |
| T41 | 自动门禁命令组（主代理） | T40 | 取消 | B23（自动部分）：.workflow/config.json 增 sync-and-sim 自动命令组（L1/LiteSync/LiteSim 定向/check-shared/L2），wf verify 通过；不含人工联测。执行者：主代理独占（施工子代理禁改） | wf verify；证据列出实际命令 | - |
| T42 | 文档回写与最终两端联测 | T41 | 取消 | B24+B23（人工部分）：3 份设计+2 份进度卷回写、索引登记、doc-lint；批次 7 后实际执行 build-player/player-smoke，两端 BuildHash+ConfigHash 对账并留证。执行者：文档由施工子代理，最终联测由主代理 | doc-lint.py；build-player.py/player-smoke；两端摘要对账证据 | - |
| T43 | LiteSync 程序集与双轨编译 | - | 完成 | B1：Assets/LiteSync 存在，asmdef refs=[LiteNet,LiteSim]+noEngineReferences+Protobuf 预编译；双轨 csproj 齐备；Unity+dotnet 双轨编译通过。执行者：施工子代理（纯 C#/asmdef） | Unity 0 error；dotnet build LiteSync.csproj/LiteNet.csproj/Tests.slnx；文件/asmdef 检查 | 新增 Assets/LiteSync/{LiteSync.asmdef,LiteSync.csproj,Directory.Build.props}；dotnet build Assets/LiteSync/LiteSync.csproj… |
| T44 | 15 件搬迁、引用收口与 BuildHash 重生成 | T43 | 完成 | B2/B3/B4/B6/B7：15 件+.meta 走 unity-pipeline；LiteNet.asmdef/csproj 删 LiteSim；LiteGame.App/Editor、Server/RoomServer、Tests.slnx 补 LiteSync 引用；SHARED_TARGETS 增 Four dirs（不含 Client）并重生成 BuildHash（主代理执行）；WeakNet 移 Dev；DiagTrace 保留。执行者：资产搬迁/gen-build-hash 主代理，C#/测试施工子代理 | unity-pipeline 回读+git status 成对；grep 零命中；Unity+dotnet 编译；Tests/LiteNet.Tests；DebugKernelEditModeTests；gen-build-hash 重生成+--check-shared | 15 件+WeakNet 经 unity-pipeline 搬迁（GUID/meta 保留）；LiteNet.asmdef refs=[]、csproj 无 LiteSim 引用、using LiteSim 零命中；SHARED_TARG… |
| T45 | 架构守卫与静态可变扫描根 | T44 | 取消 | B5/B22：Allowed 删 LiteNet->LiteSim 加 LiteSync；ClientRoots 含 LiteSync；StaticMutable 根含 LiteNet+LiteSync；两条硬边与植入违规负例生效 | ClientAsmdefAllowlistTests/ArchitectureGuardTests/StaticMutableAllowlistTests；gen-build-hash --check-shared | - |
| T46 | 两端重建与联测（批 6） | T45 | 取消 | B8：L1 全量绿；两端重建后本地测试房+KCP lossy 冒烟通过；两端 BuildHash 一致；证据落盘 | gate/test.py --suite L1；build-player.py；player-smoke；BuildHash 对账 | - |
| T47 | CombatGeometry 类型与发布闸门 | T46 | 取消 | B9（类型/闸门）：11 字段/9 参数；_published 三态；Geometry DisplayOnly；10 构造点；ResetForTests+[Collection]；Loaded 时序/编辑器不崩；坏表候选校验（表接入在 T48）；改 Shared 源后重生成 BuildHash（主代理） | CombatGeometry/CombatValues EditMode；Loaded 三态/时序；dotnet+Unity 编译；gen-build-hash 重生成+--check-shared | - |
| T48 | 表接入、13 列映射与调用点 | T47 | 取消 | B11+B9（表）：__tables__ + TableLayout.Files=Combat/tbcombatgeom；JudgmentFiles 自动包含；BuildCombatValues(...,tbcombatgeom) 13 列映射；ConfigService/ServerTableLoader/测试调用点全更新；坏表 ValidateCandidate 拒启动；重生成 BuildHash（主代理） | Luban/gen.bat；gen-build-hash 重生成+--check-shared；表/坏表/映射用例 | - |
| T49 | 判定面与外部消费者迁移 | T48 | 取消 | B10：Assets/Server/Tests 旧几何名零命中；**T49 唯一负责删除** ResolveHead*/override 四项；HUD/Dev/Editor/测试读者迁移；判定路径与烘焙写入源不读 Geometry（显示/对比白名单）；SimStep 与 9 System 签名不变；重生成 BuildHash（主代理） | grep 守卫（含白名单/负例）；Unity 0 error；定向 EditMode；gen-build-hash 重生成+check | - |
| T50 | 摘要与值校验 | T49 | 取消 | B13/B14：Combat/tbcombatgeom 进 JudgmentFiles⇒表字节进摘要；联机摘要不含覆盖；13 值与表装载结果 0 容差；黄金样本/更新流程/测试名落档；两端摘要不等拒局用例 | CombatConfigDigest/GameplayDigestBase 用例；黄金样本；gen-build-hash 重生成+check | - |
| T51 | Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated | T50 | 取消 | B12（部分）：先落表写入器/新行；三件 .g.cs 走 unity-pipeline 删除；三烘焙器写表（源只来自 prefab/anchor）；SimMapBaker 读 CC 并断言表 row≡入库 CC 夹具；`BakeTargets` 五条输出常量逐条去向+四条死路径修正+`PrefabPath` 绑定；restore-check 前置；WriteGenerated 删除；注释 0 warning；重生成 BuildHash（主代理） | 烘焙实操；BodyBake/SimMapBaker EditMode；Unity 0 warning；gen-build-hash 重生成+check | - |
| T52 | Grip 烘焙与消费面 | T51 | 取消 | B12（Grip）：先落 WeaponGripCatalog/grip.bin 与消费面，再删 GripBake.g.cs（unity-pipeline，位于 LiteGame/Modules/Battle 非共享源集，无需 BuildHash 重生成）；WeaponGripBaker 产 grip.bin；WeaponGripConstraint/WeaponAimConstraint/AimPitchPostureDriver/GripTuning/FeatureDebugTunables 迁移；内容摘要含 grip.bin。执行者：资产删除主代理，代码/测试施工子代理 | WeaponGripConstraintEditMode；catalog 用例；烘焙摘要；Unity 0 error | - |
| T53 | RootMotion 数据产物 | T52 | 取消 | B16/B17/B18：先落 RootMotionCatalog.Load/rootmotion.bin/Digest/AppComposition 绑定，再删 RootMotionData.g.cs（unity-pipeline）；LiteSim 无 RootMotion*；.bin 不进 SHARED_TARGETS；ContentIdentity 不引 LiteSim/SHA-256/无名称字段；已烘焙未接线登记；重生成 BuildHash（主代理） | RootMotionDataTests；gen-build-hash 重生成+--check-shared；文档登记 | - |
| T54 | AOI 与广播拆分 | T53 | 取消 | B15：AoiSettings 落 LiteSync/Shared；服务端真实配置（deploy/roomserver.windows.json 三档 + GM 房参数两路）注入；AoiFilter 无静态缓存；SnapshotHz 分离，客户端从 JoinAck 取；SimView 无静态读；ThrottleRadius 命名常量/表列；重生成 BuildHash（主代理） | 服务端配置注入；LocalServerHandshake/EditMode/Play；grep 零命中；gen-build-hash 重生成+check | - |
| T55 | 调参面与导出 | T54 | 取消 | B19/B20/B21：13 项滑杆；dev 三宏；单门 SimParamsGate.Evaluate 每会话一次 bool；单合并点 ConfigService+ServerTableLoader；联机/云 sandbox 禁用覆盖（断言）；判定路径零静态读（grep/负例）；四几何覆写**只做零命中复核**（删除在 T49）；Damage/Rpm 保留；B20 四条断言；导出/diff/备份/check-shared；.bin 摘要；重生成 BuildHash（主代理） | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作；gen-build-hash 重生成+check | - |
| T56 | 自动门禁命令组（主代理） | T55 | 取消 | B23（自动部分）：.workflow/config.json 增 sync-and-sim 自动命令组（L1/LiteSync/LiteSim 定向/check-shared/L2），wf verify 通过；不含人工联测。执行者：主代理独占 | wf verify；证据列出实际命令 | - |
| T57 | 文档回写与最终两端联测 | T56 | 取消 | B24+B23（人工部分）：3 设计+2 进度卷回写/索引/doc-lint；批次 7 后实际执行 build-player/player-smoke，两端 BuildHash+ConfigHash 对账并留证。执行者：文档施工子代理，最终联测主代理 | doc-lint.py；build-player.py/player-smoke；两端摘要对账证据 | - |
| T58 | CombatGeometry 类型与发布闸门 | T46 | 取消 | B9（类型/闸门）：11 字段/9 参数；_published 三态；Geometry DisplayOnly；10 构造点；ResetForTests+[Collection]；Loaded 时序断言（`BattleContextTests`，范围含 `Assets/LiteGame/Modules/Battle/Arena`）；编辑器未发布不崩；坏表候选校验（表接入 T59）；重生成 BuildHash 主代理。执行者：主代理（重生成），施工子代理（代码/测试） | CombatGeometry/CombatValues/BattleContext EditMode；Loaded 三态/时序；dotnet+Unity；gen-build-hash 重生成+check | - |
| T59 | 表接入、13 列映射与调用点 | T58 | 取消 | B11+B9（表）：__tables__ + TableLayout.Files=Combat/tbcombatgeom；JudgmentFiles 自动包含；BuildCombatValues(...,tbcombatgeom) 13 列映射；ConfigService/ServerTableLoader/测试调用点全更新；坏表 ValidateCandidate 拒启动；生成读码/表字节落 `Assets/GameData/Generated`/`Config`；重生成 BuildHash 主代理。执行者：主代理（gen/build-hash），施工子代理（代码/测试） | Luban/gen.bat；gen-build-hash 重生成+check；表/坏表/映射用例；GameData 生成物检查 | - |
| T60 | 判定面与外部消费者迁移 | T59 | 取消 | B10：Assets/Server/Tests 旧几何名零命中；T60 唯一删除四几何覆写；HUD/Dev/Editor/测试读者迁移；判定路径与烘焙写入源不读 Geometry（显示/对比白名单）；Arena 时序/断言；SimStep 与 9 System 签名不变；重生成 BuildHash 主代理。执行者：主代理（重生成），施工子代理（代码/测试） | grep 守卫（含白名单/负例）；Unity 0 error；定向 EditMode；gen-build-hash 重生成+check | - |
| T61 | 摘要与值校验 | T60 | 取消 | B13/B14：表字节进摘要；联机摘要不含覆盖；13 值与表装载结果 0 容差；改表后 GameplayDigestBase 变化可观测（测试名）；黄金样本/更新流程；两端摘要不等拒局。执行者：施工子代理 | CombatConfigDigest/GameplayDigestBase 用例；黄金样本；gen-build-hash 重生成+check | - |
| T62 | Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated | T61 | 取消 | B12：先落表写入器/新行；三件 .g.cs 走 unity-pipeline 删除（主代理执行）；三烘焙器写表（源只来自 prefab/anchor）；SimMapBaker 读 `BakeTargets.PrefabPath=Assets/ShareResource/Characters/Player(Rifle).prefab` 的 CC 并断言表 row≡`Tests/EditMode/Fixtures/BodyCc.json`（半径+高）；**BakeTargets 五常量去向**：Head/Muzzle/Body→#combatgeom 列，RootMotion→rootmotion.bin，Grip→grip.bin；四条死路径 = BakeTargets.cs:36-38/46 的 `Assets/LiteSim/Core/Scripts/*`→新去向；restore-check 前置；WriteGenerated 删除；注释 0 warning；重生成 BuildHash 主代理 | 烘焙实操；BodyBake/SimMapBaker EditMode；夹具断言；Unity 0 warning；gen-build-hash 重生成+check | - |
| T63 | Grip 烘焙与消费面 | T62 | 取消 | B12（Grip）：先落 WeaponGripCatalog/grip.bin 与消费面，再删 GripBake.g.cs（unity-pipeline，主代理；非共享源集，无需 BuildHash）；WeaponGripBaker 产 grip.bin；WeaponGripConstraint/WeaponAimConstraint/AimPitchPostureDriver/GripTuning/FeatureDebugTunables 迁移；内容摘要含 grip.bin。执行者：主代理（删除），施工子代理（代码/测试） | WeaponGripConstraintEditMode；catalog 用例；烘焙摘要；Unity 0 error | - |
| T64 | RootMotion 数据产物 | T63 | 取消 | B16/B17/B18：先落 RootMotionCatalog.Load/rootmotion.bin/Digest/AppComposition 绑定，再删 RootMotionData.g.cs（unity-pipeline，主代理）；LiteSim 无 RootMotion*；.bin 不进 SHARED_TARGETS；ContentIdentity 不引 LiteSim/SHA-256/无名称字段；已烘焙未接线登记；重生成 BuildHash 主代理。执行者：主代理（删除/重生成），施工子代理（代码/测试） | RootMotionDataTests；gen-build-hash 重生成+check；文档登记 | - |
| T65 | AOI 与广播拆分 | T64 | 取消 | B15：AoiSettings 落 LiteSync/Shared；**两路真实注入**：deploy/roomserver.windows.json 全开/30m/15m 三档 + GM `start` 创建 sandbox 房后回读生效值（scripts/deploy/gm.py）；AoiFilter 无静态缓存；SnapshotHz 分离，客户端从 JoinAck 取；SimView 无静态读；ThrottleRadius 命名常量/表列；重生成 BuildHash 主代理。执行者：主代理（重生成/云侧操作），施工子代理（代码/测试） | 三档配置回读；GM 房生效断言；LocalServerHandshake/EditMode/Play；grep 零命中；gen-build-hash 重生成+check | - |
| T66 | 调参面与导出 | T65 | 取消 | B19/B20/B21：13 滑杆；dev 三宏；单门 SimParamsGate.Evaluate 每会话一次 bool；单合并点 ConfigService+ServerTableLoader；联机/云 sandbox 禁用覆盖（断言）；判定路径零静态读（grep/负例命令）；四几何覆写只做零命中复核（删除在 T60）；Damage/Rpm 保留；B20 四条断言；编辑器+GM 两处导出/diff/备份/check-shared；.bin 摘要；重生成 BuildHash 主代理。执行者：主代理（重生成），施工子代理（代码/测试） | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作；gen-build-hash 重生成+check | - |
| T67 | 自动门禁与两端 e2e（主代理） | T66 | 取消 | B23：新增 `scripts/gate/sync-and-sim-e2e.py`：RoomServer publish + build-player + player-smoke + 两端 BuildHash/ConfigHash 对账；`.workflow/config.json` 的 sync-and-sim 命令组包含 L1/LiteSync/LiteSim 定向/check-shared/L2/e2e；wf verify 通过。执行者：主代理独占（施工子代理禁改） | wf verify；e2e 脚本输出两端摘要；证据列命令/时间/摘要 | - |
| T68 | 文档回写 | T67 | 取消 | B24：3 设计+2 进度卷回写/索引/doc-lint；记录 T66 e2e 证据链接。执行者：施工子代理 | doc-lint.py；文档检查 | - |
| T69 | 架构守卫与静态可变扫描根 | T44 | 完成 | B5/B22：Allowed 删 LiteNet->LiteSim 加 LiteSync；ClientRoots 含 LiteSync；StaticMutable 根含 LiteNet+LiteSync；两条硬边与植入违规负例生效。执行者：守卫 C#/测试=施工子代理；门禁配置=主代理 | ClientAsmdefAllowlistTests/ArchitectureGuardTests/StaticMutableAllowlistTests；gen-build-hash --check-shared | Allowed: LiteNet->[] 且新增 LiteSync；ClientRoots/StaticMutable 根含 LiteSync+LiteNet（kcp2k vendor 排除）；ArchitectureGuard 两条硬边… |
| T70 | 两端重建与联测（批 6） | T69 | 取消 | B8：L1 全量绿；客户端 build-player + RoomServer publish；本地测试房+KCP lossy 冒烟；两端 BuildHash 对账；证据落盘。执行者：主代理 | gate/test.py --suite L1；build-player.py；dotnet publish RoomServer；player-smoke；BuildHash 对账 | - |
| T71 | CombatGeometry 类型与发布闸门 | T70 | 完成 | B9（类型/闸门）：11 字段/9 参数；读口三态（未发布抛/已发布读/展示兜底）；10 构造点；ResetForTests+[Collection]；`BattleContextTests` 时序断言（范围含 Arena）；编辑器未发布不崩；坏表候选校验（表接入下一任务）；重生成 BuildHash 主代理。执行者：代码/测试=施工子代理；重生成=主代理 | CombatGeometry/CombatValues/BattleContext EditMode；Loaded 三态/时序；dotnet+Unity；gen-build-hash 重生成+check | 新增 CombatGeometry（13 字段+派生）；CombatValues 11 字段/9 参；CombatConfig _published/Loaded 抛/HasPublished/TryGetLoaded/Geometry … |
| T72 | 表接入、13 列映射与调用点 | T71 | 完成 | B11+B9（表）：__tables__ + TableLayout.Files=Combat/tbcombatgeom；JudgmentFiles 自动包含；BuildCombatValues(...,tbcombatgeom) 13 列映射；ConfigService/ServerTableLoader/测试调用点全更新；坏表 ValidateCandidate 拒启动；生成读码/表字节落 `Assets/GameData/Generated`/`Config`；重生成 BuildHash 主代理。执行者：代码/测试=施工子代理；gen/重生成=主代理 | Luban/gen.bat；gen-build-hash 重生成+check；表/坏表/映射用例；GameData 生成物检查 | 新增 Luban/Data/#combatgeom.xlsx 与 __tables__.xlsx 登记；gen_luban 产出 Tbcombatgeom/combatgeom/tbcombatgeom.bytes；TableLayout… |
| T73 | 判定面与外部消费者迁移 | T72 | 完成 | B10：Assets/Server/Tests 旧几何名零命中；本任务唯一删除四几何覆写；HUD/Dev/Editor/测试读者迁移；判定路径与烘焙写入源不读 Geometry（显示/对比白名单）；测试名：BattleCrosshairEditModeTests/BattleLaserEditModeTests/BattleSmokePlayModeTests + ArchitectureGuardTests 负例；Arena 侧 BattleContext/BattleCrosshairDriver/AutoHeadshotRig/BattleHeadshotDebugDrawer 只读展示断言；SimStep 与 9 System 签名不变；重生成 BuildHash 主代理。执行者：代码/测试=施工子代理；重生成=主代理 | grep 守卫（含白名单/负例）；Unity 0 error；上述测试名全绿；gen-build-hash 重生成+check | CombatConfig 旧几何常量/MuzzleOrigin 删除；MatchTestRules 几何覆写四件删除；SimRaycast 收 Geometry 参数；Movement/Input/Shooting/Damage 改读 v… |
| T74 | 摘要与值校验 | T73 | 完成 | B13/B14：表字节进摘要；联机摘要不含覆盖；13 值与表装载结果 0 容差；改表后 GameplayDigestBase 变化可观测（测试名）；黄金样本/更新流程/测试名；两端摘要不等拒局。执行者：代码/测试=施工子代理；重生成=主代理 | CombatConfigDigest/GameplayDigestBase 用例；黄金样本测试名；gen-build-hash 重生成+check | ConfigurationSourceTests 23/23：13 列几何表值=默认横截面（0 容差，含派生 HeadHitLine/HeadshotRadius）+ tbcombatgeom 改字节摘要变；SimDeterminism/… |
| T75 | Body/Head/Muzzle 烘焙与 SimMap | T74 | 完成 | B12：先落表写入器/新行；三件 .g.cs 走 unity-pipeline 删除（主代理）；三烘焙器写表（源只来自 prefab/anchor）；SimMapBaker 读 BakeTargets.PrefabPath 的 CC 并断言表 row≡BodyCc 夹具（半径+高）；BodyCylinderBaker 同次原子写夹具（含来源 prefab）；prefab 缺失=失败不 skip；BakeTargets 五常量去向（Head/Muzzle/Body→#combatgeom，RootMotion→rootmotion.bin，Grip→grip.bin）；四条死路径=BakeTargets.cs:36-38/46 的 `Assets/LiteSim/Core/Scripts/*`→新去向；本任务不删 WriteGenerated；注释 0 warning；重生成 BuildHash 主代理。执行者：资产删除/重生成=主代理；代码/测试=施工子代理 | 烘焙实操；BodyBake/SimMapBaker EditMode；夹具同源/缺失失败用例；Unity 0 warning；gen-build-hash 重生成+check | 新增 scripts/codegen/_combatgeom_row.py（unittest 13/13 OK）+ scripts/tests/test_combatgeom_row.py；新增 Assets/LiteGame/Edito… |
| T76 | Grip 烘焙与消费面 | T75 | 完成 | B12（Grip）：先落 WeaponGripCatalog/grip.bin 与消费面，再删 GripBake.g.cs（unity-pipeline，主代理；非共享源集，无需 BuildHash）；WeaponGripBaker 产 grip.bin；WeaponGripConstraint/WeaponAimConstraint/AimPitchPostureDriver/GripTuning/FeatureDebugTunables 迁移；内容摘要含 grip.bin。执行者：资产删除=主代理；代码/测试=施工子代理 | WeaponGripConstraintEditMode；catalog 用例；烘焙摘要；Unity 0 error | 新增 WeaponGripCatalog（GRIP/ver1/长度前缀路径/6 float 位姿/float 幅度，小端；InvalidDataException 校验；Digest=SHA-256 字节）+ BinTextAssetIm… |
| T77 | RootMotion 数据产物 | T76 | 完成 | B16/B17/B18：先落 RootMotionCatalog.Load/rootmotion.bin/Digest/AppComposition 绑定，再删 RootMotionData.g.cs（unity-pipeline，主代理）；LiteSim 无 RootMotion*；.bin 不进 SHARED_TARGETS；ContentIdentity 不引 LiteSim/SHA-256/无名称字段；已烘焙未接线登记；重生成 BuildHash 主代理。执行者：资产删除/重生成=主代理；代码/测试=施工子代理 | RootMotionDataTests；gen-build-hash 重生成+check；文档登记 | RootMotion 迁出 LiteSim 到 Modules/Battle/Arena（RootMotionEntry/Catalog/Digest 零引擎，源链接进 LiteSim.Core.Tests）；格式 RMO1/v1/tic… |
| T78 | WriteGenerated 删除收口 | T77 | 完成 | B12（收口）：五件 .g.cs 全部退役后，`BakeTargets.WriteGenerated` 零引用并删除；编译 0 warning。执行者：施工子代理（Editor 代码，无共享源变更） | grep WriteGenerated 零命中；Unity 0 warning | 删除 BakeTargets.WriteGenerated 两个重载及注释（276→232 行），清理仅其使用的 using System.Text；WriteArtifact/WriteArtifactBytes/WriteIfChan… |
| T79 | AOI 与广播拆分 | T78 | 完成 | B15：AoiSettings 落 LiteSync/Shared；两路真实注入：deploy/roomserver.windows.json 三档（改配置后重启 LiteGame-Room 再回读）+ GM `start` 创建 sandbox 房后回读生效值（scripts/deploy/gm.py）；AoiFilter 无静态缓存；SnapshotHz 分离，客户端从 JoinAck 取；SimView 无静态读；ThrottleRadius 命名常量/表列；重生成 BuildHash 主代理。执行者：重生成/云侧操作=主代理；代码/测试=施工子代理 | 三档配置回读；GM 房生效断言；LocalServerHandshake/EditMode/Play；grep 零命中；gen-build-hash 重生成+check | 新增 LiteSync/Shared/AoiSettings.cs（cell 10/radius 30/extent 640，ThrottleRadius=Radius/2，fail-fast 校验）；AoiFilter 去 static… |
| T80 | 调参面与导出 | T79 | 完成 | B19/B20/B21：13 滑杆；dev 三宏；单门 SimParamsGate.Evaluate 每会话一次 bool；单合并点 ConfigService+ServerTableLoader；联机/云 sandbox 禁用覆盖（断言）；判定路径零静态读（grep/负例命令）；四几何覆写只做零命中复核（删除在判定面任务）；Damage/Rpm 保留；B20 四条断言；编辑器+GM 两处导出/diff/备份/check-shared；.bin 摘要；重生成 BuildHash 主代理。执行者：重生成=主代理；代码/测试=施工子代理；GM 真实操作=主代理 | provider/门/合并点用例；B20 四条断言；工具实操；红线 13 真实操作；gen-build-hash 重生成+check | 参数面已交付并验证：SimParamsOverrides（13 列可空覆写+同口径校验，零引擎、L1 源链接）、SimParamsGate（纯函数）、SimParamsActivation（门+合并唯一求值点）、SimParamsDebu… |
| T81 | 自动门禁与两端 e2e（主代理） | T80 | 完成 | B23：新增 `scripts/gate/sync-and-sim-e2e.py`：RoomServer publish + build-player + player-smoke + 两端 BuildHash/ConfigHash 对账；`.workflow/config.json` 的 sync-and-sim 命令组含 L1/LiteSync/LiteSim 定向/check-shared/L2/e2e；wf verify 通过。执行者：主代理独占 | wf verify；e2e 输出两端摘要；证据列命令/时间/摘要/清理 | sync-and-sim-e2e.py 全链实测通过（20261031-232226）：read-client-hash / check-shared(be285f5c37e147d9) / server-reconcile(服务端启动横… |
| T82 | 文档回写 | T81 | 完成 | B24：3 设计+2 进度卷回写/索引/doc-lint；记录自动 e2e 证据链接。执行者：施工子代理 | doc-lint.py；文档检查 | 文档回写完成：状态同步专项设计（新增 §2.1 LiteSync 边界与协议面、§3 房级快照频率、§8 AoiSettings）、联机动画与根位移（新增 §3.1 RMO1 格式/§3.2 装载器与 fail-fast/§3.3 身份摘… |
| T83 | 批6+批7 联合回归 | T82 | 完成 | B8/B23最终：L1 全量绿（含 20 个红测全转绿）；客户端 build-player + RoomServer publish；本地测试房+KCP lossy 冒烟；两端 BuildHash/ConfigHash 对账并留证。执行者：主代理 | gate/test.py --suite L1；build-player.py；RoomServer publish；player-smoke；两端摘要对账 | 联合回归证据：L1 全量 `python scripts/gate/test.py --suite L1` PASS（1970 tests：LiteFramework 819 / LiteNet 569 / LiteSim 412 / L… |
| T84 | 本地测试房参数覆盖生效链路 | T80 | 完成 | B19/B20 运行期面：①门 SimParamsGate.Evaluate 在进入本地测试模式/进房时求值一次（不是装配期），结果以 bool 传入；②同一份合并值同时供给客户端 CombatConfig 与本地服 RoomRuntime（LocalServerTransport 建房用合并值而非 CombatValues.Default），保证 B20② 的'本地权威 checksum == 客户端预测 checksum 且两侧 CombatValues 逐字段相等'；③联机/云 sandbox 路径恒不读覆盖（断言）；④对局中改滑杆只提示'下一局生效'。 | L1：新增/扩展用例（门--会话一次、两端同值、联机禁用）；EditMode：进本地测试局后两侧 CombatValues 逐字段相等；Play：本地测试局命中判定随滑杆变化 | 门下移到进房时点：新增零引擎 SimParamsActivation.Resolve(applyOverrides, table, overrides)（只重建几何 13 列）；IConfigService.TableCombatValu… |

## 任务详情

### T1 · LiteSync 程序集与空壳

- 文件: Assets/LiteSync

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T2 · 同步件搬迁与引用收口

- 文件: Assets/LiteSync, Assets/LiteNet, Assets/LiteGame/Adapters/Dev, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T3 · BuildHash 源集与架构守卫

- 文件: scripts/codegen/gen-build-hash.py, Tests/LiteFramework.Core.Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T4 · 两端重建与联测

- 文件: Builds, Server, scripts/build

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T5 · CombatGeometry 与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T6 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Server, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T7 · 表接入、摘要与值校验

- 文件: Luban, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T8 · 烘焙全迁与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/LiteGame/Modules/Battle/Arena, Assets/GameData/Config/runtime, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T9 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T10 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T11 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Tests

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T12 · 门禁与文档收口

- 文件: .workflow/config.json, Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度

- 状态理由: 任务评审整改：按 dsh 首轮意见拆分/重排，由 T13+ 取代

### T13 · LiteSync 程序集与空壳

- 文件: Assets/LiteSync

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T14 · 15 件搬迁、引用收口与 BuildHash 重生成

- 文件: Assets/LiteSync, Assets/LiteNet, Assets/LiteGame/Adapters/Dev, scripts/codegen/gen-build-hash.py, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T15 · 架构守卫与静态可变扫描根

- 文件: Tests/LiteFramework.Core.Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T16 · 两端重建与联测

- 文件: scripts/build, Server, Builds

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T17 · CombatGeometry 类型与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T18 · 表接入与装载

- 文件: Luban, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T19 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Server, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T20 · 摘要与值校验

- 文件: Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T21 · Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated

- 文件: Assets/LiteGame/Editor/Baking, Assets/GameData, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T22 · Grip 烘焙与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/GameData/Config/runtime, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T23 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T24 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T25 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Tests

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T26 · 自动门禁命令组（主代理）

- 文件: .workflow/config.json

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T27 · 文档回写与人工联测证据

- 文件: Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度

- 状态理由: 任务评审第二轮整改：补共享源集 BuildHash 重生成、文件范围、资产搬迁方式与主代理/子代理权限；由 T28+ 取代

### T28 · LiteSync 程序集与双轨编译

- 文件: Assets/LiteSync

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T29 · 15 件搬迁、引用收口与 BuildHash 重生成

- 文件: Assets/LiteSync, Assets/LiteNet, Assets/LiteGame/Adapters/Dev, scripts/codegen/gen-build-hash.py, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T30 · 架构守卫与静态可变扫描根

- 文件: Tests/LiteFramework.Core.Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T31 · 两端重建与联测（批 6）

- 文件: scripts/build, Server, Builds

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T32 · CombatGeometry 类型与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T33 · 表接入、13 列映射与调用点

- 文件: Luban, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T34 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Server, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T35 · 摘要与值校验

- 文件: Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T36 · Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteSim/Scripts/Map, Luban/Data, Assets/GameData, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T37 · Grip 烘焙与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/LiteGame/Adapters/Dev, Assets/GameData/Config/runtime, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T38 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T39 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, deploy/roomserver.windows.json, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T40 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T41 · 自动门禁命令组（主代理）

- 文件: .workflow/config.json

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T42 · 文档回写与最终两端联测

- 文件: Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度, scripts/build, Server, Builds

- 状态理由: 任务评审第3轮整改：补 BuildHash 重生成归属/文件范围/去重/GM 参数；由 T43+ 取代

### T43 · LiteSync 程序集与双轨编译

- 文件: Assets/LiteSync

### T44 · 15 件搬迁、引用收口与 BuildHash 重生成

- 文件: Assets/LiteSync, Assets/LiteNet, Assets/LiteNet/LiteNet.csproj, Assets/LiteGame/Adapters/Dev, Assets/LiteGame/App, Assets/LiteGame/Editor, Server/RoomServer, scripts/codegen/gen-build-hash.py, Tests, Tests/Tests.slnx

### T45 · 架构守卫与静态可变扫描根

- 文件: Tests/LiteFramework.Core.Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T46 · 两端重建与联测（批 6）

- 文件: scripts/build, Server, Builds

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T47 · CombatGeometry 类型与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T48 · 表接入、13 列映射与调用点

- 文件: Luban, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T49 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Server, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T50 · 摘要与值校验

- 文件: Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T51 · Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteSim/Scripts/Map, Luban/Data, Assets/GameData, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T52 · Grip 烘焙与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/LiteGame/Adapters/Dev, Assets/GameData/Config/runtime, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T53 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T54 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, deploy/roomserver.windows.json, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T55 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T56 · 自动门禁命令组（主代理）

- 文件: .workflow/config.json

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T57 · 文档回写与最终两端联测

- 文件: Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度, scripts/build, Server, Builds

- 状态理由: 任务评审第4轮整改：补生成物范围/自动 e2e/BakeTargets 明细/GM 实注入/执行者；由 T58+ 取代

### T58 · CombatGeometry 类型与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T59 · 表接入、13 列映射与调用点

- 文件: Luban, Assets/LiteGame/Modules/Config, Assets/GameData/Generated, Assets/GameData/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T60 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Assets/LiteGame/Modules/Battle/Arena, Server, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T61 · 摘要与值校验

- 文件: Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T62 · Body/Head/Muzzle 烘焙与 SimMap/WriteGenerated

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteSim/Scripts/Map, Luban/Data, Assets/GameData, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T63 · Grip 烘焙与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/LiteGame/Adapters/Dev, Assets/GameData/Config/runtime, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T64 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T65 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, deploy/roomserver.windows.json, scripts/deploy/gm.py, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T66 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T67 · 自动门禁与两端 e2e（主代理）

- 文件: scripts/gate/sync-and-sim-e2e.py, .workflow/config.json, scripts/build, Server, Builds

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T68 · 文档回写

- 文件: Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度

- 状态理由: 任务评审第5轮整改：补执行者/WriteGenerated 收口/测试名/CC 夹具同源；由 T69~T82 重建

### T69 · 架构守卫与静态可变扫描根

- 文件: Tests/LiteFramework.Core.Tests

### T70 · 两端重建与联测（批 6）

- 文件: scripts/build, Server, Builds

- 状态理由: TDD 先红与批6 L1 全绿冲突：20 个批7红测需 T71+ 才转绿；批6/批7联合回归改由 T83 承担

### T71 · CombatGeometry 类型与发布闸门

- 文件: Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, Tests

### T72 · 表接入、13 列映射与调用点

- 文件: Luban, Assets/LiteGame/Modules/Config, Assets/GameData/Generated, Assets/GameData/Config, Server/RoomServer, Tests

### T73 · 判定面与外部消费者迁移

- 文件: Assets/LiteSim/Scripts, Assets/LiteSim/Systems, Assets/LiteGame, Assets/LiteGame/Modules/Battle/Arena, Server, Tests

### T74 · 摘要与值校验

- 文件: Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

### T75 · Body/Head/Muzzle 烘焙与 SimMap

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteSim/Scripts/Map, Luban/Data, Assets/GameData, Tests

### T76 · Grip 烘焙与消费面

- 文件: Assets/LiteGame/Editor/Baking, Assets/LiteGame/Modules/Battle/Characters, Assets/LiteGame/Adapters/Dev, Assets/GameData/Config/runtime, Tests

### T77 · RootMotion 数据产物

- 文件: Assets/LiteSim/Scripts/Map, Assets/LiteGame/Modules/Battle/Arena, Assets/LiteGame/App, Assets/GameData/Config/runtime, Assets/LiteGame/Editor/Baking/RootMotionBaker.cs, Tests

### T78 · WriteGenerated 删除收口

- 文件: Assets/LiteGame/Editor/Baking

### T79 · AOI 与广播拆分

- 文件: Assets/LiteSync, Assets/LiteSim/Scripts/Config, Assets/LiteGame/Modules/Battle/Arena, Server/RoomServer, deploy/roomserver.windows.json, scripts/deploy/gm.py, Tests

### T80 · 调参面与导出

- 文件: Assets/LiteGame/Adapters/Dev, Assets/LiteGame/Editor/Baking, Assets/LiteGame/Editor/Gm, Assets/LiteSim/Scripts/Rules, Assets/LiteGame/Modules/Config, Server/RoomServer, Tests

### T81 · 自动门禁与两端 e2e（主代理）

- 文件: scripts/gate/sync-and-sim-e2e.py, .workflow/config.json, scripts/build, Server, Builds

### T82 · 文档回写

- 文件: Docs/设计文档/网络, Docs/设计文档/客户端/动画, Docs/设计文档/游戏玩法, Docs/施工进度

### T83 · 批6+批7 联合回归

- 文件: scripts/build, Server, Builds

### T84 · 本地测试房参数覆盖生效链路

- 文件: Assets/LiteGame/App/AppComposition.cs, Assets/LiteGame/Modules/Config/SimParamsGate.cs, Assets/LiteGame/Adapters/Dev/LocalServer/LocalServerTransport.cs, Assets/LiteGame/Modules/Session, Assets/LiteGame/Modules/Procedure/Stages/ProcedureBattle.cs, Tests

