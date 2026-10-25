# 方案评审（独立子代理）：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：plan.md（方案与验收标准），修订 1
- 代码/文档基线：HEAD `0574da90`；工作区另有 UI 线在途改动（`.workflow/active/ui-p2-token-base`、`.workflow/config.json`），与本方案无关
- 评审范围：方案完整性、现状依据、分类规则覆盖、工具契约、验收可判定性、风险与回滚
- 子代理标识：独立 Explore 子代理（方案评审，2026-10-24）
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 方案检查

- 目标可观察、范围与非目标清晰：首轮部分成立；内容路径与矩阵矛盾（见 P-1）
- 现状与根因有代码/配置/文档依据：属实，但漏了 `RoomServer.csproj` 出树链接与 `MapTableProjection.cs`（P-4）
- 候选方案取舍充分，推荐理由回应约束：A/B/C 取舍成立；import 无 main 副作用（P-6）
- 设计覆盖接口、数据、关键流程、失败与边界：首轮规则互相重叠、动作集不确定（P-5）
- 通用性与扩展点明确，非特判式补丁：规则表方向成立
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：首轮矩阵数据驱动不可执行（P-3）
- 风险、监控与回滚可执行：基本成立；回滚漏文档/索引（P-8）

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 | 干净树运行 | 是（需干净树前提） | 成立 |
| AC2 | 共享源用例 | 是 | 成立 |
| AC3 | 内容/混合用例 | 否 | 内容动作与矩阵矛盾 |
| AC4 | 未知路径用例 | 是 | 成立 |
| AC5 | JSON schema | 部分 | 缺 schema 版本 |
| AC6 | 零写盘 | 否 | importlib/`__pycache__`/git index 无法判定 |
| AC7 | 矩阵数据驱动 | 否 | 散文表不可解析 |
| AC8 | 门禁 | 是 | 成立 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | blocker | 内容类只判 `rebuild-content-bundle` 且 `needsPlayerBuild=false`；内置包随 Player、服务端也读 Config bytes，矩阵第 1 行要求重打包 | 改为双口径：`innerLoop`（editor_reload）与 `delivery`（内容包 + 重打包 + 表值重启/换表提示） | 已修（修订 2） |
| P-2 | blocker | `Packages/**` 与 `deploy/roomserver.windows.json` 被判 none，实际影响两端编译/配置发布 | 加 `package`/`deploy_config` 规则；`ProjectSettings/BuildProfiles` 也显式 | 已修（修订 2） |
| P-3 | blocker | 矩阵是散文表，无法数据驱动；第 1/5/6 行与规则不符 | AC7 改为 6 条固定用例逐条断言；文档改为指向 build.plan | 已修（修订 2） |
| P-4 | major | `MapTableProjection.cs` 被 RoomServer.csproj 出树链接但未登记 SHARED_LINK_FILES | 加 `manifestDrift` 对账 + 已知例外显式警告 + 测试钉住例外集合；补登记另批（会改 BuildHash） | 已修（修订 2，登记审计债） |
| P-5 | major | `Assets/LiteGame/**` 与 Lua 规则重叠、动作不确定 | 规则改为单一路径归属 + 命中累积；内容/客户端代码分列 | 已修（修订 2） |
| P-6 | major | importlib 加载写 `__pycache__`，git 刷新 index，零写盘不可判定 | 设 `sys.dont_write_bytecode=True`；AC 改为工作树 `git status --porcelain` 前后一致 | 已修（修订 2） |
| P-7 | major | 验收缺 base 解析失败、未跟踪采集、路径归一、忽略计数 | 加 AC9 与对应用例 | 已修（修订 2） |
| P-8 | minor | 清单 kind/plane/purpose/docs 未定；docs 指向错误；回滚漏文档/索引 | 清单字段定案；docs 指 05；回滚补文档/索引 | 已修（修订 2） |

## 评审结论

（最终轮：第六轮）

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: 独立 Explore 子代理（第六轮）
- 评审时间: 2026-10-24
- 历史：首轮 FAIL（P-1/P-2/P-3）、第二轮 FAIL（R-1/R-2）、第三轮 FAIL（S-1/S-2）、第四轮 FAIL（U-1/U-2）、第五轮 FAIL（W-1）；各轮问题均已在修订 2-6 处理，记录见上。
- 第六轮 minor（已吸收）：规则 8/13/14 明确 inner/delivery；AC5 补 `ignoredCount`；规则 3 inner 移除 `rebuild_content_bundle`；`parse_csproj(text)` 作为漂移测试注入面。

## 复评记录（修订 2 → 修订 3）

- 复评子代理：独立 Explore 子代理（2026-10-24，第二轮）
- 复评输入：修订 2 的 `plan.md`、首轮问题清单、代码基线 `0574da90`
- 第二轮结论：FAIL；剩余 blocker 2、major 3、minor 1：
  | ID | 严重度 | 问题 | 修订 3 处理 |
  |---|---|---|---|
  | R-1 | blocker | 房间内核交付含 `rebuild_client_player`，与矩阵第 4 行"客户端不动"冲突 | 房间内核 delivery 只 `rebuild_server_room`；内环 `editor_reload` |
  | R-2 | blocker | `Assets/Art/Animation/FX/Model/Sound/Resources/Map` 等资源落 unknown，被判两端重建 | 内容规则加兜底：`Assets/**` 其余非 `.cs` 资源一律 content |
  | R-3 | major | 漂移守卫未排除 csproj 通配（Luban Runtime/Generated），例外断言必失败 | 对账只取单文件出树链接；通配视为已登记类别 |
  | R-4 | major | `MapTableProjection.cs` 只判客户端，服务端陈旧 | 加入"已知漏登记共享件"→按 shared 两端重建（不改哈希常量） |
  | R-5 | major | 规则重叠（LiteSim/Core/Editor、LiteGame/Lua） | 改为优先级唯一 area + 显式排除；测试钉住三类冲突路径 |
  | R-6 | minor | `restart_server` 不在枚举、schema 命名不一致、回滚漏工具索引 | 统一 `restart_room`/`schemaVersion`；回滚补索引重生成 |
- 第三轮复评：FAIL；剩余 blocker 2、major 1：
  | ID | 严重度 | 问题 | 修订 4 处理 |
  |---|---|---|---|
  | S-1 | blocker | meta 规则吞并 `META_EXTRA_FILES` 中的 3 个 RoomServer 文件，漏服务端重建 | meta 命中这三件时附加 `rebuild_server_room + publish_server` |
  | S-2 | blocker | server/room_kernel 缺"发布上传"动作；表值换表只写成 note | 动作枚举加 `publish_server`/`publish_tables`；server/room_kernel delivery 含发布；表值含换表+重启 |
  | S-3 | major | 漂移守卫排除所有通配，新增未登记通配被静默放过 | 通配白名单钉死两条（Luban Runtime/Generated），其余入 drift |
  | S-4 | major | AC4 的 `known=false` 无用例 | 加临时 csproj 注入用例 |
- 第四轮复评：FAIL；剩余 blocker 2、major 2、minor 1：
  | ID | 严重度 | 问题 | 修订 5 处理 |
  |---|---|---|---|
  | U-1 | blocker | shared 交付缺 `publish_server`；Luban 缺表发布 | shared/luban 交付补 `publish_server` |
  | U-2 | blocker | 通配白名单表述与 AC4 冲突 | 统一为"只允许已登记白名单，其余入 drift" |
  | U-3 | major | 用 RoomServer.csproj 单文件项对 `SHARED_LINK_FILES ∪ META_EXTRA_FILES`，Meta 三件在 MetaServer.csproj，会误报/漏校验 | 分别按 RoomServer→SHARED_LINK_FILES、MetaServer→META_EXTRA_FILES 对账 |
  | U-4 | major | 动作无去重排序；`collect()` ROOT 固定与 `--project-path` 矛盾 | 有序去重；`--project-path` 仅用于 git/csproj，跨仓库 fail-fast |
  | U-5 | minor | `publish_tables/restart_room` 与 deploy-room 内建动作重复；头部修订号 | 统一 `publish_server` 全链；头部改修订 5 |
- 第五轮复评：FAIL；剩余 blocker 1、major 3、minor 3：
  | ID | 严重度 | 问题 | 修订 6 处理 |
  |---|---|---|---|
  | W-1 | blocker | proto 交付缺 `publish_server`（矩阵第 3 行） | proto delivery 补 `publish_server` |
  | W-2 | major | Luban 包/unknown 交付缺 `publish_server` | 补 `publish_server`；`publish_meta` 定义全链 |
  | W-3 | major | AC7 第 2 行把 MetaServer 与服务端宿主混为一条 | AC7 拆 RoomServer/MetaServer 两条断言 |
  | W-4 | major | 规则 4/5/9/10/11 未分 inner/delivery；ring=inner 却含 publish/restart | 全部规则补 inner/delivery；ring 与动作分离 |
  | W-5 | minor | 跨仓库 fail-fast 与临时仓库测试冲突；动作顺序；配置落 unknown | 只读测试改当前仓库；语义序固定；规则 11 明确 |
- 第六轮复评：进行中（见下）
