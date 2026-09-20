# 玩法数值解耦与 Luban 表设计（精简版）

## 1. 归属规则

- **架构常量**进 `SimConfig`：帧率、容量、回滚深度、协议相关旋钮。
- **玩法数值**进 Luban：伤害、移速、射速、冷却、范围、状态时长、库存堆叠上限。
- **表现参数**进内容表/资源：Prefab、VFX、SFX、图标、动画引用。
- **玩家事实**进 Profile/Mongo：库存实例、装备、货币、耐久、绑定和结算流水。

Sim 只接受已解析的 primitive 数值；不直接读 Luban、Unity Asset 或数据库。

## 2. 现行数据链

```text
Luban xlsx
 → 客户端 binary + Lua/C# 生成物
 → 服务端 JSON
 → buildHash
 → ConfigService/CombatNumbers.LoadFrom
```

客户端和 RoomServer 必须来自同一份表源；缺表或关键行缺失时启动失败，不能静默回退默认值。

## 3. 新增表建议

| 表 | 关键字段 |
|---|---|
| `tb_weapon` | id、类型、射速、弹匣、换弹帧、伤害、射程、散布、弹丸、弹药类型 |
| `tb_action` | id、category、时间轴/轨、优先级、中断规则、效果引用 |
| `tb_action_num` | 前摇/生效/后摇、冷却、消耗、伤害、半径、持续、位移、护盾 |
| `tb_item` | 类型、堆叠上限、使用方式、装备槽、稀有度、价值 |
| `tb_status` | 效果、持续帧、叠层、属性修改、互斥组 |

所有参与 Sim 的时间量用整数帧；Timeline 导出校验 `round(seconds*60)` 和误差 `<1e-4`。

## 4. 校验和版本

- 表字段类型、范围、引用、互斥规则在生成阶段校验。
- 产物写入 content hash，并并入 buildHash。
- 对局中禁止热重载；buildHash/configHash 不一致拒绝进房。
- 玩法参数测试覆盖客户端加载、服务端 JSON、默认值守卫和边界值。
