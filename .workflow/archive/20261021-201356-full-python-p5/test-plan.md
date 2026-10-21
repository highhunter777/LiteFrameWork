# 测试计划：全量 Python P5

## 测试策略

- 红测覆盖离线可验证面：五脚本 help/参数/未知命令；`_ssh_host` 显式 env/不可达回退；PS 清零规则；清单/CI/文档结构断言。
- 不执行 SSH、云侧、起服、dotnet 常驻进程；真实部署/本地联测机器侧验收。
- 基线：Python 全测、L1、Doc、L2 meta、toolchain check/docs、BuildHash；最终 `scripts/**/*.ps1=0`。

## 用例清单

- `test_ssh_host`：显式 env 不联网；不可达隧道回退 public alias。
- `test_deploy_room`：help；`--skip-publish` 无产物 exit1（在 SSH 前）。
- `test_gm_entry`：help；未知 action exit2；缺 Room exit2（在 key/SSH 前）。
- `test_wg_peer`：help；缺/非法 Name exit2（在 SSH/Endpoint 前）。
- `test_local_chain`：help；未知命令 exit2。
- `test_toolchain_scripts`：`scripts/Old.ps1` 被 check_scripts 判 error。
- `test_toolchain`：deploy.room/gm/wg-peer/env.local-chain command Python；五 PS 不存在；`scripts/**/*.ps1` 计数 0。

## 预期失败证据

`verify --red`：缺失脚本/旧清单/PS 禁止规则未实现；断言失败，无环境错误。

## 运行方式

```powershell
python -m unittest scripts.tests.test_ssh_host scripts.tests.test_deploy_room scripts.tests.test_gm_entry scripts.tests.test_wg_peer scripts.tests.test_local_chain -v
python -m unittest discover -s scripts/tests -p "test_*.py" -v
python scripts/gate/test.py --lane L1 --profile PullRequest
python scripts/gate/test.py --lane Doc
python scripts/gate/l2-unity-gate.py --meta-scan-only
python scripts/toolchain.py check
python scripts/toolchain.py docs --check
python scripts/gate/doc-lint.py
```

部署/GM/隧道/本地联测真实执行不在离线测试内。
