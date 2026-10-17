# 上云批B 云侧执行：模型 B（云侧 VPN）实装

> **批次**：上云批B 云侧执行（用户裁决"直接上 B"）　**日期**：2026-10-17　**状态**：**已完成（附验证证据；已提交 76f81485 · 8a245d04）**
> **来源**：《[上云测试专项设计](../设计文档/architecture/上云测试专项设计.md)》§5 云侧清单 + [09-上云联机测试](../项目说明/09-上云联机测试.md) §1/§1.1/§1.2。
> **红线照抄**：封闭测试例外（不得公开/宣传、不得作为"可发布"判据）；密钥只落机器私有文件（`deploy/.env` 与测试者 `.conf`），**本记录不含任何密钥/IP 之外的凭据**。

## 1. 交付（云主机实测）

| 项 | 实况 |
|---|---|
| 主机 | Ubuntu 24.04.5 LTS / kernel 6.8.0-139 / 2 vCPU / 3.6 GB / 40 GB（30 GB 空闲）；**Docker 29.9.1 + Compose v5.6.0 已预装**、git 2.43 已装 |
| 访问 | root + 专用密钥（`~/.ssh/id_ed25519_litegame`）→ 结论：**"窄免密 sudo" 不再需要**（本就是 root）。用户裁决的"给自动化窄免密"随之作废，未改任何 sudoers |
| WireGuard 服务端 | `wireguard 1.0.20210914`；`/etc/wireguard/{server.key,server.pub,wg0.conf,litegame-endpoint}`（600）；`wg0 = 10.8.0.1/24`、ListenPort 51820；`wg-quick@wg0` **enabled + active** |
| 本地防火墙 | 现状 `INPUT DROP` + ufw active（22 原已放行，**未动任何既有规则**）；新增 `ufw allow 51820/udp`（v4+v6） |
| peer 管理 | `deploy/wg/wg-peers.sh` 装到 `/usr/local/sbin/`（755）；`add dev` → `10.8.0.2`，`wg show wg0` 实测 peer 已在表内 |
| 应用栈 | 代码 `git archive` → `/opt/litegame`（122 MB）；`deploy/.env` 传到云上（**与客户端同源的密钥**）+ `LITEGAME_PUBLISH_HOST=10.8.0.1`；`advertise_host=10.8.0.1`；`docker compose up -d --build` → 三容器 Up（mongo healthy）、`restart=unless-stopped` |
| 暴露面（B 形态达成） | `ss` 实测：`5000`/`9100`/`17777` **只绑在 `10.8.0.1`**（公网接口无监听）；`51820/udp` 绑 `0.0.0.0`（WireGuard 正常形态：密钥即门票）。**Docker Hub 直连不通（000）**，靠主机既有 registry-mirrors 拉到 `mongo:8.0`；`mcr.microsoft.com` 直连可达（200） |

## 2. 验证证据（2026-10-17，全部在云主机实跑）

| 项 | 命令/方式 | 结果 |
|---|---|---|
| 健康 | `curl http://10.8.0.1:5000/{live,ready}`、`curl http://10.8.0.1:9100/metrics` | `live` / `ready` / Prometheus 文本（`roomserver_rooms 0`） |
| 启动身份 | `docker compose logs` 取横幅 | Meta `build=528fc6dd6e9d2743`（＝本机 `--check` 的 meta 常量）；Room **`buildHash=637ab2e0cabe21ac`＝客户端包内常量**（握手必过）；`安全信封已启用`；`Lobby 注册已启用 … advertise=10.8.0.1:17777 心跳=10000ms`；`结算提交管道已启用` |
| 部署态闭环 | `POST /auth/guest` → `GET /lobby/rooms/public` | 令牌 167 字符；实例 `room-1`：`buildHash=637ab2e0cabe21ac`、**`address=10.8.0.1:17777`**、`lastSeenAgeMs≈7s`（心跳在跳）、`draining=false` |
| 持久化 | `systemctl is-enabled` | `wg-quick@wg0` enabled/active、`docker` enabled/active（重启存活面已铺） |
| 密钥同源 | 云上 `docker compose config` | 五密钥全 FILLED（各 44）、`LITENET_TICKET_KEY=l1:<44>`、`LobbyDefaultRoomId=lobby-default`、三处端口 `host_ip=10.8.0.1` |

## 3. 过程留痕（两个踩坑，都已定位并修）

1. **`git archive HEAD` 传的是提交树，不含工作区改动** → 云上拿到的是**旧版 `compose.yaml`**（读 `${META_AUTH_SIGNING_KEY}` 这套名），而传上去的 `.env` 是内环脚本格式 → `docker compose config` 五密钥**全空**（现象与"键名互相踩"一模一样，极易误判成回归）。**处置**：单独把工作区版本的 `deploy/compose.yaml` 等覆盖上去（`tar | ssh tar -x`），复核转为全 FILLED。**结论**：本批的 compose 改动未提交，云侧部署要以**工作区**为准，不是 HEAD。
2. **嵌套引号把 `wg0.conf` 的私钥写成了字面量** → 首版 `PrivateKey = $(cat /etc/wireguard/server.key)`（32 字符的字面串），`wg-quick@wg0` 直接启动失败（`PrivateKey value length = 32`）。**处置**：改用 `{ echo …; echo "PrivateKey = $(cat …)"; } > wg0.conf` 重建（长度 44），服务转 active。**结论**：跨 ssh 的内层 `$()` **不要转义**（本地单引号已保护），且改完配置必须复核值长度而非只看"文件已写"。

## 4. 边界与挂账（如实）

- **安全组（2026-10-17 补记：已放行并实测）**：用户已在控制台放行 `51820/udp`。**实测法（不装 WireGuard 也能验）**：读 `iptables -L ufw-user-input -v` 上 `dpt:51820` 规则的包计数 → 从开发机发 8 个 UDP 包 → 计数 **0 → 8 包 / 312 字节**，即数据包真的到了主机 ⇒ 安全组与 ufw 两层都通。`22` 目前仍对全部来源开放（含 root 登录），建议按 [09 手册 §1.1](../项目说明/09-上云联机测试.md) 收到管理 IP。
- **宝塔面板：用户裁决"隧道访问"，已落地并实测（2026-10-17）**：① ufw 改为 `8888/tcp on wg0`（**面向所有来源的那条已删**）＋ 清掉无监听的 `20/21/888`（FTP 系）三条陈旧放行；② 端口保持 **8888**（用户裁决，无需重启面板）；③ 面板实况（含**一次误判的更正**，留痕以免后人重走）——**HTTPS 专用**：`ssl.pl=True`，`http://` 探测得 000 是"用错协议"而非防火墙；**「访问 UA 验证」开着**（宝塔菜单项 30）：**无浏览器 UA 的 `curl` 一律 404**，我据此一度推断"面板没装 nginx/界面出不来"——**该推断不成立**：换浏览器 UA 后同一 URL 返回 **HTTP 200**（30579B 登录页，标题"阿里云 · 宝塔面板"），面板本体健康；根路径 `/` 404 属**安全入口**设计（入口路径见 `bt default`，本记录不回显）。**旁路入口（不依赖 WireGuard，已实测 200）**：`ssh -N -L 18888:127.0.0.1:8888 <host>` ＋ 浏览器开 `https://127.0.0.1:18888/<安全入口>` —— 隧道未起时这是进面板的最优路径，也是面板收敛后的破窗之一（比控制台 Web 终端更接近原生体验）。**提醒**：面板不再对公网开放，常规入口只有隧道内 `https://10.8.0.1:8888/<安全入口>`（待 peer 上线生效，届时 ufw 该规则计数开始增长）。**另发现未动**：`26372/tcp`、`39000:40000/tcp` 两条放行同样**无进程监听**（陈旧），提请用户裁决；`80/443` 亦无监听但宝塔后续建站可能要用，保留。
- **隧道侧已闭合（2026-10-17 实测）**：用户按要求双击接入包后，`wg show wg0` 显示 `latest handshake: Now`、`transfer: 3.13 KiB received / 664 B sent`、`endpoint: 120.235.59.236:34153`（用户出口）⇒ **WireGuard 客户端安装（离线 MSI）与隧道建立全链走通**。- **客户端首个真机失败与修复（两条，均为现场显影）**：① 编辑器点登录报 `Insecure connection not allowed`——根因＝工程 `insecureHttpOption: 0`（Unity 默认禁明文 HTTP）而 `-meta.url` 是 `http://10.8.0.1:5000`；**已走 unity-pipeline 设为 `AlwaysAllowed(2)`**（`ProjectSettings.asset` 已落），并以 `UnityWebRequest` 探针实证：改前抛 `InvalidOperationException: Insecure connection not allowed`、改后 **HTTP 200 / `body=[live]`**。② **顺带发现矛盾**：本工程实测 `developmentBuild: false`，与 [07 本地联测](../../Docs/项目说明/07-本地联测.md) "exe 是 Development 构建（开发页可用）"的说法不符——故 `DevelopmentOnly(1)` 覆盖不到 player，本批取 `AlwaysAllowed`；**该矛盾点提请裁决**（若确要开发构建，应改构建设置而非把 http 全局放开）。③ exe 需**重出包**（HTTP 开关构建期烙定），本批已重打。
- **未做**：密钥轮换（随 R4）、TLS（B 形态下公网只有 WG 端口，Meta 仍是隧道内明文 HTTP，符合封闭测试口径）、mongo 备份定时任务、`22` 收窄、面板端口收敛（见上条建议）。
