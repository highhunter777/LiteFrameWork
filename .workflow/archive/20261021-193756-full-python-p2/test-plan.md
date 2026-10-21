# 测试计划：全量 Python P2（内容/环境）

## 测试策略

- 先写五组契约 + 清单/注册面断言，观察 14 个失败，再实现。
- 签名测试用**临时生成 RSA 密钥 + 等价 .NET XML + 锚表 .cs**，不读真实私钥；用 `cryptography` 独立公钥验签。
- 发布/服务测试全部用临时目录与空闲端口；不写真实 CDN、不启动常驻进程。
- restore 用 repo 基线 + 空项目 blocking 两侧；不断言 WARN 数量（当前有 .NET 9 WARN）。
- 基线：Python 全测、restore、toolchain check/docs、Doc、L1、BuildHash；不跑 L2/Unity/SSH。

## 用例清单

### `test_local_cdn.py`
- 相对目录按仓库根解析；绝对目录保留；缺文件 `FileNotFoundError`；缺字段 `ValueError`。

### `test_gen_candidate.py`
- 临时 RSA XML/anchors；混合受控目录（config/lua/bundle）+ skipped；校验：
  - manifest 字段序、Files 排序/前缀/长度/SHA256、`Signature=""`；
  - 信封 `{"manifest":...,"signature":...}`，独立公钥 PKCS1v15+SHA256 验签；
  - 空目录 exit 1 + “no controlled content”。

### `test_publish_candidate.py`
- 临时内容/CDN/配置/key/anchors + bundle source：校验 `candidate.json`、`rel-test/{config,bundle}` 布局、bundle 镜像、签名；
- 内容目录缺失 exit 1。

### `test_serve_cdn.py`
- 临时目录 + 空闲端口：起服后 HTTP GET 命中文件；目录缺失 exit 1。

### `test_restore_check.py`
- repo 基线 exit 0 + `Restore check PASSED`；
- 空临时项目 exit 1 + `[MISS]`/`Restore check FAILED`。

### `test_toolchain.py`
- P2 四个 command 为 `python ...py`；五个旧 PS 不存在；`toolchain.py`/`build-player.ps1` 引 restore `.py` 不含 `.ps1`；
- `_local_cdn.py` 存在、旧 loader 不存在。

## 预期失败证据

`workflow.py verify --red`：`toolchain-tests-red` exit=1（约 8.2s），**14 个断言失败**：

- `_local_cdn` 缺失 → 4；
- gen-candidate 脚本缺失 → 2；
- publish 脚本缺失 → 2；
- serve 脚本缺失 → 2；
- restore 脚本缺失 → 2；
- 清单仍指旧 PS / 旧 loader 存在 → 2。

无环境错误/超时/导入冒泡。

## 运行方式

```powershell
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python .agents/skills/staged-delivery/scripts/workflow.py --run full-python-p2 verify --red

# 施工后
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/gate/restore-check.py
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
```
