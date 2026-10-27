# -*- coding: utf-8 -*-
"""A3 结构验收探针（run-local，先行红测）。

重构批的交付物就是结构：断言三组协作件已外提、主类不再持有对应域状态。
基线必红、施工后必绿。行为回归由既有 L1/EditMode 套件与 Play 冒烟承担，本探针不替代。

用法：python .workflow/active/client-audit-a3/red-structure-check.py
退出码：0 = 结构达标；1 = 仍有未达标项。
"""

import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parents[3]
BATTLE = ROOT / "Assets/LiteGame/App/Presentation/Battle"
BIND = ROOT / "Assets/LiteClient/UI/Bind"
FSM = ROOT / "Assets/LiteFramework/Scripts/Core/Fsm"

failures = []


def require_file(path):
    if not path.is_file():
        failures.append(f"缺文件: {path.relative_to(ROOT)}")


def require_absent(path, pattern, why):
    text = path.read_text(encoding="utf-8", errors="replace")
    if re.search(pattern, text, re.MULTILINE):
        failures.append(f"{path.relative_to(ROOT)} 仍存在 {why}")


def require_present(path, pattern, why):
    text = path.read_text(encoding="utf-8", errors="replace")
    if not re.search(pattern, text, re.MULTILINE):
        failures.append(f"{path.relative_to(ROOT)} 缺少 {why}")


def main():
    # A3-1 BattleViewRig
    for name in (
        "BattleViewAssets.cs",
        "BattleViewSceneAssembler.cs",
        "BattleHudAssembler.cs",
        "BattleFeedbackAssembler.cs",
        "BattleLookController.cs",
        "AuthoritativeCombatFeedback.cs",
    ):
        require_file(BATTLE / name)
    rig = BATTLE / "BattleViewRig.cs"
    require_absent(rig, r"\basync\s+UniTask(?:<[^>]+>)?\s+Acquire\w+Async", "逐项取件方法（应并入 BattleViewAssets）")
    for field in (
        # HUD 域
        "_crosshair", "_hud", "_rankboard", "_killFeed", "_netStats", "_respawnHud",
        "_menuClick", "_crosshairFeel", "_hudLease", "_hudInstance", "_hudView",
        # 反馈域
        "_hitFeedback", "_authorityFeedback", "_damageNumbers", "_muzzleFlash", "_impactFx",
        "_battleAudio", "_remoteFire", "_vfx", "_audio", "_worldClock",
        # 视角域
        "_pointer", "_aimGate", "_lookDeltaOf", "_aimPointOf", "_lastRespawnSerial",
        # 取件/装配域
        "_content", "_themeProvider", "_prefab", "_prefabLease", "_laser", "_laserBeam", "_laserLease",
        "_protectionFx", "_protectionShellLease", "_damageNumberLease", "_damageNumberMaterials",
        "_clipManifestLease", "_audioLeases", "_locomotion",
    ):
        require_absent(rig, rf"^\s*(?:private|protected|internal|public)\s+[^\n;]*\b{field}\b",
                       f"域状态/引用 {field}（应归协作件）")
    require_absent(rig, r"class\s+AuthoritativeCombatFeedback", "嵌套反馈类（应外提一文件）")

    # A3-2 UIBindIndex
    for name in ("UIBindLocale.cs", "UIBindEvents.cs", "UIBindListSource.cs"):
        require_file(BIND / name)
    index = BIND / "UIBindIndex.cs"
    for field in ("_listSources", "_listClick", "_ltext", "_locale", "_controlUnsub"):
        require_absent(index, rf"^\s*(?:private|protected|internal|public)\s+[^\n;]*\b{field}\b",
                       f"绑定域状态 {field}（应归协作件）")

    # A3-3 树表示法
    core = FSM / "StageMachineCore.cs"
    for field in ("_composite", "_parent", "_history", "_roots"):
        require_absent(core, rf"^\s*(?:private|protected|internal|public)\s+[^\n;]*\b{field}\b",
                       f"树字段 {field}（应收回层级机）")
    require_absent(core, r"\bvoid\s+BuildTree\s*\(", "BuildTree（应收回层级机）")
    hierarchical = FSM / "HierarchicalStageMachine.cs"
    require_present(hierarchical, r"\bvoid\s+BuildTree\s*\(", "BuildTree 定义")
    require_present(hierarchical, r"Dictionary<TId,\s*CompositeSpec<TId>>\s+_composite", "复合树字段")

    if failures:
        print(f"[RED] A3 结构探针未达标（{len(failures)} 项）：")
        for item in failures:
            print(" -", item)
        return 1
    print("[GREEN] A3 结构探针全部达标")
    return 0


if __name__ == "__main__":
    sys.exit(main())
