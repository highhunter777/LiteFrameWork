# 施工记录：全量 Python P3b（nightly/L2/install-task）

> 运行 full-python-p3b · 创建于 2026-10-21T19:50:16+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T19:50:44+08:00 · 新增任务 T1「迁移 L2 Unity 门禁」
- 2026-10-21T19:50:45+08:00 · 新增任务 T2「迁移夜间门禁并保留 PS shim」
- 2026-10-21T19:50:45+08:00 · 新增任务 T3「迁移计划任务安装器」
- 2026-10-21T19:50:50+08:00 · 新增任务 T4「同步 P3b 注册面与文档」
- 2026-10-21T19:50:50+08:00 · 新增任务 T5「全量验证与机器侧验收记录」
- 2026-10-21T19:52:21+08:00 · 开始任务 T1「迁移 L2 Unity 门禁」
- 2026-10-21T19:52:21+08:00 · 完成任务 T1 · 证据: l2-unity-gate.py meta/Pipeline/batchmode/新鲜度/console/Total；3/3 离线；旧 l2.ps1 待 T4 删除
- 2026-10-21T19:52:21+08:00 · 开始任务 T2「迁移夜间门禁并保留 PS shim」
- 2026-10-21T19:52:22+08:00 · 完成任务 T2 · 证据: nightly-gate.py 编排/日志/PlayMode 标注；PS shim 转发；help 1/1
- 2026-10-21T19:52:32+08:00 · 开始任务 T3「迁移计划任务安装器」
- 2026-10-21T19:53:48+08:00 · 完成任务 T3 · 证据: install-nightly-task.py what-if/XML/schtasks；1/1；实际注册成功（C:\Python313\python.exe -X utf8 nightly-gate.py）；l2/install PS 删除
- 2026-10-21T19:53:53+08:00 · 开始任务 T4「同步 P3b 注册面与文档」
- 2026-10-21T19:53:53+08:00 · 完成任务 T4 · 证据: 清单 env.nightly/env.nightly-install/quality.l2/surface runner=python、script=nightly-gate.py；CI L2 改 Python；toolchain doctor/索引/文档/skills 同步；旧 PS 清零
- 2026-10-21T19:53:54+08:00 · 开始任务 T5「全量验证与机器侧验收记录」
- 2026-10-21T19:56:34+08:00 · 完成任务 T5 · 证据: L1 1801；Python 全测；Doc 0/0；restore/L0/L2-meta 通过；toolchain check 0 error/4 warning；BuildHash OK；计划任务回读 python nightly-gate.py；完整 L2/远端 CI 未跑（机器侧）

## 关键决定

- **L2 完整移植、离线只验 meta**：Pipeline/batchmode/新鲜度/console/Total 代码路径按 PS 移植；本机无 Unity 会话，只验证 meta 扫描/帮助/非法 meta，完整 L2 留机器侧。
- **nightly 先 Python + PS shim**：代码落地后计划任务仍指向 PS；实际 `schtasks` 注册成功、回读 Python 后删除 shim。
- **install 用 schtasks XML**：保留 Interactive/Limited/StartWhenAvailable/2h/IgnoreNew；实际注册成功，命令为绝对 Python + nightly-gate.py。
- **toolchain runner 扩展**：manifest 校验从只允许 pwsh/powershell 扩为允许 python；surface 回读绿。
- **test.py L2 只走 Python**：删除 PS fallback，旧 l2 PS 删除后不会静默回退。

## 偏差与处理

- 原计划 install 只做 what-if；实际用户级注册成功（无需管理员），已执行并回读，随后删除 nightly PS shim。机器状态变更如实记录。
- L2 完整 Pipeline/batchmode 未执行（无 Unity 会话），不宣称 L2 全绿；只声明 meta/离线面通过。
- L2 默认 UnityExe 仍沿用旧机器路径并支持 `UNITY_EXE`/`--unity-exe` 覆盖；属既有机器路径债，未在本批重构。
- 首次 verify 因 `test_toolchain` 仍断言旧 PS surface/读 nightly PS 而 1 failure+1 error；更新为 Python 后通过。

## 问题与修复

- **L2 非法 meta 文案**：测试期望“非法 GUID”，实现初版英文；改为中文提示后 3/3。
- **manifest runner 校验**：只允许 pwsh/powershell，`python` surface 被判 error；扩展允许集后 check 0 error。
- **nightly unused import**：删除不再使用的 `shutil`。
- **文档链接误伤**：`Docs/待办总览` 的 `../scripts/gate/test.py` 被全局替换成 `.python ...`，修正为原链接。
- **任务注册**：`schtasks` 实际注册成功；回读 `Command=C:\Python313\python.exe`、`Arguments=-X utf8 "...nightly-gate.py"`。

## 验证结果

- 先行失败：`evidence/red.json`，6 个断言失败（三脚本缺失 + 清单旧 l2 PS）。
- 最终验证：`evidence/verify.json` **9/9 PASS**（L1 1801、Doc、restore、L0、Python 86/86、toolchain check 0 error/4 warning、docs current、Doc lane、L2 meta）。
- 附加：BuildHash `--check-shared` OK；CI YAML/清单 JSON OK；计划任务回读 Python；未跑完整 L2/远端 CI。
- 2026-10-21T19:51:29+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T19:54:57+08:00 · 验证 FAIL · 8/9 · 证据 evidence/verify.json
- 2026-10-21T19:56:07+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json
- 2026-10-21T19:58:16+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json

## 交付摘要

- `l2-unity-gate.py`：meta 扫描 + Pipeline/batchmode EditMode/PlayMode + 新鲜度/console/Total 判据；test.py 只走 Python L2。
- `nightly-gate.py`：L1/L2 编排、UTF-8 日志、PlayMode 标注、退出码；计划任务已从 PS 切到 Python 并回读，PS shim 删除。
- `install-nightly-task.py`：读 manifest surface、what-if、schtasks XML 注册；实际注册成功。
- 清单 `quality.l2`/`env.nightly`/`env.nightly-install`/surface runner+script、CI L2、toolchain doctor、工具索引/文档/skills 全切 Python。
- Python 86/86、L1 1801、L2 meta、Doc 0/0、toolchain check；无 Assets/Server 改动。

## 未决事项

- **完整 L2 未跑**：需 Unity 编辑器/Pipeline 或 batchmode；本批只验证 meta/离线面。
- **远端 CI L2 未跑**：CI 命令已改 Python，首次自托管 runner 需观察。
- P4/P5 未做：build/smoke、deploy/local-chain 仍 PS；`check` 仍有 4 条未触碰旧 PS 的编码/EOL 告警。
- 计划任务使用绝对 `C:\Python313\python.exe`；Python 升级/换机需重跑 installer。
- 未提交：本轮改动在工作区；`.workflow/` 本地记录不入库。

## 文件清单

- 新增：`scripts/gate/l2-unity-gate.py`、`nightly-gate.py`、`install-nightly-task.py`、`scripts/tests/test_l2_entry.py`、`test_nightly_entry.py`、`test_install_nightly.py`。
- 删除：`l2-unity-gate.ps1`、`install-nightly-task.ps1`、`nightly-gate.ps1`（注册成功后 shim 删除）。
- 修改：`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/_toolchain_manifest.py`、`scripts/gate/test.py`、`scripts/gate/nightly-gate.py`、`.github/workflows/ci.yml`、`scripts/tests/test_toolchain.py`、`scripts/README.md`、`AGENTS.md`、`Docs/**` 现行引用/skills、`Docs/工具链/工具索引.md`、`Docs/施工进度/资源与工具链线.md`（第16章）。
- 机器侧：`LiteGame-Nightly-Gate` 已重注册为 `C:\Python313\python.exe -X utf8 scripts/gate/nightly-gate.py`。

## 后续建议

- 在 Unity 编辑器/Pipeline 可用时跑一次完整 `python scripts/gate/l2-unity-gate.py`（EditMode+PlayMode），确认 batchmode/Pipeline 与 PS 等价。
- 下一运行 **P4**：build-player/player-smoke；再做 P5 deploy/local-chain 并清零 `.ps1`。
- 提交前按工具链线拆分；本轮未提交。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T19:50:16+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T19:50:39+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T19:50:55+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T19:51:08+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T19:51:56+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T19:57:13+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T19:58:16+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T19:58:16+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T19:58:20+08:00 · 完成「回写与收尾」→ 完成
