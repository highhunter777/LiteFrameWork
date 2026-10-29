# AGENTS.md——代理与新成员必读入口

> 本文件是**动代码前的必读清单与红线速查**。详细规则与查询表在 `Docs/修改指南.md`，本文件只做收敛索引；两者冲突以 `Docs/修改指南.md` 为准。

## 必读顺序（动代码前）

| 序 | 文档 | 回答什么 |
|---|---|---|
| 1 | 本文件 | 基础原则、红线速查、常用命令 |
| 2 | [Docs/修改指南.md](Docs/修改指南.md) | **§1 代码实现规范（红线，含审计反例）→ §2 修改规范（动手前/改动中/收尾流程）→ §3 领域查询表（"我想改 X 去哪改、改完跑什么"）** |
| 3 | [Docs/设计文档/架构/术语规范.md](Docs/设计文档/架构/术语规范.md) | 术语表（规范级）：全仓现用词 ↔ 业界通用词的权威对照（三档处置：换/留/英译）＋ Lane/登记一词多义消解 ＋ 代码标识符用词规约（UI/Hud 大小写、Take 消费语义）——新写的文档与代码即用，存量随改随换 |
| 4 | [Docs/审计/README.md](Docs/审计/README.md) · [客户端登记](Docs/审计/客户端/登记/) · [服务端审计表](Docs/审计/代码质量审计-服务端.md) | 审计流程与按类型登记/归档（一类型一文件；归档一行一条、单文件 ≤100 条；客户端已拆分，服务端迁移中）——改文件前查该文件有没有已登记债；**新代码禁止产生同型问题** |
| 5 | [Docs/项目说明/README.md](Docs/项目说明/README.md) | 项目是什么、怎么跑、数据怎么流、怎么分层 |
| 6 | [Docs/设计文档/README.md](Docs/设计文档/README.md) | 目标架构裁决（与本指南/代码冲突时以设计文档为准） |
| 7 | [Docs/开发导航/README.md](Docs/开发导航/README.md) | 代码地图、程序集引用图 |
| 8 | [Docs/项目说明/10-Windows云主机部署.md](Docs/项目说明/10-Windows云主机部署.md) | 云侧**现行形态**（Windows Server 2025 原生）的部署与运维——**动云主机前必读**；速查见本文件「云侧环境（外环）——操作必读」节 |
| 9 | [Docs/项目说明/11-联机测试流程.md](Docs/项目说明/11-联机测试流程.md) | **从改完代码到云上打完一局**的主线（发版矩阵 / 一键部署 / 测试者接入 / 开测与 GM 兜底 / 观测 / 收尾）——联机测试照它走 |

## 基础原则

**代码规范**

- 通用框架：面向通用性和扩展性的框架设计实现。
- 单一职责：代码文件与类定义均保持单一职责。
- 目录清晰：文件目录结构清晰、层次分明。
- 模块聚合：业务模块的代码文件与相关美术资源放在同一模块目录下，并在该目录内划分子目录管理。
- 命名统一：遵循统一的命名规范。
- 消除重复：减少重复实现，优先复用。
- 注释准确：代码注释仅描述现有设计与实现。
- 架构一致：除 ECS 架构外，均遵循面向对象规范。
- 开闭原则：对修改关闭，对扩展开放。
- 禁止硬编码：避免硬编码，使用配置、常量或数据驱动。
- 生产级质量：代码实现需达到生产级标准，注重扩展性与通用性。

**文档规范**

- 设计文档与施工进度文档分开。
- 设计文档仅描述最新设计；进度文档仅保留最新进度。
- 代码完成后，应回写对应文档。
- 新增/修改/移动/合并/归档文档按 `doc-authoring` skill（落点决策 → 命名元数据 → 索引登记 → `--suite Doc` 自检）。

**工具规范**

- PowerShell 脚本统一使用 pwsh。

**资源规范**

- 代码与脚本资源可纳入版本管理。
- **入库判定只看资源类型、不看所在目录**：只有**配置资源、预制体模板资源、场景资源**三类入库；这三类落在共享目录/模块目录里同样入库，非这三类的资源落在共享目录/模块目录里同样不入库（收集目录是"进包来源"、不是"入库理由"）。
  - **例外（2026-10-13 用户裁决）**：**TMP 中文字体 NotoSansSC SDF**（`Assets/ShareResource/Fonts/`，含 OFL 许可文件）入库——它是 75 处文本引用的视觉单一来源，7 个 EditMode 用例按真实字体资产加载；**URP 管线资产**（`Assets/Settings/`、`Assets/UniversalRenderPipelineGlobalSettings.asset`）入库——两者均为 ScriptableObject 配置资源，且已入库的 `ProjectSettings/GraphicsSettings.asset` 按 GUID 引用 URP 这批。金楷系列字体（110MB、零引用）与其余素材维持不入库。
- 第三方付费资源不纳入版本管理。
- **第三方资源引用规范**：产品资产**不得直接引用第三方资源**；须先把用到的第三方资源**复制进我们自己的收集目录**（模块内使用资源目录或 `Assets/ShareResource`，见下条目录分层），再引用**复制后的这份**。复制件按类型判定——第三方付费资源不入库，故其复制件也不入库（随打包机本地还原）；第三方原始库（如 `Assets/Synty/`）只作可复制的来源、不直接消费。
- 其他资源不纳入版本管理。
- **目录分层（2026-10-11 裁决）**：Assets 顶层只放原始资源；进包资源只放两类收集来源——**模块内使用资源目录**（模块聚合，样板 `LiteGame/Lua`）与**共享资源目录 `Assets/ShareResource`**（其下自建顶层子目录分类）；映射与迁移面见 [Docs/设计文档/客户端/内容/资源收集目录专项设计.md](Docs/设计文档/客户端/内容/资源收集目录专项设计.md) §2/§5。

> 以上原则的落地细则与审计反例见 [Docs/修改指南.md](Docs/修改指南.md) §1 与两份质量登记表。

## 核心红线速查

1. **BuildHash 源集**：改 `Assets/LiteSim/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol,Transport/Security}`、`Assets/GameData/Generated`（Luban 生成表行类/读取器）、脚本 `SHARED_LINK_FILES` 登记的两端源链接共编件（`Serialization.Luban/{SimConfigMapper,TableLayout}.cs`）→ ★ `python scripts/codegen/gen-build-hash.py`（权威清单＝该脚本 `SHARED_TARGETS/SHARED_LINK_FILES`）；漏跑＝出包门禁被拦、两端常量不同步时 Join 握手拒进房（2026-10-07 起开发期不校验，守卫在 build-player.py 门禁步 `--check-shared`）。**表数据（`.bytes` 数值）已移出闭包**（2026-10-14 批 E）——数据面守卫＝装载后**运行期摘要**（`CombatConfigDigest`：判定面表字节 + 本局规则；客户端按 `StartGame.ConfigHash` 复算比对，不等拒局），表值可两端热更不出包；但**改表源跑完 `Luban/gen.bat` 后仍要重跑本脚本**（生成读码面在闭包内），再走 `ConfigService.GameplayDigestBase` 侧的两端发布顺序。**同一脚本还产出第二个目标**：`Server/MetaServer/Host/BuildHash.g.cs`（Meta 构建身份＝`META_TARGETS` 源集摘要，启动日志 `build=` 用它；**不参与准入判定**，故不挂出包门禁——发布 Meta 产物前跑 `--check` 复算两个常量）。
2. **R11 房间内核纯度**：`Assets/RoomServer/Core/` 禁 Console/系统时钟/文件 IO/proto/LiteNet。
3. **R13 数值读口**：机制面禁读静态读口可变值，数值经实例参数传递（consts 豁免）。
4. **Unity 序列化资产**（场景/Prefab/Material/.meta）：一律走 unity-pipeline CLI（`Docs/工具链/Unity操作规范.md`），禁直改文件。
5. **禁硬编码**：魔法数字/字符串进常量或表；跨层共享常量 C# 单源权威，Lua shim/工具引用它；生成器引用运行时常量；"必须与 X 一致"只靠注释＝违规，要机制化（单源/生成/守卫测试三选一）。
6. **禁新增 public static 可变状态**（豁免名单封闭）；确定性判定路径（伤害/弹药/命中/checksum）禁读任何静态可变值——包括 dev 宏内。
7. **错误契约统一**：已关闭服务再调用抛 `ObjectDisposedException`；程序员错误 fail-fast、内容降级 Log.Warning+默认值；同层姊妹服务同契约。
8. **日志统一 `Log.*` 门面**；临时诊断带 `[Diag]` + 摘除批次号，批次收口必清；热路径禁周期性 Warning。
9. **UI 单写者**：产品 UI 开关唯一走 `UINavigationController`；控件计时统一 `UiAnimationClock`；动效参数走 `UiFx` 收口。
10. **接线纪律**：注册进容器的服务必须有生产消费者；"待接线"必须挂施工进度批次号；禁反射取私有服务（走 `GameEntry.TakeContainer()`）。
11. **类设计**：单一职责；>400 行多职责先给拆分方案；禁 public 可变字段状态袋；禁哈希当身份键；事件回调上接口端口。
12. **文档回写**：代码完成后回写设计文档/施工进度；修了审计登记条目回写"修复登记"表；新问题按格式追加登记。
13. **真机验证必须模拟人类操作**：真机/Play 端到端验证必须走真实操作路径（真实指针/键盘模拟驱动界面与流程：入口 → 操作 → 副作用断言）；UI 面先"可点性自检"（交互控件射线命中）再模拟点击；禁以直调 API/桩调用冒充"操作过"——结论必须覆盖"可操作"，全绿 ≠ 可点（细则见 [Docs/修改指南.md](Docs/修改指南.md) §2.3）。

## Skill 索引（按任务触发，ZCode）

下列流程性规范已抽为通用级 skill（`.agents/skills/`，ZCode/Claude Code 等跨工具标准位），ZCode 会话中按任务自动加载；本文件的红线条目保留（同时服务无 skill 机制的其他工具）。另有 PostToolUse 提醒 hook：脚本单源在 `scripts/hooks/shared-source-reminder.py`（客户端中立），由 `.zcode/config.json`（ZCode）与 `.claude/settings.json`（Claude Code）各自挂载，在编辑器触碰共享源集/表/Unity 序列化资产时即时提醒，不依赖模型自觉。文档纪律（设计与进度分离 / 状态只由 git 事实回填）的守卫面是 `scripts/gate/doc-lint.py`，独立门禁 `scripts/gate/test.py --suite Doc`（或直跑 `python scripts/gate/doc-lint.py`）。

| skill | 触发 |
|---|---|
| `build-hash-sync` | 动共享源集（红线 1）、`.proto`、Luban 表源/生成读码面；出包前哈希检查（表**数值**走数据面运行期摘要，见该 skill） |
| `unity-asset-pipeline` | 场景/Prefab/Material/SO/.meta、Assets 内移动删除、工程设置（红线 4） |
| `directory-move-sync` | 目录搬动（六面同步 + unity-pipeline） |
| `batch-wrapup` | 一批改动收尾/提交前（修改指南 §2.3 必跑 + §2.4 回写） |
| `audit-debt-check` | 动源码文件前查两份质量登记表（修改指南 §2.1 第 3 步） |
| `unity-pipeline` | 驱动运行中的 Unity Editor/开发 Player（CLI 包 `unity-pipeline`：装包、后台保活、edit→recompile→run_tests 环、eval C#、运行时热改码）——红线 4 的 CLI 执行层 |
| `doc-authoring` | 新增/修改/移动/合并/归档 `Docs/` 下文档：落点决策、命名与元数据、设计↔进度分离、索引零孤儿、卷内追加章、链接与锚自检（D1～D6 门禁） |

## 常用命令

```powershell
# 工具链统一入口（编辑器 Hub：LiteGame/工具链）
python scripts/toolchain.py                                 # 列工具 / 单一 CLI（list/check/docs/open/run/generate/verify/build/doctor）
python scripts/toolchain.py check                           # 清单/文档/CI/hook/计划任务注册面
python scripts/toolchain.py doctor                          # 环境 + 注册面巡检（继承 restore-check 阻塞项）
python scripts/toolchain.py open <tool-id>                  # 在编辑器 Hub 打开并选中工具

# 验证基线（白天代码批，一切改动的底线）
python scripts/gate/test.py --suite L1 --profile PullRequest     # L1 纯逻辑（含纪律扫描/表守卫/BuildHash 守卫）
python scripts/gate/l0-dep-scan.py                            # 依赖与秘密扫描
python scripts/codegen/toolbox.py                            # 生成器工具箱（build-hash/proto/luban/sync-code/refresh-hash 统一入口；形态见专项设计）
python scripts/codegen/gen-build-hash.py                     # ★ 共享源集/生成读码面/Meta 源集改动后必跑（一次写两常量；表值 .bytes 改动不必跑）
python scripts/codegen/gen-build-hash.py --check             # 复算比对两个常量（发布 Meta 前）
python scripts/codegen/gen-build-hash.py --check-shared      # 只复算共享源集常量（出包门禁用的是这个）
python scripts/codegen/gen_proto.py --protoc <protoc.exe>    # ★ 改 .proto 后
Luban/gen.bat                                                # ★ 改表源后（双端重生成）
python scripts/gate/l2-unity-gate.py                          # L2 Unity 门禁（夜间）
python scripts/gate/restore-check.py                          # 环境可复现检查
```

Unity 交互（改场景/Prefab/跑 PlayMode）一律走 unity-pipeline CLI，见 `Docs/工具链/Unity操作规范.md`。

## 云侧环境（外环）——操作必读

> 联机测试云主机（Windows Server 2025 Server Core，**2026-10-18 起为现行形态**）。部署/运维全细节见 [Docs/项目说明/10-Windows云主机部署.md](Docs/项目说明/10-Windows云主机部署.md)；Linux/compose 旧形态见 [09-上云联机测试](Docs/项目说明/09-上云联机测试.md)（备用）。

| 项 | 值 |
|---|---|
| 公网 IP / 登录账号 | **见 `LocalNotes/cloud-host.md`**（机器私有、**不入库**；只走 SSH 密钥、无口令登录）——仓库文档一律只指向该文件，不回显其值 |
| 通道 | **运维 SSH 走 WireGuard 隧道入口 `10.8.0.1:22`**（安全组不保留公网 `22/tcp` 入方向：本机出口 IP 会变，IP 白名单不可靠；本机别名 `litegame-cloud-tunnel`，`scripts/deploy/*.py` 默认只走隧道，隧道不可达直接 fail-fast；公网仅显式破窗）。应用面同样走隧道 `10.8.0.1`（Meta `5000`、Room UDP `17777` 只绑隧道地址）。细节/破窗见 10 手册 §4.1 |
| 托管 | 任务计划 `LiteGame-Meta` / `LiteGame-Room`（`SYSTEM` + `AtStartup`）；**改配置必须重启任务才生效**（配置是启动期快照） |
| 云上路径 | `C:\litegame\{meta,room,GameData,mongo,wg}`；配置源＝`deploy/roomserver.windows.json`（**普通房默认 `standard`，验结算**；`sandbox` 仅由 GM 页显式创建云端测试房：隐藏、不限时、无击杀上限、不结算） |
| 运维(GM)端点 | `http://10.8.0.1:9101/gm/*`（只绑隧道地址、仅 POST + 头 `X-GM-Key`，密钥见 `LocalNotes/cloud-host.md`）：`rooms` / `start` / `abort` / `kick` / `settle-flush`；设计见 [《云端GM通道专项设计》](Docs/设计文档/架构/云端GM通道专项设计.md)。**改房间服 C# 后必须重新 `dotnet publish -r win-x64 --self-contained` 并上传 `room/`**（只改配置则重启任务即可） |

**红线**：

1. IP 与用户名可入库；**私钥（`~/.ssh/id_ed25519_litegame`）、`deploy/.env` 密钥、测试者 `.conf` 一律不入库、不回显、不截图**。
2. 这是**封闭测试环境**：不得公开/宣传，**不得作为"可发布"判据**。
3. **禁止在 SSH 会话里 `Start-Process` 起常驻进程**（随会话被回收）——一律走任务计划（10 手册 §6）。
4. 换机/换公网 IP ⇒ **已发出的测试者 `.conf` 全部作废**（`Endpoint` + 服务端 `PublicKey` 两处要重生成）；客户端与游戏包不用动（连的是 `10.8.0.1`）。

命令级速查（发布 / 模板 / GM / 接入 / 观测）：[Docs/开发导航/云侧速查.md](Docs/开发导航/云侧速查.md)；流程与判据见 [11-联机测试流程](Docs/项目说明/11-联机测试流程.md)。

**常用（换房间配置后让它生效）**：

（下列用 `~/.ssh/config` 别名 `litegame-cloud-tunnel`（隧道入口 `10.8.0.1:22`；公网 `22/tcp` 已关），写法见 10 手册 §4.1）

```powershell
# 云端运维动作（自动起/关 SSH 隧道；本机端口避开 Windows 排除区间，勿用 9101）
python scripts/deploy/gm.py --action rooms
python scripts/deploy/gm.py --action start --room <房号>
```

```powershell
scp deploy/roomserver.windows.json litegame-cloud-tunnel:C:/litegame/room/roomserver.json
# 复杂命令别在 ssh 里拼引号（Windows OpenSSH + PS 5.1 会静默失败/按 GBK 误解中文）：
# 走「scp 上传 .ps1 + powershell -File 执行」
ssh litegame-cloud-tunnel powershell -NoProfile -ExecutionPolicy Bypass -File C:\litegame\apply-room.ps1
# 判据：C:\litegame\room\out.log 启动横幅含 rooms=[standard,four,eight,sandbox] default=standard
# 注：仓库配置已改 default=standard，云上实例需重新上传本文件并重启 LiteGame-Room 才生效
```

## 工作约定

- **脚本语言**：`scripts/` 已**全量 Python**（P0–P5 完成，2026-10-21）；新增脚本一律 Python，不再新增 `.ps1`；`toolchain.py check` 对任何 `scripts/**/*.ps1` 报错。
- PowerShell 存量脚本统一用 `pwsh`。
- **Agent 命令执行环境约定（2026-10-11，按本会话实测坑位沉淀）**：
  - 跑门禁/项目 `.ps1` 脚本 → `pwsh`（勿用 powershell 5.1）；GBK/乱码读面也用 `pwsh Get-Content`；
  - 文件手术（批量替换/改名/编码雷区/CRLF 保留）→ **python heredoc 字节模式**（`python - <<'EOF'`）——仓库存在 GBK/UTF-8 混编文件，禁无编码假设的整文件重写；写回用 `newline=''`/`'\r\n'` 保行尾；
  - git/快查用默认 Git Bash——坑：**MSYS 路径转换**（Windows 路径参数前缀 `MSYS_NO_PATHCONV=1`）、`/tmp` 为虚拟路径勿跨工具交换文件、grep 为 ugrep；
  - `.bat` 入口 → `cmd //c xxx.bat`；解析 `unity`/pipeline 的 JSON 输出 → python（`raw_decode` 容忍尾随数据）；
  - Unity 一切操作只走 unity-pipeline CLI（与 shell 无关）；深度嵌套引号的 eval 改用 run_script 落文件。
- 设计文档与施工进度分开：设计文档只描述最新设计，进度文档只保留最新进度。
- 文档与代码不一致：以代码为准，并顺手修文档。
- 目录搬动当批同步：`ScanTargets.cs`（纪律目标）、YooAsset 收集组、`Tests.slnx`/csproj、构建器路径、[Docs/开发导航/代码地图.md](Docs/开发导航/代码地图.md)。
- 资源放置与收集目录规划（打资源包热更）按「基础原则·资源规范」＋ [资源收集目录专项设计](Docs/设计文档/客户端/内容/资源收集目录专项设计.md)；版本管理按既有 `.gitignore` 裁决执行，两者无关。
