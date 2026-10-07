# scripts/

按职责分类的工程脚本（可从任意 CWD 运行；CI 入口见 `.github/workflows/ci.yml`）。

| 目录 | 职责 | 内容 |
|---|---|---|
| `gate/` | 质量门禁（CI 调用面） | `test.ps1`（L1/L3 dotnet 车道）、`l0-dep-scan.ps1`（纪律/密钥扫描）、`l2-unity-gate.ps1`（Unity 编译 + EditMode/PlayMode 门禁）、`nightly-gate.ps1`（夜间全量编排）、`restore-check.ps1`（环境还原校验）、`toolchain.json`（工具链清单） |
| `content/` | 候选发布与本地 CDN | `gen-candidate.ps1`（签名信封生成）、`publish-candidate.ps1`（CDN 布局 + 签名一条龙）、`serve-cdn.ps1`（本地 CDN 起服）、`local-cdn.json` + `local-cdn.config.ps1`（服务目录/地址配置单源） |
| `build/` | Player 构建与冒烟 | `build-player.ps1`（内含 buildHash `--check` 出包门禁，不可跳过）、`player-smoke.ps1`（`-PlayerArgs` 透传部署参数） |
| `codegen/` | 派生物生成 | `gen-proto.ps1`（协议重生成）、`gen-build-hash.py`（buildHash 生成 + `--check` 构建前校验）、`refresh-hash.ps1`（基线刷新编排） |

约定：

- 工程根由脚本自身位置推导（`$PSScriptRoot/../..`），显式 `-ProjectPath` 覆盖——移动脚本位置时必须同步核对该推导与脚本互引路径。
- 含中文的 .ps1 无 BOM 会被 PS 5.1 按 GBK 误解析——**新脚本一律 ASCII-only**（`player-smoke.ps1`/`publish-candidate.ps1` 先例）；含中文的存量脚本经 UTF-8 scriptblock 调用（`gen-candidate.ps1` 由 `publish-candidate.ps1` 这样调用）。
- 本地 CDN 端到端：`content/publish-candidate.ps1` 发布 → `content/serve-cdn.ps1` 起服 → Player 带 `-content.cdnUrl` 启动（目录/地址见 `content/local-cdn.json`）。
- 施工记录里引用的历史脚本路径是该次执行时的现状证据，不随本目录结构调整改写；活性入口以本 README 与 CI 为准。
