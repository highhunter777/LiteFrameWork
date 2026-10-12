# deploy/ —— 封闭测试部署制品（上云批B）

《上云测试专项设计》§3 批B 的可执行制品。**红线（设计 §7）**：本部署属封闭测试例外（IP 白名单、不得公开/宣传）；IL2CPP 真机核证与密钥轮换（随 R4）仍挂账，本环境不得作为"可发布"判据。

## 文件

| 文件 | 用途 |
|---|---|
| `compose.yaml` | 全链编排：mongo（单节点副本集 rs0）+ RoomServer + MetaServer (+ Caddy `--profile tls`) |
| `Dockerfile.roomserver` / `Dockerfile.metaserver` | 多阶段镜像（sdk:8.0 构建 → runtime/aspnet:8.0）；构建上下文 = 仓库根（源链接依赖） |
| `roomserver.container.json` | 容器形态房间服配置（挂载到 `/app/Config/roomserver.json`；advertise_host 云上改对外 IP） |
| `Caddyfile` | HTTPS 反代（云 LB 证书形态的替代；需 DOMAIN） |
| `.env.example` | 密钥/参数模板——复制为 `.env` 填值（`.env` 已 gitignore，**严禁入库/回显**） |

## 本机/单机启动

```bash
cd deploy
cp .env.example .env        # 填值（生成随机密钥命令见模板内注释）
docker compose up -d --build
# 验证
curl -s http://127.0.0.1:5000/ready           # → ready
curl -s http://127.0.0.1:9100/metrics | head  # Prometheus 文本
docker compose ps
# 停止（容器 stop 走排空 5 步，退出码 0；数据卷保留）
docker compose down
```

离线/受限网络注记：基础镜像 `mcr.microsoft.com/dotnet/*` 直连不可达时，经 daocloud 镜像源拉取后回打标准标签（`mcr.m.daocloud.io/dotnet/runtime:8.0` → `docker tag … mcr.microsoft.com/dotnet/runtime:8.0`，sdk/aspnet 同理）；构建容器内 NuGet 经 `.env` 的 `DOCKER_BUILD_HTTP_PROXY`（本机 Clash 形态 `http://host.docker.internal:7897`）。Dockerfile 刻意不写 `# syntax=` 行（前端镜像另需拉 Docker Hub，本环境不可达）。

## 云主机部署清单（设计 §5）

1. **编排**：云上直连 `docker compose up -d --build`；或本地构建后经 registry / `docker save|load` 分发。
2. **安全组**：`17777/udp` 仅对白名单 IP 开放；Meta 的 5000/tcp 改绑内网或走 Caddy（`--profile tls` + `.env` 的 `DOMAIN`，DNS A 记录指本机）——§6 第 1 项"白名单外不可达"须在云侧实际操作后记录。
3. **配置**：`roomserver.container.json` 的 `advertise_host` 改为对外 IP（客户端 Join 可达地址）。
4. **密钥**：全部在 `.env`（环境变量注入；严禁命令行明文/写进 compose）。
5. **持久**：`mongo-data` 卷 + 定时 dump（最小备份）；`room-outbox` 卷（结算 journal 重放面）。
6. **验收**：§6 七项对照与已证清单见 [Docs/施工进度/上云批B部署.md](../Docs/施工进度/上云批B部署.md)。
