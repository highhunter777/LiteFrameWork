#!/usr/bin/env python3
"""文档纪律扫描：设计与进度分离 + 索引覆盖 + 链接有效 + 命名可治理。

规则：
  D1（提醒）设计文档表格行内嵌批次**状态**——`Docs/设计文档/**/*.md` 中表格行含状态词
             （已交付/已实施；或 已完成/待提交 + 日期）。依据：《设计文档 README》§5 / 红线 12。
  D2（违规）审计表出现裸 `，待提交）` 状态——状态须按 git 事实回填为「已提交 <hash>」或「工作区未提交」。
             依据：修改指南 §2.4「提交状态只由 git 事实回填」。
  D3（违规）索引覆盖：`Docs/设计文档/**/*.md`（除 README）必须被《设计文档 README》引用；
             `Docs/施工进度/*.md`（除 README）必须被《施工进度 README》引用。
             依据：《设计文档 README》§5「设计改名或移动时必须在同一变更中更新索引」。
  D4（违规）相对链接或 `#锚` 失效：指向的 .md 不存在，或 `#fragment` 在该文件里找不到对应锚
             （`<a id="x">` / 标题 slug）。依据：同上「移动/合并时全引用同步」。
  D5（违规）文件名含不可治理后缀（最终版/新版/最终/最新版/具体方案/精简版/V2最新版 等），
             或设计文档用裸 `设计` 后缀（无法判定归属 §3 的类型）。
             依据：《设计文档 README》§3「文件名禁止使用不可治理后缀」。
  D6（提醒）设计文档**正文/引用块**（非表格行）含批次状态强标记且带日期——D1 的放宽面，
             用于捕获「状态：已实施（2026-10-16…）」这类头部叙述。
  D7（提醒）`Docs/` 根目录出现未登记的白名单外 .md——根目录只留入口/进度权威/动手指南；
             其余文档按职能入 设计文档/施工进度/项目说明/开发导航/工具链/环境/审计/归档记录。
             依据：Docs/README「目录结构」2026-10-18 分层裁决。

用法：python scripts/gate/doc-lint.py [--root <repo>] [--warn] [--no-links]
  --warn      仅报告不失败（exit 0），用于过渡期。
  --no-links  跳过 D4（大仓或网络盘上跑得慢时用）。
退出码：0 = 通过（或有 --warn）；1 = 有违规。
"""
import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402

# --- D1/D6 施工状态启发式 ---------------------------------------------------
# 强标记（已交付/已实施）直判；弱标记（已完成/待提交）须伴日期 20xx-xx 才判
# （后者有领域词义，如「每通道一个待提交」，无日期即不判，避免误报）。
D1_STRONG = re.compile(r'已交付|已实施')
D1_WEAK = re.compile(r'已完成|待提交')
D1_DATE = re.compile(r'20\d\d-\d\d')
D2 = re.compile(r'，待提交）')

# 弱标记 + 日期 + 出现在设计文档正文，才算 D6（避免把「已完成某动作」当状态）
D6_WEAK = re.compile(r'已完成|已落地|已实施|已交付')

# --- D5 不可治理后缀 / 设计文档类型后缀 --------------------------------------
BAD_SUFFIX = ('最终版', '最新版', '新版', '最终', '具体方案', '精简版', 'v2最新版')
# 《设计文档 README》§3 认可的类型后缀；裸「设计」不在其中（无法判定归属）
DESIGN_TYPES = ('总设计', '专项设计', '层级设计', '分层设计', '策略选型',
                '备选方案', '规范', '参考', '演进路线图')

# --- D4 链接 -----------------------------------------------------------------
MD_LINK = re.compile(r'\[([^\]]*)\]\(([^)\s]+?)\)')
HEADING = re.compile(r'^(#{1,6})\s+(.+?)\s*$')
ANCHOR_ID = re.compile(r'<a id="([^"]+)"></a>')

DOCS = 'Docs'
DESIGN_DIR = 'Docs/设计文档'
PROGRESS_DIR = 'Docs/施工进度'
AUDIT_TABLES = ('审计/代码质量审计-客户端.md', '审计/代码质量审计-服务端.md')
# Docs/ 根目录允许的文档（入口 / 进度权威 / 动手指南）
ROOT_ALLOWED = {'README.md', '待办总览.md', '修改指南.md'}

# 过渡基线：findings 降级为 WARN 的文件——挡新增、不挡存量。维护纪律：只减不增。
PENDING = set()


def slug(text: str) -> str:
    """GitHub 风格标题锚：小写、去标点、空格转 -（中文保留）。"""
    s = text.strip().lower()
    s = re.sub(r'[`*_~\[\](){}]', '', s)
    s = re.sub(r'[^\w\u4e00-\u9fff\- ]', '', s)
    return s.replace(' ', '-')


def anchors_of(path: Path):
    """收集某 md 文件可被 #fragment 命中的锚集合。"""
    out = set()
    try:
        text = path.read_text(encoding='utf-8', errors='replace')
    except OSError:
        return out
    for m in ANCHOR_ID.finditer(text):
        out.add(m.group(1))
    for line in text.splitlines():
        m = HEADING.match(line)
        if m:
            out.add(slug(m.group(2)))
    return out


def scan(root: Path, check_links: bool = True):
    viol, warns = [], []
    anchor_cache = {}

    def add(tag, rel, line_no, msg, warn_only=False):
        rel = rel.replace('\\', '/')
        where = f'{rel}:{line_no}' if line_no else rel
        entry = f'{tag} {where} {msg}'
        (warns if (warn_only or rel in PENDING) else viol).append(entry)

    # ---- D1 / D6：设计文档内嵌施工状态 ----
    design = root / DESIGN_DIR
    if design.is_dir():
        for f in sorted(design.rglob('*.md')):
            rel = f.relative_to(root).as_posix()
            for i, line in enumerate(f.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
                is_row = line.lstrip().startswith('|')
                strong = bool(D1_STRONG.search(line))
                weak = bool(D1_WEAK.search(line)) and bool(D1_DATE.search(line))
                if is_row and (strong or weak):
                    add('D1', rel, i, '设计文档表格行疑似施工状态（强标记 已交付/已实施；弱标记 已完成/待提交+日期）',
                        warn_only=True)
                elif not is_row and strong and D1_DATE.search(line):
                    add('D6', rel, i, '设计文档正文疑似施工状态叙述（状态应移入 Docs/施工进度/，本文只留现行形态+指针）',
                        warn_only=True)

    # ---- D2：审计表裸「待提交」 ----
    for name in AUDIT_TABLES:
        f = root / DOCS / name
        if not f.is_file():
            continue
        rel = f.relative_to(root).as_posix()
        for i, line in enumerate(f.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
            if D2.search(line):
                add('D2', rel, i, '审计表裸「，待提交）」——按 git 事实回填')

    # ---- D7：Docs 根目录白名单 ----
    docs_root = root / DOCS
    if docs_root.is_dir():
        for f in sorted(docs_root.glob('*.md')):
            if f.name in ROOT_ALLOWED:
                continue
            add('D7', f.relative_to(root).as_posix(), 0,
                'Docs 根目录只允许 README/待办总览/修改指南——其余按职能入子目录'
                '（设计文档/施工进度/项目说明/开发导航/工具链/环境/审计/归档记录）',
                warn_only=True)

    # ---- D3：索引覆盖 ----
    for dir_rel, index_rel, kind in (
        (DESIGN_DIR, f'{DESIGN_DIR}/README.md', '设计文档'),
        (PROGRESS_DIR, f'{PROGRESS_DIR}/README.md', '施工进度'),
    ):
        d = root / dir_rel
        idx = root / index_rel
        if not d.is_dir() or not idx.is_file():
            continue
        idx_text = idx.read_text(encoding='utf-8', errors='replace')
        for f in sorted(d.rglob('*.md')):
            if f.name == 'README.md':
                continue
            rel = f.relative_to(root).as_posix()
            # 索引里写的是相对 index 的路径；用文件名与相对路径双重匹配
            rel_to_idx = f.relative_to(idx.parent).as_posix()
            if rel_to_idx in idx_text or f.name in idx_text:
                continue
            add('D3', rel, 0, f'{kind}未入索引——须在同批补入 {Path(index_rel).name}')

    # ---- D5：文件名不可治理后缀 + 设计文档类型后缀 ----
    for f in sorted((root / DOCS).rglob('*.md')):
        stem = f.stem.lower()
        rel = f.relative_to(root).as_posix()
        for bad in BAD_SUFFIX:
            if bad in stem:
                add('D5', rel, 0,
                    f'文件名含不可治理后缀「{bad}」——版本/状态/日期写入文档元数据，不写进文件名')
                break
        else:
            # 设计文档必须是 §3 认可的某一类型
            if rel.startswith(DESIGN_DIR + '/') and f.name != 'README.md':
                if not any(f.stem.endswith(t) for t in DESIGN_TYPES):
                    add('D5', rel, 0,
                        '设计文档命名不合 §3：须以 总设计/专项设计/层级设计/分层设计/策略选型/'
                        '备选方案/规范/参考/演进路线图 之一结尾（裸「设计」不可判定类型）')

    # ---- D4：相对链接与锚 ----
    if check_links:
        targets = sorted((root / DOCS).rglob('*.md')) + [root / 'README.md', root / 'AGENTS.md']
        for f in targets:
            if not f.is_file():
                continue
            rel = f.relative_to(root).as_posix()
            for i, line in enumerate(f.read_text(encoding='utf-8', errors='replace').splitlines(), 1):
                for m in MD_LINK.finditer(line):
                    target = m.group(2).strip()
                    if target.startswith(('http://', 'https://', 'mailto:', '#')):
                        continue
                    path_part, _, frag = target.partition('#')
                    if not path_part.lower().endswith('.md'):
                        continue
                    dest = (f.parent / path_part).resolve()
                    if not dest.is_file():
                        add('D4', rel, i, f'链接失效：{target}（目标文件不存在）')
                        continue
                    if not frag:
                        continue
                    if dest not in anchor_cache:
                        anchor_cache[dest] = anchors_of(dest)
                    if frag not in anchor_cache[dest]:
                        add('D4', rel, i, f'锚失效：{target}（目标文件无 #{frag}）')

    return viol, warns


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default=None)
    ap.add_argument('--warn', action='store_true')
    ap.add_argument('--no-links', action='store_true', help='跳过 D4 链接检查')
    args = ap.parse_args()

    root = Path(args.root).resolve() if args.root else repo_root()
    viol, warns = scan(root, check_links=not args.no_links)

    for w in warns:
        print(f'[quality/doc-lint] WARN: {w}')
    for v in viol:
        print(f'[quality/doc-lint] FAIL: {v}')

    if viol and not args.warn:
        print(f'[quality/doc-lint] FAILED: {len(viol)} violation(s), {len(warns)} warning(s)  (root={root})')
        return 1
    print(f'[quality/doc-lint] OK: {len(viol)} violation(s), {len(warns)} warning(s)  (root={root})')
    return 0


if __name__ == '__main__':
    sys.exit(main())
