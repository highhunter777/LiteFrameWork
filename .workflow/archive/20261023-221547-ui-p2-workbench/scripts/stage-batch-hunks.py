"""P2-1 批量提交的 hunk 级拆分：把本批改动写进 index，不碰工作区。

背景：工作区混有并行线（DevMenu/调参/测试房语义）改动，整文件 `git add` 会把并行改动
带进本批提交（缺依赖/描述未提交代码）。本脚本对 4 个共享文件用 HEAD 内容 + 本批改动重建
index blob；其余文件由调用方 `git add`。
"""

import io
import subprocess
import sys

REPO = r"E:\unityProject\Test"


def git(*args, data=None):
    return subprocess.run(args, cwd=REPO, input=data, capture_output=True)


def head_text(path):
    result = git("git", "show", "HEAD:" + path)
    if result.returncode != 0:
        raise SystemExit("git show failed: %s\n%s" % (path, result.stderr.decode("utf-8", "replace")))
    return result.stdout.decode("utf-8")


def replace_line(text, marker, replacement, expected=1):
    lines = text.split("\n")
    hits = [i for i, line in enumerate(lines) if marker in line]
    if len(hits) != expected:
        raise SystemExit("line marker %r matched %d lines (expected %d)" % (marker, len(hits), expected))
    for i in hits:
        lines[i] = replacement
    return "\n".join(lines)


def stage_blob(path, content):
    blob = git("git", "hash-object", "-w", "--stdin", data=content.encode("utf-8"))
    if blob.returncode != 0:
        raise SystemExit("hash-object failed for %s" % path)
    sha = blob.stdout.decode().strip()
    result = git("git", "update-index", "--cacheinfo", "100644,%s,%s" % (sha, path))
    if result.returncode != 0:
        raise SystemExit("update-index failed for %s: %s" % (path, result.stderr.decode("utf-8", "replace")))
    print("staged blob", path, sha[:10])


# ---------------- 1) EditorToolHandlers.cs ----------------
path = "Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs"
s = head_text(path)
s = replace_line(s, "using LiteGame.Editor.UiTools.Widgets;",
                 "using LiteGame.Editor.UiTools.Widgets;\nusing LiteGame.Editor.UiTools.Workbench;")
s = replace_line(s, '"ui.page-new"', "")  # 删除整行
s = s.replace("\n\n\n", "\n\n")
s = replace_line(s, '"ui.compose"',
                 '            registry.RegisterWindow("ui.workbench", UiWorkbenchWindow.Open);')
stage_blob(path, s)

# ---------------- 2) toolchain.json ----------------
path = "scripts/gate/toolchain.json"
s = head_text(path)
start = s.index('    {\n      "id": "ui.page-new"')
compose = s.index('"id": "ui.compose"', start)
end = s.index("    },\n", compose) + len("    },\n")
block = (
    "    {\n"
    "      \"id\": \"ui.workbench\",\n"
    "      \"title\": \"UI 工作台\",\n"
    "      \"category\": \"ui\",\n"
    "      \"kind\": \"window\",\n"
    "      \"plane\": \"editor\",\n"
    "      \"purpose\": \"dev\",\n"
    "      \"handler\": \"ui.workbench\",\n"
    "      \"summary\": \"UI 制作唯一入口：页面/控件库/绑定/契约/校验五透视，角色 Lane 与阶段门控。\",\n"
    "      \"docs\": \"Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md\",\n"
    "      \"menu\": \"LiteGame/UI/制作/UI 工作台\",\n"
    "      \"writes\": [\"asset\", \"source\"],\n"
    "      \"destructive\": false,\n"
    "      \"order\": 30\n"
    "    },\n"
)
s = s[:start] + block + s[end:]
stage_blob(path, s)

# ---------------- 3) Docs/修改指南.md ----------------
path = "Docs/修改指南.md"
s = head_text(path)
s = replace_line(
    s, "新页面用“新建页”向导登记",
    "| Lua 页面逻辑 | `Assets/LiteGame/Lua/UI/`（脚本）；`App/UIFormCatalog.cs` 只是 tbuiform 的表投影（不是登记点）；新页面登记随 P2-2（`ui.page-register`；P2-1 后无向导，手工走 `_uiform_row.py` + `Luban/gen.bat`） | PlayMode（夜间） |")
s = replace_line(
    s, "**现行**：Hub `ui.page-new`",
    "| 新增页面接入 | **现行（P2-1 后）**：无窗口起页工具——美术手工复制 `Tmp_Page` 建 prefab；工程用 `scripts/codegen/_uiform_row.py` 写 `#uiform.xlsx` 行 + `Luban/gen.bat` + `ui.page-manifest` 对账。**P2-2** 拆为 `ui.page-scaffold`（仅 prefab+草稿）与 `ui.page-register`（表/Lua/绑定），见《[UI 编辑器工具链专项设计 §制作流水线](设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-binding)》；**不动框架** | PlayMode（夜间）+ Luban 生成检查 |")
s = replace_line(
    s, "UI 制作主窗口（现行过渡",
    "| UI 制作主窗口（`ui.workbench`：页面/控件库/绑定/契约/校验五透视 + Lane） | `Assets/LiteGame/Editor/UiTools/`：`Workbench/UiWorkbenchWindow`（纯 IMGUI 宿主）、`Workbench/UiLanePolicy`+`UiWorkbenchState`+`UiWorkbenchPageSection`+`UiWorkbenchContractSection`、`Compose/Ui*Section`（存 IMGUI 分区，控件库/受控属性仍用 Odin）、`Binding/UiBindingEditor`、`Page/UiPageScaffold`（P2-2 起页内核）、`Preview/UiPagePreview`；设计见《[UI 编辑器工具链专项设计 §工作台](设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-workbench)》 | EditMode（`UiWorkbench`/`UiPreview`/`UiWidgetProperty`/`UiWidgetLibrary`/`UiBindingTools`）+ recompile（Odin 依赖由 `scripts/gate/restore-check.py` 阻塞项保障） |")
stage_blob(path, s)

# ---------------- 4) Docs/工具链/工具索引.md ----------------
path = "Docs/工具链/工具索引.md"
s = head_text(path)
workbench_row = "| ui.workbench | UI 工作台 | 窗口 | 编辑器 | 开发 | `python scripts/toolchain.py open ui.workbench` | asset, source | — | — | [文档](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md) |"
lines = s.split("\n")
kept = []
dropped_compose = False
for line in lines:
    if line.startswith("| ui.page-new |"):
        kept.append(workbench_row)
        continue
    if line.startswith("| ui.compose |"):
        dropped_compose = True
        continue
    kept.append(line)
if not any(line.startswith("| ui.workbench |") for line in kept):
    raise SystemExit("workbench row not inserted")
stage_blob(path, "\n".join(kept))

print("hunk split done")
