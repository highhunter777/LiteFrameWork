# 施工记录：全量 Python P5（deploy/local-chain + 清零 PS）

> 运行 full-python-p5 · 创建于 2026-10-21T20:05:55+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T20:06:14+08:00 · 新增任务 T1「迁移 SSH 目的地解析」
- 2026-10-21T20:06:15+08:00 · 新增任务 T2「迁移房间服部署」
- 2026-10-21T20:06:15+08:00 · 新增任务 T3「迁移云端 GM 调用」
- 2026-10-21T20:06:15+08:00 · 新增任务 T4「迁移测试者隧道」
- 2026-10-21T20:06:21+08:00 · 新增任务 T5「迁移本地联测一键链」
- 2026-10-21T20:06:21+08:00 · 新增任务 T6「清零 PS 并同步注册面」
- 2026-10-21T20:06:21+08:00 · 新增任务 T7「全量验证与最终报告」
- 2026-10-21T20:07:50+08:00 · 开始任务 T1「迁移 SSH 目的地解析」
- 2026-10-21T20:08:23+08:00 · 开始任务 T5「迁移本地联测一键链」
- 2026-10-21T20:11:14+08:00 · 完成任务 T1 · 证据: _ssh_host.py env/TCP/ssh -G；2/2；旧 PS 删除
- 2026-10-21T20:11:14+08:00 · 完成任务 T5 · 证据: local-chain.py init/meta/room/up/status/keys；2/2 离线；旧 PS 删除
- 2026-10-21T20:11:19+08:00 · 开始任务 T2「迁移房间服部署」
- 2026-10-21T20:11:19+08:00 · 完成任务 T2 · 证据: deploy-room.py 完整移植；2/2 离线；旧 PS 删除
- 2026-10-21T20:11:19+08:00 · 开始任务 T3「迁移云端 GM 调用」
- 2026-10-21T20:11:20+08:00 · 完成任务 T3 · 证据: gm.py 六动作/隧道/HTTP；3/3 离线；旧 PS 删除
- 2026-10-21T20:11:20+08:00 · 开始任务 T4「迁移测试者隧道」
- 2026-10-21T20:11:20+08:00 · 完成任务 T4 · 证据: wg-peer.py 远端 payload/解析；3/3 离线；旧 PS 删除
- 2026-10-21T20:11:20+08:00 · 开始任务 T6「清零 PS 并同步注册面」
- 2026-10-21T20:11:20+08:00 · 完成任务 T6 · 证据: scripts/**/*.ps1=0；checker 禁止 PS；清单/文档/索引全 Python；check 0 warning
- 2026-10-21T20:11:20+08:00 · 开始任务 T7「全量验证与最终报告」
- 2026-10-21T20:12:28+08:00 · 完成任务 T7 · 证据: PS=0；Python 105/105；L1 1801；Doc 0/0；L2 meta；restore/L0；toolchain check 0 error/0 warning；BuildHash OK；未执行 SSH/云侧/起服；无 Assets/Server

## 关键决定

- **本地全 Python、远端原生 shell**：deploy-room/wg-peer 的远端 Windows payload 保留 PowerShell here-string（远端不是仓库脚本）；本机全部 Python，仓库零 `.ps1`。
- **SSH 共用**：`_ssh_host.py` 环境变量→TCP 800ms→`ssh -G` 别名；deploy/gm/wg 共用。
- **密钥安全**：gm key 仅 env/LocalNotes 解析，不打印；wg 私钥只落 `.conf`、不回显。
- **local-chain**：.env/deploy.args/appsettings/进程/端口/Mongo/hash 移植；常驻进程用 `cmd /k` + `CREATE_NEW_CONSOLE` 替代 PS Start-Process。
- **清零**：删除五个 PS；`toolchain.py check` 对任何 `scripts/**/*.ps1` 报 error；测试覆盖。

## 偏差与处理

- 未执行真实 SSH/云侧/本地起服：只做离线 help/参数/纯函数；部署与联测机器侧待办。
- PS 禁止规则使原“PS 编码/EOL 正反例”测试失效：改为断言 PS 被拒绝；hook surface 夹具改 Python；`test_toolchain_contracts` 重写为 Python help/usage。
- `deploy-room.py` 先校验 `--skip-publish` 产物再解析 SSH，避免离线测试触发网络；真实路径顺序不影响结果。
- local-chain 帮助增加 Commands epilog；常驻窗口语义由机器侧人工验收。

## 问题与修复

- **PS ban 与旧测试冲突**：更新编码测试/夹具，105/105。
- **旧契约测试仍用 pwsh/Get-Help**：重写为 Python help/usage exit2 断言。
- **local-chain 帮助缺命令列表**：增加 epilog。
- **远端 payload**：保留为字符串常量，开发机不落 `.ps1`；已在报告中标注。

## 验证结果

- 先行失败：13 个断言失败（五脚本缺失/旧清单/PS ban 未实现），无环境错误。
- 最终验证：`evidence/verify.json` 9/9 PASS（L1 1801、Doc、restore、L0、Python 105/105、toolchain check 0 error/0 warning、docs current、Doc lane、L2 meta）。
- 附加：`scripts/**/*.ps1 = 0`；BuildHash `--check-shared` OK；未执行 SSH/云侧/起服；无 Assets/Server 改动。
- 2026-10-21T20:07:42+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T20:12:17+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json
- 2026-10-21T20:13:52+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json

## 交付摘要

- `_ssh_host.py`、`deploy-room.py`、`gm.py`、`wg-peer.py`、`local-chain.py` 全部落地；五个 PS 删除。
- `scripts/**/*.ps1` 归零；checker 新增 PS 禁止规则；清单/AGENTS/文档/skills/工具索引全 Python。
- Python 105/105、L1 1801、Doc 0/0、check 0 warning、BuildHash OK；未执行真实部署/联测。

## 未决事项

- **真实部署/云侧/本地联测未跑**：需云主机/SSH 密钥/本机 MongoDB；本批只离线验证。
- 远端 Windows payload 仍为 PowerShell（在 Python 字符串内），不是仓库 `.ps1`；云主机若装 Python 可再迁远端。
- local-chain `cmd /k` 窗口与 PS Start-Process 的窗口标题/编码观感需人工验收。
- 未提交：全部 P0–P5 改动在工作区；`.workflow/` 本地记录不入库。

## 文件清单

- 新增：`scripts/deploy/_ssh_host.py`、`deploy-room.py`、`gm.py`、`wg-peer.py`、`scripts/local/local-chain.py`、对应 5 组测试。
- 删除：`_ssh-host.ps1`、`deploy-room.ps1`、`gm.ps1`、`wg-peer.ps1`、`local-chain.ps1`。
- 修改：`scripts/gate/toolchain.json`、`scripts/_toolchain_scripts.py`、`scripts/tests/test_toolchain*.py`、`scripts/README.md`、`AGENTS.md`、Docs/工具链/云侧速查/项目说明 07/09/10/11/12/13、设计文档相关引用、`.agents/skills/litegame-online-test-release`、`Docs/施工进度/资源与工具链线.md`（第18章）。

## 后续建议

- 机器侧按 11-联机测试流程跑一次 `python scripts/deploy/deploy-room.py` / `gm.py --action rooms` / `wg-peer.py --name <tester>` / `local-chain.py up/status`。
- 全量 Python 迁移完成；后续只维护 Python，`toolchain.py check` 会拒绝新 `.ps1`。
- 提交前按工具链线拆分 P0–P5；本轮未提交。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T20:05:55+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T20:06:10+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T20:06:33+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T20:07:43+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T20:12:58+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T20:13:53+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T20:13:53+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T20:13:56+08:00 · 完成「回写与收尾」→ 完成
