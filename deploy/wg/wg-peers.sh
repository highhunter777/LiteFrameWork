#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# LiteGame 封闭测试 · WireGuard peer 管理
#
# 用途：把"给某个测试者开一条隧道"从"手敲一串命令"变成一条命令，且**可被窄免密 sudo
#       单独放行**（推荐 sudoers 只放行本脚本，而不是 NOPASSWD: ALL）：
#         <user> ALL=(root) NOPASSWD: /usr/local/sbin/wg-peers.sh
#
# 用法：wg-peers.sh add <名字>     # 生成密钥 + 追加 peer + 热加载，并输出该测试者的 .conf
#       wg-peers.sh remove <名字>  # 撤回该 peer（人退出测试时用）并热加载
#       wg-peers.sh list           # 列出已开出的 peer
#
# 前置（一次性，setup 时做好）：
#   /etc/wireguard/server.pub      服务端公钥（wg pubkey < server.key）
#   /etc/wireguard/litegame-endpoint  一行 "<公网IP>:51820"（客户端 .conf 的 Endpoint）
#
# 依赖：wireguard-tools（wg / wg-quick）。本脚本**假定自己已是 root**（不做内部 sudo）。
# 覆盖用环境变量（仅供测试/非标路径）：WG_DIR WG WG_QUICK OUT_DIR PREFIX IFACE
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

WG_DIR="${WG_DIR:-/etc/wireguard}"
WG="${WG:-wg}"
WG_QUICK="${WG_QUICK:-wg-quick}"
OUT_DIR="${OUT_DIR:-/tmp}"
PREFIX="${PREFIX:-10.8.0}"
IFACE="${IFACE:-wg0}"

CONF="$WG_DIR/$IFACE.conf"
SERVER_PUB="$WG_DIR/server.pub"
ENDPOINT_FILE="$WG_DIR/litegame-endpoint"
MARKER="litegame-peer"

die() { echo "[失败] $*" >&2; exit 1; }
ok()  { echo "[完成] $*"; }

usage() {
    cat >&2 <<EOF
用法: $(basename "$0") add|remove|list [名字]

  add <名字>     给该测试者开一条隧道，并把他的 .conf 写到 $OUT_DIR/<名字>.conf（600）
  remove <名字>  撤回该 peer（余下的按名索引）
  list           列出已开出的 peer（名字 / 隧道内地址）
EOF
    exit 2
}

[ $# -ge 1 ] || usage
CMD="$1"; NAME="${2:-}"

validate_name() {
    [ -n "$NAME" ] || die "缺少名字"
    case "$NAME" in
        *[!A-Za-z0-9_-]*) die "名字只允许字母/数字/下划线/短横线（收到：$NAME）" ;;
    esac
    [ ${#NAME} -le 32 ] || die "名字过长（≤32）"
}

require_setup() {
    [ -f "$CONF" ] || die "找不到 $CONF（先在云主机上做完 WireGuard 一次性配置）"
    [ -f "$SERVER_PUB" ] || die "找不到 $SERVER_PUB"
}

reload_iface() {
    # syncconf 只更新 peer 集合，不断已有连接；失败则保持运行态不变（安全）
    if ! "$WG" syncconf "$IFACE" <("$WG_QUICK" strip "$CONF") ; then
        die "wg syncconf 失败——$CONF 可能已被改坏（备份见 $CONF.bak）"
    fi
}

peer_exists() {
    grep -q "^# $MARKER: $NAME\$" "$CONF"
}

next_address() {
    local used
    used="$(grep -oE "AllowedIPs *= *${PREFIX}\.[0-9]+/32" "$CONF" 2>/dev/null \
            | grep -oE '[0-9]+/32' | cut -d/ -f1 | sort -n | tail -1 || true)"
    if [ -z "$used" ]; then echo "${PREFIX}.2"; else echo "${PREFIX}.$((used + 1))"; fi
}

cmd_add() {
    validate_name
    require_setup
    peer_exists && die "名字 '$NAME' 已经开过了（要换人就先 remove，或换个名字）"
    local endpoint
    [ -f "$ENDPOINT_FILE" ] || die "缺 $ENDPOINT_FILE（一行 \"<公网IP>:51820\"）——客户端 .conf 需要它"
    endpoint="$(head -n1 "$ENDPOINT_FILE" | tr -d '[:space:]')"
    [ -n "$endpoint" ] || die "$ENDPOINT_FILE 是空的"

    local addr priv pub client_conf
    addr="$(next_address)"
    priv="$("$WG" genkey)"
    pub="$(printf '%s' "$priv" | "$WG" pubkey)"

    cp -f "$CONF" "$CONF.bak"
    {
        echo ""
        echo "[Peer]"
        echo "# $MARKER: $NAME"
        echo "PublicKey = $pub"
        echo "AllowedIPs = $addr/32"
    } >> "$CONF"
    reload_iface

    client_conf="$OUT_DIR/$NAME.conf"
    umask 077
    cat > "$client_conf" <<EOF
[Interface]
PrivateKey = $priv
Address = $addr/24
MTU = 1420

[Peer]
PublicKey = $(cat "$SERVER_PUB")
Endpoint = $endpoint
AllowedIPs = ${PREFIX}.0/24
PersistentKeepalive = 25
EOF
    chmod 600 "$client_conf"
    ok "已开通：$NAME → $addr（隧道内）"
    ok "客户端配置：$client_conf"
    echo "       → 这个文件里是 TA 的私钥：一人一份、不回传、不截图；测试结束用 '$0 remove $NAME' 撤回"
}

cmd_remove() {
    validate_name
    require_setup
    peer_exists || die "没有名为 '$NAME' 的 peer（用 list 看看）"
    cp -f "$CONF" "$CONF.bak"
    awk -v marker="# $MARKER: $NAME" '
        /^\[/ { if (blk != "" && index(blk, marker) == 0) printf "%s", blk; blk = $0 "\n"; next }
        { blk = blk $0 "\n" }
        END { if (blk != "" && index(blk, marker) == 0) printf "%s", blk }
    ' "$CONF" > "$CONF.tmp"
    mv -f "$CONF.tmp" "$CONF"
    reload_iface
    rm -f "$OUT_DIR/$NAME.conf"
    ok "已撤回：$NAME（客户端配置也删了）"
}

cmd_list() {
    require_setup
    local any=0
    while IFS= read -r line; do
        local n addr
        n="${line#*: }"
        addr="$(awk -v marker="# $MARKER: $n" '
            /^\[/ { if (blk ~ marker) found=blk; blk=$0 "\n"; next }
            { blk = blk $0 "\n" }
            END {
                if (blk ~ marker) found = blk;
                if (match(found, /AllowedIPs = [0-9.]+/)) { s = substr(found, RSTART, RLENGTH); sub(/AllowedIPs = /, "", s); print s }
            }
        ' "$CONF")"
        echo "  $n  →  ${addr:-?}"
        any=1
    done < <(grep -E "^# $MARKER: " "$CONF" || true)
    [ "$any" = 1 ] || echo "  （还没有开过任何 peer）"
}

case "$CMD" in
    add)    cmd_add ;;
    remove) cmd_remove ;;
    list)   cmd_list ;;
    *)      usage ;;
esac
