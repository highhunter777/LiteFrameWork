# 联机 Demo 美术资源清单（3D 俯视角射击）

> 用途：M11 demo 的**资源准备清单**（模型 / 动画 / UI 美术 / 特效 / 音频）+ 规格约束 + 命名约定 + 优先级。
> 关联：《联机Demo设计》§1（玩法规格）§5（技能数据链路）、《UI控件库Prefab落地规划》§2（UI 规范）、《动效设计方案》§1（动画归属）。

---

## 0. 先看的六条规格约束（避免返工）

1. **角色骨架统一**（Humanoid 优先）——便于 Retargeting、换装、复用动画；单角色三角面 ≤ 15k（手机），材质 1–2 个（合批友好）
2. **动画走 Animator 状态机**（参数单向驱动，见《动效设计方案》§1）；**动作时长必须对齐整帧**：`帧 = 秒 × 60` 为整数（示例 0.5s = 30 帧）——时间轴导出有"秒→帧"硬校验，非整帧会被拒
3. **俯视角拍摄约束**：相机固定俯角约 50–60°、可视半径 ~25m → 细节预算放在**俯视可见面**（顶面/侧面），贴图分辨率适中即可
4. **特效不携带判定**：命中/AOE 的判定在 Sim，特效只是"判定的可见投影"（动效设计方案 §0 原则 1）；**AOE 圈的视觉半径必须与表内判定半径一致**（半径来自表，不来自美术）
5. **UI 图集 + 九宫格切片**；色板与规范见《UI控件库Prefab落地规划》§2；**UI 内不放业务文案**（文案走表/Lua）
6. **命名即引用**：表与时间轴引用一律用**名字**（编辑器下拉选择，禁手填）→ 命名规范 `前缀_类别_名称`：

| 前缀 | 用途 | 示例 |
| --- | --- | --- |
| `fx_` | 特效 prefab | `fx_hit_default`、`fx_aoe_ring`、`fx_shield_ball` |
| `anim_` | AnimationClip / Animator 状态 | `anim_rifle_fire`、`anim_death_a` |
| `sfx_` | 音效 | `sfx_gun_fire` |
| `ui_icon_` | UI 图标 | `ui_icon_skill_dash` |
| `ui_` | UI 切片/面板 | `ui_panel_bg`、`ui_btn_normal` |

7. **交付目录**：**用工程既有的顶层资源目录**（2026-09-19 修订——原定 `Assets/LiteGame/Art/{Models,Animations,Effects,UITextures,Audio}` 从未落地，实测为空目录，与外面已有素材割裂）：
   模型 `Assets/Model/` · 动画 `Assets/Animation/` · 特效 `Assets/FX/` · 音频 `Assets/Sound/` · 地图 `Assets/Map/` · UI 图 `Assets/Art/`；由 M6 收集规则按目录打 tag

---

## 1. 模型

| # | 资源 | 规格 | 数量 | 优先级 | 备注 |
| --- | --- | --- | --- | --- | --- |
| 1 | 玩家角色（带骨骼） | Humanoid、≤15k 面、可换色区分阵营 | 1 | P1 | 俯视角重点在顶面细节 |
| 2 | 敌人 NPC | 可复用玩家骨架换色/换模型 | 1–2 变体 | P1 | 首版可同模型换色 |
| 3 | 武器（枪） | 低模，可绑到手部骨骼 | 2（步枪 / 手枪） | P2 | 也可直接做进角色动画（省绑点） |
| 4 | 地图模块 | 地面 tile / 墙体 / 掩体箱 / 门 | 5–8 种 | P1 | 模块化拼 1 张 ~60×60m 灰盒图 |
| 5 | 场景道具 | 血包 / 弹药箱 / 能量球 | 3 | P2 | 拾取用，纯色块可先顶 |
| 6 | 占位体 | 胶囊 + 方块（灰盒） | — | **P0** | **工程已有可用占位**（见 §6） |

## 2. 动画（全部走 Animator，时长对齐整帧）

| 动作 | 类型 | 数量 | 优先级 | 备注 |
| --- | --- | --- | --- | --- |
| idle / run（前） | 循环 | 2 | P1 | 俯视角可先只做前向 + 原地转向 |
| strafe（左/右/后） | 循环 | 3 | P2 | 4 向或 8 向，先 4 向 |
| 攻击（静止 / 移动中） | 一次性 | 2 | P1 | 与射击节奏配合（~0.3–0.5s = 18–30 帧） |
| 换弹 | 一次性 | 1 | P2 | |
| 受击 / 死亡 / 复活 | 一次性 | 3 | P1 | |
| **技能 ×3**（冲刺 / AOE / 护盾） | 一次性 | 3 | P1 | 与时间轴技能轨道对齐（前摇帧来自表） |
| 待机变体（呼吸/环视） | 循环 | 1 | P2 | |

## 3. UI 美术

| 类别 | 资源 | 规格 | 优先级 |
| --- | --- | --- | --- |
| 面板/弹窗 | 背景板（九宫格）、标题条、关闭按钮 | 切片 | P1 |
| 按钮 | StateButton 四态（normal / press / disable / selected） | 切片 | P1 |
| 条 | 血条前条 + 后条、进度条（直线 + **环形**）、冷却遮罩 | 切片 | P1 |
| 图标 | 技能 ×3、武器 ×2、道具 ×3、货币、头像框、等级角标 | 128/256 px | P1 |
| 联机 HUD | 比分板、击杀提示条、延迟/丢包图标、阵营色块 | | P1 |
| 联机入口 | 房间列表行、准备/取消按钮、模式标签 | | P2 |
| 手机输入 | 双摇杆（底盘 + 摇杆头）、技能按钮（含冷却环） | | P1 |
| 通用 | 红点、气泡（含朝上小三角）、飘字底、引导高亮框 | | P1 |
| 字体 | 中文 TMP SDF 字体 + 数字字体（可选） | 需生成 SDF 资产 | **P1（阻塞 UI 文案）** |

## 4. 特效（全部 prefab 形态）

| 特效 | 用途 | 优先级 | 命名示例 |
| --- | --- | --- | --- |
| 枪口火光 | 开火 | P1 | `fx_muzzle_flash` |
| 弹道拖尾 | 子弹 | P1 | `fx_bullet_trail` |
| 命中火花 / 血雾 | 命中反馈（区分材质） | P1 | `fx_hit_default` / `fx_hit_flesh` |
| 爆炸 / **AOE 圈** | 范围伤害（圈半径与表一致） | P1 | `fx_aoe_ring` |
| 护盾球 | 护盾技能 | P1 | `fx_shield_ball` |
| 冲刺残影 | 冲刺技能 | P1 | `fx_dash_trail` |
| 死亡消散 / 复活光柱 | 死亡/复活 | P1 | `fx_death_dissolve` / `fx_respawn_pillar` |
| 拾取提示 | 道具拾取 | P2 | `fx_pickup` |

## 5. 音频（P2，可先静音跑）

`sfx_gun_fire` / `sfx_reload` / `sfx_hit` / `sfx_explosion` / `sfx_skill_*` ×3 / `sfx_death` / `sfx_respawn` / `sfx_ui_click` / `sfx_countdown` / `sfx_result`；BGM 1 首（循环）。

---

## 6. 现状对账（2026-09-14 实测盘点，比预期好）

### 6.1 工程已有（可直接用，无需等待美术）

| 类别 | 现有资产 | 位置 |
| --- | --- | --- |
| **角色 + 动画状态机** | Suriyun **Kazuko**（含大刀 `Kazuko_claymore.fbx`、精灵耳）+ **`Animator/Kazuko.controller`**（Idle / NormalLocomotion / CombatLocomotion + 各方向 Start-Stop 走跑与 L90/R90/L180/R180 转向 + BlendTree） | `Assets/Suriyun/Characters/Kazuko/`、`Assets/Animator/` |
| **角色动画源** | Opsive **CoreLocomotion 30 个 FBX**（IdleTurn* / Run*/Start*/Stop* 全向）——**已被 Kazuko.controller 引用** | `Assets/Opsive/OmniAnimation/Packs/CoreLocomotion/` |
| **角色预制体** | `Prefab/Player(Kazuko).prefab` | `Assets/Prefab/` |
| **武器** | **Low Poly Weapons VOL.1**：AK74 / M4 / M107 / M249 / M2_50cal / Uzi / RPG7 / M1911 / 手雷 / 烟雾 / 瞄具等 **16 个 FBX** + 材质 + 贴图 + 预制体 + 样例场景；**LITE 包** 5 个 FBX（子弹 / 刀 / 两把枪） | `Assets/Low Poly Weapons VOL.1/`、`Assets/LowPolyWeapons_LITE/` |
| **渲染档位** | URP **Performant / Balanced / HighFidelity** 三档（机型适配的现成素材，直接映射 `GameSettings.Quality`） | `Assets/Settings/` |
| tween | DOTween（vendor + 设置资产） | `Assets/Plugins/Demigiant/` |
| UI 控件模板 | 25 件灰盒模板（本次交付） | `Assets/LiteGame/UI/Widgets/` |
| 其他 | 翅膀动画（`Anim@Idle_A_wing` / `Anim@PoseA_wing`）、玩家移动状态机试验件（`Assets/StateMachine/`） | — |
| **环境/地图（2026-09-14 新增导入）** | **`RPG_FPS_game_assets_industrial`**：55 FBX / **201 prefab** / 43 材质 / 43 贴图 / **3 个场景**（**`Map_v1.unity`、`Map_v2.unity` 两张完整地图**含光照贴图与反射探针 + `Assets_showcase_scene.unity`）/ 模块件：Buildings·Industrial / Roads（Floor_elevation_sets·Road_sets）/ Fences / Containers / Barrels / Boxes / Dumpsters / Oil_tanks / Other_props / Particles（Dust·Smoke）。**体积 457 MB** | `Assets/RPG_FPS_game_assets_industrial/` |

### 6.2 差距对账（**还缺什么**）

| # | 缺什么 | 阻塞度 | 谁能补 |
| --- | --- | --- | --- |
| 1 | **Odin Inspector 未安装**（授权已有，但工程内无 `Sirenix/`） | ⚠️ 阻塞 UI 编辑器（标记工具/编排器） | **你**：导入 Odin 包 |
| 2 | **中文 TMP 字体 0 个**（无 ttf/otf、无 SDF 资产） | ⚠️ **阻塞所有 UI 文案显示** | **你**：放一个开源中文字体（思源黑体 / 阿里普惠体等）→ 我用 TMP 生成 SDF 资产 |
| 3 | **战斗类动画缺**：现有仅 locomotion（走跑转向）；缺 **开火 / 挥砍 / 受击 / 死亡 / 冲刺 / 施法** | 🟡 不阻塞 demo（可用 CombatLocomotion + 时间轴位移占位），但观感差 | 你（正式素材）；我可先补 Animator 状态 + 占位剪辑 |
| 4 | **战斗特效 prefab 缺**（包内仅 `Dust_v1` / `Smoke_v1` **环境粒子**；枪口火光/命中火花/AOE/护盾/死亡消散/复活光柱全无） | 🟡 不阻塞（灰盒可代） | **我可程序化生成 6 件灰盒战斗特效** |
| 5 | **音频 0 个** | 🟡 不阻塞（静音可跑） | 你（音效素材）；先用引擎内置占位 |
| 6 | ~~demo 战斗场景缺~~ → ✅ **已解决**：`Map_v1.unity` / `Map_v2.unity` 两张完整工业地图（含光照）+ 模块化件可拼新图 | — | —（注意：见 §6.4 包体约束） |
| 7 | **UI 图集 / 图标 / 头像** | 🟢 灰盒纯色可顶（模板即灰盒） | 你（P2 正式素材） |
| 8 | ~~资源目录骨架未建（`LiteGame/Art|Audio|Effect|Fonts`）~~ → **口径已改（2026-09-19）**：资源放工程既有顶层目录（见 §0-7），`Assets/LiteGame/Art/` 不再使用（实测为空） | 🟢 | ✅ 已定 |
| 9 | 杂项：`Assets/LiteGame/UI/_probe` 空目录 | 🟢 | 我（删除 API 被工具安全钩子拦，需手动删） |

### 6.4 新素材包的三条注意事项

1. **457 MB 的包体约束**：M6 打包时**绝不能整包收集**——只收集 demo 实际用到的模块（地图取 `Map_v1` 场景链 + 少量 props），否则包体/下载量失控。收集规则要按"用到的 prefab 及其依赖"来定（YooAsset 的依赖收集天然支持，需在 `AssetBundleCollectorSetting` 里按目录/tag 划清）
2. **地图可用性待验**：`Map_v1/v2` 是**为 RPG/FPS 设计**的场景（可能含自己的导航/触发器/脚本引用）——接入前需检查是否引用该包外的脚本（`脚本=0` 说明包内无脚本 ✓ 风险低），并把光照/后处理与我们的 URP 档位对齐（现有 `Settings/URP-*` 三档）
3. **导入告警（无害）**：控制台有一条 `Identifier uniqueness violation: 'Road_set_v1_road_up_1m_LD_f', Type:Mesh`——该 FBX 内部有重名网格，Unity 无法保证后续导入重链；**属素材固有告警，不影响使用**（若嫌噪声可删该 LD 变体或忽略）

---

## 7. 再盘点（2026-09-14 晚）与免费资源推荐

### 7.1 全景（工程 1200 MB / 22 个顶层目录）

| 顶层 | 体积 | 内容 |
| --- | --- | --- |
| `Map/` | 457 MB | `RPG_FPS_game_assets_industrial`（含 **Map_v1/Map_v2 两张完整地图**） |
| `Animation/` | 296 MB | **`Basic Shooter Pack`(17) + `Aim`(7) + `Shoot`(6) + `Reload`(1)**：Erika Archer 枪械动画共 **31 FBX**（firing rifle / hit reaction / reloading / rifle aiming idle / jump / gunplay）+ `Opsive`（CoreLocomotion 30） |
| `Art/` | 201 MB | **`Skill Icon Pack Wenrexa 4.0`(842)**、**`789_Lorc_RPG_icons`(789)**、**`kenney_input-prompts_1.5`(4667，键盘/手柄/触屏输入提示)**、两个 **未导入的 `.unitypackage`**（4k Fantasy GUI 28MB、Emerald Treasure **153MB**） |
| `Model/` | 103 MB | Low Poly Weapons VOL.1(16) + LITE(5) + Suriyun Kazuko（含大刀/翅膀动画） |
| `LiteGame/` | 109 MB | 我们的框架/UI 模板/配置；**`Fonts/11月金楷系列`（5 个中文 ttf，各 22MB）** |
| 其余 | ~28 MB | Plugins(DOTween 等) / XLua / GameFramework / 框架代码 |

分类计数：模型 **143** / .anim 0 / controller **1** / prefab **251** / 材质 59 / 贴图 **4759** / 音频 **0** / 字体 39（含 kenney 图标字体）/ **TMP SDF 字体 0** / 场景 **15**

### 7.2 缺口（更新后只剩 3 项）

| # | 缺什么 | 阻塞度 | 备注 |
| --- | --- | --- | --- |
| 1 | **Odin 未安装**（`Sirenix/` 不存在） | ⚠️ 阻塞 UI 编辑器 | 授权已有 → 从 Asset Store 下载导入即可（**无需再花钱**） |
| 2 | **TMP SDF 中文字体资产 = 0**（ttf 有，未转 SDF） | ⚠️ 阻塞 UI 文案 | **我可生成**（用现有金楷 + 建议补一款开源黑体做正文） |
| 3 | **音频 0 个** + **战斗特效 0 个** | 🟡 不阻塞（静音/灰盒可跑） | 推荐见 §7.3 |

**已解决（本轮盘点确认）**：地图 ✅（Map_v1/v2）、战斗动画 ✅（Erika 31 个：开火/换弹/受击/瞄准）、UI 图标 ✅（技能图标 842 + RPG 图标 789 + 输入提示 4667）、中文字体文件 ✅（金楷 5 款）

### 7.3 免费/便宜资源推荐（按缺口）

| 缺口 | 推荐 | 说明 |
| --- | --- | --- |
| **中文字体（正文用）** | **思源黑体**（SIL OFL，事实标准）/ **阿里巴巴普惠体 3.0**（阿里官方免费商用）/ **MiSans**（小米）/ **HarmonyOS Sans**（华为）/ 标题可用 **得意黑**（OFL）/ 文艺正文 **霞鹜文楷**（OFL） | 现有金楷是**书法装饰体**，正文/数值建议配一款开源黑体；**商用前到官方页核对最新条款并截图存档**（字体索赔案例常见） |
| **音效（0 个，最大缺口）** | **Kenney Audio**（CC0，射击/UI/爆炸/脚步全套，kenney.nl）/ **99Sounds**（免费音效库）/ **Freesound**（筛 CC0）/ **Pixabay·爱给网 CC 区** / Asset Store 免费 SFX 包 | CC0 可商用免署名；一套 Kenney 就够 demo（射击/命中/UI/结算） |
| **战斗特效（0 个）** | ⭐ **Unity 官方 `Particle Pack`（Starter Assets，免费）**——火焰/爆炸/冰/溶解等，最省事；**Kenney Particle Pack**（CC0）；卡通风格可看 **Epic Toon FX** / **Cartoon FX Free**；枪口/命中专用可搜 Asset Store 免费区 "Muzzle Flash / Impacts" | 官方包 + Kenney 组合足够 demo；正式素材后续再换 |
| **更多角色/敌人（可选）** | **Quaternius**（CC0，数百低模含角色+动画）/ **KayKit** / **Kenney** / **Mixamo**（免费骨骼动画） | demo 现有 **Erika Archer（动画包自带模型）+ Kazuko** 已够：一个玩家、一个敌人（换色即可） |
| **输入提示图（手机摇杆/键位）** | ✅ 已有 `Art/kenney_input-prompts_1.5` | 无需再找 |
| **GUI 面板素材** | 已下载但**未导入**：`Art/` 下两个 `.unitypackage`（4k Fantasy GUI 28MB、Emerald Treasure 153MB） | ⚠️ **来源为 3DCGHub 聚合站，商用授权需核实**（此类站点常见盗版转售，建议核对原厂许可后再用） |

### 7.4 本轮发现的两个技术障碍（接入前必须处理）

1. **Rig 不匹配**：`Erika Archer` 动画 = **Generic（animationType 2）**，`Kazuko` = **Humanoid（3）** → **动画不能互换套用**。建议：**demo 的玩家与敌人用 Erika Archer（自带射击动画 + 模型）**；Kazuko 保留作 locomotion 角色（或将来在 DCC 里重定向）
2. **两个 GUI 包未导入**：是 `.unitypackage` 文件（28MB + 153MB），需双击导入（导入耗时较长）；导入后仍需核实授权来源（见 §7.3 末行）

---

## 8. 第三轮盘点与更正（2026-09-14 深夜）——缺口已基本清空

> 本节**更正 §7 的三条结论**（§7 的检查方法有误/状态已变：工程在并行导入中）。**以本节为准。**

### 8.1 三条更正（此前判断有误）

| §7 的结论 | 实际 | 说明 |
| --- | --- | --- |
| ❌ "Odin 未安装" | ✅ **已安装**：`Assets/Plugins/Sirenix`（**Odin Inspector 4.0.2.3**），`Sirenix.OdinInspector.Editor` 等程序集已加载 | 我此前只查 `Assets/Sirenix`（漏了 `Plugins/` 下）——**检查方法错误** |
| ❌ "音频 0 个" | ✅ **424 个 / 4MB**：`Sound/{kenney_impact-sounds, kenney_interface-sounds, kenney_music-jingles, kenney_rpg-audio, kenney_ui-audio}`（Kenney CC0） | 状态已变（并行导入） |
| ❌ "战斗特效 0 个" | ✅ **65 个粒子 prefab**：`FX/ParticlePack`（Unity 官方 Particle Pack）+ `FX/LuffyEffect`；另 `Explosion Effects` / `Steam Effects` 包（BigExplosion / FireBall / PlasmaExplosion / FlameStream / GoopSpray 等） | 我按**文件名**过滤（应改按**是否含 ParticleSystem 组件**）——**检查方法错误** |

**附带确认**：`Art/Emerald Treasure/`（含 Demo Scene + 2 个 AnimatorController）与 `Art/HONETi/fantasy_gui_4/` 已存在 → **两个 GUI 包内容其实已经导入**（`.unitypackage` 文件仍留在 Assets 里，见 8.2-2）。

### 8.2 当前真实缺口（只剩 4 项，且无一是"等美术"）

| # | 项 | 性质 | 处置 |
| --- | --- | --- | --- |
| 1 | **TMP SDF 中文字体资产 = 0**（ttf 齐全但未转） | ⚠️ **唯一阻碍 UI 文案** | **我可用 TMP 生成**（金楷作标题；建议正文补一款开源黑体） |
| 2 | **`.unitypackage` 原始包留在 `Assets/`**（28MB + 153MB） | ⚠️ **会被 YooAsset 收集进包体**（白进 ~180MB） | 移到 `Assets/` 外（或加入收集排除）；同时**核实 3DCGHub 来源的商用授权** |
| 3 | **Erika 射击动画没有 AnimatorController**（现有 5 个 controller：Kazuko + Emerald Treasure×2 + ParticlePack demo + Cinemachine 测试） | 🔧 接入工作，非资源缺口 | **我可建**（31 个动画 → idle/aim/开火/换弹/受击/死亡状态机；注意 **Generic Rig**，用 Erika 模型而非 Kazuko） |
| 4 | **资源导入设置待优化**（手机包体/内存） | 🔧 M6 前处理 | 488 个**未压缩**贴图、6 个 **Read/Write 开启**、13 张 **≥4K**（Kazuko 3 张 4K + Map 光源贴图 5 张 4K + 火焰/烟雾 tif + 天空盒）、1233 个无 mipmap |

### 8.3 工程健康度（本轮全查）

| 项 | 结果 |
| --- | --- |
| **缺失脚本/空引用** | ✅ **386 个 prefab，0 空引用**（导入干净） |
| 编译 | ✅ 0 error（2 条有意 `async` 无 `await` 警告） |
| 原生平台库 | ✅ Android / arm64 / iOS / x86 / x86_64 / WebGL / WSA 齐全 |
| 场景 | 21 个（我们的 Boot/Test/GuideScene + 各包 Demo 场景；`Map_v1`/`Map_v2` 可作 demo 地图） |
| 待清理 | `Plugins/UniRx`（未使用，建议移出减编译与包体）、`XLua/Tutorial` 示例场景、`LiteGame/UI/_probe` 空目录、Assets 下的 `.unitypackage` |

### 8.4 结论

**"等美术"的阶段结束了**——模型/动画/UI 图标/字体/音效/特效/地图**全部就位**，剩余 4 项要么我能做（TMP 字体、Erika Controller、导入设置）、要么是清理动作（.unitypackage 移出 + 授权核实）。**联机线（M7 起）不再被资源阻塞。**

---

## 9. 第四轮：口径与状态更新（2026-09-14 深夜）

### 9.1 口径调整：**仅 demo，不商用**

用户明确：**本项目只做 demo，不商用**。→ §7.3/§7.4 中"核实 3DCGHub 商用授权"等**授权阻塞项全部撤销**（保留一句备查：若将来转商用，需回头核对这些素材的授权链路）。素材来源不再作为选型约束。

### 9.2 Odin：**实测可用**（含证据）

| 验证层 | 结果 |
| --- | --- |
| 编译期 | 在 `LiteGame.Editor` 直接使用 `Sirenix.OdinInspector`（`Button`/`InfoBox`/`PropertySpace`/`GUIColor`）+ `OdinEditorWindow` + `SirenixEditorGUI` → **零错误**（Odin 以预编译 DLL 置于 `Assets/Plugins/Sirenix`，`overrideReferences:false` → 自动引用，**无需在 asmdef 里显式添加**） |
| 运行期 | 菜单 `LiteGame/Dev/Odin 探测` 成功打开 `OdinEditorWindow` 窗口（日志确认）；探测件已删 |
| 版本 | Odin Inspector **4.0.2.3** |
| **Odin Validator** | ❌ 未装（独立付费项）→ 按既有决议：**校验展示面板自研**，其余面板用 Odin |

### 9.3 中文字体：**已就绪**

| 项 | 结果 |
| --- | --- |
| 用户提供 | `LiteGame/Fonts/已生成字体/{3500,7000}.unitypackage`（导入前为压缩包） |
| 动作 | **已代为导入**，并把两个 TMP 资产从 `Assets/` 根**移入 `Assets/LiteGame/Fonts/`**；两个 `.unitypackage` 原件已从 `Assets/` 删除（如将来要重装请从原下载处重取） |
| 资产 | **`NotoSansSC SDF`（3952 字）**+ **`NotoSansSC-Regular SDF`（7189 字）**，思源黑体简体的 TMP 子集 |
| 关键设置 | ✅ **TMP 默认字体已设为 `NotoSansSC SDF`**（`TMP Settings` 位于 `FX/ParticlePack/TextMesh Pro/Resources/`；验证 `TMP_Settings.defaultFontAsset` 命中）→ **中文文案阻塞解除** |
| 艺术字体 | `11月金楷系列` 5 个 ttf（22MB/个，书法体）→ 用途：标题/艺术字（不建议进包全部；若用则建议转 SDF 子集） |

### 9.4 `.unitypackage` 残留清理

| 位置 | 状态 |
| --- | --- |
| `Art/` 下 GUI 包（28MB + 153MB） | ✅ 用户已删 |
| `LiteGame/Fonts/已生成字体/`（6.9MB + 8MB） | ✅ 已删（导入完成后；如实说明：删除为永久，如需原件请重取） |
| `Plugins/Sirenix/Demos/*.unitypackage`（4 个，0MB） | 保留（Odin 包自带示例，体积可忽略） |

### 9.5 唯一剩余决策点（需要你拍板）

**控件模板的文本组件用 TMP 还是 UGUI `Text`？**

| 方案 | 改动 | 利弊 |
| --- | --- | --- |
| **A. 模板改 TMP（推荐）** | 构建器的文本节点改用 `TextMeshProUGUI`；`VirtualList`/`CountText`/`Countdown`/`HpBar` 等的 `Text` 字段类型随之调整（≈25 模板 + 6 处字段 + 断言） | 中文用 SDF 清晰、包体小（用 3500 字子集）、行业标准；受控 API 的 `SetText` **已内建"TMP 优先"** ✓ |
| B. 保留 UGUI `Text` + 金楷 ttf | 零代码改动，仅给模板文本挂金楷 `Font` | 中文可显示但：①22MB×N 的 ttf 进包 ②动态字体在手机上清晰度/图集重建开销差 ③与"TMP 优先"的既有 API 设计逆向 |

**建议 A**（一次改到位，联机 HUD 全是中文，越晚改越贵）。

### 6.3 建议的下一步（按阻塞度）

1. **你**：导入 **Odin** + 放**中文字体**（这两件是唯一真阻塞）
2. **我**：① 建 `LiteGame/{Art,Audio,Effect,Fonts}` 目录骨架 ②生成灰盒特效 prefab（6 件）③生成灰盒地图场景（与 AOI 参数配套）④给 `Kazuko.controller` 补攻击/受击/死亡/冲刺状态（先挂占位剪辑）⑤清理 `_probe`
3. 其余（正式战斗动画/音效/UI 图集）按 §1-§5 准备，P1/P2 不阻塞 M7-M10

## 7. 交付约定（你准备时按这个来）

1. 按 §0.7 目录放置；文件名即引用名（§0.6 规范）
2. 交付时附一份**名清单**（模型/动画/特效/音效各一段），我并入 Luban 表的下拉选项
3. 动画 FBX 交由我建 Animator Controller（或你直接给 Controller + 状态名清单）
4. UI 图集：整图 + 九宫格边界信息（或直接给切好的 Sprite）
5. 特效 prefab：粒子参数随便，只要名字与表一致；判定圈不接受美术半径（以表为准）
