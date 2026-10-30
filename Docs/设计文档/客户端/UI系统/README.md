# UI 系统（现行设计）

> 本目录是现行 UI 体系的唯一设计入口，与旧体系目录 `客户端/UI/` 分离。运行时与绑定基座以代码为准，其余设计按下列文档执行。

| 文档 | 回答什么 |
| --- | --- |
| [UI 系统总设计](UI系统总设计.md) | uGUI × 大型/多端的架构分层、保留基座、生命周期/导航/层序/数据流、样式、多端适配、HUD、性能与质量 |
| [UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md) | 工作台 + 美术/TA/工程 Lane、起页/登记、绑定生成、页面契约、校验、预览与资源工具 |
| [UI 运行时调试工具链专项设计](UI运行时调试工具链专项设计.md) | 读模型、`ui.*` 命令、状态注入、可点性、编辑器深检 + F8、属性级写回与证据 |
| [UI 美术协作与交接规范](../../../规范/06-UI美术协作与交接.md) | 角色/RACI/资产所有权、契约交接、评审门、变更管理与度量 |
| [UI 制作规范](../../../规范/05-UI制作.md) | 目录、Prefab 根、命名、布局、渲染、文本、动效、列表、交互、提交与门禁硬规则 |
| [UI 命名与控件词表参考](UI命名与控件词表参考.md) | 名字模型、角色词表、控件模板清单、选段指引、硬约束速查 |
| [UI 逻辑 API 参考](UI逻辑API参考.md) | Lua `self.ui`/`Bridge.ui`、C# `UIBindBase`/`UIBindIndex`、列表行协议、错误语义 |
| [设置系统专项设计](设置系统专项设计.md) | 设置项单源（分组/存储键/值域/默认）、四组生效器（音频/图形/显示/控制）、影子管线资产与平台能力矩阵、`Bridge.settings` 门面、设置面板页签结构与文案键 |
| [战斗 HUD 对局倒计时专项设计](战斗HUD对局倒计时专项设计.md) | 局时 `mm:ss` 的语义/真值源/显示状态机/刷新纪律/prefab 落位与验收（草案） |

- 机器可读 schema：[UiPageContract.schema.json](UiPageContract.schema.json) · [UiStyleTokens.schema.json](UiStyleTokens.schema.json) · [UiWidgetManifest.schema.json](UiWidgetManifest.schema.json) · [UiPageManifest.schema.json](UiPageManifest.schema.json) · [UiRuleExceptions.schema.json](UiRuleExceptions.schema.json) · [UiDeviceProfiles.schema.json](UiDeviceProfiles.schema.json)。
- 旧体系（已退役）：全文见 [2026-10-22/UI旧体系](../../../归档记录/2026-10-22/UI旧体系/README.md)；旧路径 `客户端/UI/` 的跳转壳已退役，映射见 [2026-10-29 归档说明](../../../归档记录/2026-10-29/README.md)。
- 操作手册：[08-UI 制作与调试](../../../说明/08-UI与Lua脚本层.md)；调试：[13-调试手册](../../../说明/13-调试面与排障.md)。
