# 测试计划：全量 Python P4

## 测试策略

- 红测：build/smoke 缺失 + 清单旧命令，共 5 断言失败。
- 实现后离线：两脚本 `--help`；build 缺 profile exit 1；smoke 缺 exe exit 1；py_compile。
- 完整 IL2CPP 构建/Player 冒烟需 Unity/Player，不在离线测试内；机器侧验收。
- 基线：Python 全测、L1、Doc、toolchain check/docs、BuildHash。

## 用例清单

### `test_build_player.py`
- `--help` 含 `--profile`；
- `--profile BuildProfiles/no-such.json` exit 1 + profile missing。

### `test_player_smoke.py`
- `--help` 含 `--exe-path`；
- `--exe-path Builds/no-such.exe` exit 1 + exe missing。

### 结构
- 清单 build.player/build.smoke Python；旧 PS 不存在；CI 构建/冒烟 Python。

## 预期失败证据

`verify --red`：5 个断言失败（两脚本缺失 + 清单旧 build PS），无环境错误。

## 运行方式

```powershell
python -m unittest scripts.tests.test_build_player scripts.tests.test_player_smoke -v
python scripts/build/build-player.py --help
python scripts/build/player-smoke.py --help
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/gate/test.py --lane L1 --profile PullRequest
python scripts/gate/test.py --lane Doc
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
```

完整构建/冒烟另行机器侧验收。
