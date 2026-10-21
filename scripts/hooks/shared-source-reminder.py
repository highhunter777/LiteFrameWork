# -*- coding: utf-8 -*-
"""PostToolUse reminder for BuildHash sources, tables and Unity assets.

Mounted by ZCode (.zcode/config.json) and Claude Code (.claude/settings.json).
Input: hook event JSON on stdin (tool_input.file_path / notebook_path).
Output: client-specific JSON when a red line matches; silent exit 0 otherwise.
This is a reminder surface, not a gate; the authoritative checks live in
scripts/codegen/gen-build-hash.py.
"""

import json
import os
import sys
from pathlib import Path

try:
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    from _common import configure_stdio  # noqa: E402
except Exception:
    def configure_stdio():
        pass


def _message(relative):
    build_hash = "python scripts/codegen/gen-build-hash.py"
    if relative.endswith("buildhash.g.cs"):
        return "[红线1] BuildHash.g.cs 是生成物,勿手改——由 gen-build-hash.py 从共享源重生成。"
    if relative.endswith(".proto"):
        return (
            "[红线1] 改了 .proto:先 python scripts/codegen/gen_proto.py --protoc <protoc.exe> "
            f"重生成,再 {build_hash} 。Proto/Generated 生成物勿手改。"
        )
    if relative.startswith("assets/litenet/proto/generated/"):
        return "[红线1] Proto/Generated 是 gen_proto.py 的产物,勿手改;改行为去改 .proto 源再重生成。"
    if relative.startswith(
        (
            "assets/litesim/core/scripts/",
            "assets/litesim/core/systems/",
            "assets/litenet/protocol/",
            "assets/litenet/transport/security/",
        )
    ):
        return (
            "[红线1] 触碰 BuildHash 共享源集,收尾必跑: "
            f"{build_hash} ——漏跑=出包门禁被拦/两端常量不同步时 Join 拒进房。详见 skill: build-hash-sync"
        )
    if relative.startswith("server/metaserver/") and relative.endswith((".cs", ".csproj")):
        return (
            "[P1④] 触碰 Meta 源集:Meta 构建身份(Server/MetaServer/Host/BuildHash.g.cs,启动日志 build= 那个)由 "
            f"{build_hash} 生成,收尾必跑(一次写两个常量:共享握手红线 + Meta 身份;发布 Meta 前跑 --check 复算)。"
        )
    if relative.startswith(
        (
            "luban/data/",
            "luban/defines/",
            "assets/gamedata/config/",
            "assets/litegame/lua/cfg/",
        )
    ):
        return (
            "[红线1] 触碰表数据/表生成物:表源改动跑 Luban/gen.bat 双端重生成 + "
            f"{build_hash} ;.bytes 与 Lua/Cfg 是生成物勿手改。"
        )
    if relative.startswith("assets/") and relative.endswith(
        (".unity", ".prefab", ".mat", ".asset", ".controller", ".anim", ".meta")
    ):
        return "[红线4] Unity 序列化资产/.meta 禁直改文件,一律走 unity-pipeline CLI。详见 skill: unity-asset-pipeline"
    return None


def main():
    configure_stdio()
    raw = sys.stdin.read()
    if not raw.strip():
        return 0
    event = json.loads(raw)
    tool_input = event.get("tool_input") or {}
    path = tool_input.get("file_path") or tool_input.get("notebook_path")
    if not path:
        return 0

    root = os.environ.get("ZCODE_PROJECT_DIR")
    is_claude = False
    if not root and os.environ.get("CLAUDE_PROJECT_DIR"):
        root = os.environ["CLAUDE_PROJECT_DIR"]
        is_claude = True

    relative = str(path).replace("\\", "/").lower()
    if root:
        project_root = str(root).replace("\\", "/").rstrip("/").lower()
        if relative.startswith(project_root + "/"):
            relative = relative[len(project_root) + 1 :]
        elif relative == project_root:
            return 0

    message = _message(relative)
    if not message:
        return 0

    if is_claude:
        payload = {"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": message}}
    else:
        payload = {"additionalContext": message}
    print(json.dumps(payload, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        raise SystemExit(0)
