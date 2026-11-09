# 验收标准确认：Settings 页面端口 + 守卫棘轮（v6.1）

> 运行 client-module-recut · 创建于 2026-11-09T11:50:08+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC0 | **基线可复现**：开工时记录 `git status --short -- Assets Packages Tests Server` 快照（当前 8 M + 5 ?? 并行 WIP）；相对快照不得新增改动；`.workflow` 方案目录已提交/排除；L1/arch/domain/doc/`build-hash --check-shared` 基线记录；BuildHash meta 红登记到 `Docs/审计/客户端/登记/技术债.md` + 施工进度卷（可 grep `BuildHash meta stale`） | `git status --short -- Assets Packages Tests Server`；`python scripts/gate/test.py --suite L1 --profile PullRequest`；`python scripts/gate/arch-check.py --check`；`python scripts/gate/domain-matrix.py --check`；`python scripts/gate/doc-lint.py`；`python scripts/codegen/toolbox.py build-hash --check-shared`；grep `BuildHash meta stale` | Phase 0 |
| AC1 | **守卫落地**：friend 允许集（作用域 9 个 `Modules/*`、55 基线、`arch/units.json` `friends` + L1 用例、最近 asmdef 目录归属、不递归子单元）与零使用边脚本/基线（`arch/zero-use-edges.json`、复用 `domain-matrix` 口径、`quality.zero-use-edges` 登记 `toolchain.json`、只减不增）落地；合成负例变红；生产基线记录 | 新增 friend L1 用例；`python scripts/gate/zero-use-edges.py --check`；`python scripts/toolchain.py docs --check`；负例 fixture 变红 | Phase 0 |
| AC2 | **Settings 页面端口**：UI asmdef 不再引 Settings；页面代码 `ProductSettings.Current` = 0；`IPageContext`/`PageLogicBase` 有 `Settings`；`ISettingsPagePort` 28 成员（15 读 + 13 写/动作，无 Apply）；adapter 创建点在 `BindProductSettings` 之后/惰性；`SettingsPagePlayModeTests`（fake `ILocalizationService`、真 `Page_Settings.prefab`、Dropdown/Slider 事件、空端口 10 处 `?.` + OnShow 早退）绿；allowlist 删 `ProductSettings`；units/domain-matrix 去 Ui→Settings；L1/arch/domain/doc 绿 | `rg "ProductSettings\.Current" Assets/LiteGame/Modules/UI/Pages` = 0；`arch-check`/`domain-matrix`/`doc-lint`；`python scripts/gate/unity-tests.py --mode playmode --filter SettingsPagePlayModeTests`；L1 | Phase 1 |
| AC3 | **棘轮**：friend 55；零使用边基线只减不增；跨模块白名单 15→14 已在 Phase 1 完成（删 `ProductSettings`），Phase 2 不重复；记录数字，不承诺一步到 0/≤5 | `zero-use-edges.py --check`；allowlist 计数；friend 计数 | Phase 2 |
| AC4 | **门禁全绿**：L1/arch/domain/doc/`build-hash --check-shared`/hot-boundary 全绿；BuildHash shared 常量不变；meta 红为已知债；不跑 `toolchain-tests`（`scripts/tests/test_arch_check.py:23` 存量 34 vs 33 红与本批无关） | `test.py --suite L1 --profile PullRequest`；`arch-check.py --check`；`domain-matrix.py --check`；`doc-lint.py`；`toolbox.py build-hash --check-shared`；`hot-boundary-check.py` | 全程 |
| AC5 | **文档回写**：说明 03/08/10/14/15/17/18/19/20、UI 系统总设计/UI 编辑器工具链专项/UI 逻辑 API/DI/设置/技术债/施工进度卷做**事实性回写**；路线图/Lua 退出专项等章程类只记录“不变/待另立设计”，不预占 §7 结论；审计登记路径核销；无死链/孤儿 | `doc-lint.py`；`python scripts/toolchain.py docs --check`；清单逐项核对 | 全程 |

## 提审记录

- 提审时间：2026-11-09T15:30:54+08:00（Asia/Hong_Kong）
- 提审方式：对话中逐条列出 AC0-AC5，请用户确认
- 用户原话或选择：“确认，先提交本地所有改动。”（确认 AC0-AC5；先提交本地全部改动）

## 用户确认

- 确认结论: 通过
- 确认人: user
- 确认时间: 2026-11-09T15:30:54+08:00
- 变更要求：无（“先提交本地所有改动”是流程指令，不改 AC）

## 变更记录

- v6.1：按第六轮评审 PASS_WITH_MINOR 的 N38-N47 修订（AC0 快照口径、friend 55 作用域、零使用边口径/toolchain、Phase 0→1 顺序、白名单归 Phase 1、meta 债落点、空端口 10 处 `?.`、事实性回写、不跑 toolchain-tests）；六轮评审记录见 `reviews/plan-review.md`。
