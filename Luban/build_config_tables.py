# -*- coding: utf-8 -*-
"""2026-09-28 配置批：demo_item → ItemConfig（扩字段）；新增 MovementConfig 单行表。

- __tables__.xlsx：demo.TbItem 行改 TbItemConfig（output=itemconfig）；追加 TbMovementConfig 行。
- #itemconfig.xlsx：新建（道具类型 + 刷新/存在/拾取/携带/使用 + 医疗/护盾/手雷/EMP/雷达/投掷物/钩爪传送）。
- #movementconfig.xlsx：新建（走/跑/冲刺 + 加速度/持续 + 滑铲 + 空中控制 + 重力/跳/二段跳 + 钩爪 + 闪现）。
- #demo.item.xlsx：删除（表名让位）。
"""
import os
import openpyxl

ROOT = r"E:\unityProject\Test\Luban"
DATA = os.path.join(ROOT, "Data")

def set_row(ws, row_idx, values):
    for i, v in enumerate(values, start=1):
        ws.cell(row=row_idx, column=i, value=v)

# ---------- ① __tables__.xlsx：改 demo.TbItem → TbItemConfig，追加 TbMovementConfig ----------
tp = os.path.join(DATA, "__tables__.xlsx")
wb = openpyxl.load_workbook(tp)
ws = wb.worksheets[0]
hit = None
for row in ws.iter_rows():
    for c in row:
        if c.value == "demo.TbItem":
            hit = c.row
            break
    if hit:
        break
assert hit, "未找到 demo.TbItem 行"
# 原 full_name/value_type/input/output 同行改写（列序：A full_name | B value_type | C r s f | D input | E index | F mode | G group | H comment | I tags | J output）
set_row(ws, hit, ["TbItemConfig", "ItemConfig", True, "#itemconfig.xlsx", "id", "", "c,s",
                  "道具表（类型 + 刷新/拾取/携带/使用 + 各类型效果数值）", "", "itemconfig"])
last = ws.max_row
set_row(ws, last + 1, ["TbMovementConfig", "MovementConfig", True, "#movementconfig.xlsx", "id", "one", "c,s",
                       "移动数值表（单行：走跑冲/加减速/滑铲/空中/跳跃/钩爪/闪现；消费点 MovementConfig 静态类）", "", "movementconfig"])
wb.save(tp)
print("__tables__.xlsx updated: TbItemConfig + TbMovementConfig")

# ---------- ② #itemconfig.xlsx：新建 ----------
ic = openpyxl.Workbook()
s = ic.active
s.title = "Sheet"
set_row(s, 1, ["##var", "id", "type", "name", "spawn_interval", "max_alive", "pickup_radius", "carry_limit", "use_duration",
               "heal_amount", "shield_amount", "grenade_damage", "grenade_radius", "grenade_falloff",
               "emp_radius", "emp_duration", "radar_radius", "radar_duration", "projectile_speed", "travel_distance"])
set_row(s, 2, ["##type", "int", "int", "string", "float", "int", "float", "int", "float",
               "int", "int", "int", "float", "float",
               "float", "float", "float", "float", "float", "float"])
set_row(s, 3, ["##group", "", "c,s", "c,s", "c,s", "c,s", "c,s", "c,s", "c,s",
               "c,s", "c,s", "c,s", "c,s", "c,s",
               "c,s", "c,s", "c,s", "c,s", "c,s", "c,s"])
set_row(s, 4, ["##", "主键", "类型：1医疗包 2护盾电池 3手雷 4EMP 5雷达 6钩爪 7传送", "名称",
               "刷新间隔 s（0=不刷）", "场上最大存在数", "拾取半径 m", "携带上限", "使用耗时 s",
               "医疗包回复 HP", "护盾充能 mAh", "手雷伤害", "爆炸半径 m", "伤害衰减（0=无衰减）",
               "EMP 半径 m", "EMP 持续 s", "雷达半径 m", "雷达持续 s", "投掷物初速 m/s", "钩爪/传送距离 m"])
rows = [
    (1,  "医疗包",   15,  4, 1.5, 3, 1.5, 50, 0, 0,  0,   0,   0, 0, 0, 0, 0,  0,  0),
    (2,  "护盾电池", 15,  4, 1.5, 3, 1.0, 0,  25, 0,  0,   0,   0, 0, 0, 0, 0,  0,  0),
    (3,  "手雷",     20,  4, 1.5, 4, 0.6, 0,  0,  80, 5,   0.5, 0, 0, 0, 0, 20,   0),
    (4,  "EMP",      25,  2, 1.5, 2, 0.6, 0,  0,  0,  0,   0,   6, 3, 0, 0, 15,   0),
    (5,  "雷达",     30,  2, 1.5, 1, 0.8, 0,  0,  0,  0,   0,   0, 0, 40, 8, 0,   0),
    (6,  "钩爪",     40,  2, 1.5, 1, 0.5, 0,  0,  0,  0,   0,   0, 0, 0, 0, 0,    30),
    (7,  "传送器",   45,  2, 1.5, 1, 0.5, 0,  0,  0,  0,   0,   0, 0, 0, 0, 0,    20),
]
r = 5
for row in rows:
    set_row(s, r, [""] + list(row))
    r += 1
ic.save(os.path.join(DATA, "#itemconfig.xlsx"))
print("#itemconfig.xlsx created: 7 item rows")

# ---------- ③ #movementconfig.xlsx：新建（单行） ----------
mc = openpyxl.Workbook()
s = mc.active
s.title = "Sheet"
set_row(s, 1, ["##var", "id", "walk_speed", "run_speed", "sprint_speed", "acceleration", "sprint_duration",
               "slide_speed", "slide_friction", "slide_turn_penalty",
               "air_control", "gravity", "jump_speed", "double_jump_count", "double_jump_speed",
               "grapple_distance", "grapple_speed", "grapple_cooldown", "blink_distance", "blink_cooldown"])
set_row(s, 2, ["##type", "int", "float", "float", "float", "float", "float",
               "float", "float", "float",
               "float", "float", "float", "int", "float",
               "float", "float", "float", "float", "float"])
set_row(s, 3, ["##group", "", "c,s", "c,s", "c,s", "c,s", "c,s",
               "c,s", "c,s", "c,s",
               "c,s", "c,s", "c,s", "c,s", "c,s",
               "c,s", "c,s", "c,s", "c,s", "c,s"])
set_row(s, 4, ["##", "主键（单行表固定 1）", "走速 m/s", "跑速 m/s", "冲刺速度 m/s", "加速度 m/s²", "冲刺持续 s",
               "滑铲初速 m/s", "滑铲衰减 m/s²", "滑铲转向惩罚（0~1）",
               "空中控制系数（0~1）", "重力 m/s²（负值向下）", "跳跃初速 m/s", "二段跳次数", "二段跳速度 m/s",
               "钩爪距离 m", "钩爪速度 m/s", "钩爪冷却 s", "闪现距离 m", "闪现冷却 s"])
set_row(s, 5, ["", 1, 2.5, 5, 7.5, 25, 2.0,
               9, 8, 0.5,
               0.35, -20, 8, 1, 7,
               30, 20, 8, 10, 10])
mc.save(os.path.join(DATA, "#movementconfig.xlsx"))
print("#movementconfig.xlsx created: 1 row")

# ---------- ④ 删除 #demo.item.xlsx ----------
old = os.path.join(DATA, "#demo.item.xlsx")
if os.path.exists(old):
    os.remove(old)
    print("#demo.item.xlsx removed")

print("DONE")
