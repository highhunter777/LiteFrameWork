# deploy/wg/ —— WireGuard peer 管理（云侧，访问模型 B）

`wg-peers.sh` 把"给某个测试者开一条隧道"从手敲一串命令变成一条命令，并且**可以只放行这一个脚本**（不必给 `NOPASSWD: ALL`）。

## 前置（一次性，setup 时做好）

| 文件 | 内容 |
|---|---|
| `/etc/wireguard/wg0.conf` | 服务端隧道（`[Interface]`：Address / ListenPort / PrivateKey） |
| `/etc/wireguard/server.pub` | 服务端公钥（`wg pubkey < server.key`） |
| `/etc/wireguard/litegame-endpoint` | 一行 `<公网IP>:51820`——客户端 `.conf` 的 `Endpoint` |

装法见 [09 手册 §1.2](../../Docs/项目说明/09-上云联机测试.md) 的云侧命令块。

## 用法（把脚本放到云主机，例如 `/usr/local/sbin/wg-peers.sh`，`chmod 755`）

```bash
sudo /usr/local/sbin/wg-peers.sh add alice     # 生成密钥 + 追加 peer + 热加载；输出 /tmp/alice.conf
sudo /usr/local/sbin/wg-peers.sh list          # 列出已开出的 peer（名字 → 隧道内地址）
sudo /usr/local/sbin/wg-peers.sh remove alice  # 人退出测试时撤回（并删掉本机那份 .conf）
```

- `add` 的输出 `/tmp/<名字>.conf` 就是**给那个人的客户端配置**（`chmod 600`）——拿回本机交给 [`Dist/New-Package.ps1`](../../Dist/README.md) 打包。
- **一人一份**：`.conf` 里是 TA 的私钥，不要重复发给两个人；`remove` 后那份配置即失效。
- 地址自动递增（`10.8.0.2`、`.3`…）；改动前会备份到 `wg0.conf.bak`，`syncconf` 失败则运行态不变（安全失败）。

## 窄免密（可选，让自动化自己加人）

```
<deploy-user> ALL=(root) NOPASSWD: /usr/local/sbin/wg-peers.sh
```

只放行这一个 root 拥有的脚本（不是 `ALL`）——它只做"加/删/列 peer + 热加载 + 写一份 600 的客户端配置"，可审计。脚本本身**不做内部 sudo**（假定已是 root），所以这条 sudoers 行就是全部特权面。

## 实测边界（如实）

本机实测面：`bash -n` 语法、**用假 `wg`/`wg-quick` + 假配置目录跑完** add→list→remove（地址递增、重复名拒绝、非法名字拒绝、缺 endpoint 文件显式失败、`remove` 只摘目标 block），并修掉首跑抓到的一个 `list` 地址截断与一个 `set -e` 崩溃。**未实测**：真 `wg syncconf` 对真接口的热加载、`chmod 600` 的最终权限（Git Bash 下 `stat` 恒报 644，属 MSYS 现象；Linux 上 `umask 077` + `chmod 600` 才是判据）——首次在云主机上跑时确认这两条。
