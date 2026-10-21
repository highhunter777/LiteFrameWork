# 代码评审：全量 Python P3b

> 运行 full-python-p3b · 创建于 2026-10-21

## 变更范围

- 新增 l2/nightly/install 三个 Python 入口与测试；删除对应 PS（nightly shim 在任务重注册后删除）。
- 清单/CI/toolchain/tool index/文档/skills 切 Python；test.py L2 只走 Python。
- 机器侧：计划任务已重注册并回读 Python。

## 正确性与边界

- **L2**：meta 扫描/非法 meta、Pipeline 的 autotick/eval_file/recompile/console/异步跑测、batchmode EditMode+PlayMode、Total=0/console error/新鲜度判据均按 PS 移植；离线验证 meta/help。
- **nightly**：L1 调 test.py、L2 调 Python、UTF-8 日志、PlayMode 标记与退出码；PS shim 仅过渡，注册成功后删除。
- **install**：manifest surface 读值；what-if 不写；schtasks XML 保留 Interactive/Limited/2h/IgnoreNew；实际注册回读 Python。
- **注册面**：清单 runner 扩展 python；surface 回读绿；CI L2 改 Python；test.py 无 PS fallback。
- **边界**：完整 L2（Unity Pipeline/batchmode）与远端 CI 未跑；本批不宣称 L2 全绿。

## 测试充分性

- 红测 6；最终 Python 86/86。
- L2 离线 3/3（repo/非法 meta/help）；nightly help；install what-if；结构断言覆盖清单/surface/CI/test.py。
- 未覆盖：Pipeline/batchmode 实跑、计划任务实际触发夜间脚本；已登记机器侧待办。

## 通用性与扩展性

- 复用 `_common`；Unity 命令集中 L2；nightly/install 从 manifest/docs 取值，不硬编码任务名/脚本。
- install XML 可复用；L2 的 Pipeline 辅助可被 P4 构建复用。
- 未新增通用调度框架；L2 默认 UnityExe 仍为旧机器路径，属既有债。

## 安全与性能

- 计划任务以当前用户 Interactive/Limited 注册；不提升权限；日志 UTF-8。
- L2 对 Assets/Packages 做文件扫描；Pipeline 测试异步轮询；batchmode 超时 1800s。
- 任务 action 使用绝对 Python 路径；换解释器需重跑 installer（已登记）。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 信息 | L2 | 完整 Pipeline/batchmode 未实跑 | 本机无 Unity 会话；只验 meta/离线，机器侧验收待办 | 遗留（非代码阻塞） |
| C-2 | 信息 | 计划任务 | action 固定绝对 Python 路径 | 保留可重跑 installer；换机/升级需重注册 | 接受 |
| C-3 | 信息 | L2 | 默认 UnityExe 仍是旧机器路径 | 支持 `UNITY_EXE`/`--unity-exe`；登记后续债 | 接受 |
| C-4 | 次要 | manifest 校验 | 只允许 pwsh/powershell runner，python surface 失败 | 扩展允许 python；check 复绿 | 已修复 |
| C-5 | 次要 | 测试/surface | 旧测试仍断言 nightly PS；nightly PS 已删 | 更新为 Python surface，86/86 | 已修复 |
| C-6 | 信息 | 计划范围 | 原计划 install 只 what-if，实际执行了注册 | 用户“继续 P3b”授权；回读验证，shim 删除，机器状态已记录 | 已说明 |

无阻塞代码问题；机器侧完整 L2 是明确待办。

## 复测记录

- `workflow.py verify` → 9/9 PASS（L1 1801、Doc、restore、L0、Python 86/86、check 0 error/4 warning、docs current、Doc lane、L2 meta）。
- 附加：BuildHash OK；CI YAML/清单 JSON OK；schtasks 回读 Python；完整 L2/远端 CI 未跑。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
