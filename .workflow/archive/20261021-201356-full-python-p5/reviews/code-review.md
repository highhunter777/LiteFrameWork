# 代码评审：全量 Python P5

> 运行 full-python-p5 · 创建于 2026-10-21

## 变更范围

- 新增 `_ssh_host.py`、`deploy-room.py`、`gm.py`、`wg-peer.py`、`local-chain.py` 与 5 组离线测试。
- 删除五个 PS；`scripts/**/*.ps1 = 0`；checker 禁止新 PS。
- 清单/AGENTS/文档/skills/工具索引全 Python；无 Assets/Server 改动。

## 正确性与边界

- SSH host：env→TCP 800ms→`ssh -G` 别名；失败回退 public；无密钥读取。
- deploy-room：publish/tar/停服/上传/解压/起服/表同步/回读按 PS；先校验产物再解析 SSH（离线可测）。
- gm：参数校验在 key/SSH 前；六动作/body/隧道/HTTP/关留；key 不回显。
- wg-peer：Name 校验在 SSH/Endpoint 前；远端 payload/scp/ssh/解析；私钥只落 `.conf`。
- local-chain：.env/deploy.args/appsettings/进程/端口/Mongo/hash/脱敏 keys；unknown exit2。
- 真实 SSH/云侧/起服未执行；不宣称部署通过。

## 测试充分性

- 红测 13；最终 Python 105/105。
- 覆盖 help/usage/未知 action/缺 Room/缺 Name/缺产物/SSH 回退/PS ban/zero PS。
- 未覆盖：真实云主机、密钥文件、WireGuard、MongoDB、常驻窗口；机器侧待办。

## 通用性与扩展性

- `_common`/`_ssh_host` 复用；远端 payload 集中常量；local-chain 配置注入单处。
- 未抽通用 SSH/任务框架；不硬编码云 IP/密钥（SSH host 解析可覆盖）。
- 远端 PS 仅作为云主机原生 shell payload，不落仓库 `.ps1`；后续可再迁。

## 安全与性能

- 密钥来源 env/LocalNotes，私钥不回显；scp/ssh 子进程；gm HTTP 带 key 头。
- 停服/重装隧道仍为破坏性动作，脚本头/文档保留提示；未执行。
- 本地 status 只做 TCP/HTTP/进程/端口探测，不阻塞。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 信息 | deploy-room/wg-peer | 远端 payload 仍是 PowerShell | 云主机原生 shell；仓库零 `.ps1`；已记录 | 接受 |
| C-2 | 高 | deploy/gm/wg/local | 真实云侧/本地联测未跑 | 本批只离线；机器侧验收明确待办 | 遗留（非代码阻塞） |
| C-3 | 中 | local-chain | `cmd /k` 窗口与 PS Start-Process 观感可能不同 | 机器侧人工验收；失败可回退 | 遗留 |
| C-4 | 次要 | 测试 | PS ban 使旧 PS 编码/契约测试失效 | 编码用例改断言 PS 拒绝；契约测试改 Python help/usage；105/105 | 已修复 |
| C-5 | 次要 | local-chain | `--help` 缺命令列表 | 加 Commands epilog | 已修复 |

无阻塞代码问题；真实部署/联测是明确机器侧待办。

## 复测记录

- `workflow.py verify` → 9/9 PASS（L1 1801、Doc、restore、L0、Python 105/105、check 0 error/0 warning、docs current、Doc lane、L2 meta）。
- 附加：PS 计数 0；BuildHash OK；未执行 SSH/云侧/起服。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
