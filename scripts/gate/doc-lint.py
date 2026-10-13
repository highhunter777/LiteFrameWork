#!/usr/bin/env python3
"""文档纪律扫描：设计与进度分离 + 审计表状态只由 git 事实回填。

规则：
  D1（违规）设计文档正文内嵌批次**状态**表行——`Docs/设计文档/**/*.md` 中 `| 批N | ... |` 且同行含状态词
             （已完成/已交付/已实施/进行中/未开始/待提交）。依据：AGENTS.md「文档规范」/
             《设计文档 README》§2「设计文档不记录完成状态」/ 红线 12。
  D2（违规）审计表出现裸 `，待提交）` 状态——状态须按 git 事实回填为「已提交 <hash>」或「工作区未提交」。
             依据：修改指南 §2.4 / 文档审查 §2.1 口径「提交状态只由 git 事实回填」。

用法：python scripts/gate/doc-lint.py [--root <repo>] [--warn]
  --warn  仅报告不失败（exit 0），用于过渡期。
退出码：0 = 通过（或有 --warn）；1 = 有违规。
"""
import argparse
import re
import sys
from pathlib import Path

# D1 施工状态启发式：强标记（已交付/已实施）直判；弱标记（已完成/待提交）须伴日期 20xx-xx 才判
# （后者有领域词义，如「每通道一个待提交」，无日期即不判，避免误报）。
D1_STRONG = re.compile(r'已交付|已实施')
D1_WEAK = re.compile(r'已完成|待提交')
D1_DATE = re.compile(r'20\d\d-\d\d')
D2 = re.compile(r'，待提交）')

AUDIT_TABLES = ('代码质量审计-客户端.md', '代码质量审计-服务端.md')

# 过渡基线：findings 降级为 WARN 的文件——挡新增、不挡存量。维护纪律：只减不增。
# 当前为空（服务端卷 §2.1 状态回填已于 2026-10-13 收口）。
PENDING = set()


def scan(root: Path):
    viol, warns = [], []

    def add(tag, rel, line_no, msg, warn_only=False):
        rel = rel.replace('\\', '/')
        entry = f'{tag} {rel}:{line_no} {msg}'
        (warns if (warn_only or rel in PENDING) else viol).append(entry)

    design = root / 'Docs' / '设计文档'
    if design.is_dir():
        for f in sorted(design.rglob('*.md')):
            rel = str(f.relative_to(root))
            for i, line in enumerate(f.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
                row = line.lstrip().startswith('|')
                if row and (D1_STRONG.search(line) or (D1_WEAK.search(line) and D1_DATE.search(line))):
                    add('D1', rel, i, '设计文档表格行疑似施工状态（强标记 已交付/已实施；弱标记 已完成/待提交+日期）', warn_only=True)

    for name in AUDIT_TABLES:
        f = root / 'Docs' / name
        if not f.is_file():
            continue
        rel = str(f.relative_to(root))
        for i, line in enumerate(f.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
            if D2.search(line):
                add('D2', rel, i, '审计表裸「，待提交）」——按 git 事实回填')

    return viol, warns


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default=None)
    ap.add_argument('--warn', action='store_true')
    args = ap.parse_args()

    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[2]
    viol, warns = scan(root)

    for w in warns:
        print(f'[WARN] {w}')
    for v in viol:
        print(f'[FAIL] {v}')

    if viol and not args.warn:
        print(f'doc-lint FAILED: {len(viol)} violation(s), {len(warns)} warning(s)  (root={root})')
        return 1
    print(f'doc-lint OK: {len(viol)} violation(s), {len(warns)} warning(s)  (root={root})')
    return 0


if __name__ == '__main__':
    sys.exit(main())
