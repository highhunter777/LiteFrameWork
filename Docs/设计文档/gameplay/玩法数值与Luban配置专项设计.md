# 玩法数值与 Luban 配置专项设计

> 状态：现行专项设计（数值实例化及重力/出生生命表源收口，Unity 集成待本机验收）
> 版本：1.3
> 更新日期：2026-10-08
> 适用范围：玩法数值归属、Luban schema、生成物和客户端/服务端一致性
> 维护责任：玩法数值

## 1. 归属规则

- **架构常量**进 `SimConfig`：帧率、容量、回滚深度、协议相关旋钮。
- **玩法数值**进 Luban：伤害、移速、射速、冷却、范围、状态时长、库存堆叠上限。
- **表现参数**进内容表/资源：Prefab、VFX、SFX、图标、动画引用。
- **玩家事实**进 Profile/Mongo：库存实例、装备、货币、耐久、绑定和结算流水。

Sim 只接受已解析的 primitive 数值；不直接读 Luban、Unity Asset 或数据库。

## 2. 数据链与生成

```text
Luban xlsx
 → 客户端 binary + Lua/C# 生成物
 → 服务端直读同一份 binary（同源，不另存 JSON）
 → buildHash
 → 装载产出 `CombatValues`/`MovementValues` 实例（两端同源；机制消费经参数显式接收——技术债 #1）
```

生成与装载链遵循[热更与内容发布专项设计](../client/content/热更与内容发布专项设计.md)：生成文件清单 → 候选解析/完整验证 → 不可变 ConfigSnapshot → Room/Match 固定快照；客户端 binary 与服务端读取以同一规范化玩法摘要证明语义一致，文件完整性分别校验。

客户端和 RoomServer 必须来自同一份表源；缺表或关键行缺失时拒绝候选，首次启动无可用版本则进入恢复态，不能静默回退默认值。验证完成前不得发布 Tables 或发布玩法数值实例（实例是"装载后运行态数值"的唯一载体——客户端原子发布读口、服务端装配输入；静态装载面已拆除，见下）。

**数值实例化（技术债 #1 根治，2026-10-08）**：装载产出 `CombatValues`/`MovementValues`/`WeaponTable` 只读实例；服务端 `CombatNumbers` → `ServerTableLoad` → `HostAssembly.Inputs`（必填）→ 宿主/房间快照；客户端 `ConfigService` 装载校验后原子发布读口（表现面便捷读，机制禁读）；Sim 机制（`SimStep`→系统/回溯/回放）一律经参数接收实例。摘要（`CombatConfigDigest`）按数值实例计算，字段序/格式与旧口径逐字节一致（同值同摘要）。**防复辟纪律 = 纪律扫描 R13**（机制面禁读静态读口可变值，常量面豁免；受守根与例外见 `ScanTargets.cs`）。施工与证据见[配置实例化](../../施工进度/配置实例化.md)。

**单源映射**：`combatnum` 不再存储重力和出生生命。`tbmovementconfig.gravity` 是重力唯一表源；`tbentityconfig.initial_hp` 是实体出生生命唯一表源，当前消费者为 id=1 默认角色。`SimConfigMapper` 放在 `LiteClient/Adapters/Serialization.Luban/`（文件所在程序集名为 `LiteClient.Serialization.Luban`，但**声明的命名空间是 `LiteClient`**），RoomServer 只源链接该无 Unity/UniTask 依赖文件，两端共用必要行与数值校验及投影。`CombatValues.Gravity/EntityHp` 是固定对局配置中的投影值，不构成第二套可编辑表源；Sim 不直接读表或静态配置。所属表行缺失、非有限重力或非正出生生命必须拒绝装载，不回退代码默认值。同值的 `CombatConfigDigest` 文本和摘要保持一致，schema 迁移通过重生成 buildHash 保护准入。

**生成入口**：`Luban/gen.bat` 委托 `scripts/codegen/gen-luban.ps1`，路径从仓库根推导。Luban 先在 `TestResults/luban-generation/<run>/` 生成 C#/binary/Lua；成功后只发布相应文件类型且跳过内容相同的文件，保留现有文本行尾，binary 不做文本归一化。生成器不清空 Assets 输出目录，也不通过 Git 恢复 asmdef 或 `.meta`。Unity 导入与资源收集验证仍由本机 Pipeline 执行。

## 3. 表设计

> **表名口径**：落地表名为 `Tables.Tb<name>` / `<name>.bytes`（如 `tbweapon` / `tbweapon.bytes`，**无下划线**）；下表的 `tb_weapon` 等写法是设计口语，代码/生成物一律用无下划线形式。

| 表 | 状态 | 关键字段 |
|---|---|---|
| `tbcombatnum` | **已落地** | id、move_speed、hitscan_range、base_damage、damage_spread；重力/出生生命不在此表 |
| `tbmovementconfig` | **已落地** | 走跑冲/滑铲/空中/跳跃/位移数值（19 字段），gravity 为唯一重力表列 |
| `tbentityconfig` | **已落地** | id、initial_hp；默认角色 id=1 |
| `tbweapon` | **已落地** | id、name、archetype、slot（槽位默认映射单源）、fire_mode（auto/semi→bool）、damage/rpm/magazine_size/reserve_ammo/reload_frames/range/spread/pellets/ammo_type/switch_frames、vfx/sfx |
| `tbitemconfig` | **已落地** | 道具表（类型 + 刷新/拾取/携带/使用 + 各类型效果数值）；**空表即拒装载** |
| `tbuiform` | **已落地** | UI 表单投影（lua_path/prefab/layer/full_screen）；**排除在 buildHash 外**（纯表现） |
| `tbcontententry` | **已落地** | 内容入口表（样例①真实资源包段的入口 location 单源） |
| `tbstrategy` | **已落地** | 策略表 |
| `tbanimationprofile` | **已落地** | 动画 Profile 登记行（id＝行键〔定义语义 ID/回退源 ID/Mask 路径〕、model_family 分派键、kind〔single/blend/fallback/mask〕、channel〔base/overlay/override→枚举〕、binding、bindings〔逗号分隔串——拆分在装载边界〕、loop、min/max_speed、hold_on_finish、requires_load、fallback_id）；**空表即拒装载**，类别/通道字符串与全族语义校验（FromRows 演练）在装载门 |
| `tb_action` | **未落地（目标态）** | id、category、时间轴/轨、优先级、中断规则、效果引用——`Luban/Data/` 无同名 xlsx、无 `Tb*` 类、无 `.bytes`；目前仅 `SimCombatRuntime` 注释提及 |
| `tb_action_num` | **未落地（目标态）** | 前摇/生效/后摇、冷却、消耗、伤害、半径、持续、位移、护盾 |
| `tb_status` | **未落地（目标态）** | 效果、持续帧、叠层、属性修改、互斥组 |

装载门（`ConfigService.ValidateCandidate`）：全部表在 `TableDataFiles` 登记；缺表/空表、`tbweapon` 缺 id=0 默认步枪、`fire_mode` 非法、`slot` 越界、`pellets < 1`、`tbanimationprofile` 空表/非法类别/通道串/全族装载演练失败一律拒绝装载并抛明确原因。

所有参与 Sim 的时间量用整数帧；Timeline 导出校验 `round(seconds*60)` 和误差 `<1e-4`（**随 `tb_action` 落表才有实现可校验**）。

## 4. 校验和版本

- 表字段类型、范围、引用、互斥规则在生成阶段校验。
- 文件完整性使用原始字节摘要；玩法语义摘要覆盖 schema、全部参与 Sim 的表与动作导出物。表现配置、构建身份与玩法版本按热更专项拆分，不对 binary 做换行归一化。
- buildHash 严格校验；新协议字段与双端准入就绪后才允许独立数据发布。短 configHash 不是完整内容身份或签名。
- 已有 schema/指令集内的玩法参数允许双端发布，只有新房间采用；对局固定快照，重连继续原版本。算法、协议语义和不兼容 schema 走代码升级。
- 玩法参数测试覆盖客户端加载、服务端读取、默认值守卫和边界值。
