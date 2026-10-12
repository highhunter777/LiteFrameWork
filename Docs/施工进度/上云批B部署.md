# 上云测试批B：封闭测试部署（编排制品 + 本机容器级验收）

> **批次**：上云测试批B（部署）　**日期**：2026-10-12　**状态**：**制品交付 + 本机容器级验收完成（未提交）**；云侧执行与 §6 第 1–3 项挂账（如实）
> **来源**：《[上云测试专项设计](../../设计文档/architecture/上云测试专项设计.md)》§3 批B / §5 部署注意清单 / §6 端到端验收 / §7 红线。
> **红线照抄**：本部署属封闭测试例外（IP 白名单、不得公开/宣传）；IL2CPP 真机核证与密钥轮换（随 R4）仍挂账，**本环境不得作为"可发布"判据**。

## 1. 交付

| 交付物 | 说明 |
|---|---|
| `deploy/compose.yaml` | 全链编排：mongo（单节点副本集 rs0，`mongo-init` 一次性 rs.initiate——与 L3 夹具 `MongoFixture.cs` 同口径）+ MetaServer + RoomServer（+ Caddy `--profile tls`）；持久卷 `mongo-data` / `room-outbox`；`stop_grace_period: 30s` 排空预算 |
| `deploy/Dockerfile.roomserver` / `Dockerfile.metaserver` | 多阶段（sdk:8.0 构建 → runtime/aspnet:8.0）；构建上下文 = 仓库根（源链接/双轨依赖）；`--combat-table /app/GameData/Config`（表与客户端同一份 .bytes） |
| `deploy/roomserver.container.json` | 容器形态配置：lobby/settlement 指向 `metaserver:5000`、audience=lobby、metrics_port=9100、advertise_host（云上改对外 IP） |
| `deploy/Caddyfile` + `deploy/.env.example` + `deploy/README.md` | HTTPS 反代形态；密钥模板（**只登记名字**，值进 gitignore 的 `.env`）；用法与云侧清单 |
| `.dockerignore` + `.gitignore`（`deploy/.env` 规则） | 构建上下文瘦身；机器私有密钥不入库 |
| `Server/RoomServer/Program.cs` | **`LITENET_TICKET_KEY` 环境变量口**（`<kid>:<base64>`）——设计 §5"密钥经环境变量/Secret，严禁命令行明文"的最后一个缺口（票据密钥此前只有 `--ticket-key` CLI）；同批把解析收 `ParseTicketKeySpec`（CLI/env 共用）并**修掉异常回显密钥原文**（改"原值不回显"口径） |

注记：Dockerfile **刻意不写 `# syntax=` 行**（前端镜像需拉 Docker Hub，本环境不可达）；基础镜像直连 mcr 超时，经 daocloud 镜像源拉取后回打标准标签（命令已入 `deploy/README.md`，属机器环境事项、非制品缺陷）；云构建直连即可。

## 2. 本机容器级验收（§6 对照）

| §6 项 | 状态 | 证据（2026-10-12，本机 `litegame-mongo`/compose 全链） |
|---|---|---|
| 4 幂等（100 次只落一条） | ✅（引用 L3） | R3-Profile L3 已实证（[记录](服务端R3-Profile.md)）；本批未重复 |
| **5 杀 Room 重启 → Outbox 重放不丢不重** | ✅ **已证** | 预置 journal 一条 → 重启后日志 `结算日志重放：待提交 1 条（坏行跳过 0）` → Meta `/matches/result` **200** → Mongo `settlement_ledger=2`（双玩家）/`match_results=1`；**不重**：抹掉完成标记行重放同一条（模拟"落库后、标记前崩溃"窗）→ Meta 幂等吸收，计数**不变** |
| **6 Mongo 不可用 → 重试 → 恢复收敛** | ✅ **已证** | 停 mongo → Room 日志 `提交失败（连续 1 次）：TaskCanceledException（HttpClient 5s 超时）… 条目保持待提交；对局服务不受影响` → 起 mongo → 收敛至 `ledger=4`/`results=2`，Meta `/ready=ready` |
| **7 容器 stop → 排空 5 步** | ✅ **已证** | `docker stop` → `收到停止信号 → 开始排空` → `排空完成（8ms）：房间数=0 结算在盒=0` → **exit code 0** |
| 1 白名单外不可达 | 挂账 | 需云侧安全组/UDP 白名单实际操作（无云主机凭据） |
| 2 一键游客获取令牌 → Join Ticket → 进房 | 挂账（前半已证） | 本批已证**登录→令牌→投影**（下条）；完整进房需客户端（批C） |
| 3 打完整局 → MatchEnded | 挂账 | 需客户端（批C）；协议/结算链已分别交付 |

**附加证据**：`GET /ready → ready`；`GET :9100/metrics` 64 行（Prometheus 文本）；`POST /auth/guest` 取令牌 → `GET /lobby/rooms/public` 返回 `room-1`（`buildHash=d392d83ced326411`、`address=127.0.0.1:17777`、`lastSeenAgeMs=155`、心跳注册持续 200）——**部署态闭环（登录面 + 实例注册 + 投影）实证**。

## 3. §14 最小满足逐项（设计 §5 要求"测试豁免"显式标注）

| 项 | 状态 |
|---|---|
| 多阶段 Dockerfile | ✅ 实测（sdk → runtime/aspnet） |
| 非 root 用户 / 只读根 / 资源限额 / 健康探针集成 | **测试豁免**（封闭测试最小形态；Room 用 `restart: unless-stopped` 守护，Meta 走 `/live` `/ready`）——不冒充生产达标 |

## 4. 边界与挂账（如实）

- **云侧执行未做**：安全组（`17777/udp` 白名单）+ Meta 的 TLS（Caddy/LB）+ `advertise_host` 改公网——需云主机与域名，本机不可达。
- §6 第 1–3 项随云侧/批C；密钥轮换归 R4 批2；客户端 PSK/服务器地址构建期注入归批C。
- 本机验收密钥为 throwaway（`deploy/.env`，已 gitignore）；容器内中文日志 UTF-8 正常。
- 验收为手动命令序列（已固化进 `deploy/README.md`）；如需夜间复跑另立脚本（不在本批）。

## 5. 过程留痕

- 构建两坑：`# syntax=` 前端镜像拉不动（去行）；mcr 直连超时（daocloud 源 + `docker tag` 回打）——均为本环境网络事项，已写进 `deploy/README.md`。
- "不重"窗口的容器级模拟法：预置 journal 时**抹掉完成标记行**（`Kind:"c"`）重放同一条，等价于"Meta 已收、标记未落"的宕机窗。
- 观测：已完成条目对在后续重启/停止周期后从 journal 消失（压实清理，与"完成标记行合并进完成集"的加载语义一致）——journal 终态 = `[s2 行, c2 标记]`。
