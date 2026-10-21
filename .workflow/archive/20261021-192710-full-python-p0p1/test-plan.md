# 测试计划：全量 Python P0/P1

## 测试策略

- 红先于实现：先写 `_common`、hook、L0 三组契约测试与清单/CI/hook 注册面断言，观察 18 个预期失败，再实现。
- 只标准库：P0/P1 不引入第三方依赖，测试用 `unittest` + `subprocess` + `tempfile`。
- 行为对照：hook 逐条对照 PS 的消息与双客户端 schema；L0 逐项对照 PS 的检查项、排除名单与 0/1 退出码。
- 夹具隔离：L0 的四类违规用临时 git 仓库（personal path / floating git / missing LICENSE / secret）；hook 用 stdin JSON + 环境变量切换客户端。
- 不跑 Unity/L2/SSH/云侧；本次不涉及共享源集/BuildHash。

## 用例清单

### `scripts/tests/test_common.py`

| 用例 | 断言 |
|---|---|
| `test_repo_root_points_at_repository` | `repo_root()` 指向仓库根，含 `AGENTS.md` |
| `test_exit_codes` | 0/1/2/4/5 常量 |
| `test_run_returns_child_exit_code` | `run` 返回子进程退出码 3 |
| `test_output_helpers` | `info` stdout、`warn`/`fail` stderr 前缀与 `fail` 返回 1 |
| `test_configure_stdio_is_safe` | UTF-8 初始化不抛异常 |

### `scripts/tests/test_hook_reminder.py`

| 用例 | 断言 |
|---|---|
| `test_zcode_shared_source_warning` | ZCode schema `additionalContext`，含红线1与 gen-build-hash 命令 |
| `test_claude_serialized_schema` | Claude schema `hookSpecificOutput.hookEventName=PostToolUse` |
| `test_unity_asset_warning` | `.prefab` 触发红线4 |
| `test_non_match_is_silent` | 不相关路径 stdout 为空、exit 0 |
| `test_invalid_json_is_silent` | 坏 JSON stdout 为空、exit 0 |

### `scripts/tests/test_l0_dep_scan.py`

| 用例 | 断言 |
|---|---|
| `test_repo_matches_known_baseline` | 真实仓库 PASS；若 ParrelSync 个人路径复现则只允许该已知项，无其他违规类型 |
| `test_clean_fixture_passes` | `--project-path` clean 临时仓库 exit 0 |
| `test_personal_absolute_path_fails` | `file:C:/...` → exit 1 + personal absolute path |
| `test_floating_git_dependency_fails` | `.git` 未 pin → exit 1 + floating git dep |
| `test_vendored_package_without_license_fails` | tracked vendored 包缺 LICENSE → exit 1 |
| `test_secret_pattern_fails` | tracked 文本硬编码凭据 → exit 1 + possible secret |

### `scripts/tests/test_toolchain.py`（新增结构断言）

- `test_l0_entry_is_python`：清单 command 为 `python l0-dep-scan.py`；旧 PS 不存在；`toolchain.py` fallback、CI、`build-player.ps1` 均指向 `.py`。
- `test_hook_entries_are_python`：`.zcode`/`.claude` 配置均指向 `.py`；旧 PS 不存在。

## 预期失败证据

`workflow.py verify --red`：`toolchain-tests-red` exit=1（约 5.1s），**18 个失败**：

- `_common` 缺失 → 5 个 AssertionError；
- hook `.py` 缺失 → 5 个 AssertionError；
- L0 `.py` 缺失 → 6 个 AssertionError；
- 清单/注册面仍指旧 PS → 2 个断言失败（command 列表不等、hook 配置仍为 `.ps1`）。

失败均为断言，不是导入冒泡/环境错误/超时。

## 运行方式

```powershell
# 红测
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python .agents/skills/staged-delivery/scripts/workflow.py --run full-python-p0p1 verify --red

# 施工后
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/gate/l0-dep-scan.py
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
```
