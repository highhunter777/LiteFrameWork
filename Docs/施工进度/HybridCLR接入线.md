# HybridCLR接入线（C# 热更接入）

> **定位**：C# 热更接入（HybridCLR 9.0.3）的施工线：P0 设计裁决与迁移清单、P1 工具接入、P2 分区与装载、P3 热更链。P4 双端演练与 P5 商业验收另批。

> **章法**：一卷一条线；每章＝一批记录（标题、正文、证据），章首保留 `原文件` 行供旧引用查证。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | 迁移任务清单（P0–P3） | — |
| 2 | P0 设计裁决落地 | — |

---

## 第1章 迁移任务清单（P0–P3）

<a id="ch1-迁移任务清单"></a>

> 依据：[路线图 §5.3](../设计文档/架构/客户端架构重构演进路线图.md)（分界/身份/旧房/回滚）· [热更与内容发布专项设计 §3](../设计文档/客户端/内容/热更与内容发布专项设计.md)（范围与档位）。
> 范围：只做 P0–P3。P4（双端演练、Android 真机矩阵）与 P5（商业验收）不在本轮。

| 阶段 | # | 任务 | 落点 | 退出判据 |
| --- | ---: | --- | --- | --- |
| **P0** | 1 | 热/AOT 边界定稿（hot＝`LiteSim`/`LiteSync`/`LiteGame.*`；AOT＝`LiteNet`/`Bootstrap`/框架/Unity 壳） | 路线图 §5.3.1 | 与 asmdef 引用图一致 |
| **P0** | 2 | 双哈希身份（`BaseHash` + `KernelHash`，Join 要求 `KernelHash` 相等） | 路线图 §5.3.3 | 口径可判定，无"再议"项 |
| **P0** | 3 | 旧房与回滚（房间级锁版本、排空、双槽 + 内置兜底） | 路线图 §5.3.8 | 三条路径（正常/版本不符/回滚）各有判据 |
| **P0** | 4 | 旧裁决改判落点（热更专项、客户端总设计、待办 F） | 见第 2 章 | Doc 套件绿 |
| **P1** | 1 | 内嵌 `Packages/com.code-philosophy.hybridclr` 9.0.3（MIT，LICENSE 随包） | `Packages/` | `toolchain.py check` 无新增 WARN；L0 绿 |
| **P1** | 2 | il2cpp 安装与恢复检查（`Library/HybridCLR/LocalIl2CppData-*/il2cpp/libil2cpp/hybridclr` 存在性） | `scripts/gate/restore-check.py` | 缺失可诊断（BLOCKING/WARN 分档） |
| **P1** | 3 | 构建链前置 `GenerateAll`（含 EmptyMethodBridge 先行的顺序） | `scripts/build/` | 构建脚本调用序与 spike 验证一致 |
| **P1** | 4 | 工具注册（安装/生成入口） | `scripts/gate/toolchain.json` | `check` 通过；索引最新 |
| **P1** | 5 | 环境复原文档补安装步骤 | `Docs/说明/16-环境复原与机器差异.md` | 新机器按文档可装回 |
| **P2** | 1 | hot asmdef 标记清单落成（`HybridCLRSettings.asset`） | `ProjectSettings/` | Editor 读回与清单一致 |
| **P2** | 2 | `IHotEntry` + `Bootstrap` 唯一装载点（`Assembly.Load`，不再 `Type.GetType`） | `Assets/LiteGame/Bootstrap/` | AOT 侧不引用任何 hot 类型 |
| **P2** | 3 | `link.xml` / `AOTGenericReferences.cs` 生成并入库 | `HybridCLRGenerate/` | 生成物入库、构建可重复 |
| **P2** | 4 | H2 构建期守卫：AOT 程序集不得引用 hot | 新增脚本/用例 | 负例能红 |
| **P3** | 1 | 候选布局 `code/` 前缀（与 `lua/`、`config/`、`bundle/` 并列） | `ReleaseLayout.cs` | 发布脚本与清单同批 |
| **P3** | 2 | 程序集候选校验（摘要 + 签名 + `requiredBaseHash`） | `CandidateLuaValidator` → 通用校验 | 篡改/不兼容候选被拒 |
| **P3** | 3 | 健康探针（装载 + 入口类型解析，不执行业务） | `LiteFramework` 探针族 | 坏包不激活 |
| **P3** | 4 | 双槽 `current/previous` + 内置兜底 + 回滚记录 | `ActivationStore`/启动链 | 回滚演练通过 |

**顺序约束**：P1 必须先在空/最小工程验通过再进主仓；P2 的 hot 集合由 asmdef 引用图算出，不手抄；P3 依赖 P2 的装载点，不能并行。

---

## 第2章 P0 设计裁决落地

<a id="ch2-P0设计裁决落地"></a>

> **批次**：P0 设计裁决（2026-11-08）　**状态**：已完成（附证据）

### 2.1 裁决内容

- **边界**：hot＝`LiteSim`、`LiteSync`、`LiteGame.Core/Shared/Battle.Abstractions/Modules.*/Adapters.*/App`；AOT＝`LiteGame.Bootstrap`、`LiteFramework.*`、`LiteNet`（Proto/Protocol/Transport/Security）、Luban、第三方、Unity 序列化载体。**`LiteNet` 改留 AOT**（原 §5.3.1 列 hot）：传输/握手是"坏了就连不上、也下不了修复"的最后一段。
- **身份**：`BaseHash`（AOT 构建期常量）+ `KernelHash`（hot 程序集运行期摘要）；Join/Reconnect 带双哈希，服务端要求 `KernelHash` 相等，`BaseHash` 满足 hot 包 `requiredBaseHash`；`ContentIdentity` 增 `CodeSetId`。
- **旧房**：一局锁 `KernelHash`；服务端发布新 Kernel 先 draining、停新房、旧房跑完、重启后新房生效；同进程不并行两版 Sim。
- **回滚**：客户端双槽 + 内置兜底（与 AOT 同版本），两槽皆坏也须启动到错误面；服务端 A/B 目录切换；`requiredBaseHash` 不匹配直接拒收。

### 2.2 改判落点（旧裁决作废）

| 文档 | 改动 |
| --- | --- |
| [路线图 §5.3](../设计文档/架构/客户端架构重构演进路线图.md) | 由"条件设计/B6 不开工"改为**现行设计**；补 `LiteNet` 留 AOT、双哈希、旧房与回滚 |
| [热更与内容发布专项设计 §3](../设计文档/客户端/内容/热更与内容发布专项设计.md) | 表格行与档位定义改为 HybridCLR hot 程序集；§14 结论段改判 |
| [商业级通用客户端框架总设计](../设计文档/架构/商业级通用客户端框架总设计.md) | C# 热更手段裁决改判 |
| [待办总览 §7.1 F 档](../待办总览.md) | 改为 P0–P3 施工中，旧「不用 HybridCLR」作废 |

### 2.3 证据

- `python scripts/gate/test.py --suite Doc` 通过（见提交记录）。
- 工具可行性 spike（独立工程 `E:\hclr-spike`，不属本仓）：Unity 中国版 2022.3.55f1c1 上 HybridCLR 9.0.3 安装成功、`PrebuildCommand.GenerateAll()` 成功、IL2CPP Player 构建成功、运行期 `Assembly.Load` 执行热更方法返回 `HCLR_OK`；HybridCLR 4.0.15 因 `il2cpp_plus v2022-4.0.8` 与中国版 il2cpp 头不一致（C2660）失败。

---

## 第3章 P2 分区与唯一装载点

<a id="ch3-P2分区与唯一装载点"></a>

> **批次**：P2 分区与装载（2026-11-08）　**状态**：已完成（编译 0 错、H2 守卫通过；Player 实机归 P4）

### 3.1 改动

| 项 | 落点 | 说明 |
| --- | --- | --- |
| hot 清单 | `ProjectSettings/HybridCLRSettings.asset` | 24 个程序集：`LiteSim`/`LiteSync`/`LiteGame.Core/Shared/Battle.Abstractions/Modules.*/Adapters.*/App` + `RoomServer.{Runtime,Application.Runtime}`（Dev 本地服） |
| AOT 装载入口 | `Assets/LiteGame/Bootstrap/HotUpdateLoader.cs` | 从 `StreamingAssets/HybridCLR/hot/*.dll.bytes` 逐件 `Assembly.Load`；目录缺失返回 false |
| 入口解析 | `Assets/LiteGame/Bootstrap/ClientEntryLoader.cs` | 先装载再 `Type.GetType`；非编辑器缺 hot 包即显性失败（编辑器允许回退） |
| 标记与生成 | `Assets/LiteGame/Editor/HybridCLRTool.cs` | 新增 `MarkHotAssemblies`/`GenerateForCommit` 与菜单项（`LiteGame/HybridCLR/*`） |
| H2 守卫 | `scripts/gate/hot-boundary-check.py` | 解析 asmdef 图：玩家 AOT 不得引用 hot；编辑器/测试程序集豁免 |
| 构建接线 | `scripts/build/build-player.py` | 构建前跑 H2 守卫与 `GenerateAll`，并把 hot dll 复制到 `StreamingAssets/HybridCLR/hot` 后刷新 |

### 3.2 证据

- Editor 重编译 `failed=false`、`compilationFailed=false`（含新增 Bootstrap/Loader/工具件）。
- `python scripts/gate/hot-boundary-check.py` → `OK: hot=24 aot=66，无 AOT→hot 引用`。
- `python scripts/toolchain.py check` 通过；Doc 套件通过。

### 3.3 未做与风险

- `link.xml`/`AOTGenericReferences.cs` 未生成：需要本机先 `python scripts/build/hybridclr.py install` 并跑 `GenerateAll`（约 800MB 生成物、依赖 gitee），属构建前置，留到 P4 在构建机执行。
- 本机主仓尚未安装 HybridCLR il2cpp：**出包前必须先 install**，否则 `build-player` 的 GenerateAll 步会失败（`restore-check` 报 WARN）。
- `RoomServer.*` 进 hot 是为满足 H2（本地服仅 Dev 用）；发布包剔除 Dev 面的裁剪随 P4 处理。

---

## 第4章 P3 热更链（code/ 段与回滚）

<a id="ch4-P3热更链"></a>

> **批次**：P3 热更链（2026-11-08）　**状态**：已完成（代码与守卫通过；候选发布/回滚实机演练归 P4）

### 4.1 改动

| 项 | 落点 | 说明 |
| --- | --- | --- |
| 候选布局 | `ReleaseLayout.CodePrefix = "code/"` + `CodeAssemblies`/`CodeAssemblyNameOf` | 热更 dll 与 `config/`/`lua/`/`bundle/` 并列；程序集名 = 文件名去 `.dll` |
| 程序集条目 | `Content/Abstractions/CodeEntry.cs` | 与 `ScriptEntry` 同形 |
| 健康探针 | `CodeAssembliesHealthProbe` | 逐件读字节 + PE 头校验（摘要/签名仍归发布清单与验签）；空段 = 无需检查 |
| 装配接线 | `AppComposition.CodeHealthProbeFactory` → `ContentSegment` → `CompositeHealthCheck` | 与 config/lua/bridge 探针同一条健康确认链 |
| 双槽回滚 | `HotUpdateLoader` | 依次尝试 `HybridCLR/hot` → `hot.previous` → `hot.builtin`；槽位装载失败换下一槽，`ActiveSlot` 留诊断证据 |
| 内置兜底 | `build-player.py` | 构建时把 hot dll 同时写入 `hot` 与 `hot.builtin`（与 AOT 同版本），两槽皆坏仍可启动到错误面 |

### 4.2 证据

- Editor 重编译 `failed=false`（含 ReleaseLayout/探针/装配/装载器）。
- `python scripts/gate/hot-boundary-check.py`：hot=24 / aot=66，无 AOT→hot 引用。
- `python scripts/gate/test.py --suite L1 --profile PullRequest` 通过（见提交记录）。
- Doc 套件通过。

### 4.3 未做

- 候选发布→激活→回滚的实机演练（需要先 `install` + 出包 + 内容通道）：归 P4。
- `requiredBaseHash` 的清单级强制：字段已在设计（路线图 §5.3.3），候选校验的完整实现在 P4 与发布脚本同批。
- Android StreamingAssets 读取（APK 内需 UnityWebRequest）：当前装载器按文件路径实现，P4 真机矩阵处理。
