# -*- coding: utf-8 -*-
# M10 前置：联机版本 hash 生成器（2026-09-17）。
# 用途：Join 握手 buildHash 校验 + 启动期断言——两端 hash 不等 → 拒绝进房（《状态同步实施方案》§40/§380）。
#
# 覆盖范围（**两端必须逐字节一致的东西**）：
#   Packages/com.litegame.sim/Scripts/**   —— 判定逻辑（Sim 内所有确定性行为）
#   Packages/com.litegame.net/Core/Proto/**          —— 协议单源（.proto + 生成物）
#   Packages/com.litegame.net/Core/Protocol/**       —— 打包解包纯函数
#   SHARED_LINK_FILES                —— 两端源链接共编的映射/布局件（RoomServer.csproj <Compile Include>；
#                                       csproj 每加一条出树链接，这里必须同批加对应条目）
#   注：Systems/ 与 Scripts/ 平级（2026-09-19 目录收纳的最终形态），同属判定源集，
#       漏了会让两端 System 分叉不被握手校验出来，故必须显式列入 TARGETS。
#   **清单与实际不符即 fail-fast**（2026-11-11）：extra_files 声明的文件不存在时直接失败，报出清单名 +
#       缺失相对路径——静默跳过会让"清单写错/文件被重构掉"变成少算，且永不报错（缺口永远发现不了）。
# 不覆盖：表现层/业务/UI（客户端差异不影响判定与协议）；**表数据**（2026-10-14 批 E 移出——
#   判定数值走"表 bytes 双端发布 + 装载后运行期摘要校验"（`CombatConfigDigest` + 客户端校验点），
#   数据可两端热更不出包；留在编译期闭包会与热更版数据互斥（改仓库数据 = 常量重录 + 全员握手换号），
#   且闭包本来也查不出"两端跑着同一常量、数据却不同源"。数据面守卫归
#   `TableLayout.JudgmentFiles` + `CombatConfigDigest`（客户端比对 StartGame.ConfigHash）。
#
# 算法：按**相对路径（Ordinal）排序**后，逐个喂入 "相对路径\0<内容（行尾归一化 + 注释/空白归一）>"
#       到 SHA-256，取前 16 个十六进制字符作 hash 值。确定性要点：
#   ① 排序固定（不依赖文件系统枚举顺序）
#   ② **行尾归一化**（CRLF/CR → LF）——否则不同机器 clone（git autocrlf）会算出不同 hash
#   ③ **注释与排版不计入身份**（2026-11-06）：剥掉 // 与 /* */、空白折叠为单空格，
#      字符串/字符字面量原样保留。理由：注释/缩进不改变编译产物，改注释不该触发两端重发；
#      代码 token 的任何变化（含字符串内容）仍会改 hash。
#   ④ 排除自身输出（BuildHash.g.cs）与 bin/obj/.dotnet/.meta
#
# Toolbox entry:  python scripts/codegen/toolbox.py build-hash [--check | --check-shared]
# Direct entry (deprecated, prints [deprecated]):
#                 python scripts/codegen/gen-build-hash.py [--check | --check-shared]
#
# 两个模式（2026-10-07 起校验时点 = 构建前，开发期不校验——`BuildHashTests`/
# `BuildHashSourceSet.g.cs` 随之退役，规则单源回归本脚本）：
#   生成（默认）  python scripts/codegen/gen-build-hash.py
#       改了 Sim/协议/表后重跑，产出 `Packages/com.litegame.net/Core/Protocol/BuildHash.g.cs`；
#       RoomServer 重建 + Unity 重编译把新常量各自烤进产物（refresh-hash.py 编排这三步）。
#       **同时产出第二个目标**（P1④）：`Server/MetaServer/Host/BuildHash.g.cs` = Meta 自身
#       源集摘要（含源链接件）——取代旧的手写占位 "meta-0.0.0-unwired"；改 Meta 源码后同样重跑。
#   校验 --check  python scripts/codegen/gen-build-hash.py --check
#       按同规则**复算**并比对**两个**常量文件，任一不等即 exit 1。
#   校验 --check-shared  python scripts/codegen/gen-build-hash.py --check-shared
#       只复算共享源集（握手红线）那一个——`scripts/build/build-player.py` 出包门禁步用它：
#       Meta 是独立产物，Meta 常量陈旧不该拦客户端出包（Meta 常量把守点 = 发布 Meta 前跑 --check）。
#       规则只有本脚本一份，复算与生成同源，不存在第二清单漂移的入口。
import argparse
import hashlib
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import fail, info, repo_root, run_tool, signature  # noqa: E402

TOOL = 'build-hash'
ROOT = repo_root()
SHARED_TARGETS = [
    'Packages/com.litegame.sim/Scripts',
    'Packages/com.litegame.sim/Systems',
    'Packages/com.litegame.net/Core/Proto',
    'Packages/com.litegame.net/Core/Protocol',
    # 安全信封（2026-10-03）：装饰在 Transport 上的线上协议层——密文帧形状/握手版本
    # 变更即两端 wire 不兼容，必须进 buildHash 闭包（KCP 适配层本体仍不入闭包：纯传输）。
    'Packages/com.litegame.net/Core/Transport/Security',
    # 同步协议层（2026-10-31 批 6）：仅共享映射/会话子集进闭包；LiteSync/Client（客户端专属）不入。
    'Packages/com.litegame.sync/Shared',
    'Packages/com.litegame.sync/Snapshot',
    'Packages/com.litegame.sync/Input',
    'Packages/com.litegame.sync/Events',
    # Luban 生成的表行类/读取器（gen.bat Pass 2 产出）：两端共编（服务端 csproj 通配 include 全目录、
    # 客户端 `Luban.Tables.asmdef`）——**生成读码身份**（列/类型/schema）在此钉住；表**数值**不在这里
    # （.bytes 不进闭包，见头部"不覆盖"段——值面守卫 = CombatConfigDigest 运行期摘要）。
    'Assets/GameData/Generated',
]
SHARED_LINK_FILES = [
    # 两端**源链接共编**件（不在 SHARED_TARGETS 目录树内——由 RoomServer.csproj 的 <Compile Include>
    # 显式出树链接进服务端编译）。它们同时决定两端的装载映射/路径/摘要取数，改一端而另一端不重建 =
    # 静默分叉且 buildHash 不动。登记规则（与 META_EXTRA_FILES 同一条）：csproj 每加一条出树链接，
    # 这里必须同批加对应条目。
    'Packages/com.litegame.shared/Runtime/Config/SimConfigMapper.cs',   # 表 → CombatValues 投影（两端同链）
    'Packages/com.litegame.shared/Runtime/Config/BattleStrategyTableReader.cs', # 战斗策略键/规则四值投影（两端同链）
    'Packages/com.litegame.shared/Runtime/Config/TableLayout.cs',       # 表清单/判定面清单/取数键单源
    'Packages/com.litegame.shared/Runtime/Config/MapTableProjection.cs', # 地图 → 表投影（两端同链）
]
# 第二目标（P1④）：**Meta 服务自身**的构建身份——"接真"取代原手写占位常量
# （`Host/BuildHash.cs` 的 "meta-0.0.0-unwired"，该文件随本批删除）。
# 覆盖 MetaServer.csproj 的编译输入：`Server/MetaServer/**` + 其源链接件（csproj 里显式
# <Compile Include> 进来的跨端共编文件——漏了它，改票据线格式时 Meta 身份不动，日志会报旧值）。
# **它不是握手红线**：不参与任何准入判定（Lobby 版本准入/票据盖章消费的是房间实例上报的
# LiteNet buildHash），故不挂出包门禁；把守点 = 发布 Meta 产物前跑 --check。
META_TARGETS = [
    'Server/MetaServer',
]
META_EXTRA_FILES = [
    'Assets/RoomServer/Modules/JoinTicketFormat.cs',   # 源链接共编（MetaServer.csproj）
    # 2026-10-12 两处修正：Base64Url.cs 链接自 P2 引入起漏登记（改它 Meta 身份不动 = 静默失联）；
    # MatchStateMachine.cs 为房间明细批新增链接（相位闸单源）。登记规则：csproj 每加一条
    # <Compile Include> 出树链接，这里必须同批加对应条目。
    'Assets/RoomServer/Modules/Base64Url.cs',
    'Assets/RoomServer/Core/MatchStateMachine.cs',
    # Meta HTTP 契约线形状与序列化行为（客户端编译、MetaServer/RoomServer 源链接共编）：
    # 改字段名/口径 = 协议变更，Meta 构建身份必须跟着变。
    # 2026-11-12 P1a：MetaWire 共享源已迁出到 Packages/com.litegame.shared/Runtime/MetaWire/
    # （UPM 包 LiteGame.MetaWire，命名空间不变）——Meta 构建身份仍按本清单取源。
    'Packages/com.litegame.shared/Runtime/MetaWire/Core/MetaError.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Login/GuestLoginRequest.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Login/GuestLoginInfo.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Join/JoinTicketRequest.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Lobby/CreateRoomRequest.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/JoinTicketInfo.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Lobby/RoomsResponse.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Lobby/LobbyInstanceView.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Lobby/LobbyRoomEntry.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Match/MatchesResponse.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Match/MatchRecordInfo.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Instance/InstanceRegisterRequest.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Instance/InstanceRegisterResponse.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Match/MatchResultSubmission.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Match/PlayerResultEntry.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Core/MetaWireEndpoints.cs',
    'Packages/com.litegame.shared/Runtime/MetaWire/Core/MetaWireLimits.cs',
    'Packages/com.litegame.framework/Runtime/Adapters/Newtonsoft/WireJsonSettings.cs',
]
SHARED_OUT_REL = 'Packages/com.litegame.net/Core/Protocol/BuildHash.g.cs'


def shared_out_file(root):
    """Shared-constant output path for one repo root (the single path source:
    refresh-hash / local-chain / doctor all call this, never a hand copy)."""
    return os.path.join(str(root), *SHARED_OUT_REL.split('/'))


SHARED_OUT_FILE = shared_out_file(ROOT)
META_OUT_FILE = os.path.join(ROOT, 'Server', 'MetaServer', 'Host', 'BuildHash.g.cs')
# 生成器自身输出，收集时排除（否则自指不稳）。BuildHash.g.cs 落在 LiteNet/Core/Protocol（= hash 源集目录）
# 里，而收集规则是 `*.cs` 全收——不排除就会"污染自己"，且是**不收敛**的：
# 文件内容含 hash → hash 变 → 文件内容变 → hash 再变……（与源集无关的文件不该影响 hash）。
# BuildHashSourceSet.g.cs 已随开发期守卫退役（2026-10-07），名字保留在排除表里作防御：
# 工作区残留的旧副本不得重新混进闭包。
SELF_NAMES = ('BuildHash.g.cs', 'BuildHashSourceSet.g.cs')
SKIP_DIRS = {'bin', 'obj', '.dotnet', '__pycache__', 'Editor', '.vs'}
# Editor 目录排除（2026-09-18 批③ 修正）：LiteSim.csproj 的 Compile 通配带 Exclude="Scripts/Editor/**"
# ——编辑器脚本不在客户端/服务器共同编译的源集里，喂进 hash 会让"hash 变了但两端行为没变"
# （例如给菜单加一行注释 → 所有客户端与服务器握手上全部拒绝进房）。hash 只覆盖**共同源集**。


def collect(targets, extra_files=(), manifest='extra_files'):
    """收集一个目标的源文件清单。``extra_files`` = 显式声明的源链接共编件（不在 targets 目录树内），
    ``manifest`` 只用于失败信息。声明了却不存在 = 清单与实际不符，直接失败——绝不静默少算。"""
    # 清单先校验：缺失即 fail-fast（报清单名 + 缺失相对路径，让"改共享件而 hash 不动"的缺口无处藏身）。
    missing = [rel for rel in extra_files
               if not os.path.isfile(os.path.join(ROOT, rel.replace('/', os.sep)))]
    if missing:
        raise SystemExit(fail(
            TOOL,
            'manifest mismatch: %s lists %d file(s) that do not exist: %s - the manifest no longer matches '
            'the repo, fix %s in scripts/codegen/gen-build-hash.py first (never drop entries to make this pass)'
            % (manifest, len(missing), ', '.join(missing), manifest)))
    files = []
    for target in targets:
        base = os.path.join(ROOT, target)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for name in filenames:
                if not name.endswith('.cs'):
                    continue
                if name in SELF_NAMES or name.endswith('.meta'):
                    continue
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, ROOT).replace('\\', '/')
                files.append((rel, full))
    for rel in extra_files:                     # 显式列出的共编件（源链接——不在自身目录树内）
        files.append((rel, os.path.join(ROOT, rel.replace('/', os.sep))))
    files.sort(key=lambda x: x[0])          # Ordinal 排序（python 默认码点序，与 C# Ordinal 一致）
    return files


def strip_for_hash(raw):
    """注释与排版归一：去掉 // 与 /* */（字符串/字符字面量内原样保留），空白折叠为单空格。
    构建身份只覆盖"会影响两端行为的源码"，注释/缩进不该改握手 hash。"""
    out = bytearray()
    i, n = 0, len(raw)
    state = 'code'
    pending = False

    def emit(b):
        nonlocal pending
        if pending and out and out[-1] != 0x20:
            out.append(0x20)
        pending = False
        out.append(b)

    while i < n:
        c = raw[i]
        nxt = raw[i + 1] if i + 1 < n else -1
        if state == 'code':
            if c == 0x2F and nxt == 0x2F:
                state = 'line'; i += 2; pending = True; continue
            if c == 0x2F and nxt == 0x2A:
                state = 'block'; i += 2; pending = True; continue
            if c == 0x22:
                if out.endswith(b'@'): state = 'vstr'
                elif out.endswith(b'$'): state = 'istr'
                else: state = 'str'
                emit(c); i += 1; continue
            if c == 0x27:
                state = 'chr'; emit(c); i += 1; continue
            if c in b' \t\r\n':
                pending = True; i += 1; continue
            emit(c); i += 1; continue
        if state == 'line':
            if c == 0x0A: state = 'code'
            i += 1; continue
        if state == 'block':
            if c == 0x2A and nxt == 0x2F: state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'chr':
            out.append(c); i += 1
            if c == 0x5C and i < n: out.append(raw[i]); i += 1; continue
            if c == 0x27: state = 'code'
            continue
        if state == 'vstr':
            out.append(c); i += 1
            if c == 0x22:
                if i < n and raw[i] == 0x22: out.append(raw[i]); i += 1; continue
                state = 'code'
            continue
        out.append(c); i += 1
        if c == 0x5C and i < n: out.append(raw[i]); i += 1; continue
        if c == 0x22: state = 'code'
    return bytes(out)


def normalize(raw):
    """行尾归一化：CRLF/CR → LF（跨机器 clone 一致性）。"""
    return raw.replace(b'\r\n', b'\n').replace(b'\r', b'\n')


def compute(files):
    h = hashlib.sha256()
    for rel, full in files:
        h.update(rel.encode('utf-8'))
        h.update(b'\x00')
        with open(full, 'rb') as f:
            h.update(strip_for_hash(normalize(f.read())))
    return h.hexdigest()[:16]


SHARED_TEMPLATE = '''%snamespace LiteNet
{
    /// <summary>
    /// 联机版本 hash（M10 前置）：覆盖判定逻辑（Sim）与协议（Proto/Protocol）的源码内容，
    /// **两端（客户端/服务器）必须一致**——不一致 = 逻辑或协议版本不同 = 必然分叉。
    /// 用途：`JoinRequest.build_hash` 握手校验 + 启动期断言（不等 → 拒绝进房，§40/§380）；
    /// 生成与校验规则单源 = `scripts/codegen/toolbox.py build-hash`（含行尾归一化，保证跨机器可复现）：
    /// 出包前由 `scripts/build/build-player.py` 跑 `--check` 复算拦截"改了源忘了重跑"（开发期不校验）。
    /// </summary>
    public static class BuildHash
    {
        /// <summary>当前构建的版本 hash（16 位小写 hex）。</summary>
        public const string Value = "%s";
    }
}
'''

META_TEMPLATE = '''%snamespace MetaServer
{
    /// <summary>
    /// Meta 服务**构建身份**（本工程源集内容摘要，16 位小写 hex）——供启动日志/产物标识（§P0-5）。
    ///
    /// **与 <c>LiteNet.BuildHash</c> 不是一回事，别混用**：那一个是**两端握手红线**
    /// （Sim/协议/表源集，两端不等即拒绝进房）；本值只标识"这一份 Meta 构建"，**不参与任何准入判定**
    /// ——Lobby 的版本准入与票据盖章消费的是**房间实例上报的** buildHash
    /// （房间端 <c>LiteNet.BuildHash.Value</c> → <c>InstanceRegistry.Entry.BuildHash</c>）。
    ///
    /// 生成与校验规则单源 = `scripts/codegen/toolbox.py build-hash`：`--check` 复算**两**个常量文件，
    /// `--check-shared` 只管握手红线那一个（出包门禁步用它——Meta 是独立产物，不拦客户端出包）。
    /// 本文件取代旧的**手写占位**常量（`Host/BuildHash.cs` 的 "meta-0.0.0-unwired"）：
    /// 占位值看起来像版本号却没有来源，属"看起来权威的假身份"。
    /// </summary>
    public static class BuildHash
    {
        /// <summary>当前构建的 Meta 构建身份（16 位小写 hex）。</summary>
        public const string Value = "%s";
    }
}
'''

# Signature input label + regen args per target (the head's third line runs the
# whole tool; both constants are written by one invocation).
SIGNATURE_LABELS = {'shared': 'shared source set', 'meta': 'meta source set'}

def read_baked_constant(path):
    """从已生成的 .g.cs 读出烤入的常量（缺失/不可读返回 None）。"""
    try:
        with open(path, 'r', encoding='utf-8') as f:
            text = f.read()
    except OSError:
        return None
    m = re.search(r'"([0-9a-f]{16})"', text)
    return m.group(1) if m else None


def target_specs():
    """(标签, collect 参数, 输出文件, 模板) —— 单一入口，生成与校验共用同一份定义。
    collect 参数带 ``manifest``：fail-fast 报错必须能说出缺的是哪份清单。"""
    return [
        ('shared', dict(targets=SHARED_TARGETS, extra_files=SHARED_LINK_FILES,
                        manifest='SHARED_LINK_FILES'), SHARED_OUT_FILE, SHARED_TEMPLATE),
        ('meta', dict(targets=META_TARGETS, extra_files=META_EXTRA_FILES,
                      manifest='META_EXTRA_FILES'), META_OUT_FILE, META_TEMPLATE),
    ]


def path_report():
    """Declared inputs/outputs (repo-relative POSIX) derived from the constants
    above; doctor consumes this instead of keeping a second path list."""
    entries = []
    for rel in SHARED_TARGETS + SHARED_LINK_FILES + META_TARGETS + META_EXTRA_FILES:
        entries.append({'path': rel.replace('\\', '/'), 'kind': 'input'})
    entries.append({'path': SHARED_OUT_REL, 'kind': 'output'})
    entries.append({'path': os.path.relpath(META_OUT_FILE, ROOT).replace('\\', '/'),
                    'kind': 'output'})
    return entries


def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        prog='gen-build-hash.py',
        description='Recompute the two buildHash constants (shared handshake + Meta build identity).',
    )
    parser.add_argument('--check', action='store_true',
                        help='recompute and compare both constants without writing')
    parser.add_argument('--check-shared', action='store_true',
                        help='only compare the shared source-set constant (build gate)')
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    check_all = args.check
    check_shared = args.check_shared      # 出包门禁用（只把守握手红线那一个）
    specs = target_specs()

    if check_all or check_shared:
        failed = False
        for label, collect_args, out_file, _template in specs:
            if check_shared and label != 'shared':
                continue
            files = collect(**collect_args)
            value = compute(files)
            current = read_baked_constant(out_file)
            rel = os.path.relpath(out_file, ROOT).replace('\\', '/')
            if current is None:
                info(TOOL, 'CHECK FAILED[%s]: %s missing or hash constant unreadable' % (label, rel))
                failed = True
            elif current != value:
                info(TOOL, 'CHECK FAILED[%s]: %s stale - baked %s != recomputed %s'
                     % (label, rel, current, value))
                failed = True
            else:
                info(TOOL, 'CHECK OK[%s]: %s = %s (%d files)' % (label, rel, value, len(files)))
        if failed:
            info(TOOL, 'rerun: python scripts/codegen/toolbox.py build-hash')
            return 1
        return 0

    for label, collect_args, out_file, template in specs:
        files = collect(**collect_args)
        value = compute(files)
        # The head's fingerprint is the source-set digest itself (first 8 hex of
        # the 16 baked below); the head lives in a self-excluded file, so writing
        # it never feeds back into the constant.
        head = '\n'.join(signature('build-hash', SIGNATURE_LABELS[label], value[:8])) + '\n'
        with open(out_file, 'w', encoding='utf-8', newline='\n') as f:
            f.write(template % (head, value))
        rel = os.path.relpath(out_file, ROOT).replace('\\', '/')
        info(TOOL, '%s written: %s = %s (%d files)' % (rel, label, value, len(files)))
    return 0


if __name__ == '__main__':
    run_tool(TOOL, main)
