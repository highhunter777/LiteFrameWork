# -*- coding: utf-8 -*-
"""L0 dependency and secret scan.

Checks:
  1) manifest/lock contain zero personal absolute file: paths
  2) git deps are pinned to a commit (.git#<40-hex>)
  3) tracked vendored packages keep a LICENSE file
  4) secret patterns in git-tracked text files
  5) toolchain manifest/docs/surfaces are consistent
  6) GM HTTP contract matches server routes, editor page and CLI

Exit codes: 0 = pass, 1 = violations.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root, warn  # noqa: E402
from _gm_contract import check_consistency as check_gm_contract  # noqa: E402

TEXT_EXTENSIONS = re.compile(r"\.(json|ps1|psm1|py|cs|md|txt|yml|yaml|xml|config|bat|sh)$")
SECRET_PATTERNS = (
    (re.compile(r"-----BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----"), "private key material"),
    (re.compile(r"AKIA[0-9A-Z]{16}"), "AWS access key"),
    (re.compile(r"ghp_[A-Za-z0-9]{36}"), "GitHub token"),
    (re.compile(r"(?i)(api[_-]?key|secret|password)\s*[:=]\s*[\"'][A-Za-z0-9+/=_.-]{24,}[\"']"), "hardcoded credential"),
)
# Scanner tests embed intentional fake credentials.
SECRET_EXCLUDES = {"l0-dep-scan.py", "JoinAdmissionGateTests.cs", "test_l0_dep_scan.py"}


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="L0 dependency and secret scan")
    parser.add_argument("--project-path", default=None, help="project root (default: repository root)")
    return parser.parse_args(argv)


def git_lines(project, *args):
    try:
        result = subprocess.run(
            ["git", *args],
            cwd=str(project),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    except FileNotFoundError:
        return None
    if result.returncode != 0:
        return None
    return [line for line in result.stdout.splitlines() if line]


def check_dependency_files(project, violations):
    for relative in ("Packages/manifest.json", "Packages/packages-lock.json"):
        path = project / relative
        if not path.is_file():
            violations.append(f"{relative} missing (dependency declarations must be committed)")
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            if re.search(r"file:[A-Za-z]:", line):
                violations.append(f"{relative} line {lineno}: personal absolute path dep - {line.strip()}")
            if re.search(r'\.git"', line) and not re.search(r"\.git#[0-9a-f]{40}", line):
                violations.append(
                    f"{relative} line {lineno}: floating git dep (pin to commit: .git#<40-hex>) - {line.strip()}"
                )


def check_vendored_licenses(project, violations):
    packages = project / "Packages"
    if not packages.is_dir():
        return
    for package_json in sorted(packages.glob("*/package.json")):
        tracked = git_lines(project, "ls-files", "--", f"Packages/{package_json.parent.name}/package.json")
        if not tracked:
            continue
        licenses = [
            path
            for path in package_json.parent.iterdir()
            if path.is_file() and re.match(r"^(LICENSE|License)", path.name)
        ]
        if not licenses:
            violations.append(f"vendored package {package_json.parent.name} has no LICENSE file (redistribution requires it)")


def check_secrets(project, violations):
    tracked = git_lines(project, "ls-files")
    if tracked is None:
        warn("quality/l0", "git unavailable or not a repo - secret scan skipped (CI always runs inside a repo)")
        return
    for relative in tracked:
        if not TEXT_EXTENSIONS.search(relative):
            continue
        path = project / relative
        if not path.is_file() or path.name in SECRET_EXCLUDES:
            continue
        try:
            content = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for pattern, name in SECRET_PATTERNS:
            if pattern.search(content):
                violations.append(
                    f"possible secret ({name}): {relative} - review manually; if false positive register it in "
                    "l0-dep-scan.py SECRET_EXCLUDES"
                )


def check_toolchain(project, violations):
    cli = project / "scripts" / "toolchain.py"
    if not cli.is_file():
        violations.append("toolchain CLI missing: scripts/toolchain.py")
        return
    result = subprocess.run([sys.executable, str(cli), "check"], cwd=str(project))
    if result.returncode != 0:
        violations.append("toolchain check failed: manifest/docs/surfaces are inconsistent (see output above)")


def check_gm_contract_file(project, violations):
    violations.extend(check_gm_contract(project))


def check_server_packages(project, violations):
    # 服务端 NuGet 登记面：PackageReference 必须显式钉死版本（浮动/缺版本 = 不可复现）。
    # 原先 l0 只覆盖 Unity manifest/lock 与密钥，服务端包无门禁——引入第二个 NuGet 后补上。
    import xml.etree.ElementTree as ET

    server = project / "Server"
    if not server.is_dir():
        return
    for csproj in sorted(server.rglob("*.csproj")):
        relative = csproj.relative_to(project).as_posix()
        try:
            root = ET.parse(str(csproj)).getroot()
        except ET.ParseError as error:
            violations.append(relative + ": csproj 解析失败 - " + str(error))
            continue
        for node in root.iter():
            if not node.tag.endswith("PackageReference"):
                continue
            name = node.get("Include") or node.get("Update")
            version = node.get("Version") or node.get("VersionOverride")
            if not name:
                violations.append(relative + ": PackageReference 缺 Include")
                continue
            if not version:
                version = (node.findtext("Version") or "").strip()
            if not version:
                violations.append(relative + ": " + name + " 未显式钉死 Version（浮动依赖不可复现）")
                continue
            if any(ch in version for ch in "*[](),"):
                violations.append(relative + ": " + name + " 版本不是精确值：" + version)


def main(argv=None):

    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    violations = []

    check_dependency_files(project, violations)
    check_vendored_licenses(project, violations)
    check_secrets(project, violations)
    check_toolchain(project, violations)
    check_gm_contract_file(project, violations)
    check_server_packages(project, violations)

    info("quality/l0", f"L0 dependency & secret scan | project: {project}")
    if not violations:
        info("quality/l0", "PASS: deps reproducible, zero floating deps, vendored licenses kept, no secret hits")
        return EXIT_OK
    for violation in violations:
        info("quality/l0", f"FAIL: {violation}")
    return fail("quality/l0", f"L0 scan FAILED: {len(violations)} violation(s)")


if __name__ == "__main__":
    sys.exit(main())
