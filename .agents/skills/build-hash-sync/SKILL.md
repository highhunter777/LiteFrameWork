---
name: build-hash-sync
description: 共享源集改动后的生成物重生成流程(BuildHash / 协议 / Luban 表)。只要动过或即将动 Assets/LiteSim/Core/{Scripts,Systems}、Assets/LiteNet/{Proto,Protocol,Transport/Security}、.proto 文件、Luban 表源(Luban/Data、Luban/Defines)、Assets/GameData/Config 表数据,或用户提到 BuildHash、握手、生成物、重生成、出包前检查,就必须先加载本 skill——漏跑 = 出包门禁被拦/两端常量不同步时 Join 拒进房。
---

# 共享源集与生成物同步(BuildHash / 协议 / 表)

**为什么存在**:`BuildHash.g.cs` 把两端共享源(Sim 内核、协议、安全信封、表数据)的内容指纹编译成常量,**随各自编译烤进两端程序集**;客户端与服务器 Join 时比对哈希,不等直接拒进房。改了源不重生成 = 本地联机(两端都旧)不会立刻炸,炸点在出包门禁被拦(返工一轮:重生成+双端重建+重打包)或两端构建不同步时握手失败——这正是修改指南 §3.9 称它"最容易被忘的收尾"的原因。权威细则在 `Docs/修改指南.md` §3.9 与《生成器工具箱专项设计》;源集清单权威 = `scripts/codegen/gen-build-hash.py` 的 TARGETS/DATA_TARGETS。

## 校验时点(2026-10-07 起)

- **开发期不校验**(BuildHashTests/源集守卫测试已退役,规则单源回归 gen-build-hash.py 本体)。
- **唯一守卫在出包门禁**:`build-player.ps1` 门禁步跑 `--check` 复算比对,不等即拦——只校验不代跑,所以**重生成这个动作必须在收尾做**,别攒到出包时返工。

## 源集 → 必跑命令对照

| 改了什么 | 必跑 |
|---|---|
| `Assets/LiteSim/Core/{Scripts,Systems}`、`Assets/LiteNet/{Proto,Protocol,Transport/Security}`、表数据 | ★ `python scripts/codegen/gen-build-hash.py` |
| `.proto`(源在 `Assets/LiteNet/Proto/*.proto`) | ★ `scripts/codegen/gen-proto.ps1 -ProtocPath <protoc.exe>` **再** ★ BuildHash 重跑 |
| 表源(`Luban/Data/*.xlsx`、`Luban/Defines/` schema) | ★ `Luban/gen.bat`(双端重生成 C#/binary/Lua)**再** ★ BuildHash 重跑 |
| 出包前 | `python scripts/codegen/gen-build-hash.py --check`(复算比对;`build-player.ps1` 门禁步也会做) |

## 规则

1. **先改源、后重生成,顺序不能反**:重生成以当前源内容为输入,生成后源又有改动必须重跑。
2. **生成物勿手改**:`Assets/LiteNet/Proto/Generated/`、`Assets/LiteNet/Protocol/BuildHash.g.cs`、`Assets/GameData/Config/*.bytes`、`Assets/LiteGame/Lua/Cfg/*.lua` 都是生成物——要改行为就改它的源(表源 / .proto / 共享源),再重生成。
3. **统一入口**:`pwsh scripts/codegen/toolbox.ps1 <tool> [args...]`,与上表单脚本直调等价;工具形态/失败语义的权威是 `Docs/设计文档/architecture/生成器工具箱专项设计.md`。
4. protoc 来自 nuget `Google.Protobuf.Tools`,版本须与运行时对齐;`Proto/Generated/Battle.cs` 勿手改。
5. 改表数值同时涉及代码默认值的:表值与默认值必须一致(L1 表守卫钉住);机制面禁读静态读口(R13,见修改指南 §1.3)。

## 收尾联动

BuildHash 跑完后,整批验证走 `batch-wrapup` skill:L1 全量(内含 BuildHash 守卫与表守卫)。
