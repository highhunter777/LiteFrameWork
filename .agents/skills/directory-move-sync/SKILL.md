---
name: directory-move-sync
description: 目录搬动/文件移动同步清单。只要涉及在 Assets/ 或 Server/ 下移动、重命名、删除或合并目录与文件(用户说"挪过去"、"重构目录"、"搬一下"、"整理一下结构"),或刚做完这类操作,就必须加载本 skill——目录结构被六个独立机制引用,漏同步任何一处都是静默失效(扫描扫不到、打包收不进、测试编不过)。
---

# 目录搬动同步清单

**为什么存在**:目录结构被纪律扫描、资源收集、双轨工程文件等多个机制按路径引用,它们不会随文件系统移动自动更新;漏同步的表现是静默的——守卫失效、资源断链、编译报错找不到源头。规则原文在 `Docs/修改指南.md` §2.3"目录搬动"行与 AGENTS.md 工作约定。

## 步骤

1. **Assets 内的移动/重命名/删除先走 unity-pipeline CLI**(见 `unity-asset-pipeline` skill)——保 .meta/GUID 关联;禁文件系统直移。Server/ 下无 Unity 序列化内容,可直接文件系统操作。
2. **同批同步六面**(全部要动,缺一即静默漂移):
   - `ScanTargets.cs`(纪律扫描目标清单——漏了则 DisciplineScanner 对新位置失效)
   - YooAsset 收集组(收集目录规划按 `Docs/设计文档/client/content/资源收集目录专项设计.md` §2/§5;Assets 顶层只放原始资源,进包来源只有模块内使用资源目录与 `Assets/ShareResource`)
   - `Tests.slnx` / csproj 引用(双轨源链接,框架用例直接链 Assets/ 源文件)
   - `.gitignore` 白名单
   - 构建器路径(`build-player.ps1` 等)
   - `Docs/开发导航/代码地图.md`
3. **新增收集目录 = collector + 本清单 + 专项文档三面同批**。
4. **验证**:`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`(DisciplineScannerTests 钉 ScanTargets);资源收集变更另用出包冒烟验证。
