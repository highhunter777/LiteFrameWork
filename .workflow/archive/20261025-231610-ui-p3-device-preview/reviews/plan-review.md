# 方案评审（独立子代理）：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 创建于 2026-10-25T19:47:59+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：plan.md（两轮：初版 → 按首轮意见修订 → 复评）
- 代码/文档基线：git HEAD `104a74d3`；抽查面＝UiPagePreview/UiPreviewDriver、SafeAreaReceiver.cs:17/48-50、UiWorkbenchWindow.cs:179/189、UiWorkbenchSettings、UiPageManifestEditor（DeviceProfiles）、Packages/manifest.json、TableLayout.cs:27、Tables.cs:38、LTextCatalog/LocalizationService 构造面、总设计 §7.1–§7.3/§7.5、工具链设计 §7、UiPreviewEditModeTests（6 例实测）、.gitignore 全文、ProjectVersion.txt（2022.3.55f1c1）、toolchain.json（68 工具无冲突）
- 评审范围与不在范围：UI 系统 P2-3 B3（设备档与预览矩阵）；工作区并行线在途改动不在范围
- 子代理标识：general-purpose `agent_c2f0d747-7968-4942-b64c-55999b2599cc`（ZCode Agent 工具）
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 方案检查

- 目标可观察、范围与非目标清晰：通过（数据档/真链路/竖屏/减少动效/性能预算均显式排除——减少动效系首轮意见补入）。
- 现状与根因有依据：**逐项抽查属实**（注入缝 SetStrategy 立即重算、占位 file:line、清单 DeviceProfiles、tbltext 直载链路核实、UiPreviewEditModeTests 恰 6 例）；初版两处事实错误（Artifacts/并不在 .gitignore；Device Simulator 在 2022.3 是内建非包——"manifest 无 ⇒ 未装"推理不成立）由子代理抓出并修正。
- 候选 A/B/C 取舍：通过（修正后 C 的否决理由更硬：内建模拟器无公开脚本切换面、设备库非项目单源——"自动化与单源双输"）。
- 设计覆盖接口/数据/流程/失败边界：通过（GameView 反射降级＋Simulator 模式注意、表不可直载降级、中断安全、安全区"当前屏幕像素空间 Rect"语义、字体缩放恢复）。
- 通用性与扩展点：通过（档案三方共用、新增档零代码、PseudoLocale 可复用 P3）；SessionState 与红线 6 相容性子代理给出明确判断（编辑器会话键值 API 调用，与 EditorPrefs 先例同类，不构成新增静态可变字段；键常量收私有）。
- 测试策略：通过（初版全留人工冒烟 → 依首轮意见补 PlayMode 夹具〔apply/restore 链〕，基建先例 ui-theme-apply-playmode 在册）。
- 风险/监控/回滚：可执行（不选档＝旧行为、新文件可摘、不动场景）。

## 验收标准可判定性

| AC | 判定 | 说明 |
|---|---|---|
| AC1 | 可判定 | JSON＋校验负例＋确定性逐字节比对可落 EditMode |
| AC2 | 可判定 | 免重启机制面核实存在（SetStrategy/SetLocale→OnLocaleChanged→binder.Apply）；apply/restore 链有 PlayMode 夹具 |
| AC3 | 可判定（修复后闭环） | 初版"Artifacts 已在 ignore"事实错——本批显式增 `[Aa]rtifacts/`＋git status 零污染验证 |
| AC4 | 可判定 | 占位/设置键/预览按钮 file:line 核实 |
| AC5 | 可判定 | 非目标登记＋§7.5 字体档口径对齐＋减少动效注记 |
| AC6 | 可判定 | config.json 门禁模式对齐、纯增量 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | major | "Artifacts/ 已在 ignore"为事实错误（.gitignore 全文无此条目）——产物不入库只剩纪律无机制 | `.gitignore` 增 `[Aa]rtifacts/` 列为范围任务；AC3 加零污染验证 | 已修，复评确认 |
| P-2 | major | "未装 Device Simulator 包"推理不成立（2022.3 内建）——候选 C 否决理由错误 | 背景第 4 条/C 行/D2 按事实改写（内建、无公开脚本面、非项目单源；Simulator 模式与 SetSize 交互注意） | 已修，复评确认 |
| P-3~P-10 | minor | 8 条非阻塞（SessionState 判断/减少动效/DPI 字段/安全区语义/PlayMode 夹具/字体档对齐 §7.5/meta 说明/tbltext 升已核实） | 全部采纳改文 | 已修，复评逐条核对到位 |

## 复测记录

- 复评方式：同子代理会话续用，逐项核对修订版与修复清单（输出含逐条行号核对）。
- 复评最终：**结论 PASS；验收标准 PASS（AC1–AC6）；阻塞问题 0**；两处措辞残留（推荐理由旧话头/重复"施工核实"）主代理已随后清掉。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: general-purpose agent_c2f0d747（ZCode Agent 工具）
- 评审时间: 2026-10-25（首轮＋修复复评两轮）
