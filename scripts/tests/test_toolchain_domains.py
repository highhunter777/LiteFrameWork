# -*- coding: utf-8 -*-
"""Domain-form rule contracts (R1 synonyms / R2 doc authority / R3 prefixes)."""

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from _toolchain_domains import check_domains  # noqa: E402
from _toolchain_manifest import load_manifest  # noqa: E402

DOC = "Docs/fixture/domain.md"


def tool(tool_id, category="demo", kind="cli", command=None, docs=DOC, args=None):
    payload = {
        "id": tool_id,
        "title": tool_id,
        "category": category,
        "kind": kind,
        "summary": "fixture",
        "docs": docs,
    }
    if command is not None:
        payload["command"] = command
    if args is not None:
        payload["args"] = args
    return payload


def manifest(tools, category="demo", docs=DOC, allow_docs=None):
    entry = {"id": category, "title": category.upper()}
    if docs is not None:
        entry["docs"] = docs
    if allow_docs is not None:
        entry["allowDocs"] = allow_docs
    return {"schemaVersion": 3, "categories": [entry], "tools": tools, "surfaces": []}


def write_script(root, relative, text):
    path = Path(root) / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")
    return path


class SynonymRuleTests(unittest.TestCase):
    def test_mixed_preview_flags_are_reported(self):
        data = manifest([
            tool("demo.a", args=[{"name": "dry-run", "type": "bool"}]),
            tool("demo.b", args=[{"name": "what-if", "type": "bool"}]),
        ])
        issues = check_domains(data, ROOT)
        self.assertEqual(len(issues), 1)
        self.assertEqual(issues[0][0], "warning")
        self.assertIn("domain demo", issues[0][1])
        self.assertIn("--dry-run", issues[0][1])
        self.assertIn("--what-if", issues[0][1])

    def test_one_name_per_domain_is_clean(self):
        data = manifest([
            tool("demo.a", args=[{"name": "dry-run", "type": "bool"}]),
            tool("demo.b", args=[{"name": "dry-run", "type": "bool"}]),
        ])
        self.assertEqual(check_domains(data, ROOT), [])


class DocAuthorityTests(unittest.TestCase):
    def test_tool_outside_authority_is_reported(self):
        data = manifest([tool("demo.a", docs="Docs/other.md")])
        issues = check_domains(data, ROOT)
        self.assertEqual(len(issues), 1)
        self.assertIn("authority", issues[0][1])

    def test_allow_listed_doc_is_clean(self):
        data = manifest(
            [tool("demo.a", docs="Docs/other.md")],
            allow_docs=["Docs/other.md"],
        )
        self.assertEqual(check_domains(data, ROOT), [])

    def test_missing_authority_is_reported(self):
        data = manifest([tool("demo.a")], docs=None)
        issues = check_domains(data, ROOT)
        self.assertEqual(len(issues), 1)
        self.assertIn("not declared", issues[0][1])


class PrefixRuleTests(unittest.TestCase):
    def test_mixed_roots_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            write_script(temp, "scripts/a/one.py", 'print("[a/one] ok")\n')
            write_script(temp, "scripts/b/two.py", 'print("[b/two] ok")\n')
            data = manifest([
                tool("demo.one", command=["python", "scripts/a/one.py"]),
                tool("demo.two", command=["python", "scripts/b/two.py"]),
            ])
            issues = check_domains(data, temp)
        self.assertEqual(len(issues), 1)
        self.assertIn("2 output-prefix roots", issues[0][1])
        self.assertIn("a: a/one", issues[0][1])

    def test_missing_prefix_is_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            write_script(temp, "scripts/a/quiet.py", "print('no label')\n")
            data = manifest([tool("demo.quiet", command=["python", "scripts/a/quiet.py"])])
            issues = check_domains(data, temp)
        self.assertEqual(len(issues), 1)
        self.assertIn("no stable output prefix", issues[0][1])

    def test_non_script_entry_is_ignored(self):
        data = manifest([
            tool("demo.dotnet", command=["dotnet", "run", "--project", "Server/ReplayTool/ReplayTool.csproj"]),
        ])
        self.assertEqual(check_domains(data, ROOT), [])

    def test_toolbox_dispatch_resolves_to_mapped_script(self):
        with tempfile.TemporaryDirectory() as temp:
            write_script(temp, "scripts/toolbox.py", (
                "TOOLS = {\n"
                "    'alpha': ('gen_alpha.py', '', 'fixture'),\n"
                "}\n"
            ))
            write_script(temp, "scripts/gen_alpha.py", (
                "from _codegen import info\n"
                "TOOL = 'alpha'\n"
                "info(TOOL, 'ok')\n"
            ))
            data = manifest([
                tool("demo.alpha", command=["python", "scripts/toolbox.py", "alpha"]),
            ])
            self.assertEqual(check_domains(data, temp), [])


class RepoBaselineTests(unittest.TestCase):
    """Domain drift must stay at zero.

    Caps stay per-domain (instead of asserting an empty list) so a regression
    names the drifting domain; every cap is 0 after convergence.
    """

    DOMAIN_CAPS = {"deploy": 0, "quality": 0, "build": 0, "content": 0, "env": 0}

    def test_repo_domain_warnings_stay_within_caps(self):
        manifest_data = load_manifest(ROOT / "scripts" / "gate" / "toolchain.json")
        issues = check_domains(manifest_data, ROOT)
        self.assertTrue(all(level == "warning" for level, _ in issues))

        counts = {}
        for _, message in issues:
            self.assertTrue(message.startswith("domain "), message)
            domain = message.split(":", 1)[0][len("domain "):]
            counts[domain] = counts.get(domain, 0) + 1

        self.assertEqual(set(counts) - set(self.DOMAIN_CAPS), set(), f"unknown domain drift: {counts}")
        for domain, cap in self.DOMAIN_CAPS.items():
            self.assertLessEqual(
                counts.get(domain, 0), cap,
                f"domain {domain} grew past its baseline cap ({cap}): {counts}",
            )


if __name__ == "__main__":
    unittest.main()
