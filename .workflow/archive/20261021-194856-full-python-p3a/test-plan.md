# 测试计划：全量 Python P3a（test 主入口）

## 测试策略

- 先写 test.py 的 4 个契约（help/非法 lane/Doc/发现守卫）+ 清单/CI/nightly 结构断言，观察 5 个失败。
- 真跑 `test.py --lane L1` 作为主验收（1801）；Doc lane 同为实际执行；不跑 L3/L2。
- 发现守卫用临时 `Tests/Bad.csproj`，在 dotnet 前失败，避免误跑。
- 环境变量恢复由 T3 的全测/重复执行间接验证；不引入额外框架。

## 用例清单

### `test_test_entry.py`
- `--help` exit 0，含 `--lane/--profile`；
- `--lane BOGUS` exit 2；
- `--lane Doc` exit 0，输出 doc-lint；
- 临时 Tests 只有 `Bad.csproj` → exit 1 + “discovery contract”。

### `test_toolchain.py`
- `quality.l1/l3/doc` command 为 `python test.py --lane ... --profile ...`；
- `test.py` 存在、`test.ps1` 不存在；
- CI 引 test.py、不含 test.ps1；
- `toolchain.py` 引 test.py、不含 test.ps1；
- `nightly-gate.ps1` L1 调用 test.py、不含 test.ps1。

## 预期失败证据

`verify --red`：`toolchain-tests-red` exit=1（约 10s），**5 个断言失败**（test.py 缺失 4 + 清单仍指旧 test.ps1 1）。无环境错误。

## 运行方式

```powershell
python -m unittest scripts.tests.test_test_entry scripts.tests.test_toolchain.ManifestContractTests.test_test_entry_is_python -v
python .agents/skills/staged-delivery/scripts/workflow.py --run full-python-p3a verify --red

# 施工后
python scripts/gate/test.py --lane L1 --profile PullRequest
python scripts/gate/test.py --lane Doc
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
```
