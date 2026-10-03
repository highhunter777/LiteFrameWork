# LiteFramework

轻量自研游戏框架：骨架九件（事件 / 引用池 / 对象池 / 游戏时钟 / FSM / Setting / 日志 / FileSys / 装配）+ DI + Lua 逻辑注册表。

> 本仓库不止框架本体，还包含**一个确定性联机射击 Demo**（框架的首个真实业务）与其服务端。
> 完整的项目说明按主题分成多文件，入口见下。

## 项目说明文档

**先读** → [Docs/项目说明/README.md](Docs/项目说明/README.md)（是什么 / 怎么跑 / 数据怎么流 / 怎么验）

| 文件 | 回答的问题 |
| --- | --- |
| [01-项目总览](Docs/项目说明/01-项目总览.md) | 组成、技术栈与版本、仓库地图、程序集分层、已交付/未交付边界 |
| [02-总体流程](Docs/项目说明/02-总体流程.md) | 启动与运行流程、对局数据流、开发与交付流程 |
| [03-技术细节-客户端](Docs/项目说明/03-技术细节-客户端.md) | 装配与作用域、时钟与暂停、UI 与 Lua、内容与热更、表现层、输入三件 |
| [04-技术细节-Sim与联机](Docs/项目说明/04-技术细节-Sim与联机.md) | 确定性 Sim、预测/回滚/和解、协议与 buildHash、权威房间、Meta 接缝 |
| [05-验证与门禁](Docs/项目说明/05-验证与门禁.md) | L0～L4 分层与命令、L2 门禁、构建与冒烟、纪律扫描、CI |
| [06-环境与协作纪律](Docs/项目说明/06-环境与协作纪律.md) | 环境恢复、Unity 操作纪律、文档权威层级、三条红线、常见坑 |

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Assets/LiteFramework` | 框架本体（UPM 本地包 `com.litegame.framework`） |
| `Assets/LiteFramework/Scripts/Core` | `LiteFramework.Core`：纯 C#，无引擎依赖 |
| `Assets/LiteFramework/Scripts/Unity` | `LiteFramework.Unity`：Unity 封装层，依赖 UniTask |
| `Assets/LiteSim` | `Core`＝确定性判定逻辑（两端共编）；`View`＝表现层 |
| `Assets/LiteNet` | 协议（proto 单源）、打包解包纯函数、KCP 传输 |
| `Assets/RoomServer` | 权威房间内核（`Runtime` 纯化 / `Application` 编排） |
| `Assets/LiteGame` | 业务与装配（`App` 引导与流程、`Runtime` 会话/输入、`UI`、`Adapters`） |
| `Assets/Plugins/UniTask` | 框架唯一第三方依赖（随库内置） |
| `Docs` | 文档中心：`design/`（目标架构）、`施工进度/`（证据）、`开发导航/`（代码地图）、`项目说明/`（本文档集） |
| `Assets/LiteTesting` | Unity/.NET 双轨测试基础设施 |
| `Tests` | dotnet 测试（xUnit，net8.0） |
| `RoomServer/`、`MetaServer/`、`scripts/` | .NET 服务端宿主、构建与门禁脚本 |

## 运行单元测试

```powershell
# 快速纯逻辑回路
powershell -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest

# 网络与无头集成回路
powershell -NoProfile -File scripts/gate/test.ps1 -Lane L3 -Profile PullRequest

# Unity 编译、资源和 EditMode 门禁
powershell -NoProfile -File scripts/gate/test.ps1 -Lane L2 -Profile PullRequest
```

客户端商业化、运行闭环和发布门槛见 [商业级通用客户端框架总设计](Docs/design/architecture/商业级通用客户端框架总设计.md)；共享测试分层、分类约定、确定性与 CI 基础见 [测试开发框架总设计](Docs/design/quality/测试开发框架总设计.md)。
