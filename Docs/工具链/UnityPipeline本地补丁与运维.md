# Unity Pipeline 包本地补丁（com.unity.pipeline 跑在 Unity 2022.3）

> 更新：2026-10-12（换机重放：按 §0.1 重抓 0.7.0-exp.1、§2 八处补丁全部重新核对并应用；**发现 §4 记的 CLI 子命令在当前公开发布版已不存在**，改走 §6 的 HTTP 路径）。
> 更新：2026-09-15。目的：把 **Unity 官方 CLI（`unity`）** 的"连接运行中编辑器"能力接入本工程。
> 现状：**已跑通** —— 包编译成功、Pipeline HTTP 服务在 **端口 7800** 运行、CLI 可执行 **151 条命令**。
> ⚠️ 前提认知：上游包 **只支持 Unity 6000.0**（registry 里 7 个版本全声明 `"unity": "6000.0"`）；本工程是 **2022.3.55f1**，因此下列补丁是**为 2022.3 定制的兼容层**。升级到 Unity 6 后应还原上游原包（补丁均带 `#if` 守门或可逆）。

## 0. 来源与落位

| 项 | 值 |
| --- | --- |
| 来源 | `https://packages.unity.com/com.unity.pipeline`（**直连可达，无需代理**） |
| 版本 | **0.7.0-exp.1**（抓取时 `dist-tags.latest`；全版本均 exp） |
| 包体 | `https://download.packages.unity.com/com.unity.pipeline/-/com.unity.pipeline-0.7.0-exp.1.tgz` |
| 落位 | `Packages/com.unity.pipeline/`（embedded，821 文件；**已加入 `.gitignore`**，见 §5） |

### 0.1 抓取步骤（换机 / 重抓的标准动作）

包不入库（§5），换机或包体损坏时按此重建。**先结束编辑器**——批处理导入与工程锁冲突。

```bash
# 1) 查版本与包体地址（latest 会随上游前进，务必按 §2 对应的版本号取）
curl -s https://packages.unity.com/com.unity.pipeline \
  | python -c "import json,sys; d=json.load(sys.stdin); print(d['dist-tags']); print(list(d['versions']))"

# 2) 直接抓 tarball（npm 上没有同名包；registry 直连即可）
mkdir -p Temp/pipeline_dl
curl -sL -o Temp/pipeline_dl/com.unity.pipeline-0.7.0-exp.1.tgz \
  https://download.packages.unity.com/com.unity.pipeline/-/com.unity.pipeline-0.7.0-exp.1.tgz

# 3) 解包为 embedded 包（--strip-components=1 去掉 tgz 的 package/ 前缀）
mkdir -p Packages/com.unity.pipeline
tar -xzf Temp/pipeline_dl/com.unity.pipeline-0.7.0-exp.1.tgz \
  -C Packages/com.unity.pipeline --strip-components=1

# 4) 按 §2 重放 8 处补丁（这一步不能省，见 §2 原因列）
# 5) 重开编辑器；UPM 自动解析 embedded 包并编译
```

校验：`tar -xzf` 后 `Packages/com.unity.pipeline/package.json` 的 `version` 应为 `0.7.0-exp.1`，`file` 计数约 **821**。

> ⚠️ **上游 latest 会前进**：2026-10-12 实测 `dist-tags.latest` 已是 **`0.8.0-exp.1`**（另含 `console_status`、动态命令注册等新增）。§2 的补丁是**针对 0.7.0-exp.1 逐处核对过的**，换版本必须重新逐处核对锚点是否还在，不能直接套用。

### 0.2 `unity` CLI 的现状（2026-10-12 实测，与 §4 的旧记录不符）

Unity Hub 的官方 CLI 只能从 CDN 装：

```powershell
$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
```

装到 `%LOCALAPPDATA%\Unity\bin\unity.exe`（脚本自带 SHA-256 校验）。但**公开发布的 `0.1.0-beta.3` 只有编辑器/工程管理类子命令**（`editors` / `install` / `open` / `projects` / `auth` …），**没有 §4 里记的 `unity command` / `unity list` / `unity status` / `unity pipeline`**——`unity command` 会直接报 `unknown command 'command'`。

因此**当前驱动编辑器的可行路径是直接走 HTTP API**（见 §6），不依赖 CLI。包侧的服务、命令目录、鉴权全都是 CLI 之上的独立能力，CLI 缺席不影响。

## 1. 必需依赖（UPM 自动解析）

`com.unity.nuget.mono-cecil 1.11.6`（registry）、`com.unity.nuget.newtonsoft-json`（工程已有 3.2.1）、`com.unity.test-framework 1.1.33`（已有）、`com.unity.inputsystem`（已有 1.11.2）。

## 2. 补丁清单（8 处；重抓后按此重放）

> 2026-10-12 复核：原标题写「7 处」是笔误——表体一直是 **8 行**。本次换机重放时 **8 处锚点全部命中未改动的上游 0.7.0-exp.1**，均可原样应用；逐处结论见下行「原因」列的补注。

| # | 文件 | 改动 | 原因 |
| --- | --- | --- | --- |
| 1 | `Runtime/Plugins/CodeAnalysis/*.dll.meta`（5 个） | **转为 2022.3 原生 v2 格式**（`serializedVersion: 2`）+ 平台对齐（**Editor ✓ / Standalone Win·Win64·Linux64·OSX ✓**） | 上游 meta 用 `serializedVersion: 3`（Unity 6 格式）+ map 式 `platformData`，**2022.3 误读为全平台禁用**（`GetCompatibleWithEditor()=False`）→ 插件 DLL 未生效 → `IlInterpreter` 找不到 `UnityPipeline.*` 命名空间。2022.3 的原生形状是**列表式** `platformData`（`- first: {Editor: Editor}` / `second: {enabled: 1}`），与 v3 的 map 式不兼容——**照抄 v3 改 `serializedVersion` 序号不管用，必须整段重写**（模板见 `Library/PackageCache/cn.tuanjie.codely.bridge/Plugins/*.dll.meta`，即本编辑器自己写出的 v2 形状）。`defineConstraints` 需原样保留（上游用它把 DLL 限定在 `UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| ENABLE_RUNTIME_PIPELINE`） |
| 2 | `Tests/` → **`Tests~`** | 重命名目录 | 包自带测试程序集引用 nunit.framework，2022.3 下解析失败；`~` 后缀被 Unity 忽略（源码保留） |
| 3 | **新增** `Editor/AnalyticInfoCompat_2022.cs` | `#if !UNITY_6000_0_OR_NEWER` 下提供 `UnityEngine.Analytics.IAnalytic` 与 `AnalyticInfoAttribute` | 这两个类型是 Unity 6 新增，2022.3 编辑器内**不存在**（实测全程序集扫描为 0）。该文件需**自带 `.meta`** 或让 Unity 首次导入时生成 |
| 4 | `Editor/PipelineAnalytics.cs` | `s_Send` 在 2022.3 下置空（`analytic => { }`） | 2022.3 的 `EditorAnalytics` 只有 `SendEventWithLimit` 系列，**没有 `SendAnalytic(IAnalytic)`** |
| 5 | `Editor/Console/EditorConsoleGroundTruth.cs` | `ConsoleWindowUtility.consoleLogsChanged` 订阅与 `GetConsoleLogCounts(...)` 在 2022.3 下 `#if` 排除（计数置零） | `ConsoleWindowUtility` 为 Unity 6 API |
| 6 | `Editor/Commands/Materials/MaterialCommands.cs` | `mat.rawRenderQueue` → 2022.3 用 `mat.renderQueue` | Unity 6 新增 `rawRenderQueue`（-1=继承）；2022.3 无 → **读回值退化为有效值**（写入侧 `renderQueue:-1` 仍可用） |
| 7 | `Editor/Commands/Assets/AssetCommands.cs` | 文件头加 `using PhysicsMaterial = UnityEngine.PhysicMaterial;`（`#if !UNITY_6000_0_OR_NEWER`） | Unity 6 更名 `PhysicMaterial` → `PhysicsMaterial` |
| 8 | `Runtime/Analyzers/IlInterpreterAnalyzer.dll` | **移入 `Runtime/Analyzers/Disabled2022~/`**（`~` 目录 Unity 不导入）并删除其 `.meta` | 该分析器 DLL 引用 `Microsoft.CodeAnalysis`/`System.Collections.Immutable`，2022.3 的 Mono **解析不了这些引用** → Unity 每次导入都抛 "Assembly ... will not be loaded due to errors"。**仅移除 RoslynAnalyzer 标签不够**——Unity 仍会把它当普通插件程序集做加载校验。藏出导入管线后错误永久消失（Unity 6 恢复包原样时移回即可） |
| 9 | `Editor/Commands/Build/BuildCommand.cs` | 类声明 `static class BuildCommand` → **`public static class BuildCommand`**（方法与成员不动） | 上游 0.8.x 起该类收窄为 internal，而 `build-player.ps1` 的 dry_run/正式构建 eval 直接引用 `Unity.Pipeline.Editor.Commands.Build.BuildCommand.Build(...)`——类不可见即 eval 编译失败（CS0122），出包链在 dry_run 一步被拦。本工程成员本就是 `public static`，补 `public` 修饰即可复用官方 CLI 的 build 出口（2026-10-18 出包批实测）。**重抓包后须重放本条**，否则 `build-player.ps1` 必卡 dry_run |

**副作用（可接受）**：分析遥测（#3/#4）与 console 计数对账（#5）在 2022.3 下不生效；材料 renderQueue 读回语义降级（#6）。**Pipeline 服务/命令能力不受影响。**

## 3. 验证记录

### 3.1 换机重放复验（2026-10-12）

按 §0.1 重抓 + §2 八处补丁重放后，批处理导入结果：

| 检查 | 结果 |
| --- | --- |
| 包完整性 | `package.json` version = `0.7.0-exp.1`；`Packages/com.unity.pipeline/` **821 文件**（上游原始 tarball，未改） |
| 补丁锚点 | 8 处全部命中未改动的上游源码，逐处可原样应用 |
| 程序集产物 | `Unity.Pipeline.dll`(336KB) / `Unity.Pipeline.Editor.dll`(**649KB**) / `IlInterpreter.dll`(338KB) / `Attributes.dll`(5.6KB) / `CodeGen.dll`(18KB) —— **5 个全部产出**，与 §3.2 首录体积同量级 |
| 编译 | `error CS` **0**（`Unity.exe -batchmode -nographics -quit`） |

> **踩坑记录（补丁 3）**：首版 shim 只声明了 `Initialize()`/`Send()`，编译炸出 45 个错。正确契约要看**包怎么用**而不是照 Unity 6 文档猜——三个 `IAnalytic` 实现类各自只声明了一个成员 `TryGatherData(out IData, out Exception)`，且载荷写成 `struct XxxData : IAnalytic.IData`。所以接口**有且仅有** `TryGatherData` + 嵌套 `IData`；多声明一个成员就会让三个类全部变成"未实现"。`AnalyticInfoAttribute` 同理要四个命名参数（`eventName`/`vendorKey`/`maxEventsPerHour`/`maxNumberOfElements`），少一个就 `CS1739` 命名实参绑定不上。

### 3.2 首录（2026-09-15）

| 检查 | 结果 |
| --- | --- |
| 程序集产物 | `Unity.Pipeline.dll`(354KB) / `Unity.Pipeline.Editor.dll`(**672KB**) / `IlInterpreter.dll`(361KB) / `Attributes.dll` / `CodeGen.dll` —— **全部产出** |
| 程序集加载 | 5 个 `Unity.Pipeline*` 全部载入 AppDomain，`PipelineServerStartup` 类型可达 |
| 服务 | `Library/Pipeline/.unity-pipeline-port` → **port 7800 / pid 215852 / mode=editor**；`unity status` → `7800 ready`；`unity pipeline list` → **Server Reachable=true** |
| CLI 往返 | `unity command console_status` → `success:true`（返回 `compilationFailed:false`、`counts{error:1,warn:8}`）；`unity command recompile_status` → `{"status":"idle","failed":false}` |
| 命令目录 | `unity list` → **151 条命令**（GameObject/组件/prefab/Animator/Timeline/build/tests/截图/console/audit/navmesh/batch…） |
| 控制台 | 编译 error **0** |

## 4. 使用方式与注意事项

> ⚠️ 本节命令基于 `unity command` / `unity list` / `unity status` —— **2026-10-12 实测当前公开 CLI 已无这些子命令**（§0.2）。下面内容保留作历史记录与期望接口；**当前请改用 §6 的 HTTP 调用**，两者打的是同一个服务。

```bash
# 前置：Unity CLI 已在 PATH（C:\Users\hunter\AppData\Local\Unity\bin\unity）
unity status                                  # 看已连接编辑器与端口
unity list    --project-path "E:\unityProject\Test"     # 列可用命令（151 条）
unity command console_status --project-path "E:\unityProject\Test"
unity command recompile_status --project-path "E:\unityProject\Test"
```

- ⚠️ **本机需带 `--project-path`**：CLI 当前把工程登记了两次（`E:\...` 与 `e:\...`，后者无 PID），不带参数会报 "Multiple Unity Editor instances"（Hub 注册表为空，疑为端口描述符大小写变体所致；功能不受影响）
- ⚠️ 服务由包的 `[InitializeOnLoad]`（`PipelineServerStartup`）**自动启动**；本次是首次导入后手动调 `EnsureServerStarted()` 拉起，**重启 Unity 后应自动就绪**
- ⚠️ 描述符自带提示：编辑器**非自动化模式**启动时可能卡在模态对话框；无人值守场景建议用 `unity open` 启动编辑器
- ⚠️ 已知无害告警：`Runtime/Analyzers/IlInterpreterAnalyzer.dll` 加载失败（Roslyn 分析器，2022.3 下版本不匹配）——仅诊断功能缺失，可忽略；如要消除可移除其 meta 的 `RoslynAnalyzer` 标签

## 5. git 策略

`Packages/com.unity.pipeline/` **不入库**（第三方包 + 本地补丁，与 `Packages/MCPForUnity/` 同策略）；**本文档入库**——重抓包后按 §2 重放补丁即可复原。

## 6. 不依赖 CLI 的驱动方式（HTTP，当前唯一可用路径）

包的 HTTP 服务是 CLI 之上的独立能力，CLI 缺席不影响。契约见包内 `Packages/com.unity.pipeline/Documentation~/connectivity.md`。

### 6.1 三步接通

```bash
# 1) 读端口描述符（编辑器唯一发现通道；gitignore 覆盖）
cat Library/Pipeline/.unity-pipeline-port
# => {"pid":12345,"port":7800,"evalToken":"<base64>","mode":"editor",...}

# 2) 列出命令目录
curl -s -H "Authorization: Bearer <evalToken>" http://127.0.0.1:<port>/api/commands

# 3) 执行命令（POST /api/exec）
curl -s -X POST http://127.0.0.1:<port>/api/exec \
  -H "Authorization: Bearer <evalToken>" -H "Content-Type: application/json" \
  -d '{"command":"editor_status","parameters":{}}'
```

### 6.2 请求体形状（踩过）

**参数键名是 `parameters`，不是 `args`。** 用 `args` 时服务端不报错地返回 400
`Required parameter 'path' is missing or empty`——因为它只认 `parameters`。

```jsonc
{"command": "create_scene", "parameters": {"path": "Scenes/Boot", "template": "empty"}}
```

先读 `/api/commands` 的命令目录，每个命令都带完整 JSON Schema（`parameters[]` 的
`name`/`type`/`required`/`defaultValue` 齐全）——**先看 schema 再发请求**，别猜参数名。

### 6.3 ObjectRef 必须用 globalId，别用层级路径

`add_component` / `set_tag` / `set_transform` 的 `target` 收 ObjectRef。
**传 `/MainCamera` 这种层级路径会被当文件系统路径解析**（实测报
`No GameObject at hierarchy path 'E:/Git/MainCamera'`——拿当前工作目录当基准）。
用上一步返回的 `globalId` 最稳：

```
GlobalObjectId_V1-2-<sceneGuid>-<fileId>-0
```

### 6.4 改 ProjectSettings 后必须显式 `save_all`

`add_scene_to_build` / `set_tags_layers` 这类改工程设置的命令**只改内存**，
返回 `success:true` 但 `ProjectSettings/*.asset` 的 mtime 不动。必须补一条
`save_all` 才落盘：

```bash
# 改完构建场景列表后
curl -s -X POST .../api/exec -d '{"command":"save_all","parameters":{}}'
```

否则会看到"命令成功 + 文件没变"的假象。踩过：`add_scene_to_build` 报成功，
`EditorBuildSettings.asset` 仍是旧 guid，`save_all` 后才刷新。

### 6.5 连接约束

| 约束 | 说明 |
| --- | --- |
| **必须用 `127.0.0.1`，不能用 `localhost`** | Mono 的 `HttpListener` 对 IPv6 回环请求会回 `400`（`::1` 主机名解析成带方括号形式被误解析）；`localhost` 在 Windows 上按 Node 默认 DNS 顺序会随机落到 `::1`，表现为**间歇性**连不上 |
| **必须带 `Authorization: Bearer <evalToken>`** | 每个请求都校验，常量时间比较；缺失/错误回 `401` |
| **不能带 `Origin` 头** | 服务端直接拒收带 `Origin` 的请求（挡浏览器页）。curl/脚本默认不带，天然满足 |
| 端点 | `/api/status`、`/api/editor_status`、`/api/dialog`、`/api/commands`、`/api/exec`(POST)、`/api/test-status`、`/api/progress` |
| 端口范围 | 编辑器 `7800-7849`（测试实例 `7850-7899`）；取范围内首个空闲端口 |
| Token 生命周期 | 存 `SessionState`，**跨域重载存活**、编辑器重启才换 |

### 6.3 服务何时起来

由包的 `[InitializeOnLoad]`（`PipelineServerStartup`）自动启动。**新增 embedded 包后必须重启编辑器**才会走 UPM 解析 + 编译 + 起服务——这正是 §0.1 第 5 步写「重开编辑器」的原因。描述符文件在关闭时删除。
