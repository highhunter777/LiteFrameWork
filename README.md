# LiteFramework

轻量自研游戏框架：骨架九件（事件 / 引用池 / 对象池 / 游戏时钟 / FSM / Setting / 日志 / FileSys / 装配）+ DI + Lua 逻辑注册表。

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Assets/LiteFramework` | 框架本体（UPM 本地包 `com.litegame.framework`） |
| `Assets/LiteFramework/Scripts/Core` | `LiteFramework.Core`：纯 C#，无引擎依赖 |
| `Assets/LiteFramework/Scripts/Unity` | `LiteFramework.Unity`：Unity 封装层，依赖 UniTask |
| `Assets/Plugins/UniTask` | 框架唯一第三方依赖（随库内置） |
| `Docs` | 配套文档：设计方案、实施手册、帧同步 / 状态同步方案等 |
| `Assets/LiteTesting` | Unity/.NET 双轨测试基础设施 |
| `Tests` | dotnet 测试（xUnit，net8.0） |

## 运行单元测试

```powershell
# 快速纯逻辑回路
powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest

# 网络与无头集成回路
powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest

# Unity 编译、资源和 EditMode 门禁
powershell -NoProfile -File scripts/test.ps1 -Lane L2 -Profile PullRequest
```

客户端商业化、运行闭环和发布门槛见 [商业级通用客户端框架总设计](Docs/design/architecture/商业级通用客户端框架总设计.md)；共享测试分层、分类约定、确定性与 CI 基础见 [测试开发框架总设计](Docs/design/quality/测试开发框架总设计.md)。
