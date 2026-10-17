# 10-Windows 云主机部署操作手册（原生形态）

> **本文定位**：回答"云主机是 Windows（Server Core、无桌面）时，怎么把 Meta + Room + Mongo + WireGuard 这一套装起来、跑起来、自检过"。
> **不做的事**：不解释项目机制（归 [03](03-技术细节-客户端.md)/[04](04-技术细节-Sim与联机.md)）；不重复客户端侧接入与联机测试用例（归 [09-上云联机测试](09-上云联机测试.md)）；不裁决验收标准（归《上云测试专项设计》§6）。
> **冲突裁决**：与服务器实际状态不一致以实机为准，并顺手修本文。
> **现行形态**：2026-10-18 起云侧为**本形态**（用户裁决换用 Windows Server 2025）；[09 手册 §2](09-上云联机测试.md) 的 Docker/compose 形态对应 `deploy/` 制品，保留备用（换回 Linux 主机时照用）。

---

## 0. 为什么原生，而不是 Docker / WSL2

**结论：WSL2 那条路对本项目不可行，卡在 UDP。** 游戏的对局通道是 **UDP 17777**（KCP），而 Windows 的端口转发（`netsh interface portproxy`）**只支持 TCP**；WireGuard 的 51820 同样需要 UDP 入站。把栈塞进 WSL2 后，公网 UDP 没有可靠的入站通路。

原生形态则天然没有这层 NAT：.NET 进程直接监听在 Windows 主机上，隧道（同样在主机上）投递到 `10.8.0.1` 即到位。代价是放弃 `deploy/` 的 compose 编排，换来一套 Windows 侧的手工装配（就是本文）。

---

## 1. 主机与前置

| 项 | 本次实机（2026-10-18） |
|---|---|
| 系统 | Windows Server 2025 Datacenter，**Server Core（不含图形化桌面）**，build 26100 |
| 规格 | 2 vCPU / 3.9 GB 内存 / C: 40 GB（可用 26 GB） |
| 时区 | China Standard Time（服务内部一律 UTC，日志按本地时区看） |
| Windows 防火墙 | 三个配置文件**默认均为关闭**（当前唯一门槛是**云安全组**；见 §9 硬化项） |

**安全组口径**（云控制台，只有你能改）：

- `22/tcp` → 你的管理 IP（我（Agent）与运维走 SSH）
- `51820/udp` → **所有来源**（WireGuard 的正常形态：密钥即门票，不认 IP）
- 其余不开。`5000`/`17777`/`9100` 都不需要公网入口

**必须先装的两个东西**（Server Core 缺）：

1. **OpenSSH Server**（`Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0`，走 Windows Update，国内可能很慢；实在慢就先用 WinRM：`Enable-PSRemoting -Force -SkipNetworkProfileCheck` + 放行 `5985`）
   - 装后：`Set-Service sshd -StartupType Automatic; Start-Service sshd`，防火墙放行 22，把管理公钥写进 `%ProgramData%\ssh\administrators_authorized_keys`（`icacls` 收权限），并把默认 shell 设为 PowerShell
2. **VC++ 2015–2022 x64 运行库**（`https://aka.ms/vs/17/release/vc_redist.x64.exe` /install /quiet）
   - **不装它 mongod 起不来**：Server Core 只有 `*_clr0400.dll`，缺独立的 `vcruntime140.dll`/`msvcp140.dll`

---

## 2. 目录布局与发布方式

```text
C:\litegame\
  ├─ meta\        MetaServer.exe + appsettings.json + run.cmd + out.log/err.log
  ├─ room\        RoomServer.exe + roomserver.json + run.cmd + out.log/err.log
  ├─ GameData\Config\   玩法表 .bytes（与客户端同一份；--combat-table 指这里）
  ├─ wg\          server.key / server.pub / wg0.conf（服务端隧道）
  └─ mongo\       bin\（mongod/mongosh）+ data\ + log\ + mongod.cfg
```

**发布方式：本机出版本，服务器只收产物。**

```powershell
# 开发机（有 SDK）：自包含发布，服务器不需要 .NET 运行时/SDK
dotnet publish Server/MetaServer/MetaServer.csproj -c Release -r win-x64 --self-contained true -o Builds/win-server/meta
dotnet publish Server/RoomServer/RoomServer.csproj -c Release -r win-x64 --self-contained true -o Builds/win-server/room
# 表数据与两个产物一起打包上传（tar 走 ssh，Windows 自带 tar）
tar -czf - -C Builds/win-server meta room GameData | ssh <host> "tar -xzf - -C C:/litegame"
```

产物大小：`meta` 约 105 MB、`room` 约 73 MB（自包含，含运行时）。服务器上**不装 SDK**（2 核小机没必要）。

---

## 3. MongoDB

- **来源**：官方 Windows zip（本机/服务器均可；`downloads.mongodb.org/current.json` 查当前 8.0.x 的下载地址）。本次：`mongodb-windows-x86_64-8.0.34.zip` → 解出 `bin\` 到 `C:\litegame\mongo\bin`。
- **mongosh 另下**（`downloads.mongodb.com/compass/mongosh-<ver>-win32-x64.zip`）——`rs.initiate` 需要它（zip 里不含）。
- **配置** `C:\litegame\mongo\mongod.cfg`：

```yaml
storage:
  dbPath: C:\litegame\mongo\data
systemLog:
  destination: file
  path: C:\litegame\mongo\log\mongod.log
  logAppend: true
net:
  bindIp: 127.0.0.1
  port: 27017
replication:
  replSetName: rs0        # 事务依赖（与 L3 夹具 MongoFixture.cs 同口径）
```

- **装服务并初始化**：

```powershell
& C:\litegame\mongo\bin\mongod.exe --config C:\litegame\mongo\mongod.cfg --install    # 服务名 MongoDB
Set-Service MongoDB -StartupType Automatic; Start-Service MongoDB
& C:\litegame\mongo\mongosh\bin\mongosh.exe --quiet --eval "rs.initiate({_id:'rs0',members:[{_id:0,host:'127.0.0.1:27017'}]})"
& C:\litegame\mongo\mongosh\bin\mongosh.exe --quiet --eval "var s=rs.status(); print(s.ok + ' ' + s.members[0].stateStr)"   # 期望 1 PRIMARY
```

---

## 4. WireGuard（服务端角色）

Windows 官方客户端可以当服务端用（同一套协议实现），关键是**装成服务**而不是开界面：

```powershell
# 装客户端（官方 MSI，签名 WireGuard LLC；国内直连 download.wireguard.com 常不通，可从开发机带一份过去）
msiexec /i wireguard-amd64-1.1.1.msi /qn /norestart

# 生成服务端密钥（私钥只落文件、不回显）
& 'C:\Program Files\WireGuard\wg.exe' genkey > C:\litegame\wg\server.key
& 'C:\Program Files\WireGuard\wg.exe' pubkey < C:\litegame\wg\server.key > C:\litegame\wg\server.pub

# 服务端配置（写 C:\litegame\wg\wg0.conf）：
#   [Interface] Address = 10.8.0.1/24 / ListenPort = 51820 / PrivateKey = <server.key>
#   [Peer]      # <测试者代号>   PublicKey = <他 conf 里的公钥>   AllowedIPs = 10.8.0.2/32
& 'C:\Program Files\WireGuard\wireguard.exe' /installtunnelservice C:\litegame\wg\wg0.conf
Get-Service 'WireGuardTunnel$wg0'      # Running
```

- **换服务器/换配置**：`/uninstalltunnelservice wg0` 再 `/installtunnelservice`（同名服务直接重装会失败，且旧配置不会自动更新）。客户端侧接入脚本已内置"先卸后装"（[Dist/README.md](../../Dist/README.md)）。
- **客户端 conf 只认两处**：`Endpoint = <公网IP>:51820` 与 `PublicKey = <服务端公钥>`；其余（`Address = 10.8.0.x/24`、`AllowedIPs = 10.8.0.0/24`、`MTU 1420`、`PersistentKeepalive 25`）不变。**换机只需重生成这两处**——客户端与游戏包一个字都不用改（它们连的是隧道内 `10.8.0.1`）。
- **peer 管理**：Linux 形态有 `deploy/wg/wg-peers.sh`（bash）；**Windows 侧尚无等价脚本**（当前手工加 `[Peer]` 段 + 重装服务）——挂账，人多了再补 PowerShell 版。

---

## 5. 应用配置：以**环境变量**为准

**实测结论：把 `appsettings.json` 放在 exe 同目录**（内容正确、JSON 可解析）**没有被 Meta 采用**——它仍绑了默认的 `http://127.0.0.1:5000`。改用**机器级环境变量**（与 compose 形态同一套名字）**一次就对**，故本形态以环境变量为准。

```powershell
# Meta（12 项，示例；密钥值来自开发机 deploy/.env，保证与客户端同源）
[Environment]::SetEnvironmentVariable('META_Meta__BindAddress','http://10.8.0.1:5000','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__MongoConnectionString','mongodb://127.0.0.1:27017/?replicaSet=rs0','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__MongoDatabaseName','litegame','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__AuthSigningKeyBase64','<AUTH_SIGNING_KEY>','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__LobbyInstanceKeyBase64','<LOBBY_INSTANCE_KEY>','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__LobbyTicketKeyBase64','<LOBBY_TICKET_KEY>','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__LobbyTicketKeyId','l1','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__LobbyTicketAudience','lobby','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__LobbyDefaultRoomId','lobby-default','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__SettlementInstanceKeyBase64','<SETTLEMENT_KEY>','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__AllowNonLoopbackHttp','true','Machine')
[Environment]::SetEnvironmentVariable('META_Meta__EnableSampleEndpoints','false','Machine')

# Room（4 项）
[Environment]::SetEnvironmentVariable('LITENET_SECURE_ENVELOPE_KEY','<ENVELOPE_PSK>','Machine')
[Environment]::SetEnvironmentVariable('LITENET_LOBBY_INSTANCE_KEY','<LOBBY_INSTANCE_KEY>','Machine')
[Environment]::SetEnvironmentVariable('LITENET_SETTLEMENT_INSTANCE_KEY','<SETTLEMENT_KEY>','Machine')
[Environment]::SetEnvironmentVariable('LITENET_TICKET_KEY','l1:<LOBBY_TICKET_KEY>','Machine')
```

> 密钥红线：值只经环境变量注入，**不进仓库、不回显**；本条与设计 §5 同口径。代价是机器上其他管理员可读（封闭测试接受）。

**房间服配置**：由 `deploy/roomserver.container.json` 改三处 + **删掉中文 `_comment` 数组**（PS 5.1 经 SSH stdin 会按 GBK 误解而炸）→ `C:\litegame\room\roomserver.json`：

| 字段 | 值 |
|---|---|
| `lobby.url` | `http://10.8.0.1:5000/lobby/instances/register` |
| `lobby.advertise_host` | `10.8.0.1`（客户端可达地址＝隧道地址） |
| `settlement.submit_url` | `http://10.8.0.1:5000/matches/result` |

---

## 6. 托管与开机自启：任务计划（**不要用 SSH 会话里 Start-Process**）

**实测坑**：从 SSH 会话里 `Start-Process` 起的进程，会**随会话结束被回收**（Windows 的会话作业对象）——表现为"起完看着是好的，等我这条命令结束就没了"。所以两个应用一律交给任务计划。

```powershell
# run.cmd（任务计划不支持 stdout 重定向，所以套一层 cmd）
# C:\litegame\meta\run.cmd:
#   @echo off
#   cd /d C:\litegame\meta
#   "C:\litegame\meta\MetaServer.exe" >> "C:\litegame\meta\out.log" 2>> "C:\litegame\meta\err.log"
# C:\litegame\room\run.cmd 同理，带上两个参数：
#   "C:\litegame\room\RoomServer.exe" --config "C:\litegame\room\roomserver.json" --combat-table "C:\litegame\GameData\Config" >> ... 

$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$trigger   = New-ScheduledTaskTrigger -AtStartup
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'LiteGame-Meta' -Action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c "C:\litegame\meta\run.cmd"') -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName 'LiteGame-Room' -Action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c "C:\litegame\room\run.cmd"') -Trigger $trigger -Principal $principal -Settings $settings
Start-ScheduledTask -TaskName 'LiteGame-Meta'; Start-ScheduledTask -TaskName 'LiteGame-Room'
```

**两个关键点**（都踩过）：

1. **Action 必须 `cmd.exe` + `/c "…run.cmd"`**：直接把 `.cmd` 当程序给任务计划，`CreateProcess` 不认 → 任务返回 `0x1`、日志一个字节都不写（症状：看起来注册成功、进程就是没有）
2. **`SYSTEM` + `AtStartup`**：机器级环境变量对它可见；且随机器启动，与是否登录无关

---

## 7. 部署态自检（与 [09 §2.4](09-上云联机测试.md) 同口径）

```powershell
# 在云主机上（走隧道地址）
(Invoke-WebRequest http://10.8.0.1:5000/live  -UseBasicParsing).Content    # live
(Invoke-WebRequest http://10.8.0.1:5000/ready -UseBasicParsing).Content    # ready
$t = (Invoke-RestMethod -Method Post -Uri http://10.8.0.1:5000/auth/guest -ContentType application/json -Body '{"requestId":"chk-1","deviceId":"chk-1"}').accessToken
Invoke-RestMethod -Uri http://10.8.0.1:5000/lobby/rooms/public -Headers @{ Authorization = 'Bearer ' + $t }
```

**本次实机结果（2026-10-18）**：`live` / `ready` / 指标可读；令牌 167 字符；大厅投影 `instance=room-1`、**`buildHash=637ab2e0cabe21ac`（＝客户端包内常量，握手必过）**、**`address=10.8.0.1:17777`**、`lastSeenAgeMs≈4s`、`draining=false`。房间服日志另有 `advertise=10.8.0.1:17777`、票据验签（kid l1 / audience lobby）、安全信封启用、结算提交管道启用。

**端口面（实测）**：TCP `10.8.0.1:5000`（Meta，只绑隧道地址 ✓）、TCP `0.0.0.0:9100`（指标）、TCP `127.0.0.1:27017`（Mongo）、UDP `0.0.0.0:17777`（对局）、UDP `0.0.0.0/:: 51820`（WireGuard）。

---

## 8. 坑清单（本形态实测，按踩到顺序）

| # | 症状 | 根因 | 处置 |
|---|---|---|---|
| 1 | `mongod` 起不来 / 服务装不上 | Server Core 缺 VC++ 运行库（只有 CLR 版 DLL） | 装 `vc_redist.x64.exe` |
| 2 | 任务计划 `LastTaskResult=0x1`、日志一字节不写 | `.cmd` 不能直接当任务程序（CreateProcess 不认） | Action 用 `cmd.exe` + `/c "…cmd"` |
| 3 | 手动起的进程"过一会儿自己没了" | **SSH 会话子进程随会话回收** | 一律走任务计划（或服务） |
| 4 | 路径被写坏（如 `C:\litegame\oom`） | 用 Python/字符串拼 Windows 路径时 `\r`、`\a` 被当转义 | **本地用编辑器写文件再传**，别拼字符串 |
| 5 | 经 SSH stdin 执行 PS 脚本静默失败（无输出、文件没写） | PS 5.1 `-Command -` 对 here-string 不可靠；含中文的无 BOM 脚本按 GBK 误解 | 用 **scp 传文件 + `-File` 执行**；脚本保持纯 ASCII |
| 6 | Meta 仍绑 `127.0.0.1` | `appsettings.json` 未被采用（本次实测） | 用机器级 `META_Meta__*` 环境变量 |
| 7 | 隧道装不上 / 还指着旧服务器 | 同名服务已存在时 `installtunnelservice` 失败，旧配置也不更新 | `uninstalltunnelservice` 后再装（接入脚本已内置） |

---

## 9. 换机 / 换 IP / 硬化（运维面）

- **换机或换公网 IP**：**所有已发出的隧道 conf 都要重生成**（`Endpoint` + 服务端 `PublicKey` 两处）。客户端与游戏包不用动。→ 强烈建议给实例**绑弹性公网 IP（EIP）**，并把入口换成域名，避免"换一次 IP 作废一批配置"（本次就作废了一份）。
- **可选硬化**（当前未做，接受现状即可）：
  - 打开 Windows 防火墙，把 `5000`/`9100`/`17777` 限成只允许 `10.8.0.0/24`（目前 `17777`/`9100` 绑在 `0.0.0.0`，靠安全组把关）
  - `22` 收窄到管理 IP（现状即如此）；把 `22` 也收进隧道是更彻底的做法（需控制台 Web 终端作破窗）
- **数据**：Mongo 数据在 `C:\litegame\mongo\data`（随机器盘）；结算 journal 在 `C:\litegame\room\Outbox`。**没有定时备份**——封闭测试最小形态，接受。
- **容量**：单机 2 核 / 3.9 GB，内存实测余量尚可（Server Core ~1.5 GB + mongod ~0.4 GB + 两个 .NET 进程 ~0.2 GB），但**不做容量承诺**（同设计 §0）。

---

## 10. 与其他文档的关系

- 客户端接入、进房流程、§6 验收与留痕、通用排障：[09-上云联机测试](09-上云联机测试.md)
- Linux/Docker 形态（备用）：`deploy/`（compose + Dockerfile）与 [09 §2](09-上云联机测试.md)
- 本地联测三路径、改码后三端操作序：[07-本地联测](07-本地联测.md)
- 测试者分发（含隧道切换与离线安装包）：[Dist/README.md](../../Dist/README.md)
