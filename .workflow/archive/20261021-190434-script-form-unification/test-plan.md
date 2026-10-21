# 测试计划：脚本形态统一

## 测试策略

- **红先于实现**：先落三组契约测试，观察“缺清单条目 / 缺检查器 / 退出码不符 / 缺 `.gitattributes`”四类预期失败，再实现。
- **静态为主**：脚本形态是工程约定，不依赖 Unity；用 Python 单测、子进程 CLI、`Get-Help`、文件字节检查覆盖。云侧 deploy 脚本只验证帮助/参数校验，不执行部署主体。
- **正反例夹具**：孤儿检查与编码检查用临时目录构造“未登记/`_` 库/tests/`@oneshot`/被引用/hook surface”五类文件和 BOM/非 ASCII PS/CRLF 三类编码样本。
- **仓库级收口**：真实仓库跑 `toolchain.py check`（非 strict，允许存量未触碰文件的告警）、`docs --check`、Doc lane、L1；strict 行为只在临时夹具上验证，避免把“按触碰归一”误判为全仓必须一次清零。
- **不改业务**：测试不启动 Unity、不跑 L2、不发起 SSH/云侧调用；deploy 脚本的缺失参数测试利用参数绑定失败路径，不进入脚本主体。

## 用例清单

### `scripts/tests/test_toolchain.py`（清单/结构）

| 用例 | 断言 |
|---|---|
| `test_user_facing_scripts_registered` | `deploy.room` / `deploy.gm` / `deploy.wg-peer` / `env.local-chain` / `quality.doc-lint` / `quality.scan-missing-guids` 存在，command 指向真实脚本，docs 存在 |
| `test_deploy_category_declared` | `categories` 含 `deploy` |
| `test_internal_toolchain_modules_use_underscore_prefix` | `_toolchain_*.py` 存在，旧名不存在 |
| `test_local_cdn_loader_is_internal` | `_local-cdn.ps1` 存在，`local-cdn.config.ps1` 不存在 |
| `test_pipeline_patch_is_marked_oneshot` | `patch_p1_plugin_metas.py` 前 20 行含 `@oneshot` |
| `test_gitattributes_declares_script_eol` | `.gitattributes` 存在，含 `scripts/**` 与 `eol=lf` |

### `scripts/tests/test_toolchain_scripts.py`（检查器）

| 用例 | 断言 |
|---|---|
| `test_unregistered_executable_is_warning` | 未登记可执行脚本返回 warning，消息含脚本路径 |
| `test_strict_upgrades_orphan_to_error` | strict 模式把同项升级为 error |
| `test_internal_library_is_exempt` | `_lib.py` 不告警 |
| `test_tests_directory_is_exempt` | `scripts/tests/*` 不告警 |
| `test_oneshot_marker_is_exempt` | 前 20 行 `@oneshot` 不告警 |
| `test_registered_entry_is_exempt` | 清单 command 指向的脚本不告警 |
| `test_leaf_referenced_by_registered_entry_is_exempt` | 被登记入口脚本文本引用的叶子脚本不告警 |
| `test_hook_surface_reference_is_exempt` | hook surface 配置引用的脚本不告警 |
| `test_global_entry_is_exempt` | `scripts/toolchain.py` 不告警 |
| `test_python_bom_is_reported` | UTF-8 BOM Python 报错/告警 |
| `test_powershell_non_ascii_without_bom_is_reported` | 无 BOM 含中文 PS 报错/告警 |
| `test_crlf_is_reported` | CRLF 文件报错/告警 |
| `test_compliant_files_are_clean` | LF + UTF-8 无 BOM 文件零告警 |
| `test_repo_has_no_script_form_errors` | 真实仓库非 strict 检查无 error（存量告警允许） |

### `scripts/tests/test_toolchain_contracts.py`（新入口契约）

| 用例 | 断言 |
|---|---|
| `test_unknown_command_is_usage_error` | `local-chain.ps1 no-such-command` 退出 2，输出含命令列表 |
| `test_help_lists_commands` | `Get-Help local-chain.ps1` 含 init/meta/room/up/status/keys |
| `test_help_has_synopsis` | deploy 三件 `Get-Help` 含 synopsis |
| `test_gm_missing_action_is_usage_error` | `gm.ps1` 无参数退出 2（非交互） |
| `test_wg_peer_missing_name_is_usage_error` | `wg-peer.ps1` 无参数退出 2（非交互） |
| `test_doc_lint_help` | `--help` 退出 0，含 usage |
| `test_scan_missing_guids_help` | `--help` 退出 0，含 usage，不执行扫描 |

## 预期失败证据

`workflow.py verify --red` 记录 `evidence/red.json`：`toolchain-tests-red` exit=1（约 10.5s），共 **26 个失败**，均为断言失败，覆盖：

- 清单缺 `deploy` 分类与六条登记；
- `_toolchain_scripts.py` 不存在（检查器用例全部断言失败，而非导入错误冒泡）；
- `.gitattributes` 缺失；
- `toolchain_*` 未改名、`local-cdn.config.ps1` 未改名、`@oneshot` 未标注；
- `local-chain` 未知命令退出 1（期望 2），`gm`/`wg-peer` 缺参退出 1（期望 2）；
- `local-chain` 无 comment-based help；
- `scan_missing_guids.py --help` 未提供 usage（当前直接执行扫描）。

没有环境故障、ImportError 冒泡或测试超时。

## 运行方式

```powershell
# 先行失败
python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
python .agents/skills/staged-delivery/scripts/workflow.py --run script-form-unification verify --red

# 施工后
python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
```

云侧脚本验证只走 `Get-Help` 与缺参路径；不执行 `deploy-room` / `gm` / `wg-peer` 的主体。
