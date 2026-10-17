# Dist/ —— 分发组装目录

**把要发给测试者的东西都放这里**：测试者侧脚本、每人的隧道配置、游戏构建拷贝、以及组装出来的待发包——一个目录收口，不用在 `Builds/` 与 `deploy/` 之间来回找。

访问模型（A 白名单 / B 云侧 VPN / C 隧道反代）与云侧生成配置的命令块见 [09-上云联机测试 §1.2](../Docs/项目说明/09-上云联机测试.md)。

## 本目录的文件

| 文件 | 入库 | 说明 |
|---|---|---|
| `README.md` | ✅ | 本文件 |
| `加入联机环境.cmd` | ✅ | **测试者双击的入口**（ASCII-only：cmd 按系统码页解析，中文提示由 .ps1 输出） |
| `Join-LiteGameTest.ps1` | ✅ | 测试者侧一键脚本（装 WireGuard → 装隧道服务 → 连通自检；UTF-8 **带 BOM**，改文件务必保留 BOM） |
| `New-Package.ps1` | ✅ | **你用的组装助手**：拼一份待发包（ASCII-only，与仓库其它脚本同口径） |
| `server.txt.example` | ✅ | 地址模板——复制成 `server.txt` 填值 |
| `wireguard-*.msi` / `*.exe` | ❌ | **官方 WireGuard 安装包**（取回一次放这里，组装时自动随包分发；`.gitignore` 挡 `*.msi`/`*.exe`） |
| `server.txt` | ❌ | 本机测试环境地址（`.gitignore` 挡） |
| `wg-<测试者>.conf` | ❌ | 云主机上生成、一人一份（**含私钥**；`.gitignore` 挡） |
| `out/` | ❌ | 组装产物（含游戏拷贝，`.gitignore` 挡） |

## 为什么把 WireGuard 安装包内置（2026-10-17 现场反馈）

- **现象**：测试者侧走 `winget` 安装会失败——`download.wireguard.com` 国内直连不稳（实测 winget 报 `0x80072efd`、我们的云主机连同一地址也超时）；让每个测试者各自下载＝各自踩坑。
- **做法**：**取回一次**，把官方安装包放到本目录（名字须匹配 `wireguard*.msi`，`.msi`/`.exe` 都行）→ `New-Package.ps1` 组装时自动拷进每个包 → 测试者侧接入脚本**优先用包内这份离线安装**（`.msi` 走 `msiexec /qn`），全程不联网。
- **取不到时的兜底**：组装器会 `[warn] no WireGuard installer`，接入脚本退回 `winget`（常失败）——所以正式发包前请确认包里有它。
- **来源与合规**：官方站 <https://www.wireguard.com/install/> 的原版安装包，**未做任何改动**；随包分发只为免去测试者下载。WireGuard 为 GPLv2，允许再分发（保留来源说明即可）；安装包本体**不入库**（`.gitignore` 挡）。

## 组装一份包（三步）

```powershell
# 1) 云主机上为该测试者生成隧道配置（命令块见 09 手册 §1.2），把 /tmp/<他>.conf 取回本机
# 2) 复制地址模板并填值（模型 B 填 VPN 侧地址；公网 IP 那条路对测试者是关着的）
cp Dist/server.txt.example Dist/server.txt      # 然后编辑那一行
# 3) 组装（默认取 Builds/StandaloneWindows64；-Zip 直接产出压缩包）
pwsh -NoProfile -File Dist/New-Package.ps1 -Tester alice -Conf <alice.conf> -Zip
```

产物：`Dist/out/alice/`（加了 `-Zip` 则同时有 `Dist/out/alice.zip`），解压后测试者看到：

```text
alice/
  ├─ 加入联机环境.cmd        ← 让他先双击这个
  ├─ Join-LiteGameTest.ps1
  ├─ wg-alice.conf          ← 他的隧道配置（私钥在里面，一人一份）
  ├─ server.txt
  └─ Test.exe + Test_Data/…  ← 游戏（平铺在根：脚本在同目录找 exe，测试者双击即玩）
```

## 组装助手的检查项（七种情形均在本机实测）

| 检查 | 行为 |
|---|---|
| `.conf` 缺失 / 不是 WireGuard 配置（无 `[Interface]`/`[Peer]`/`PrivateKey`） | 拒绝（exit 1） |
| **`AllowedIPs` 含 `0.0.0.0/0` 或 `::/0`** | **硬拦**——那会把测试者**全部上网流量**拽进隧道（他变慢、你成了他的出口）；确要如此才加 `-Force` |
| 服务地址缺失（没给 `-ServerUrl` 且没有 `server.txt`） | 拒绝 |
| 构建目录不存在 / 里面没有 `.exe`（`UnityCrashHandler` 除外） | 拒绝（防把空目录发出去） |
| 输出目录已存在 | 拒绝（防新旧混装；`-Force` 覆盖） |
| `-CheckOnly` | 只报告不写盘（发之前先自查） |
| `-Zip` | 额外产出 `<Tester>.zip` |

也可以 `-SkipGameCopy` 只拼"脚本 + conf + 地址"（游戏另外单独发时用）。

## 转发给测试者的话

1. 解压到任意目录（路径别含空格更保险）；
2. **双击「加入联机环境.cmd」** → Windows 权限框点"是" → 等它跑完（约 1 分钟：装 WireGuard + 建隧道 + 连服务器自检）；
3. 看到 `── 全部就绪 ──` 后，双击游戏 exe。
4. 以后不用再跑这个脚本（隧道是开机自启的系统服务）；换网络、开热点都不影响。

若 Windows 拦着不让跑：右键 → 属性 → 勾选**解除锁定** → 再双击。

## 测试者脚本行为与边界（如实）

| 事 | 说明 |
|---|---|
| 装 WireGuard | 优先用**包内**官方安装包（`wireguard*.msi` 走 `msiexec /qn`；`.exe` 试 `/S`）——离线、免下载；没有则 `winget install WireGuard.WireGuard`（**国内常因官方源不可达而失败**，失败时打印 winget 退出码而不是笼统归因杀软）；两条都不可用就明确报错——**脚本不含任何下载 URL**（不猜网址） |
| 装隧道 | **先卸后装**：同名隧道已存在时先 `uninstalltunnelservice` 再 `installtunnelservice <conf>`（换服务器/换配置必须重装才生效，且重装才是幂等的）；装成系统服务、开机自启、免开界面；起不来时提示兜底：双击 `.conf` → 弹窗点"是"走 GUI 导入 |
| 连通自检 | `GET <server>/live` 得 200 = 隧道与服务器都对；失败给三条自查（服务未 RUNNING / 网络封 UDP / 截图给发件人） |
| 体检模式 | `powershell -NoProfile -ExecutionPolicy Bypass -File Join-LiteGameTest.ps1 -CheckOnly` |
| 卸载 | 设置→应用卸 WireGuard，再删掉那份 `.conf` |
| **未实测** | 本机实测面＝前置判定/资源定位/权限分支/入口链路/中文渲染/退出码；**真机全流程（真装、真隧道起来、`/live` 真 200）需在一台干净 Windows 上跑一次才算实证**——首次发出去的那个测试者请留话收失败截图 |

## 红线

`.conf` 就是那个人的私钥：**一人一份、不回传、不截图、不入库**（本目录 `.gitignore` 已挡 `*.conf`/`server.txt`/`out/`/`*.zip`/`*.exe`/`*.msi`）。测试结束、某人退出测试时，撤回他的 `[Peer]` 段并撤销那份 `.conf`。
