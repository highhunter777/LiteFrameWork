# -*- coding: utf-8 -*-
"""平台脚本宏（scriptingDefineSymbols）期望集校验面。

期望宏集登记在 toolchain 清单的 `platformDefines`——它是"平台脚本宏差集逐项裁决"
的唯一登记点。`toolchain.py check` 读 `ProjectSettings.asset` 的
`scriptingDefineSymbols` 段逐平台比对：缺宏/多宏都算错误（切平台编出不同代码路径
是静默失效的典型形态，不能靠自觉）。

Findings 是 error（不是 warning）——宏集漂移必须当场拦下，才配得上"守卫"二字。
"""

import re
from pathlib import Path

PROJECT_SETTINGS = Path("ProjectSettings") / "ProjectSettings.asset"
SECTION = "scriptingDefineSymbols"
SECTION_PREFIX = SECTION + ":"
_ENTRY = re.compile(r"^(\s+)([^:\s][^:]*):\s*(.*)$")


def split_defines(value):
    """`A;B;;` → ['A', 'B']（Unity 以分号分隔，空项无意义）。"""
    value = (value or "").strip().strip("'\"")
    if not value or value in ("{}", "[]"):
        return []
    return [item.strip() for item in value.split(";") if item.strip()]


def parse_define_symbols(text):
    """读 `scriptingDefineSymbols` 段 → {平台: [宏...]}；无该段/行内空映射返回 {}。"""
    symbols = {}
    in_section = False
    section_indent = 0
    for raw in (text or "").replace("\r\n", "\n").replace("\r", "\n").split("\n"):
        stripped = raw.strip()
        if not in_section:
            if not stripped.startswith(SECTION_PREFIX):
                continue
            in_section = True
            section_indent = len(raw) - len(raw.lstrip())
            tail = stripped[len(SECTION_PREFIX):].strip()
            if tail and tail != "{}":
                return symbols  # 行内形态（YAML 列表/标量）：本项目不使用，按"无平台键"处理
            continue
        if not stripped:
            continue
        if len(raw) - len(raw.lstrip()) <= section_indent:
            break  # 段结束：回到同级或更外层的键
        match = _ENTRY.match(raw)
        if not match:
            continue
        symbols[match.group(2).strip()] = split_defines(match.group(3))
    return symbols


def check_defines(manifest, root, settings_path=None):
    """返回 [(level, message)]——期望宏集与工程设置不一致即为 error。"""
    expected_by_platform = {
        platform: expected
        for platform, expected in (manifest.get("platformDefines") or {}).items()
        if not platform.startswith("$")
    }
    if not expected_by_platform:
        return []  # 未登记期望集的平台不受本校验面把守

    path = Path(settings_path) if settings_path else Path(root) / PROJECT_SETTINGS
    if not path.is_file():
        return [("error", f"platformDefines: 工程设置文件不存在（{PROJECT_SETTINGS}）——宏集校验面输入缺失")]

    actual = parse_define_symbols(path.read_text(encoding="utf-8-sig", errors="replace"))
    issues = []
    for platform, expected in sorted(expected_by_platform.items()):
        if not isinstance(expected, list) or not expected:
            issues.append(("error", f"platformDefines:{platform} 必须是非空宏列表（空集等于放弃守卫）"))
            continue
        expected_set = set(expected)
        actual_set = set(actual.get(platform) or [])
        missing = sorted(expected_set - actual_set)
        extra = sorted(actual_set - expected_set)
        if missing:
            issues.append((
                "error",
                f"platformDefines:{platform} 缺少宏 {', '.join(missing)}"
                f"——ProjectSettings.asset scriptingDefineSymbols 与期望集不一致",
            ))
        if extra:
            issues.append((
                "error",
                f"platformDefines:{platform} 多出未登记宏 {', '.join(extra)}"
                f"——先核对平台差集裁决（或改期望集），别让平台分叉静默扩散",
            ))
    return issues
