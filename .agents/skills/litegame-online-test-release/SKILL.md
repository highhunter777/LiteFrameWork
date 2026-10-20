---
name: litegame-online-test-release
description: LiteGame 联机测试发布前置流水线：BuildHash 复算与门禁 → Room/Meta 自包含发布、云侧停服/上传/起服/回读 → Unity 内置内容包与 IL2CPP Player 重建 → 启动冒烟 → 大厅投影 buildHash 核对。默认本机测试，不组发布包；仅对外分发时追加 release 通道基包 + 每人 conf。当共享源/协议/服务端宿主/客户端改动后要“准备联机测试、重发云侧、重打客户端”，或遇到 409 version-mismatch / 两端 BuildHash 不一致时使用；不用于实际双人对局验收（见 Docs/项目说明/11-联机测试流程.md §5）。
---

# 联机测试发布前置（云侧 + 客户端 + 测试包）

**为什么存在**：联机发布不是“重打个包”。共享源一改，客户端、Room、Meta 的身份常量与云上产物会分别漂移；漏掉任何一端，症状都是签票 `409 version-mismatch` 或开局拒，而不是“连不上”。本 skill 把 2026-10-18 实际跑通的发布链固化下来，并收录本机已踩过的工具链坑。

**边界**：默认本机测试，本 skill 结束于“两端版本一致、客户端可启动”；仅对外分发形态才追加“测试包已生成”。实际双人对局、死亡/血量/动画、RTT/丢包验收按 `Docs/项目说明/11-联机测试流程.md` §5–§7 另行执行；本环境是封闭测试，不得作为公开/可发布判据。

## 0. 测试形态与发布面

### 0.1 默认本机测试（不组发布包）

未明确说明要发给外部测试者时，一律按本机测试执行：Room + 客户端 Player + 本机第二实例（`-device.id`）。**不建 `LiteGame-base.zip` / 全量包，不调 `wg-peer` 生成 conf，不建 release 通道包**——这些只在 §4.1/§4.2 的对外分发形态执行。

**打包/发布必须收到用户明确指令**：代码/资产修改、生成物重生成与测试可以完成；`build-player.ps1`、BuiltinBundle 内容包重建、`Dist/New-Package*.ps1`、`deploy-room.ps1`、Meta/云侧发布未经用户明确说“打包/出包/发布/部署”一律不跑，改完停在可打包状态。

### 0.2 发布面判定

| 改动 | 云侧 | 客户端 |
|---|---|---|
| 共享源集（`LiteSim` / `LiteNet` / `Assets/RoomServer` 双编件）、`.proto` | `gen-build-hash.py` → 重发 Room（Meta 若其源也变则一并重发） | **必须重打包**（旧包握手拒） |
| 表源（`Luban/Data`、`Luban/Defines`） | `Luban/gen.bat` → `gen-build-hash.py` → 重发需要表的服务端 | 重编译 + 重打包 |
| `Server/RoomServer` 宿主 `.cs` | 重发 Room | 不动 |
| `Server/MetaServer` 宿主 `.cs` | 重发 Meta | 不动 |
| 纯表现 / Lua / prefab / 美术 | 不动 | 编辑器重编译；重打包 |
| 只改 `deploy/roomserver.windows.json` | `deploy-room.ps1 -SkipPublish -WithConfig` | 不动 |

判据永远是两端 `buildHash` 同值。表**数值**（`.bytes`）不在 BuildHash 闭包内（数据面走运行期摘要），但表源改动后仍要 `Luban/gen.bat` 重生成读码面，再跑 BuildHash。表源改动时服务端 `C:\litegame\GameData\Config` 是否要重传，按 `Docs/项目说明/10-Windows云主机部署.md` §2 现场确认；本 skill 的已验证路径未覆盖这一分支。

## 1. 前置门禁（不通过不发布）

### 1.1 代码与哈希

- 本批先按 `batch-wrapup` 完成 L1/收尾；共享源/表改动先按 `build-hash-sync` 重生成。
- 发布前**必须**复算两个常量；`deploy-room.ps1` 与 Meta publish 本身没有哈希门禁：

```powershell
python scripts/codegen/gen-build-hash.py
python scripts/codegen/gen-build-hash.py --check          # shared + meta
python scripts/codegen/gen-build-hash.py --check-shared   # 出包门禁同款
```

`--check` 不通过就停。生成器改了 shared 常量 → 重发 Room + 重打客户端；改了 meta 常量 → 重发 Meta。客户端常量在 `Assets/LiteNet/Protocol/BuildHash.g.cs` 的 `BuildHash.Value`。

### 1.2 编辑器

```powershell
unity command editor_status
# 需要：status=ready、playMode=stopped、compiling=false
# status=blocked_by_dialog：停下，把 dialog.title/message 告诉用户，不要重试
unity command set_autotick --enable true
unity command eval "return 1;"     # 预热；冷编辑器首次 eval_file 可能 5s 超时
```

多个 Unity 实例时先 `unity pipeline list`，必要时加 `--project-path E:\unityProject\Test`。

### 1.3 云侧

```powershell
ssh litegame-cloud-tunnel powershell -NoProfile -Command "Get-ScheduledTask -TaskName 'LiteGame-Meta','LiteGame-Room' | Select-Object TaskName,State"
ssh litegame-cloud-tunnel powershell -NoProfile -Command "(Invoke-WebRequest http://10.8.0.1:5000/live -UseBasicParsing).Content"
```

> 运维 SSH 走 **WireGuard 隧道入口**（公网 22 已关，见 10 手册 §4.1）；`scripts/deploy/*.ps1` 自动选路，手敲命令用 `litegame-cloud-tunnel`。

停服会断开测试者。若 GM 端点已开，先 `pwsh -NoProfile -File scripts/deploy/gm.ps1 -Action rooms` 看有没有活跃房；没有把握时先问用户。

### 1.4 本机 shell 约定

- 项目 `.ps1` 一律 `pwsh`；云上日志用 `powershell Select-String` 读，Git Bash 直接看可能 GBK 乱码。
- 手工打包 Meta 一律用 `C:\Windows\System32\tar.exe`；Git Bash 的 `/usr/bin/tar` 会把 `E:\` 当远端主机。`deploy-room.ps1` 已修，若仍见 `tar (child): Cannot connect to E:`，说明脚本是旧版。
- 远程 `-File` 路径用正斜杠 `C:/litegame/...`；Git Bash 会吃掉反斜杠（曾变成 `C:litegamemeta-stop.ps1`）。复杂命令走 scp 脚本 + `-File`，不在 ssh 里拼引号。
- 云侧脚本保持纯 ASCII（PS 5.1 会把无 BOM UTF-8 中文按 GBK 误解）。

## 2. 服务端

### 2.1 Room

```powershell
# C# / 共享源 / 玩法表变更：发布 + 打包（room + GameData/Config）+ 停服 + 上传 + 解压 + 起服 + 回读
pwsh -NoProfile -File scripts/deploy/deploy-room.ps1

# 只改配置（heartbeat / template / ports）
pwsh -NoProfile -File scripts/deploy/deploy-room.ps1 -SkipPublish -WithConfig

# 只重试上传现有产物
pwsh -NoProfile -File scripts/deploy/deploy-room.ps1 -SkipPublish

# 只换二进制、不同步玩法表（一般别用：见下方数据面判据）
pwsh -NoProfile -File scripts/deploy/deploy-room.ps1 -SkipPublish -SkipTables
```

判据：`stopped=True`、`extract=0`、`task=Running`、`[RoomServer] buildHash=…`，以及 `[Tables] 玩法数据身份基摘要:0x…` 与客户端日志「本地基摘要」同值。配置是启动期快照，改完必须重启任务；C# 变更必须重 publish，只重启任务会继续跑旧 exe。

**数据面红线**：`Assets/GameData/Config/*.bytes` 变了必须随 Room 一起上云（脚本默认已带 `-SkipTables` 才跳过）。只换二进制不传表 ⇒ 客户端 `StartGame` 阶段以「玩法数据身份不符（ConfigHash）」**拒整局**（2026-10-19 实测：`Map/tbmap.bytes` 漏传，服务端 `0xb0102ea9` vs 客户端 `0xa8a7e328`）。

### 2.2 Meta（仓库无 deploy-meta 脚本，按已验证手工路径）

```powershell
# 1) 本地发布 + 用 Windows tar 打包
dotnet publish Server/MetaServer/MetaServer.csproj -c Release -r win-x64 --self-contained true -o Builds/win-server/meta --nologo -v q
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
& $tar -czf Builds/win-server/meta.tgz -C Builds/win-server meta
if ($LASTEXITCODE -ne 0) { throw 'meta.tgz failed' }
```

把下面两段写成**纯 ASCII** 脚本（如 `Builds/win-server/meta-stop.ps1` / `meta-start.ps1`）：

```powershell
# meta-stop.ps1
Stop-ScheduledTask -TaskName 'LiteGame-Meta' -ErrorAction SilentlyContinue
Get-Process -Name MetaServer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
Write-Output ('stopped=' + ($null -eq (Get-Process -Name MetaServer -ErrorAction SilentlyContinue)))
```

```powershell
# meta-start.ps1
C:\Windows\System32\tar.exe -xzf C:\litegame\meta.tgz -C C:\litegame
if ($LASTEXITCODE -ne 0) { Write-Output ('extract=' + $LASTEXITCODE); exit 1 }
Write-Output 'extract=0'
Start-ScheduledTask -TaskName 'LiteGame-Meta'
Start-Sleep -Seconds 8
Write-Output ('task=' + (Get-ScheduledTask -TaskName 'LiteGame-Meta').State)
Select-String -Path 'C:\litegame\meta\out.log' -Pattern 'build=' | Select-Object -Last 1 | ForEach-Object { $_.Line }
```

```powershell
scp -q Builds/win-server/meta-stop.ps1 litegame-cloud:C:/litegame/meta-stop.ps1
ssh litegame-cloud powershell -NoProfile -ExecutionPolicy Bypass -File C:/litegame/meta-stop.ps1
scp -q Builds/win-server/meta.tgz litegame-cloud:C:/litegame/meta.tgz
scp -q Builds/win-server/meta-start.ps1 litegame-cloud:C:/litegame/meta-start.ps1
ssh litegame-cloud powershell -NoProfile -ExecutionPolicy Bypass -File C:/litegame/meta-start.ps1
```

判据：`stopped=True`、`extract=0`、`task=Running`、`build=…`。Meta 配置走机器级 `META_Meta__*` 环境变量，`appsettings.json` 不被采用；没有环境变量变更就不要上传配置。若 `extract` 非 0 或看不到 `build=`，先看 `C:\litegame\meta\err.log`，不要宣称发布成功。Meta 没有版本化回滚：确认 tgz 非空再停服，并留到起服验证完成。

### 2.3 大厅投影

在云上或已连隧道的开发机执行（token 是凭据，不要回显）：

```powershell
$t = (Invoke-RestMethod -Method Post -Uri http://10.8.0.1:5000/auth/guest -ContentType application/json -Body '{"requestId":"chk-1","deviceId":"chk-1"}').accessToken
Invoke-RestMethod -Uri http://10.8.0.1:5000/lobby/rooms/public -Headers @{ Authorization = 'Bearer ' + $t } | ConvertTo-Json -Depth 8
```

要求 `buildHash` 与客户端常量同值、`address=10.8.0.1:17777`、`lastSeenAgeMs` 合理。Room 重启后心跳 1s，投影通常几秒内更新。

## 3. 客户端重打

### 3.1 内置内容包

先判定：改过收集资源（prefab/场景/美术/表）才需要重建；只改 C#/协议/配置可复用现有包。复用前核对 `Assets/StreamingAssets/yoo/DefaultPackage` 的文件数与时间戳，并在发布记录里写明复用依据。

**已提交的收集资源改动也算改动**：不要只看 `git status` 是否干净。命名空间迁移/批量重序列化会改写 prefab 内的脚本或类型字符串（例：`CustomTypeName` 从 `LiteGame.UI.UIDropdownWrap` 变为 `LiteClient.UI.UIDropdownWrap`），工作区干净但旧包仍携带旧值，运行时表现为 `绑定索引[Dropdown_Map] 不是下拉件:期望 UIDropdownWrap/Dropdown 实得 RectTransform`。判定：包版本时间 ≥ 最近一次触碰收集资源的提交时间；否则重建。

正常路径让 `build-player.ps1` 自己构建；若 `BuiltinBundleBuildInvoker.Status` 长时间停在 `starting`（本机 `delayCall` 不执行的已知坑），改用同步兜底：

```csharp
// Temp/build-bundles-sync.cs（放 Temp/，不进 Assets）
public static class BuildBundlesSync
{
    public static string Run()
    {
        LiteGame.EditorTools.BuiltinBundleBuildInvoker.Run();
        return "ok | out=" + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;
    }
}
```

```powershell
unity command run_script --file Temp/build-bundles-sync.cs --entry BuildBundlesSync.Run --timeout_ms 1200000 --timeout 1300
# 实测 CLI 仍可能报 30s 超时；不要据此判失败。构建在主线程继续，轮询：
unity command eval "return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status + '|' + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;"
# 直到 success；再核对 LastOutputDirectory 与 Assets/StreamingAssets/yoo 的文件数/时间戳
```

`Status=failed` 或编辑器崩溃就停，读 Editor.log；不要带旧包出 Player。`Run()` 是同步主线程调用，构建期间其他 pipeline 命令可能超时，等它完成后再轮询。

### 3.2 Player

```powershell
pwsh -NoProfile -File scripts/build/build-player.ps1 -Profile BuildProfiles/windows-x64-closedtest.local.json -SkipRestoreCheck -SkipBundles
```

- 本机测试用 closedtest profile；默认 profile 的 `endpoints:{}` 会删掉 `Assets/StreamingAssets/deploy.args`，编辑器下次 Play 变“未配置服务器”。对外分发另用 release 本机档（§4.1）。
- `-SkipRestoreCheck` 是本机绕过：`Packages/manifest.json` 的 ParrelSync 指向本机绝对路径。该开关会同时跳过 `l0-dep-scan`（脚本把两者放在同一分支）；L0/L1 应在 `batch-wrapup` 阶段完成。不要为发布改 manifest。
- `-SkipBundles` 只在内容包已构建或已核对可复用时使用；内容变了却跳过 = 把旧内容烤进包。
- 判据：`Player build PASSED`、`unity command build_status` 为 completed、`Builds/StandaloneWindows64/GameAssembly.dll` 新鲜。**不要看 `Test.exe` 的时间戳**——它可能保持旧时间。构建报 completed 但产物不新，按编译错误处理，不要出货。

### 3.3 启动冒烟

```powershell
pwsh -NoProfile -File scripts/build/player-smoke.ps1 -TimeoutSec 90
# 期望：[OK] defined-state marker reached: '[Asset] ready'，随后 player smoke PASSED
```

失败先读 `Player.log`；冒烟不过不要组包。

## 4. 测试包（仅对外分发）

**默认本机测试跳过本节**：直接运行 `Builds/StandaloneWindows64/Test.exe`；同机第二客户端用 `-device.id ed-b` 另起一个实例，不需要复制目录或组包。

### 4.1 release 通道客户端

仓库 `BuildProfiles/windows-x64-release.json` 是环境中立档（`endpoints` 为空，直建会删 `deploy.args`，客户端无服务器地址）。本机复制为 gitignored 的 `BuildProfiles/windows-x64-release.local.json`，填入与 closedtest 相同的 `endpoints`，再出包：

```powershell
pwsh -NoProfile -File scripts/build/build-player.ps1 -Profile BuildProfiles/windows-x64-release.local.json -SkipRestoreCheck -SkipBundles
pwsh -NoProfile -File scripts/build/player-smoke.ps1 -ExePath Builds/StandaloneWindows64-release/Test.exe -TimeoutSec 90
```

### 4.2 基包 + 每人 conf

```powershell
# 一份共享基包（无 conf；游戏 + 启动脚本 + server.txt + WireGuard 安装包）
pwsh -NoProfile -File Dist/New-Package.ps1 -Tester LiteGame-base -Base -Zip -GameBuild Builds/StandaloneWindows64-release -Force

# 每个测试者一人一份 conf → 小叠加包（基包 + 本人 conf）
pwsh -NoProfile -File scripts/deploy/wg-peer.ps1 -Name <tester>
pwsh -NoProfile -File Dist/New-Packages.ps1 -Testers <tester> -Overlay -Zip -Force
```

先确认 `Dist/server.txt` 是 `http://10.8.0.1:5000`。基包不含私钥，可重复分发；`.conf` 内含私钥，一人一份，不回显、不入库、不截图。核对 zip 时间戳与大小。

## 5. 全绿判据

### 5.1 本机测试（默认）

- `gen-build-hash.py --check` 通过（shared + meta）。
- Room：任务 Running、日志 `buildHash` = 客户端常量。
- Meta：任务 Running、日志 `build` 为本次值（meta 源未变则沿用）。
- 大厅投影：`buildHash` = 客户端常量、`address=10.8.0.1:17777`、`lastSeenAgeMs` 正常；`/live`=live、`/ready`=ready。
- 客户端：`Player build PASSED` + `GameAssembly.dll` 新鲜 + 冒烟 `[Asset] ready`。
- 未生成发布包（基包/全量包/conf）——本机测试不需要。

### 5.2 对外分发追加

- release 通道客户端 `Player build PASSED` + release 冒烟 `[Asset] ready` + 包内 `deploy.args` 已烙入。
- `LiteGame-base.zip` 已刷新（时间戳/大小正确、包内无 `.conf`）；测试者各自有独立 `.conf`。
- 没有把旧客户端或任何密钥材料交出去。

## 6. 故障分流

| 症状 | 原因 | 处置 |
|---|---|---|
| `restore-check` 报 `manifest ... parrelsync` | 本机 manifest 绝对路径 | `-SkipRestoreCheck`；不要为发布改 manifest |
| `eval_file ... Main thread operation timed out after 5000ms` | 冷编辑器主线程窗口 | `unity command eval "return 1;"` 预热后重试 |
| 内容包 `Status=starting` 数分钟 | `delayCall` 未执行 | 同步 `Run()` + 轮询 Status；不要干等 |
| `run_script` 报 `timed out after 30000ms` | CLI 外层超时，主线程构建继续 | 轮询 `Status`；success 才继续，failed 停 |
| `tar (child): Cannot connect to E:` | Git Bash tar 抢占 PATH | 用 `System32\tar.exe`；确认 `deploy-room.ps1` 是修复版 |
| 云侧 `-File C:litegame...` 找不到文件 | Git Bash 吃反斜杠 | 改用 `C:/litegame/...` + scp 脚本 |
| `New-Package` 报 `output folder already exists` | 旧包目录 | 加 `-Force` |
| 构建报 completed 但产物旧 | 编译错误静默 no-op | 看 `PASSED`/freshness 与 Editor.log，不要出货 |
| 页面报 `绑定索引[X] 不是下拉件…实得 RectTransform` | 内容包早于 prefab/命名空间迁移，包内脚本或类型字符串陈旧 | 重建 BuiltinBundle + 重打客户端；只重编 C# 不修包 |
| 签票 `409 version-mismatch` | 两端 hash 不同 | `--check` → 重发 Room / 重打客户端 |
| 开局被拒：`玩法数据身份不符：StartGame.ConfigHash=… 本地复算=…` | 服务端 `GameData/Config` 未随表更新（只换二进制） | 重发 Room（脚本默认同步表）→ 看日志 `[Tables] 玩法数据身份基摘要:0x…` 与客户端「本地基摘要」同值 |
| 大厅 hash 未更新 | Room 旧进程或心跳未到 | 看 Room 日志、任务状态与 `lastSeenAgeMs` |
| 配置改了但端点/模板没变 | 只重启、没上传配置；或改 C# 只重启 | `-WithConfig` 重发配置；C# 变更重 publish |

## 7. 证据、交接与边界

- 把本次 shared/meta hash、Room/Meta 任务状态、大厅投影、客户端产物时间、偏差项追加到当前上云进度卷（现为 `Docs/施工进度/上云测试线.md`），并写明“双人真对局是否已执行”；仅对外分发时再记包大小。
- 默认本机测试，不产出发布包；`Dist/out` 出现 `LiteGame-base.zip` 或 testers 的 `.conf` 即说明走了分发形态，发布记录需写明。
- 跑 `python scripts/gate/doc-lint.py --no-links`（或 `pwsh scripts/gate/test.ps1 -Lane Doc`）。
- 提交/推送前先问用户；不要自动提交云侧配置、测试包或任何 `LocalNotes`/`.conf`。
- 交接提醒：必须用新客户端（旧 exe 会 409）；云上 `sandbox` 不限时/无击杀上限/1s 复活/不产结算；同机第二客户端用 `-device.id`；测结算要把 `default_template` 切回 `standard` 并重发配置。
- 本 skill 不验证实际对局；不把封闭测试环境当作公开可发布环境；不硬编码本次 hash（以生成器与日志为准）。

## 参考

- `Docs/项目说明/11-联机测试流程.md`（发布矩阵、测试者接入、开测与收尾）
- `Docs/项目说明/10-Windows云主机部署.md`（云主机布局、Meta/Room 发布、任务计划、坑清单）
- `Docs/开发导航/云侧速查.md`（发布/模板/GM 命令速查）
- `build-hash-sync` skill（生成物与 BuildHash 的权威流程）
- `batch-wrapup` skill（L1、文档回写、审计登记）
