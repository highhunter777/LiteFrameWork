# LiteFramework

轻量自研游戏框架：确定性 Sim ＋ 服务器权威状态同步 ＋ 客户端框架（生命周期/作用域/内容事务/UI/热更）＋ 联机射击 Demo 与云侧服务端。

> 文档分四层：**规范**（必须遵守）、**说明**（是什么/怎么跑）、**设计文档**（应该怎样）、**施工进度**（实际怎样）。入口：[Docs/README.md](Docs/README.md)。

## 文档入口

| 目的 | 去 |
|---|---|
| 动代码前：红线与流程 | [AGENTS.md](AGENTS.md) ＋ [规范 02 代码与修改规范](Docs/规范/02-代码与修改规范.md) |
| 想改 X 去哪改 | [规范 03 领域查询表](Docs/规范/03-领域查询表.md) |
| 项目是什么、怎么跑 | [说明 01 项目定位](Docs/说明/01-项目定位与技术栈.md) · [说明 02 架构](Docs/说明/02-架构总览与程序集分层.md) · [说明索引](Docs/说明/README.md) |
| 测试、构建、联测、云侧 | [说明 10](Docs/说明/10-测试与门禁.md) · [说明 11](Docs/说明/11-构建与本地联测.md) · [说明 12](Docs/说明/12-云侧部署与联机测试.md) |
| 代码地图与根目录 | [说明 14 代码地图与根目录](Docs/说明/14-代码地图与根目录.md) |
| 目标架构 | [设计文档中心](Docs/设计文档/README.md) |
| 当前进度与剩余面 | [待办总览](Docs/待办总览.md) · [施工进度](Docs/施工进度/README.md) |

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Assets/LiteFramework/Runtime` | `LiteFramework.Runtime`：Host、Modules（UI/内容/动画/相机/输入/音频/实体/对局/本地化…）、Core（DI/Scope/Event/Command/Fsm/Pool/Clock/IO/Logging） |
| `Assets/LiteFramework/Editor` | 编辑器侧（工具页件、诊断） |
| `Assets/LiteSim` | 确定性判定：单 `LiteSim.asmdef`（Scripts/Systems）＋ `LiteSim.Editor`；客户端/服务端/测试三处共编 |
| `Assets/LiteNet/Core` | 协议（proto 生成物）、编解码纯函数、KCP 传输、安全信封、会话 |
| `Assets/LiteView/Modules` | 表现层：SimView、角色/动画、VFX、相机驱动 |
| `Assets/LiteView/Core` · `Adapters` | 表现纯件（伤害数字/命中反馈/变换）与引擎适配 |
| `Assets/LiteBattle/Abstractions` | 对局接口（`IBattleView`/`IIntentSource` 等端口） |
| `Assets/RoomServer/{Core,Modules}` | 权威房间内核（纯化）与应用层（编排/宿主协作） |
| `Assets/LiteGame/{App,Dev,Editor,Lua,LuaBridge}` | 业务：引导/流程/UI 逻辑/开发调试面/Lua 注册表与桥 |
| `Assets/ShareResource` | 跨模块共享收集目录（UI 模板、字体、角色、FX） |
| `Assets/Tests` | Unity EditMode/PlayMode 用例 |
| `Server/{RoomServer,MetaServer,ReplayTool}` | .NET 服务端宿主（房间服/元服务/回放工具） |
| `Tests` | dotnet 测试工程（xUnit，`Tests.slnx`） |
| `scripts` | 门禁/生成/构建/部署（全 Python） |
| `Docs` | 文档中心：`说明/`、`规范/`、`设计文档/`、`施工进度/`、`审计/`、`归档记录/` |

## 运行测试

```powershell
# 纯逻辑（秒级，无需 Unity）
python scripts/gate/test.py --suite L1 --profile PullRequest

# 网络与无头集成
python scripts/gate/test.py --suite L3 --profile PullRequest

# Unity 编译 + EditMode 门禁（需运行中的编辑器/CI 机器）
python scripts/gate/l2-unity-gate.py
```

客户端商业化与发布门槛见 [商业级通用客户端框架总设计](Docs/设计文档/架构/商业级通用客户端框架总设计.md)；测试分层与完成定义见 [测试开发框架总设计](Docs/设计文档/测试与性能/测试开发框架总设计.md)。
