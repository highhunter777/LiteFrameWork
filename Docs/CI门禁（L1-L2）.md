# CI 门禁：L1、L2 与 L3

> 建立：2026-09-15。依据：《测试开发框架总设计》的分层门禁要求。
> 触发原因：**2026-09-15 事故**——上游提交的 16 个 `.meta` 把 `guid` 写成 64 位 base64，Unity 拒绝导入这些资源 → `Unity.Pipeline`/`GameScheduler` 等类型在编译中消失 → 依赖方 9 条 `CS0246`；而 **L1 全程绿色**（dotnet 按路径 glob 编译、不读 `.meta`、不经 Unity 编译器）。

## 1. 分层门禁的分工

| 级别 | 在哪跑 | 抓什么 | 时长 |
| --- | --- | --- | --- |
| **L1** | GitHub-hosted **矩阵：`windows-latest` + `ubuntu-latest`**（`.github/workflows/ci.yml` → `l1-dotnet-test`） | 纯 C# Unit/Contract + **基线防篡改校验** | 分钟级 |
| **L2** | **自托管 runner**（Windows + 完整工程）→ `l2-unity-gate` | ① **非法 meta/GUID 扫描**（纯文件）② **Unity 侧编译/诊断状态**（经 Unity Pipeline）③ **EditMode 用例**（`Assets/Tests/EditMode`：IEEE 基线逐位对账 + UI 模板/资源完整性） | ①秒级 ②③分钟级 |
| **L3** | GitHub-hosted Windows + Linux（`l3-network-test`） | RoomServer、真实 UDP/KCP、无头客户端；夜间包含长跑 | 分钟级/夜间长跑 |

**L2 的独有价值**：它是唯一能发现"**资源导入 / Unity 编译 / 跨运行时数值**"类故障的一层——L1 结构性看不见。

**2026-09-18 升级（《待办总览》§5-27/28/30/31/32）**：

| 项 | 落地形态 |
| --- | --- |
| **跨平台 matrix**（§5-31） | L1 双平台并行（`fail-fast: false`）；已核对无平台专属依赖（临时目录 / 路径 Ordinal 归一化 / BuildHash 行尾归一化 / 无 Win32 API） |
| **基线防篡改**（§5-30，轻量版） | CI 步：`Tests/**/Baselines/**` 变更必须在 PR 标题/正文或提交信息带 **`[baseline]`**，否则失败——基线是确定性的裁判，静默改动会掩盖回归 |
| **超时保护**（§5-32） | job `timeout-minutes` + `dotnet test --blame-hang-timeout 5m`（挂死输出挂起栈）；`setup-dotnet cache` 顺带缓存 NuGet |
| **L2 EditMode 用例**（§5-27） | `Assets/Tests/EditMode/`（asmdef `LiteGame.EditModeTests`，`UNITY_INCLUDE_TESTS` 门控）；编辑器在跑时经 `unity command run_tests --mode editor` 直跑，**无需关闭编辑器**；`Total=0` 判失败（防"测试程序集没编进来"静默通过） |
| **Unity 侧 IEEE 对账**（§5-28） | `LiteSim.Core.Editor/IeeeBaselineChecker`（菜单「LiteSim/对账 IEEE 基线」+ `RunCli`）；**探针下沉到 `LiteSim.Core/IeeeProbe`**——两侧同一份样本与运算，避免各写一份漂移 |

> ⚠️ **2026-09-18 实测发现 → 当晚定案 B 并落地**：Unity(2022.3/Mono) 与 .NET 8 在 **10k 步运算链**上 checksum 不一致
> （`Chain`：.NET `2896875742` / Unity `3683559206`），116 条逐值/相邻对行全同。
> **真因**（诊断修正）：**Mono JIT 把 `a*b + c*d` 自动融合成 FMA**（单次舍入），.NET 8 严格逐步舍入 → 差 1 ulp；
> 拆局部变量**不能**阻止融合（实测）。BCL `Math.Sqrt` **不是**原因（逐值行全同证明了这一点）。
> **修法两刀**：① `SimMath.Sqrt` 改自研 software sqrt（纯整数位运算 + 正确舍入，消除 BCL 数学依赖）；
> ② **融合安全算术件**（`MulAdd2/3`、`MulSub2`、`MulAdd/Sub` 等，双精度累积+单次舍入）——float×float 在 double 精确 → 融合无害。
> **结果**：Chain 两侧逐位一致（=3683559206 ✓）；基线重录（`[baseline]`）；**Chain 升为硬判据**（EditMode 用例直跑断言）。

## 2. 脚本用法（`scripts/l2-unity-gate.ps1`）

```powershell
# Windows PowerShell 5.1 亦可（本机未装 pwsh）
powershell -NoProfile -File scripts/l2-unity-gate.ps1                     # 全量（自动选模式）
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly       # 只跑①（无 Unity 环境也能用）
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -RunEditModeTests   # 强制 batchmode EditMode 测试
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -UnityExe 'D:\Unity\2022.3.55f1c1\Editor\Unity.exe'
```

**自动选择的两种模式**：

| 模式 | 触发条件 | 做法 | 特点 |
| --- | --- | --- | --- |
| **A. Pipeline**（推荐） | `Library/Pipeline/.unity-pipeline-port` 存在且服务可达 | `set_autotick` + `recompile_status` + `console` + **`run_tests --mode editor`** | **无需关闭编辑器**；复用已连接的编辑器会话；EditMode 用例直跑 |
| **B. batchmode** | 编辑器未跑，或显式 `-RunEditModeTests` | `unity test --mode EditMode --output TestResults/editmode-results.xml` | 需要授权激活，且**不得有其它实例占用该工程**（脚本会检测并明确报错） |

退出码：**0 = 通过，1 = 有 FAIL**（可直接用作 pre-push/CI 门禁）。

## 3. 为什么 L2 不进 GitHub-hosted CI

| # | 原因 |
| --- | --- |
| 1 | **全新克隆缺件**：按仓库约定只提交自研代码/预制品/文档；`ProjectSettings/`、美术素材、场景、Odin 授权包均不入库（见《克隆后自备清单》）→ GitHub runner 上工程**无法编译** |
| 2 | **编辑器版本不匹配**：本机是 **Unity 中国版 2022.3.55f1c1**，GitHub 上的 Unity 镜像与 EULA/授权都不对应 |
| 3 | **授权**：Unity 需激活（Personal/Pro），在共享 runner 上做激活需要把凭据放进 Secrets，成本与风险都高于收益（demo 阶段） |

→ 结论：**L2 走自托管 runner（本机/内网机器）**，用仓库变量 `L2_ENABLED=true` 开启；未开启时该作业直接跳过（不产生红叉）。

**开启步骤**：仓库 Settings → Secrets and variables → Actions → Variables → 新建 `L2_ENABLED` = `true`；并在该机器上注册 self-hosted runner（标签 `self-hosted`、`windows`）。

## 4. 建议的本地用法（比 CI 更实用）

- 大改（改 `.meta`、动包、动资源）后：`powershell -NoProfile -File scripts/l2-unity-gate.ps1` —— 编辑器开着时走 Pipeline，**几秒出结论**
- 纯逻辑代码：`powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest`
- 网络/RoomServer：`powershell -NoProfile -File scripts/test.ps1 -Lane L3 -Profile PullRequest`
- 可选：装成 pre-push 钩子（`powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly`，零成本拦截"坏 meta"）

## 5. 现状与后续

- ✅ L1：已在 CI 跑（`l1-dotnet-test`）
- ✅ L3：已按 Integration/EndToEnd 分类独立运行（`l3-network-test`）
- ✅ L2：脚本 + 作业就绪（作业默认跳过，待 `L2_ENABLED=true` + 自托管 runner）
- ⏳ 后续可加：把「非法 meta 扫描」这条规则**并入 `Assets/Tools/DisciplineScanner`**（则 L1 也能拦住它，L2 只留 Unity 编译/测试）——**这是性价比最高的一步**
