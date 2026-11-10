#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AR3 AOT 不引 hot：AOT 程序集不得出现对 hot 程序集的编译引用。

规则（热更与内容发布专项设计「AOT/hot 分界」/ 规范 02 §1.14）：
  AR3-1 分区表三类：
        · AOT＝LiteFramework（含 Core/Services/Adapters/Hosting）+ LiteNet + Bootstrap
          + LiteCodeGen.Runtime（被 LiteFramework.Services 编译期引用，只能是 AOT）；
        · hot＝LiteGame（产品，含原 LiteGame.Core/Modules.*/Adapters.*/App）
          + LiteSim + LiteSync + Luban.Tables（表值可热更，显式登记）；
        · 不参与分区（非玩家侧）＝RoomServer*、MetaServer*、LiteTesting*、LiteCodeGen.Editor、
          DisciplineScanner*、ReplayTool、测试工程、Unity.Pipeline*、MCPForUnity*。
          非玩家侧不在玩家包的热更面上，把它算成 hot 只会产出「分区定义产物」而非实锤。
        精确程序集名优先于前缀兜底：LiteCodeGen.Runtime（AOT）与 LiteCodeGen.Editor（非玩家侧）
        因此能分开；未点名的 LiteCodeGen.* 仍按前缀归非玩家侧。
  AR3-2 只判 AOT -> hot 一条边：AOT -> 不参与分区、不参与分区 -> 任意，都不报。
  AR3-3 引用边两条来源都覆盖：asmdef `references`（按名或 GUID）与 csproj
        `ProjectReference`。Editor-only 程序集不参与玩家侧 AOT/hot 分区，跳过。
  AR3-4 外部程序集（Unity 包、第三方）不属于任何一侧，跳过。
  AR3-5 分区表未点名的一方程序集按 hot 兜底，并在输出里点名（宁严勿漏）。

⚠ 分区表是**可替换配置**（PARTITION）。权威来源见
  Docs/设计文档/客户端/内容/热更与内容发布专项设计.md 的「AOT/hot 分界」章节
  （该章节正在补齐；当前值按规范 02 §1.14 的文字裁决填写）。
  真实的 AOT/hot 集合要等 HCLR P2 给出。
  因此本守卫**现阶段默认 warning 级**（有违规也退出 0，只打 WARN），
  `--strict` 升为 error（退出 1）。
  **待 HCLR P2 给出真实分区后：替换 PARTITION 的 assemblies/paths，删掉 --strict，
  把默认行为改为 error（default-error），并同步规范 02 §1.14 的判据。**

用法：
  python scripts/gate/hot-boundary-check.py                # warning 级（退出 0）
  python scripts/gate/hot-boundary-check.py --strict       # error 级（违规退出 1）
  python scripts/gate/hot-boundary-check.py --json         # 结构化输出到 stdout
  python scripts/gate/hot-boundary-check.py --project-path <repo>

退出码：0 通过（warning 模式下有违规也是 0）；1 仅 --strict 下有违规。
"""
import argparse
import json
import os
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info  # noqa: E402

TOOL = "quality/hot-boundary"
HYBRIDCLR_SETTINGS = "ProjectSettings/HybridCLRSettings.asset"

# ---------------------------------------------------------------- 配置（可替换分区表）
# 三类分区：aot（预编译进玩家包）/ hot（玩家侧热更面）/ outside（非玩家侧，不参与判定）。
# 待 HCLR P2 给出真实集合后只改这一块；判定逻辑不写死任何程序集名。
PARTITION = {
    "aot": {
        # 四件与合并后的单件同时列出：LiteFramework 收敛中两种形态都会出现。
        "assemblies": (
            "LiteFramework",
            "LiteFramework.Core",
            "LiteFramework.Services",
            "LiteFramework.Adapters",
            "LiteFramework.Hosting",
            "LiteFramework.Editor",
            "LiteNet",
            "LiteGame.Bootstrap",
            "LiteCodeGen.Runtime",
        ),
        "assemblyPrefixes": (),
        "assemblySuffixes": (),
        "paths": (
            "Packages/com.litegame.framework/",
            "Packages/com.litegame.net/",
            "Assets/LiteGame/Bootstrap/",
        ),
    },
    # 玩家侧热更面：产品 LiteGame 全域（Core/Modules.*/Adapters.*/App/Editor*）+ 共享包 + 表程序集。
    "hot": {
        "assemblies": ("Luban.Tables",),
        "assemblyPrefixes": ("LiteGame.", "LiteSim", "LiteSync"),
        "assemblySuffixes": (),
        "paths": (
            "Assets/LiteGame/",
            "Assets/GameData/Generated/",
            "Packages/com.litegame.sim/",
            "Packages/com.litegame.sync/",
        ),
    },
    # 非玩家侧：服务端 / 工具 / 测试 / 代码生成。不参与 AOT|hot 判定（裁决：不报边）。
    "outside": {
        "assemblies": ("ReplayTool", "LiteCodeGen.Editor"),
        "assemblyPrefixes": (
            "RoomServer",
            "MetaServer",
            "LiteTesting",
            # LiteCodeGen.* 的前缀兜底；精确名 LiteCodeGen.Runtime 已在 AOT 优先命中。
            "LiteCodeGen",
            "DisciplineScanner",
            "Unity.Pipeline",
            "MCPForUnity",
        ),
        "assemblySuffixes": ("Tests",),  # LiteGame.EditModeTests / *.Tests 等测试工程
        "paths": (
            "Assets/RoomServer/",
            "Assets/LiteTesting/",
            "Assets/LiteCodeGen/",
            "Assets/Tests/",
            "Assets/Tools/DisciplineScanner/",
            "Packages/com.unity.pipeline/",
            "Packages/MCPForUnity/",
            "Server/",
            "Tests/",
        ),
    },
    # 仓库内一方程序集的根（不含这些根的 asmdef 一律算外部，不判）。
    "firstPartyRoots": (
        "Assets/",
        "Packages/com.litegame.",
        "Server/",
        "Tests/",
    ),
    # 不参与扫描的随包第三方（与 dir-flat-lint / comment-lint 口径一致）。
    "excludedRoots": (
        "Assets/ThirdParty/",
        "Packages/com.tuyoogame.yooasset/",
        "Packages/com.code-philosophy.luban/",
        "Packages/com.code-philosophy.hybridclr/",
    ),
}
SKIP_DIRS = {"obj", "bin", "__pycache__", "node_modules", "Library", "Temp", "Logs", ".git",
             ".vs", ".dotnet"}
GUID_REF = re.compile(r"^GUID:([0-9a-fA-F]{32})$")
PROJECT_REF = re.compile(r"<ProjectReference\s+Include=\"([^\"]+)\"")
ASSEMBLY_NAME = re.compile(r"<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>")
HOT_LIST = re.compile(r"^\s*-\s*(\S+)\s*$")


def rel_posix(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def is_excluded(rel: str) -> bool:
    if any(rel == p.rstrip("/") or rel.startswith(p) for p in PARTITION["excludedRoots"]):
        return True
    return any(part in SKIP_DIRS for part in rel.split("/"))


def is_first_party(rel: str) -> bool:
    return any(rel.startswith(p) for p in PARTITION["firstPartyRoots"]) and not is_excluded(rel)


def side_matches(side: str, path_rel: str, name: str) -> bool:
    """是否命中某一侧的显式规则（不含未点名一方程序集的 hot 兜底）。"""
    spec = PARTITION[side]
    if name:
        if name in spec["assemblies"]:
            return True
        if any(name.startswith(prefix) for prefix in spec["assemblyPrefixes"]):
            return True
        if any(name.endswith(suffix) for suffix in spec["assemblySuffixes"]):
            return True
    return any(path_rel == p.rstrip("/") or path_rel.startswith(p) for p in spec["paths"])


def _exact_side(name: str) -> str:
    """精确程序集名优先：同一程序集名只能归一侧，胜于任何前缀/后缀兜底。"""
    if not name:
        return ""
    for side in ("aot", "outside", "hot"):
        if name in PARTITION[side]["assemblies"]:
            return side
    return ""


def partition_of(path_rel: str, name: str) -> str:
    """返回 aot / hot / outside / external；outside＝非玩家侧，不参与 AOT|hot 判定。

    精确名优先（LiteCodeGen.Runtime 归 AOT，LiteCodeGen.Editor 归非玩家侧）。
    其后顺序 aot -> outside -> hot：`Assets/LiteGame/Bootstrap/` 是 AOT 但挂在
    `Assets/LiteGame/` 之下；测试工程（`*.Tests`、`Assets/Tests/`）在 `LiteGame.`
    前缀下，必须先于 hot 命中。
    """
    exact = _exact_side(name)
    if exact:
        return exact
    for side in ("aot", "outside"):
        if side_matches(side, path_rel, name):
            return side
    if side_matches("hot", path_rel, name):
        return "hot"
    if is_first_party(path_rel):
        return "hot"  # 兜底：未点名的一方程序集按 hot 计（宁严勿漏，见 docstring AR3-5）
    return "external"


def is_unlisted_first_party(path_rel: str, name: str) -> bool:
    """一方程序集但三张表都没点名 ⇒ 靠兜底判成 hot，输出里单独点名。"""
    if any(side_matches(side, path_rel, name) for side in ("aot", "hot", "outside")):
        return False
    return is_first_party(path_rel)


def iter_files(root: Path, suffixes):
    """只走一方程序集可能落地的顶层根；避免扫 Library/Builds 之类大目录。"""
    for top in ("Assets", "Packages", "Server", "Tests"):
        base = root / top
        if not base.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            rel_dir = rel_posix(Path(dirpath), root)
            dirnames[:] = [d for d in dirnames
                           if d not in SKIP_DIRS and not d.startswith(".")
                           and not is_excluded(rel_dir + "/" + d)]
            for name in filenames:
                if name.endswith(suffixes):
                    rel = rel_dir + "/" + name if rel_dir != "." else name
                    if not is_excluded(rel):
                        yield Path(dirpath) / name, rel


def load_asmdefs(root: Path):
    """返回 (entries, by_name, by_path, guid→path)。GUID 引用按 .meta 反查。"""
    entries, by_name, by_path = [], {}, {}
    for path, rel in iter_files(root, (".asmdef",)):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError) as exc:
            entries.append({"path": rel, "name": "", "references": [], "error": str(exc)})
            continue
        entry = {
            "path": rel,
            "name": data.get("name", ""),
            "references": [str(r) for r in (data.get("references") or [])],
            "includePlatforms": data.get("includePlatforms") or [],
        }
        entries.append(entry)
        by_path[rel] = entry
        if entry["name"]:
            by_name[entry["name"]] = entry
    guid_map = {}
    for path, rel in iter_files(root, (".asmdef.meta",)):
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})", text, re.M)
        if m:
            guid_map[m.group(1).lower()] = rel[:-len(".meta")]
    return entries, by_name, by_path, guid_map


def is_editor_only(entry) -> bool:
    return (entry.get("includePlatforms") or []) == ["Editor"]


def load_csprojs(root: Path):
    out = []
    for path, rel in iter_files(root, (".csproj",)):
        try:
            text = path.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        name = ASSEMBLY_NAME.search(text)
        out.append({
            "path": rel,
            "name": name.group(1) if name else path.stem,
            "refs": [m.group(1) for m in PROJECT_REF.finditer(text)],
        })
    return out


def resolve_asmdef_ref(reference: str, by_name, by_path, guid_map):
    """返回被引用 asmdef 的 entry；外部/无法解析返回 None。"""
    m = GUID_REF.match(reference)
    if m:
        target = guid_map.get(m.group(1).lower())
        return by_path.get(target) if target else None
    return by_name.get(reference)


def resolve_csproj_ref(root: Path, referrer_rel: str, include: str):
    """解析 ProjectReference 到仓库相对路径（posix）；越界返回 None。"""
    base = os.path.dirname(referrer_rel)
    joined = os.path.normpath(os.path.join(base, include.replace("\\", "/"))).replace("\\", "/")
    if joined.startswith("../") or joined == "..":
        return None
    return joined


def hybridclr_hot(root: Path):
    path = root / HYBRIDCLR_SETTINGS
    if not path.is_file():
        return []
    names, collecting = [], False
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if line.startswith("hotUpdateAssemblies:"):
            collecting = True
            continue
        if collecting:
            if line.startswith("- "):
                names.append(line[2:].strip())
            elif line and not line.startswith("-"):
                break
    return names


def scan(root: Path):
    asmdefs, by_name, by_path, guid_map = load_asmdefs(root)
    csprojs = load_csprojs(root)
    violations, skipped_editor, checked, unlisted = [], 0, Counter(), set()

    for entry in asmdefs:
        if entry.get("error"):
            violations.append({"source": "asmdef", "from": entry["path"], "to": "?",
                               "reason": f"asmdef 解析失败：{entry['error']}"})
            continue
        if is_editor_only(entry):
            skipped_editor += 1
            continue
        checked["asmdef"] += 1
        side = partition_of(entry["path"], entry["name"])
        checked["asmdef:" + side] += 1
        if is_unlisted_first_party(entry["path"], entry["name"]):
            unlisted.add(entry["name"] or entry["path"])
        if side != "aot":
            continue
        for reference in entry["references"]:
            target = resolve_asmdef_ref(reference, by_name, by_path, guid_map)
            if target is None or target.get("error"):
                continue  # 外部程序集：不属于 AOT/hot 任一侧
            target_side = partition_of(target["path"], target["name"])
            if target_side == "hot":
                violations.append({
                    "source": "asmdef", "from": entry["path"], "fromName": entry["name"],
                    "to": target["name"], "toPath": target["path"],
                })

    by_csproj = {c["path"]: c for c in csprojs}
    for project in csprojs:
        side = partition_of(project["path"], project["name"])
        checked["csproj"] += 1
        checked["csproj:" + side] += 1
        if is_unlisted_first_party(project["path"], project["name"]):
            unlisted.add(project["name"])
        if side != "aot":
            continue
        for include in project["refs"]:
            target_rel = resolve_csproj_ref(root, project["path"], include)
            if not target_rel:
                continue
            target = by_csproj.get(target_rel)
            target_name = target["name"] if target else os.path.basename(target_rel)[:-len(".csproj")]
            target_side = partition_of(target_rel, target_name)
            if target_side == "hot":
                violations.append({
                    "source": "csproj", "from": project["path"], "fromName": project["name"],
                    "to": target_rel, "toName": target_name,
                })

    # HCLR 现状漂移（信息行）：HybridCLR 的 hotUpdateAssemblies 是旧口径，
    # 与配置分区不一致、且会影响 AOT 判定的项单独列出（HCLR P2 前以本配置为准）。
    drift = []
    hclr = set(hybridclr_hot(root))
    if hclr:
        aot_targets = set()
        for entry in asmdefs:
            if is_editor_only(entry) or partition_of(entry["path"], entry["name"]) != "aot":
                continue
            for reference in entry["references"]:
                target = resolve_asmdef_ref(reference, by_name, by_path, guid_map)
                if target:
                    aot_targets.add(target["name"])
        for name, entry in sorted(by_name.items()):
            side = partition_of(entry["path"], name)
            if side not in ("aot", "hot"):
                continue  # 非玩家侧 / 外部：不参与分区，无漂移可言
            if side == "aot" and name not in hclr:
                continue
            if side == "hot" and name not in aot_targets:
                continue
            config_side = "hot" if side == "hot" else "aot"
            hclr_side = "hot" if name in hclr else "aot"
            if config_side != hclr_side:
                drift.append({"assembly": name, "config": config_side, "hybridclr": hclr_side})
    return {
        "partition": PARTITION,
        "violations": violations,
        "checked": dict(checked),
        "skippedEditorOnly": skipped_editor,
        "unlistedFirstParty": sorted(unlisted),
        "hybridclrDrift": drift,
        "asmdefs": len(asmdefs),
        "csprojs": len(csprojs),
    }


def main(argv=None) -> int:
    configure_stdio()
    parser = argparse.ArgumentParser(
        description="AR3：AOT 程序集不得引用 hot 程序集（asmdef references + csproj ProjectReference）")
    parser.add_argument("--project-path", default=None, help="仓库根（默认脚本上两级）")
    parser.add_argument("--strict", action="store_true",
                        help="有违规即退出 1（默认 warning：只打 WARN，退出 0）")
    parser.add_argument("--json", action="store_true", help="结构化结果输出到 stdout")
    args = parser.parse_args(argv)

    project = Path(args.project_path).resolve() if args.project_path else \
        Path(__file__).resolve().parents[2]
    result = scan(project)

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=1))
    violations = result["violations"]
    checked = result["checked"]
    info(TOOL, f"partition: AOT assemblies={len(PARTITION['aot']['assemblies'])} "
               f"paths={len(PARTITION['aot']['paths'])}; "
               f"hot prefixes={len(PARTITION['hot']['assemblyPrefixes'])} "
               f"paths={len(PARTITION['hot']['paths'])}; "
               f"outside patterns="
               f"{len(PARTITION['outside']['assemblies']) + len(PARTITION['outside']['assemblyPrefixes']) + len(PARTITION['outside']['assemblySuffixes']) + len(PARTITION['outside']['paths'])}")
    info(TOOL, f"asmdefs checked: aot={checked.get('asmdef:aot', 0)} "
               f"hot={checked.get('asmdef:hot', 0)} "
               f"outside={checked.get('asmdef:outside', 0)} "
               f"external={checked.get('asmdef:external', 0)}; "
               f"csproj checked: aot={checked.get('csproj:aot', 0)} "
               f"hot={checked.get('csproj:hot', 0)} "
               f"outside={checked.get('csproj:outside', 0)} "
               f"external={checked.get('csproj:external', 0)}")
    info(TOOL, f"skipped editor-only asmdef(s): {result['skippedEditorOnly']}")
    if result["unlistedFirstParty"]:
        info(TOOL, "INFO 未点名的一方程序集按 hot 兜底（应补 PARTITION）："
                   + ", ".join(result["unlistedFirstParty"]))

    for item in result["hybridclrDrift"]:
        info(TOOL, f"INFO HCLR drift: {item['assembly']} 配置={item['config']} / "
                   f"HybridCLR={item['hybridclr']}（HCLR P2 前以配置为准）")

    if violations:
        for item in violations:
            if item.get("reason"):
                print(f"[{TOOL}] WARN: {item['from']}: {item['reason']}")
            elif item["source"] == "asmdef":
                print(f"[{TOOL}] WARN: asmdef {item['fromName']} ({item['from']}) "
                      f"-> {item['to']} (hot)")
            else:
                print(f"[{TOOL}] WARN: csproj {item['fromName']} ({item['from']}) "
                      f"-> {item['toName']} (hot)")
        message = (f"{len(violations)} 条 AOT→hot 引用（AR3）；分区表待 HCLR P2 —— "
                   f"默认 warning，--strict 升 error")
        if args.strict:
            return fail(TOOL, message)
        print(f"[{TOOL}] WARN: {message}")
        return EXIT_OK

    info(TOOL, "OK: 无 AOT→hot 引用")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
