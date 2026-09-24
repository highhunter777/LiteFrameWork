# Meta 服务宿主骨架：施工进度

> 依据：[《Meta 服务专项设计》](../design/architecture/Meta服务专项设计.md) §4.1（宿主选型）/§4.2（工程结构）/§5.2（错误与版本）/§10（配置、启动与关闭）/§11.2-11.3（Metrics/Health）/§15（施工映射 G1 行）；[服务端总设计](../design/architecture/商业级通用服务端框架总设计.md) §12（Generic Host、非零退出码）、§P0-3（解析前限制长度）、§P0-5（buildHash）；[《框架先行建设与业务接入专项设计》](../design/architecture/框架先行建设与业务接入专项设计.md) §4"持久化"行、§5-4"会话与房间端口"。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

## 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| M0-a 宿主骨架 | `MetaServer/` 工程 + Generic Host 装配 + Options 范围校验 `ValidateOnStart` + `/live` `/ready` `/metrics` + 优雅关闭与 drain + 入站请求体上限 | **已完成**（见下） |
| M0-b 接缝登记 | gitignore 白名单、`Tests.slnx`、L0 纪律扫描目标（R11 纯化边界） | **已完成**（见下） |
| M0-c 持久化接缝 | 存储端口、迁移/事务/幂等约束、故障夹具、一个持久化样例（框架先行 §4"持久化"行） | **未开始** |
| M0-d 票据接缝 | `IJoinTicketValidator` 接口 + 非法票据测试（服务端总设计 §P0-6；§5-4"没有真实登录业务时也不能省略票据验证接口与非法票据测试"） | **未开始** |

**范围界定**：本批只交付宿主骨架，**不含任何业务模块**——Auth/Lobby/Profile 归 G3（《Meta 服务专项设计》§15），不提前建空壳模块（客户端 `ProcedureId` 已按同一原则刻意未加 Login/Lobby/Result 枚举）。框架先行 §5-4 的票据验证接口属框架期必建项，**尚未交付**，见 M0-d。

## 施工记录

### 2026-09-25 · 宿主骨架交付（含三处实测缺陷修正）

**① 工程与宿主（M0-a）**

- `MetaServer/MetaServer.csproj`：`Microsoft.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，**零 `PackageReference`**（实测 `project.assets.json` 的 `libs: 0`）；`net8.0` / `LangVersion 10.0` / `Nullable disable` / `ImplicitUsings disable`，与 `RoomServer` 同档除语法版本。
- `MetaServer/Host/MetaConfig.cs`：宿主装配参数（BindAddress / MaxInboundBytes / ShutdownTimeoutSeconds）+ `Validate()` 返回全部违规项。
- `MetaServer/Host/MetaHost.cs`：`Build()` 单一装配路径（测试与生产共用）、`WireGracefulShutdown()`、健康端点、请求计数中间件。
- `MetaServer/Host/Ops.cs`：进程级计数 + Prometheus 文本最小出口（`/metrics`）。
- `MetaServer/Host/BuildHash.cs`：`"meta-0.0.0-unwired"` **显式占位**——`gen-build-hash.py` 接线随 R2/G3，不伪造权威哈希（§20 完成定义第 1 条）。
- `MetaServer/Host/Program.cs`：`Build()` 与 `StartAsync()` 同置于捕获 `OptionsValidationException` 的 try 内；非法配置打印全部违规项并**返回退出码 2**。

**② 语法档位裁决修正（文档同步）**

`LangVersion` 取 **10.0** 而**非** `RoomServer` 的 9.0。2026-09-25 实测：C# 9 下最小 API 惯用写法全部编译失败（`CS1593` target-typed lambda / `CS1503` 方法组 / `CS0030`），只有逐端点显式委托转换可用。该实测**证伪**了《Meta 服务专项设计》§1.1 原写的"与 RoomServer 同档"，已按总设计 §0 规则 2 更新文档。

**③ 三处静默失效缺陷：实测发现并修正**

本批首版"看起来"符合 §10/§12，实测后确认三处**不抛异常、不报警告**的失效，均违反设计：

| # | 缺陷 | 实测证据 | 违反条款 |
|---|---|---|---|
| 1 | `ValidateOnStart` 形同虚设 | 探针：`MaxInboundBytes=10`（越下限）下宿主**启动成功** | §10/§12"不能带默认错配置继续运行" |
| 2 | 配置源全部静默失效 | 字段形态类经 `ConfigurationBinder` **完全不绑定**；文件/环境变量/命令行覆盖一律读不到 | §10"版本化文件、环境变量" |
| 3 | 入站上限未落地 | `MaxInboundBytes` 只是配置对象里的数，**从未限制请求体** | §P0-3"包体在解析前限制长度" |

根因与修正：

- **实例身份**：`AddSingleton(config)` 与 `AddOptions<MetaConfig>()` 解析出**两个不同实例**，`IValidateOptions` 校验的是没人使用的那个。改为配置**只由 Options 管线持有一份**，消费方一律经 `IOptions<MetaConfig>`。
- **绑定形态**：`ConfigurationBinder` **只绑定属性**，`RoomConfig` 式的 public 字段会让配置源完全不生效。`MetaConfig` 改用**属性**——这是与 `RoomConfig` 的**有意差异**，已在两处注释说明。
- **限制落地**：`MaxInboundBytes` 接入 `KestrelServerOptions.limits.MaxRequestBodySize` 与 `FormOptions.MultipartBodyLengthLimit`；`ShutdownTimeoutSeconds` 接入 `HostOptions.ShutdownTimeout`（原配置项未接线，属装饰）。
- **删除手写预校验**：首版在 `Program` 里手写了一次 `config.Validate()` 兜底——它恰好掩盖了缺陷 1，且只看命令行覆盖、看不到配置文件。已移除，改用设计要求的 `ValidateOnStart` 作为唯一门禁。
- **`HttpRejected` 实装**：原字段有注释无写入（恒为 0），现按响应状态码（≥400）记账。

装配契约（三个陷阱的规避方式）已写入《Meta 服务专项设计》§10，回归钉落在 `Tests/MetaServer.Tests/MetaConfigTests.cs`。

**④ 接缝登记（M0-b）**

- `.gitignore`：补 `!MetaServer/*.csproj` 与 `!Tests/MetaServer.Tests/*.csproj`。**不加则工程文件被 `*.csproj` 全局忽略、交付静默丢失**。
- `Tests/Tests.slnx`：追加 `MetaServer` 与 `MetaServer.Tests`。slnx 内的非测试工程**必须**被某测试工程 `ProjectReference`，否则 `test.ps1` 的 no-restore 构建失败——已由测试工程引用满足。
- `Assets/Tools/DisciplineScanner/Scripts/ScanTargets.cs`：新增 `MetaServer` 扫描目标（`MetaPurityRules` = R11），排除 `MetaServer/Host`（宿主装配层合法使用 Web/IO/Console，§12）。**`Contracts/`、`Modules/`、`Infrastructure/` 落盘即自动受扫**，无需再改配置。
- `Tests/LiteFramework.Core.Tests/DisciplineScannerTests.cs`：补登记意图用例（`纪律_MetaServer_登记为受守根_仅宿主层豁免`）。
- 首次扫描命中 `MetaServer/` 根下文件——根因是文件未按 §4.2 结构落位，已迁入 `Host/`。

## 测试面

- `Tests/MetaServer.Tests/`（命名必须匹配 `*.Tests.csproj`，否则被 `test.ps1` 静默漏跑）。
- **零依赖 HTTP 集成测试**（§4.1 依据 5）：`WebHost` 绑定 `http://127.0.0.1:0` 取临时端口 → `IServerAddressesFeature` 取回真实地址 → `HttpClient` 打真实回环。**不用 `WebApplicationFactory`/`TestServer`**（两者都要 `PackageReference`，与 §4.1 零 NuGet 约束冲突）。
- L1（18 例）：配置范围校验边界、`覆盖项_经IOptions生效`、`入站上限_落到Kestrel限制`、`关闭时限_落到HostOptions`、`非法配置_拒绝启动`。
- L3（6 例）：存活/就绪分离、drain 后 live 仍 200 而 ready 转 503、指标出口无高基数字段、请求/拒绝计数、未知路由 404、多实例端口隔离。

## 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **630 通过 / 0 失败**（含 MetaServer 18；纪律扫描"全部目标零违规"绿） |
| L3 | `powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest` | **15 通过 / 0 失败**（含 MetaServer 6） |
| 非法配置 | `MetaServer.exe --bind "not-a-uri"` | 打印违规项，**退出码 2** |
| 正常路径 | `MetaServer.exe --bind http://127.0.0.1:18322` + curl | `/live`→200、`/ready`→200、`/metrics`→计数（4 请求 / 1 拒绝）、未知路由→404 |

**本次未跑 L2/Unity**：纯 .NET 新增，未触碰 `Assets/` 下 Unity 侧代码——除 `ScanTargets.cs` 与 `DisciplineScannerTests.cs`（纯 C# 工具与测试，不参与 Unity 编译）。**未跑 Player 构建**，本批不涉及。

## 已知边界

- **M0-c / M0-d 未交付**：持久化样例与票据验证接口仍是框架先行 §4/§5-4 的缺口，归后续批；在此之前不得宣称 Meta 接缝完备。
- `BuildHash` 为占位值，未接 `gen-build-hash.py`——按 §P0-5，该字段在接线前**不可**用作版本身份或签名。
- 生产编排面（TLS、限流分层、Secret Provider、Docker）未接，归 R4/§12。
- 本批无业务端点，`/metrics` 为最小文本出口；正式 Prometheus/OTel 出口归 R4（§11.2）。
