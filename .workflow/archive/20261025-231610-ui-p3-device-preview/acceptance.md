# 验收标准确认：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 创建于 2026-10-25T19:47:59+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | 设备档案单源：UiDeviceProfiles.json 上任（六横屏档＋竖屏预留，含分辨率/方向/安全区 inset/可选 dpi/入矩阵标记）；校验负例齐（宽高比不符/inset 越界/ID 文法）；确定性（重读逐字节一致、按 id 排序、无时间戳）；驱动/工作台/矩阵三方读同一档案 | UiDeviceProfilesEditModeTests＋代码审读 | T1 |
| AC2 | 预览驱动升级：设备档切换（GameView 分辨率〔反射容错降级〕＋安全区策略注入/撤销〔当前屏幕像素空间 Rect〕）、语言（中/英/伪本地化长文本，tbltext 直载）、字体缩放档 1.0/1.15/1.3（TMP 原值恢复）；切换免重启；退出回场景无残留（apply/restore 链有 PlayMode 夹具证） | UiPreviewDriverPlayModeTests＋UiPreviewMatrixEditModeTests＋人工冒烟（eval 打开切档） | T2 |
| AC3 | 截图矩阵：ui.preview-matrix 产出组合截图（页×档×语言×长文本）到 Artifacts/PreviewMatrix/＋matrix.json 索引（分辨率应用结果/降级记录）；中断安全；失败组合不中断；产物不入库有机制（.gitignore 增 [Aa]rtifacts/，git status 零污染） | 真跑一页小矩阵实产＋目录核对＋git status | T3 |
| AC4 | 工作台接线：上下文条设备档真下拉（档案单源＋"跟随预览"缺省，选择经 IWorkbenchSettings 持久）；预览按钮携当前选择；占位"P2-3"移除 | UiPreviewEditModeTests 扩展＋窗口冒烟 | T4 |
| AC5 | 边界与回写：数据档/真链路/竖屏/减少动效/P4 显式登记非目标；总设计 §7.3、§7.5（字体档口径）、工具链设计 §7.1（减少动效延后注记）回写；工具索引/toolchain.json 同步；既有回归全绿 | doc-lint＋docs --check＋回归夹具 | T5 |
| AC6 | 门禁：新 EditMode 三套＋PlayMode 一套全绿；config.json 增门禁；toolchain.py check 0 error；wf verify 全绿 | wf verify | T6 |

## 提审记录

- 提审时间：2026-10-25
- 提审方式：结构化提问（AskUserQuestion：AC 总确认＋D1/D2/D3 裁决）
- 用户原话或选择（2026-10-25 对话确认）：「通过，按推荐」——AC1–AC6 全部确认；D1＝自动批量；D2＝反射 SetSize＋容错降级；D3＝JSON 单源。

## 用户确认

- 确认结论: 通过
- 确认人: highhunter777（对话用户）
- 确认时间: 2026-10-25
- 变更要求：无

## 变更记录

- 2026-10-25：首轮提审后评审子代理抓出两处方案事实错误（Artifacts/ 不在 .gitignore、Device Simulator 系 2022.3 内建）→ 修订并复评 PASS → 本轮提审确认。
