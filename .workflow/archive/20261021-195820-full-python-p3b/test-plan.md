# 测试计划：全量 Python P3b

## 测试策略

- 红测覆盖可离线面：L2 help/meta 扫描/非法 meta；nightly help；install what-if；注册面结构断言。
- L2 的 Pipeline/batchmode 与计划任务实际注册依赖 Unity/机器，测试只覆盖代码路径存在与 what-if，不宣称完整 L2 通过。
- 全量基线：Python 全测、L1、Doc、toolchain check/docs、BuildHash；不跑 batchmode/云侧。

## 用例清单

### `test_l2_entry.py`
- `--help` 含 `--meta-scan-only`；
- repo `--meta-scan-only` exit 0 + `L2(meta)`；
- 临时非法 `.meta` exit 1 + 非法 GUID。

### `test_nightly_entry.py`
- `--help` 含 `--skip-l1`。

### `test_install_nightly.py`
- `--what-if` exit 0，输出 `nightly-gate.py`、python、WhatIf；不注册任务。

### `test_toolchain.py`
- quality.l2/env.nightly/env.nightly-install command Python；
- l2/install PS 不存在；nightly shim 允许存在；
- surface runner=python/script=nightly-gate.py；
- CI L2 用 Python；test.py 优先 Python L2。

## 预期失败证据

`verify --red`：6 个断言失败（三个脚本缺失 + 清单仍指旧 l2 PS），无环境错误。

## 运行方式

```powershell
python -m unittest scripts.tests.test_l2_entry scripts.tests.test_nightly_entry scripts.tests.test_install_nightly -v
python scripts/gate/l2-unity-gate.py --meta-scan-only
python scripts/gate/install-nightly-task.py --what-if
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/gate/test.py --lane L1 --profile PullRequest
python scripts/gate/test.py --lane Doc
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
```

完整 L2（Pipeline/batchmode）与计划任务重注册为机器侧验收，不在本次离线测试内。
