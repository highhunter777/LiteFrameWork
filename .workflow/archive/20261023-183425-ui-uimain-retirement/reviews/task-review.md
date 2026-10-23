# 任务评审：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 运行 ui-uimain-retirement · 创建于 2026-10-23T17:29:30+08:00

## 评审输入

- 方案：plan.md
- 任务清单：tasks.md（T1～T6）
- 评审范围：任务粒度/依赖/验收可测性/测试策略/通用性与回滚；对照现行代码与 2026-10-23 P1 收口后的门禁能力。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1：tbuiform 无 id=1、页面清单 11/12、LuaKeys 6 常量 | T4 | 是 | 表行删除 + Luban/LuaKeys + ui.page-manifest；T5 用真实清单断言 |
| AC2：RegistryFiller 锚指 Page_Login、main.lua 去 UIMain | T2（守卫 T1） | 是 | 单源常量；守卫用例在 T1 先红 |
| AC3：Tmp_UiTestPage/Tests.UiTestPage 载体可加载，真链路不回退 | T3（夹具 T1） | 是 | unity-pipeline 改名/去 CanvasScaler/BindName；6 夹具路径同步 |
| AC4：真实面 Error=0/Warning=302/命名 0；11 契约不变 | T5 | 是 | 真实面校准 + PageManifest/Wiring 断言 |
| AC5：全量门禁绿 | T5 + 验证阶段 | 是 | L1/UI EditMode/PlayMode/doc 由最终 verify 统一跑 |
| AC6：文档/审计/进度/代码地图回写 | T6 | 是 | doc-lint + 人工链接检查 |

## 检查项

- [x] 任务粒度可独立执行与验证
- [x] 依赖关系无循环，顺序合理（T1 → T2/T3 → T4 → T5 → T6）
- [x] 每项任务有明确验收标准与验证方式
- [x] 测试策略足以覆盖验收标准（先红：退役守卫 + 既有夹具目标态；后绿：真实面/PlayMode）
- [x] 方案中的通用性与扩展性要求已落到任务（单源锚、测试载体命名、通用 delete-id）
- [x] 不存在硬编码、特判式补丁或范围外改动（不写死 UIMain 删除逻辑；测试载体保留是显式范围）
- [x] 回滚与风险处理已安排（_uiform_row.py 备份/回滚、unity-pipeline/git、真实面探针）

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 低 | T4 的任务级 verify 命令只复算 BuildHash；Luban/page-manifest 的完整性由 T5 的真实清单断言与最终 verify 兜住 | 接受：验收标准已写死 11/12/无 UIMain，T5 定向断言会抓住漏生成 | 记录 |
| R-2 | 低 | T1 的 RegistryFiller 守卫若用源码文本断言偏脆 | 实施时优先用 `KnownUiKey` 常量断言（T2 落地后补）；源码守卫仅作兜底 | 记录 |
| R-3 | 低 | T3 依赖 unity-pipeline 与编辑器就绪；窗口最小化会挂起主循环 | 已按 2026-10-23 沉淀保持窗口前台；失败则先恢复窗口再重试，不杀进程 | 记录 |

无阻塞问题。评审依据：方案 AC 与任务逐条映射；所有产物/生成物/测试/文档均有可执行验证面；退役范围显式排除“删除测试载体”（候选 B/C 另批）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 评审人: Qoder 代理
- 评审时间: 2026-10-23
