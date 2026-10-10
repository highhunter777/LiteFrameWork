---
name: unity-asset-pipeline
description: Unity 序列化资产与工程操作的强制管线。只要任务涉及创建/修改场景(.unity)、Prefab、Material、ScriptableObject、AnimationClip、AnimatorController、Timeline、.meta 文件,移动/重命名/删除 Assets 下的文件,场景对象操作,Unity 工程设置,或批量导入外部资源,就必须先加载本 skill——这些内容禁直改文件,一律走 unity-pipeline CLI。
---

# Unity 资产操作管线(红线 4)

**为什么存在**:Unity 序列化资产(.unity/.prefab/.mat/.asset/.controller/.anim/.meta)携带 GUID 与序列化格式,文件系统直改会破坏引用、丢 .meta、产生不可合并的脏 diff。权威规范是仓库根 `UNITY-GUIDE.md`,本 skill 是其执行摘要,冲突以原文为准。

## 判定表

| 操作 | 怎么做 |
|---|---|
| `.cs`、Shader、CSV、JSON、Markdown | 直接编辑(纯文本);涉及多文件时先批量改完,再统一触发 Unity 刷新编译,避免逐文件往返重复编译 |
| 创建/修改场景、Prefab、Material、ScriptableObject、AnimationClip、AnimatorController、Timeline | 必须走 unity-pipeline 技能/CLI(以当前实例 `unity command` 列表为准,不假定能力) |
| 移动/重命名/复制/删除 `Assets/` 下既有文件与文件夹 | 必须 Pipeline,禁文件系统工具(丢 .meta/GUID 关联);目录搬动的完整同步面另见 `directory-move-sync` skill |
| 外部资源的 Unity 导入、导入设置与验证 | 必须 Pipeline;批量导入可先把源文件复制到目标目录 |
| 场景对象操作(GameObject 增删/层级/Transform/组件及序列化字段) | 必须 Pipeline |
| Unity 工程设置(Build Settings、Tags/Layers、Input、Quality、Graphics、Player Settings) | 必须 Pipeline |
| 手动创建/修改/删除 `.meta`、复用其中 GUID | 禁止 |
| 直改 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`*.csproj`、`*.sln` | 禁止 |
| 以字节方式改写二进制资产 | 禁止 |

## 规则

1. 存在多个可连接的 Unity Editor 或开发版 Player 时,必须显式指定目标,避免误操作。
2. Pipeline 不可达时:仅可编辑纯文本,不得修改序列化资源;未完成的必要验证必须明确说明。
3. 大范围重新导入、切换构建目标、构建 Player、批量改写资源——必须先获得明确授权。
4. 场景/Prefab/材质等项目内容在编辑器中制作并持久化(提供必要可调参数),不得以运行时代码临时创建替代资产制作。
5. 收尾:执行与改动匹配的必要验证(非必要不跑全量测试);视觉改动按批次截图;区分本轮新增日志与历史日志;Scene/Prefab/Play Mode 结束后保存有意义修改、丢弃临时测试变更,不擅动用户已有的未保存内容。
