# UI工作台W1（进度演示接线 + Loading 视觉补齐）

《UI工作台设计》W1 执行（2026-10-11）。依赖说明：`LoadingScope.Report` 属《通用加载页设计》§3 契约、原未实现——本批随 W1 一并落地最小面（Report/缓存/回读），加载页表单化完整批仍归加载页线。

## 交付

| 面 | 落点 |
|---|---|
| 契约落地 | `FeedbackService.ReportLoading(message, progress01)`（缓存首写不丢/嵌套后写胜出/开面后写全量）+ `LoadingScope.Report` + `CurrentLoadingMessage` 回读；`BeginLoading` 复位写全量；`EndLoading` 清缓存 |
| 控件 | `LoadingMask` 补 `ProgressFill`（Filled/Horizontal）+ `SetStatus(message, progress01)`（写全量：文案回落"加载中…"、进度 null=不定态隐藏） |
| 模板补齐 | `Assets/ShareResource/UI/Widgets/Tmp_Loading.prefab`（原空壳：无 Mask 无视觉）——`_Blocker`（全屏阻断）/`Txt_Message` + `LoadingMask` 接线（pipeline，Type.GetType+SerializedObject）；202 壳 `Screens/Tmp_Loading.prefab` 只留 Canvas+CanvasGroup+嵌套实例 |
| 工作台 | `Button_Progress` 接 `Report`（0.25 步进演示，未开 Loading 时提示先开）；状态行补加载文案回读 |

## 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | `completed / failed:false / 0 错误` |
| `FeedbackServiceEditModeTests`（回归） | **11/11**（既有嵌套阻断/取消/合并语义零回归） |
| 模板构建 | run_script `MASK wired (Blocker/Message/ProgressFill) / PREFAB saved` |

## 遗留（如实）

- 加载页表单完整批（加载页批A/B：进度条显隐策略细化、Report 与真实场景进度对接）仍归加载页线；
- `Button_Progress` 演示为合成步进（无真实场景进度源）——真实消费者接入后替换。
