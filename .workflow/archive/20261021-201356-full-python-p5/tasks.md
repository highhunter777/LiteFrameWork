# 实施任务清单：全量 Python P5（deploy/local-chain + 清零 PS）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 迁移 SSH 目的地解析 | - | 完成 | _ssh_host.py 环境变量→TCP探测→ssh -G 别名；显式 env 不联网；输出/回退语义与 PS 一致；旧 PS 删除 | python -m unittest scripts.tests.test_ssh_host -v | _ssh_host.py env/TCP/ssh -G；2/2；旧 PS 删除 |
| T2 | 迁移房间服部署 | T1 | 完成 | deploy-room.py 保留 publish/tar/停服/上传/解压/起服/表同步/回读；--help/参数；不执行真实 SSH；旧 PS 删除 | python -m unittest scripts.tests.test_deploy_room -v | deploy-room.py 完整移植；2/2 离线；旧 PS 删除 |
| T3 | 迁移云端 GM 调用 | T1 | 完成 | gm.py 六动作/参数/密钥来源/隧道/HTTP/关留与 PS 一致；未知 action/缺 Room/缺 Player exit2；不真实调用；旧 PS 删除 | python -m unittest scripts.tests.test_gm_entry -v | gm.py 六动作/隧道/HTTP；3/3 离线；旧 PS 删除 |
| T4 | 迁移测试者隧道 | T1 | 完成 | wg-peer.py Name/Ip/Endpoint/DryRun/OutDir；远端 payload/解析/私钥不回显；缺 Name/非法 Name exit2；不真实 SSH；旧 PS 删除 | python -m unittest scripts.tests.test_wg_peer -v | wg-peer.py 远端 payload/解析；3/3 离线；旧 PS 删除 |
| T5 | 迁移本地联测一键链 | - | 完成 | local-chain.py init/meta/room/up/status/keys；.env/deploy.args/appsettings/进程/端口/Mongo/hash；未知命令 exit2；不启动真实服务；旧 PS 删除 | python -m unittest scripts.tests.test_local_chain -v | local-chain.py init/meta/room/up/status/keys；2/2 离线；旧 PS 删除 |
| T6 | 清零 PS 并同步注册面 | T1, T2, T3, T4, T5 | 完成 | 删除五个 PS；清单 deploy/env.local-chain Python；_toolchain_scripts 对任何 scripts/**/*.ps1 报 error；工具索引/AGENTS/文档/skills 全 Python；旧引用清零 | python -m unittest scripts.tests.test_toolchain_scripts -v; python scripts/toolchain.py check; python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py | scripts/**/*.ps1=0；checker 禁止 PS；清单/文档/索引全 Python；check 0 warning |
| T7 | 全量验证与最终报告 | T1, T2, T3, T4, T5, T6 | 完成 | PS 计数 0；Python 全测；L1 1801；Doc 0/0；L2 meta；toolchain check 0 error/0 warning；BuildHash；未执行 SSH/云侧；详细报告 | python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/gate/test.py --lane L1 --profile PullRequest && python scripts/gate/test.py --lane Doc && python scripts/gate/l2-unity-gate.py --meta-scan-only && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py | PS=0；Python 105/105；L1 1801；Doc 0/0；L2 meta；restore/L0；toolchain check 0 error/0 warning；BuildHash OK；未执行 SSH/云侧/起服；无 A… |

## 任务详情

### T1 · 迁移 SSH 目的地解析

- 文件: scripts/deploy/_ssh_host.py, scripts/deploy/_ssh-host.ps1, scripts/tests/test_ssh_host.py

### T2 · 迁移房间服部署

- 文件: scripts/deploy/deploy-room.py, scripts/deploy/deploy-room.ps1, scripts/tests/test_deploy_room.py

### T3 · 迁移云端 GM 调用

- 文件: scripts/deploy/gm.py, scripts/deploy/gm.ps1, scripts/tests/test_gm_entry.py

### T4 · 迁移测试者隧道

- 文件: scripts/deploy/wg-peer.py, scripts/deploy/wg-peer.ps1, scripts/tests/test_wg_peer.py

### T5 · 迁移本地联测一键链

- 文件: scripts/local/local-chain.py, scripts/local/local-chain.ps1, scripts/tests/test_local_chain.py

### T6 · 清零 PS 并同步注册面

- 文件: scripts/gate/toolchain.json, scripts/toolchain.py, scripts/_toolchain_scripts.py, scripts/tests/test_toolchain_scripts.py, scripts/README.md, AGENTS.md, Docs/工具链/Docs/开发导航/Docs/项目说明/Docs/设计文档/.agents

### T7 · 全量验证与最终报告

- 文件: scripts, Docs, AGENTS.md, .github

