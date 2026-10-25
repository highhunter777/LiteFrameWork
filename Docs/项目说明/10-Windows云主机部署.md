# 10-Windows 云主机部署操作手册（原生形态）

> **本文定位**：回答"云主机是 Windows（Server Core、无桌面）时，怎么把 Meta + Room + Mongo + WireGuard 这一套装起来、跑起来、自检过"。
> **不做的事**：不解释项目机制（归 [03](03-技术细节-客户端.md)/[04](04-技术细节-服务端.md)）；不重复客户端接入与联机测试流程（现行归 [11-联机测试流程](11-联机测试流程.md)，Linux 旧形态细节归 [09-上云联机测试](09-上云联机测试.md)）；不裁决验收标准（归《上云测试专项设计》§6）。
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
| **地址与登录** | 公网 IP 与管理员账号见 **`LocalNotes/cloud-host.md`**（机器私有、**不入库**；只走 SSH 密钥、无口令登录，私钥也不入库）。本机 `~/.ssh/config` 加**两个**别名：`litegame-cloud-tunnel`（隧道入口 `10.8.0.1:22`，**日常用这个**）与 `litegame-cloud`（公网，仅破窗时临时放行 22）——写法与理由见 §4.1；**本手册不回显 IP/账号** |
| 系统 | Windows Server 2025 Datacenter，**Server Core（不含图形化桌面）**，build 26100 |
| 规格 | 2 vCPU / 3.9 GB 内存 / C: 40 GB（可用 26 GB） |
| 时区 | China Standard Time（服务内部一律 UTC，日志按本地时区看） |
| Windows 防火墙 | 三个配置文件**默认均为关闭**（当前唯一门槛是**云安全组**；见 §9 硬化项） |

**安全组口径**（云控制台，只有你能改）：

- `22/tcp` → 你的管理 IP（我（Agent）与运维走 SSH）
- `51820/udp` → **所有来源**（WireGuard 的正常形态：密钥即门票，不认 IP）
- 其余不开。`5000`/`17777`/`9100` 都不需要公网入口

**必须先装的两个东西**（Server Core 缺）：

1. **OpenSSH Server**（`Add-WindowsCapability -Online --name OpenSSH.Server~~~~0.0.1.0`，走 Windows Update，国内可能很慢；实在慢就先用 WinRM：`Enable-PSRemoting -Force -SkipNetworkProfileCheck` + 放行 `5985`）
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
  ├─ python\      embedded Python 3.12（remote_runner 运行时，不写 PATH）
  ├─ remote_runner.py  云上 Python 执行入口（deploy-room/wg-peer 自动上传）
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

> **玩法表必须与二进制同批**：`scripts/deploy/deploy-room.py` 默认把 `Assets/GameData/Config` 一起同步（`--skip-tables` 可跳过）。只换 Room 二进制、不传表时，客户端会在 `StartGame` 阶段以「玩法数据身份不符（ConfigHash）」**拒整局**——这是数据面守卫（判定面表字节 + 本局规则的运行期摘要），不是网络问题。判据：Room 启动日志 `[Tables] 玩法数据身份基摘要:0x…` 应与客户端日志「本地基摘要」同值（2026-10-19 实测：`Map/tbmap.bytes` 未传 ⇒ 服务端 `0xb0102ea9` vs 客户端 `0xa8a7e328`）。

### 2.1 云上 Python 与 `remote_runner.py`

`deploy-room.py` / `wg-peer.py` 的云上动作不再内嵌 PowerShell 字符串，而是上传 `deploy/remote_runner.py` 为 `C:\litegame\remote_runner.py`，用云上 Python 执行（stdlib-only）：

```powershell
# 一次性/升级时：下载 pinned embeddable Python、校验 SHA256、解压到 C:\litegame\python，并上传 runner
python scripts/deploy/bootstrap-cloud-python.py                 # 已装则只上传 runner
python scripts/deploy/bootstrap-cloud-python.py --dry-run       # 只看计划（--what-if 为兼容别名）
python scripts/deploy/bootstrap-cloud-python.py --force         # 强制重装运行时

# 日常部署：ensure_remote_ready 自动探测并上传 runner；未装 Python 会提示先跑 bootstrap
python scripts/deploy/deploy-room.py
python scripts/deploy/wg-peer.py --name alice
```

- 默认运行时 = `C:\litegame\python\python.exe`（embedded，不写 PATH、不装系统包）；如改用系统 Python，设 `LITEGAME_REMOTE_PYTHON=python`。
- `bootstrap-cloud-python.py` 默认 pin Python 3.12.9 + SHA256；自定义 `--python-url` 时必须同时给 `--sha256`（否则会跳过校验并告警）。
- `remote_runner.py` 提供 `room-stop` / `room-start` / `room-status` / `wg-peer` 四个动作；日志/错误保持 ASCII，私钥只经 stdout 回传到本机 `.conf`，不落云上日志。中文系统的 `schtasks` 状态字段是 `模式:`（值如 `正在运行`），runner 已按代码页解码。
- 云端 Python 缺失时，`deploy-room`/`wg-peer` 会在停服/上传前 fail-fast，避免半截部署。

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
- **peer 管理**：`scripts/deploy/wg-peer.py`（Windows 等价物：云上 `wg.exe` 生成密钥 + 追加 `[Peer]` + 重装服务 → 本机落 `.conf`；见该脚本头注释）。

### 4.1 运维通道（SSH）：隧道优先，公网 22 关闭

**结论**：日常运维 SSH 走 **WireGuard 隧道入口 `10.8.0.1:22`**，公网 `22/tcp` 从安全组撤掉。理由：公网 22 的白名单按**来源 IP** 放行，而开发机出口 IP 会变（换宽带、切代理、代理 TUN/全局模式都会变 ⇒ 每次都要改安全组）；隧道内的 22 由 WireGuard 公钥 + SSH 公钥识别身份，**与来源 IP 无关**，且安全组只看得到封装用的 `UDP 51820`。

本机 `~/.ssh/config`（账号/私钥路径见 `LocalNotes/cloud-host.md`；**密钥不入库**）：

```
Host litegame-cloud-tunnel
    HostName 10.8.0.1
    User <云主机账号>
    IdentityFile <私钥路径>
    IdentitiesOnly yes
    StrictHostKeyChecking accept-new
    ServerAliveInterval 30
```

- **首次接入先核对 host key**（确认是同一台主机）：`ssh-keyscan -T 10 -p 22 10.8.0.1` 的三把键应与 `ssh-keygen -F <公网IP>` 的输出逐字节相同；相同再接受。
- **脚本默认只走隧道**：`scripts/deploy/` 的 `deploy-room.py` / `gm.py` / `wg-peer.py` 共用 `_ssh_host.py`——探测 `10.8.0.1:22` 可达且别名已配 → 用隧道别名；不可达/别名不符会直接 fail-fast，提示检查 WireGuard。**不再自动回退公网**；破窗时显式 `--ssh-host litegame-cloud` 或设 `LITEGAME_SSH_HOST=litegame-cloud`（需临时开放安全组 22）。
- 本机隧道服务开机自启：`WireGuardTunnel$wg-dev`（StartType=Automatic）；起不来先 `Get-Service 'WireGuardTunnel$wg-dev'`。

**安全组**：只保留 `UDP 51820`（测试者也走它）；**删除 `22/tcp` 的入方向规则**。`5000`/`17777` 本就只绑 `10.8.0.1`（公网不可达），无需放行。

**破窗（必须留）**：

1. ECS 控制台「远程连接 / VNC」进系统修 WireGuard（改配置后 `/uninstalltunnelservice wg0` → `/installtunnelservice`）；
2. 或在安全组**临时**加回 `22/tcp` 白名单，修好即删。

**常见现象**：

- `ssh litegame-cloud` 超时、但 `10.8.0.1:5000` 通 ⇒ **正常**（公网 22 已关），改用隧道别名；
- 隧道也连不上 ⇒ 查本机 `WireGuardTunnel$wg-dev` 与云上 `WireGuardTunnel$wg0`；若公网 IP 能 ping 通但 `10.8.0.1` ping/22 不通，是**云端 WireGuard 服务/握手问题**，公网 22 已关，需 ECS 控制台按 §4.1 破窗修隧道；
- 挂代理（TUN/全局）时公网出口会变成代理节点 IP（`LocalNotes/cloud-host.md` 有记录）——隧道路径不受影响，这也是不再依赖 IP 白名单的原因之一。

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

**现成文件（2026-10-18 起）**：`deploy/roomserver.windows.json` 就是上表三处已改好、且**不带 `_comment`** 的成品（纯 ASCII，避开 PS 5.1 经 SSH stdin 按 GBK 误解中文的坑）——直接传成 `C:\litegame\room\roomserver.json` 即可，不必再从 `roomserver.container.json` 手工派生。

**模板：普通房 / 云端测试房**：`rooms` 里除 `standard`/`four`/`eight` 外另有 **`sandbox`（云端测试房专用）**；`default_template` 现指向 **`standard`**——普通房默认会结算：

```json
"standard": { "expected_players": 2, "match_duration_frames": 10800, "kill_limit": 20, ... }
"sandbox":  { "expected_players": 2, "match_duration_frames": 0,     "kill_limit": 0,  ... }
"default_template": "standard"
```

- 普通房：大厅选图/人数建房，`standard` 模板，对局自然结束并产出结算；
- 云端测试房：只能由编辑器 GM 页「创建并进入云端测试房」显式按 `template=sandbox` 建房（隐藏、不进大厅；地图随建房票据盖章）；`match_duration_frames = 0` → **不限时**（`MatchSystem.Tick` 的 `Timer <= 0` 即跳过），`kill_limit = 0` → 不按击杀结束，`respawn_delay_frames` 必须 ≥ 1（此处 60 = 1 s 复活）；**代价：测试房不产结算**（Outbox/战绩不会落）。
- 客户端**选人数**会覆盖模板的 `expected_players`（票据盖章 `0 = 按模板`）；普通房不指定模板时走 `default_template=standard`，测试房由 GM 页显式带 `sandbox`。

**实机落地（2026-10-18）**：`deploy/roomserver.windows.json` 已上推为 `C:\litegame\room\roomserver.json`（原文件留 `roomserver.json.bak`），当时横幅为 `rooms=[standard,four,eight,sandbox] default=sandbox`；闭环自检：`/ready`=ready、令牌 167 字符、大厅投影 `address=10.8.0.1:17777`、`buildHash=637ab2e0cabe21ac`（＝客户端包内常量）、`lastSeenAgeMs≈8.9s`、`draining=false`。

> **2026-10-23 调整**：仓库配置的 `default_template` 已改为 `standard`（普通房验结算），`sandbox` 仅由 GM 页显式测试房使用。云上实例需重新上传 `deploy/roomserver.windows.json` 并重启 `LiteGame-Room`，横幅才会变为 `default=standard`；未重启前云上仍按旧 `default=sandbox` 运行。

**心跳口径（2026-10-18 调低）**：云上现为 `heartbeat_interval_ms=1000`（与 container.json 同值，内核下限；大厅列表可见延迟上限 ≈3 s）。此前 10000 的差异已消除；改配置后必须重启 `LiteGame-Room` 才生效。

### 5.1 运维（GM）端点（2026-10-18 启用）

客户端 GM 面板在**真 KCP 独立进程**形态下够不到权威侧（《云端GM通道专项设计》§0），故房间服另起一个**只绑隧道地址的写端点** `http://10.8.0.1:9101/gm/*`（仅 POST + 头 `X-GM-Key`），云上运维用它做"强制开局 / 收房 / 踢席位 / 回灌结算 / 看房间列表"。

- **配置**：`roomserver.json` 的 `gm_port` / `gm_bind`（**通配绑定面装载期即拒**——公网可写的端点不属配置失误）；密钥**只走机器级环境变量 `LITENET_GM_KEY`**（值见 `LocalNotes/cloud-host.md`），**空 ⇒ 端点不启用**（写面 fail-closed）。
- **动作**：`POST /gm/rooms`（列表）、`/gm/start`（须有 owner）、`/gm/abort`、`/gm/kick`（`{"room":"..","player":N}`）、`/gm/settle-flush`。审计行前缀 `[GM]` → `C:\litegame\room\out.log`。
- **改代码必须重新发布上传**（本次踩过）：云上跑的是自包含产物，**只改 `roomserver.json` 重启即可，改 C# 必须重跑 `dotnet publish -r win-x64 --self-contained` 并把 `room/` 传上去**——否则配置生效而 exe 仍是旧的（表现为"配置里开了端点、日志里没有端点"）。上传须**先停服再解压**：运行中的 DLL 被占用会 `Can't unlink already-existing object`。
- 本机调用走 SSH 隧道转发：**别用 9101 作本机端口**——它在本机落在 Windows TCP 排除端口区间（9014-9113），`ssh -L` 会 `bind … Permission denied`。已封装为 `scripts/deploy/gm.py`（默认本机端口 19101，自动起/关隧道、自动带 body）：

```powershell
python scripts/deploy/gm.py --action rooms                       # {"ok":true,"rooms":[]}
python scripts/deploy/gm.py --action start  --room <房号>
python scripts/deploy/gm.py --action kick   --room <房号> --player 1
python scripts/deploy/gm.py --action abort  --room <房号>
python scripts/deploy/gm.py --action settle-flush
```

密钥取本机用户环境变量 `LITEGAME_GM_KEY`，缺失则回退读 `LocalNotes/cloud-host.md`。手拼 curl 时记得**必须带 body**（无 `Content-Length` 会被 `HttpListener` 回 411）。

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
Register-ScheduledTask -TaskName 'LiteGame-Meta' --action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c "C:\litegame\meta\run.cmd"') -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName 'LiteGame-Room' --action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c "C:\litegame\room\run.cmd"') -Trigger $trigger -Principal $principal -Settings $settings
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

- 客户端接入、进房流程、验收与留痕、通用排障：[11-联机测试流程](11-联机测试流程.md)
- Linux/compose 旧形态（备用）：`deploy/`（compose + Dockerfile）与 [09 §1/§2/§8](09-上云联机测试.md)
- 本地联测三路径、改码后三端操作序：[07-本地联测](07-本地联测.md)
- 测试者分发（含隧道切换与离线安装包）：[Dist/README.md](../../Dist/README.md)
