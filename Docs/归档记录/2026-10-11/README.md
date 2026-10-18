# 归档记录 2026-10-11

## 生成器工具文档吸收归档

**事件**：五件 CLI 生成器（buildHash/协议/表链/同步代码/刷新编排）统一为**生成器工具箱**（`scripts/codegen/`，统一入口 `python scripts/codegen/toolbox.py <tool>`，共享单源 `_codegen.py`）。

**吸收**：`scripts/README.md` 的 codegen 操作知识（顶部 Luban 段 + `codegen/` 行细节）与 `gen-proto.ps1` 中文头注释（protoc 来源/版本对齐约束）吸收进《[生成器工具箱专项设计](../../设计文档/架构/生成器工具箱专项设计.md)》；原位替换为吸收指针/ASCII 头，保留约束事实。吸收前原文照录：[生成器工具文档吸收前存档.md](生成器工具文档吸收前存档.md)。

**不归档说明**：`scripts/README.md` 本体仍是脚本目录索引（活文档），仅 codegen 段瘦身为指针；《修改指南》§2.3/§3.9、AGENTS.md 常用命令、项目说明 04/05/06、《开发导航/代码地图》为红线查询面/索引面，不在吸收范围，仅同步工具箱入口与指针。五件工具头注释保留各自 why（实现依据），不在归档范围。

**本批施工记录**：[施工进度/生成器工具箱.md](../../施工进度/资源与工具链线.md#ch6-生成器工具箱)。

## 生成器 Python 迁移归档

**事件**：同日二批——五件生成器全部迁移 Python（`gen_sync_code.py` 逐字节等价、`gen_proto.py`/`gen_luban.py`/`refresh_hash.py` 语义等价、`_codegen.py`/`toolbox.py` 取代 ps1 共享单源与入口）。

**处置**：旧六件 `.ps1`（gen-proto/gen-luban/gen-sync-code/refresh-hash/_common/toolbox）与死遗留 `Luban/gen.sh`（指向不存在的 Luban 可执行体路径）删除——**不另建吸收前存档**：脚本是代码，原文以 git 历史为归档；其中 `gen-proto.ps1` 中文头注释知识已于首批存档于本文[生成器工具文档吸收前存档.md](生成器工具文档吸收前存档.md)。裁决理由与逐条语义映射见《[生成器工具箱专项设计](../../设计文档/架构/生成器工具箱专项设计.md)》v1.1。
