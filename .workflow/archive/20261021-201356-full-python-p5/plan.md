# 方案：全量 Python P5（deploy/local-chain + 清零 PS）

> 运行 full-python-p5 · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6（P5）

## 背景与目标

- P0–P4 已完成，`scripts/` 只剩 deploy/local-chain 的 5 个 PS（含 `_ssh-host.ps1`）。
- P5 把它们迁 Python，删除全部 PS，达成 `scripts/**/*.ps1 = 0`；`toolchain.py check` 对任何新增 PS 报错。
- 云侧/本地联测脚本只做离线 help/参数验证，不执行 SSH/云侧/进程启动。

## 需求澄清与假设

- 已确认：全量 Python；P5 为最后一批；最终零 `.ps1`。
- 假设：云主机仍需 PowerShell 远端执行（deploy-room/wg-peer 的远端 here-string 保留为远端原生 shell，不落仓库 `.ps1`）；本机 Python 用 subprocess/urllib 编排；local-chain 的 dotnet 常驻窗口用本机进程宿主替代 PS Start-Process。
- 影响：完整部署/本地联测未跑，机器侧验收待办。

## 范围与非目标

- 新增 `_ssh_host.py`、`deploy-room.py`、`gm.py`、`wg-peer.py`、`local-chain.py`。
- 删除五个 `.ps1`；兼容壳不留（P5 清零）。
- 清单 deploy.room/gm/wg-peer、env.local-chain；工具索引；AGENTS/scripts README/云侧速查/10/11/13/技术支持/代码地图/待办/skills。
- `_toolchain_scripts.py` 增加“禁止 `.ps1`”检查 + 测试。
- 非目标：不改 SSH/WireGuard/GM 协议、密钥来源、破坏性语义；不真实部署/起服；不提交。

## 现状与约束

- `_ssh-host.ps1`：环境变量/隧道 TCP 探测/ssh -G 别名校验。
- deploy-room：dotnet publish、tar、scp/ssh 停服/上传/解压/起服、表同步、回读判据；远端 PS here-string。
- gm：读 key（env/LocalNotes）、SSH 隧道、HTTP POST 六动作、关/留隧道。
- wg-peer：远端 PS here-string、scp/ssh、解析 .conf、私钥不回显。
- local-chain：init/.env/deploy.args、meta/room/up/status/keys；dotnet 常驻窗口、appsettings 注入、status 进程/端口/Mongo/hash。
- 约束：密钥不入库/不回显；停服/重装隧道为破坏性；无云侧验证；Windows 主机。

## 候选方案与取舍

| 方案 | 思路 | 取舍 |
|---|---|---|
| A 全 Python 本地 + 远端 PS payload（采用） | 本机脚本全 Python；远端 Windows 仍用 PS here-string | 仓库零 `.ps1`；远端原生 shell 保留，风险最低 |
| B 远端也改 Python | 云上需 Python 运行远端脚本 | 云主机 Python 可用性未验证；风险高 |
| C 保留 deploy PS | 不迁 deploy | 与清零目标不符 |

## 设计要点

- `_ssh_host.py`：`resolve_ssh_host()` 环境变量 → TCP 800ms 探测 → `ssh -G` 别名校验；输出提示；不读密钥。
- `deploy-room.py`：argparse 四开关；dotnet publish/tar/scp/ssh 子进程；远端 stop/start PS payload 写临时 ASCII；保留表同步/回读。
- `gm.py`：argparse 六动作/参数；key 解析（env/LocalNotes 代码块）；`ssh -N -L` Popen；等待本地端口；urllib POST（带 `X-GM-Key`/Content-Length）；关闭/保留隧道。
- `wg-peer.py`：argparse Name/Ip/Endpoint/DryRun/SshHost/OutDir；远端 PS payload；解析 BEGIN_CONF/ADDR；写 `.conf`；私钥不回显。
- `local-chain.py`：argparse 子命令/`--force`；.env 读写、base64 key、deploy.args、appsettings 注入；dotnet 常驻进程用 `CREATE_NEW_CONSOLE`/`cmd /k`；status 用 tasklist/netstat/HTTP/Mongo TCP/hash。
- `_toolchain_scripts.py`：P5 后任何 `scripts/**/*.ps1` 报 error（迁移窗口结束）。

## 通用性与扩展性

- 复用 `_common`；SSH host 解析单处；deploy 脚本共享 `_ssh_host.py`。
- 远端 payload 作为常量保留在 Python 内，不落仓库 PS；后续云主机若装 Python 可再迁远端。
- 不抽通用 SSH 框架；不硬编码云 IP/密钥。

## 测试策略

- 红测：五脚本缺失/旧清单；`_toolchain_scripts` 无 PS 禁止规则。
- 实现后离线：`--help`；gm 未知 action/缺 Room/缺 Player exit2；wg 缺 Name/非法 Name exit2；deploy-room `--help`；local-chain 未知命令 exit2；`_ssh_host` 显式 env 返回不联网。
- 结构：清单五 command Python；旧 PS 全部不存在；`scripts/**/*.ps1` 计数 0；check 无 PS 错误。
- 基线：Python 全测、L1、Doc、L2 meta、toolchain check/docs、BuildHash；不跑 SSH/云侧/本地联测。

## 验收标准

- [ ] 五 Python 入口行为移植；离线 help/参数/exit2 通过。
- [ ] 清单/文档/skills 全 Python；五个 PS 删除；`scripts/**/*.ps1 = 0`。
- [ ] `_toolchain_scripts` 对 PS 报错；测试覆盖。
- [ ] Python 全测、L1 1801、Doc 0/0、toolchain check（理想 0 warning）、BuildHash。
- [ ] 未执行 SSH/云侧/起服；未改 Unity/共享源集；未提交。

## 风险与回滚

- SSH/云侧未经实跑 → 保留旧 PS 内容于 git 历史可回退；不宣称部署通过。
- local-chain 窗口/进程差异 → 机器侧人工验收；失败回退。
- 密钥/地址泄露 → 不打印私钥；只输出脱敏；测试不读 LocalNotes。
- 回滚：恢复五个 PS 与清单/文档命令；无数据迁移。
