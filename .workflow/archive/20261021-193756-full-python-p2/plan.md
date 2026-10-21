# 方案：全量 Python P2（内容/环境）

> 运行 full-python-p2 · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6（P2）

## 背景与目标

- P0/P1 已完成：`_common.py`、hooks、L0 已 Python；P2 继续迁移**内容发布链与环境检查**：
  `_local-cdn.ps1`、`serve-cdn.ps1`、`publish-candidate.ps1`、`gen-candidate.ps1`、`restore-check.ps1` → Python。
- 目标：五脚本行为等价（参数/输出/退出码/产物），删除旧 PS，清单/构建器/CI/文档同批切换；不改变签名、CDN 布局、环境检查口径。
- 可观察结果：`python scripts/content/gen-candidate.py` 产出的信封与旧 PS 字节语义一致且能用内置锚验签；`publish-candidate.py` 布局/签名一条龙可用；`serve-cdn.py` 能起本地 HTTP；`restore-check.py` 在 repo PASS。

## 需求澄清与假设

- **已确认**：全量 Python 目标；P2 按设计迁移内容/环境；P3–P5 未做。
- **假设**：
  1. 运行环境有 Python 3.10+、`cryptography`（当前 46.0.7；用于 RSA PKCS#1 v1.5 + SHA-256）；`openpyxl` 仍在；不新增其它依赖；
  2. 私钥仍是 .NET XML（`RSAKeyValue`，Modulus/Exponent/P/Q/DP/DQ/InverseQ/D），仓库外 `~/.unitylib-content-signing/release-key-2026-09-26.xml`；测试用临时生成的等价 XML；
  3. `gen-candidate` 的规范 JSON 字节必须与 PS 手写序化一致：字段序/大小写/空串/空对象/空数组/布尔/整数/转义顺序不变；
  4. `serve-cdn` 继续用 `python -m http.server`（PS 已是该实现），只换入口；`publish` 的 robocopy 语义用 `shutil` 复现（只看成功/失败与最终布局）；
  5. `restore-check` 环境检查项不减少；新增 `cryptography` 检查（内容签名依赖）；
  6. 本次不执行真实签名机私钥、不发布到真实 CDN、不跑 Unity/SSH；测试用临时密钥/目录/端口。

## 范围与非目标

### 范围内

- 新增 `scripts/content/_local_cdn.py`、`serve-cdn.py`、`publish-candidate.py`、`gen-candidate.py`、`scripts/gate/restore-check.py`。
- 删除对应五个 `.ps1`。
- 更新清单 `content.gen-candidate/publish-candidate/serve-cdn`、`env.restore`；`toolchain.py` restore 子命令；`build-player.ps1` 的 restore 调用；工具索引。
- 更新现行文档/AGENTS/项目说明/环境指南/技术支持/代码地图/待办/设计引用（历史施工卷除外）。
- 测试：`_local_cdn` 配置、gen-candidate 签名/规范字节/空目录、publish 布局/签名、serve HTTP、restore repo 基线与缺件、P2 结构断言。

### 非目标

- 不迁移 P3–P5（test/nightly/l2/install-task/build/smoke/deploy/local-chain）。
- 不修改签名算法、规范 JSON 字段/顺序、ReleaseLayout 前缀、CDN URL 契约、restore 检查口径。
- 不真实发布/部署、不读取真实私钥、不跑 Unity/L2/SSH。
- 不提交（提交另行确认）。

## 现状与约束

- `gen-candidate.ps1` 164 行：扫 `config/**`、`lua/**.lua`、`bundle/**`，SHA256，手写紧凑 manifest（PascalCase，固定字段序），RSA XML 私钥签名，内置锚表公钥自校验，信封 `{"manifest":...,"signature":"..."}`。
- `publish-candidate.ps1` 102 行：可选 bundle 镜像、CDN 布局 `{CdnRoot}/{ReleaseId}/`、调用 gen-candidate 写 `{CdnRoot}/candidate.json`。
- `serve-cdn.ps1` 50 行：`python -m http.server <port> --directory <dir> --bind <bind>`。
- `_local-cdn.ps1` 39 行：读 `local-cdn.json`，返回绝对 directory/bind/port。
- `restore-check.ps1` 185 行：Unity 版本/修订、ProjectSettings、manifest/lock、vendored、dotnet/python/openpyxl/git、付费插件、字体/URP、场景/JDK、toolchain check；blocking exit 1。
- 约束：签名字节必须与运行时/旧 PS 对齐；私钥不读取/不打印；`cryptography` 需进入 restore/文档依赖；serve 端口/目录可覆盖；publish 的清理语义要保留。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 全 P2 一批（推荐） | 五个脚本 + 测试 + 注册面一次迁 | 内容链一次闭环；模块间复用 `_local_cdn` | 签名/布局验证面较大 | **采用** |
| B 先 gen+serve，publish/restore 后置 | 降低单批体量 | 风险小 | 内容链仍断、清单半套 | 不采用 |
| C 用 openssl 代替 cryptography | 避开 Python 包 | 无第三方依赖 | openssl 在 Windows/CI 可用性不确定；XML 私钥转换更复杂 | 不采用 |

## 设计要点

### 1. `_local_cdn.py`

- `LocalCdnConfig`（config_path/directory/bind/port）+ `load_config(path=None)`。
- 默认 `scripts/content/local-cdn.json`；缺文件/缺字段 fail loudly；相对 directory 按仓库根解析为绝对路径。
- serve/publish 共用；不再有 `_local-cdn.ps1`。

### 2. `gen-candidate.py`

- argparse：`--content-dir/--release-id/--revision/--out-file/--expires-in-days/--platform/--anchors-cs/--key-path/--key-id`。
- 扫目录：`config/`、`lua/**.lua`、`bundle/`；其余 skipped；条目按 path 稳定排序；空目录 fail。
- 规范 manifest：**手写字符串**，字段序/大小写与 PS 完全一致；字符串转义按 PS 顺序（`\`、`"`、`\n`、`\r`、`\t`）；不借助 json.dumps 生成整段。
- 私钥：解析 .NET XML → `RSAPrivateNumbers` → `cryptography` 签名（PKCS1v15 + SHA256）。
- 自校验：从 anchors .cs 提取首个 `FromBase64String("...")` Modulus，公钥验签；失败 exit 1。
- 信封：`{"manifest":<canonical>,"signature":"<b64>"}`，UTF-8 无 BOM；输出 `[content/gen-candidate]`；0/1。
- 默认 key/anchors 与 PS 一致；publish 可显式传测试 key/anchors。

### 3. `publish-candidate.py`

- argparse 对应 PS + `--config-path/--key-path/--anchors-cs/--key-id`（测试/自动化）。
- 可选 `--bundle-source`：清空并镜像到 `<ContentDir>/bundle`。
- `CdnRoot` 缺省取 config.directory；相对路径按仓库根；清理同名 ReleaseId 目录后复制 contents。
- 调 `gen-candidate.py`（同解释器）写 `<CdnRoot>/candidate.json`，透传退出码。
- 输出 serve/consume 提示；0/1。

### 4. `serve-cdn.py`

- argparse：`--config-path/--port/--bind/--directory`。
- 配置/覆盖解析；目录缺失 exit 1。
- `subprocess.run([sys.executable,"-m","http.server",...])`，透传退出码。

### 5. `restore-check.py`

- argparse `--project-path`；与 PS 相同的 OK/WARN/MISS 清单与顺序语义。
- Python 依赖检查：openpyxl + **cryptography**；dotnet 8 警告；toolchain check 非 0 只 Warn（与 PS 一致）。
- 汇总 `[OK]/[WARN]/[MISS]`；有 MISS exit 1，否则 0。
- 不读取私钥；不改变付费插件/字体/URP 阻塞规则。

### 6. 注册面

- `toolchain.json` 四个 command → Python；`toolchain.py` 的 restore 子命令 → Python；`build-player.ps1` restore 调用 → Python；工具索引重生成。
- 现行文档/AGENTS/环境指南/技术支持/代码地图/待办/设计引用 → Python；历史施工卷/归档不回改。
- 删除五个 PS；`test_toolchain` 增结构断言（命令/文件/旧路径）。

## 通用性与扩展性

- `_common.py` 复用根推导/输出/退出码；`_local_cdn.py` 供 serve/publish 共用；gen-candidate 的规范序列化保持“独立实现”，不抽取运行时序列化器（保留跨实现证据）。
- 测试密钥/锚表/目录/端口全部临时生成，不依赖签名机；后续 P3–P5 可复用临时夹具模式。
- 不做通用 RSA 工具类；签名逻辑留在 gen-candidate 单处。

## 测试策略

- **红测**：新增五组测试 + `test_toolchain` 结构断言，先观察缺失/旧命令失败。
- `_local_cdn`：合法/相对目录/缺文件/缺字段。
- gen-candidate：临时 RSA XML + anchors，内容混合受控/跳过，校验条目、签名、信封结构、空目录失败；规范 manifest 字段序/空值。
- publish：临时内容 + CDN + 测试 key/anchors，校验 `candidate.json` 与 `{ReleaseId}/` 布局、bundle 镜像、签名可用；不真实发布。
- serve：临时目录 + 空闲端口，启动后 HTTP GET 命中文件，终止；目录缺失 exit 1。
- restore：repo 基线 PASS；空临时项目 exit 1 且列 blocking；`--help`。
- 基线：Python 全测、L1、Doc、toolchain check/docs；BuildHash 不变；不跑 L2/Unity/SSH。
- 命令：
  ```powershell
  python -m unittest discover -s scripts/tests -p "test_*.py" -v
  python scripts/gate/restore-check.py
  python scripts/toolchain.py check
  python scripts/toolchain.py docs --check
  python scripts/gate/doc-lint.py
  pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest
  ```

## 验收标准

- [ ] **AC1 配置**：`_local_cdn.py` 合法/缺文件/缺字段行为与 PS 一致；serve/publish 共用。
- [ ] **AC2 签名**：gen-candidate 产出规范 manifest + RSA 签名 + 内置锚自校验；条目前缀/排序/空目录/转义正确；信封可被独立公钥验签。
- [ ] **AC3 发布**：publish 布局/清理/bundle 镜像/candidate.json 与 PS 等价；失败透传非 0。
- [ ] **AC4 服务**：serve 可起 HTTP 并返回目录文件；端口/目录覆盖生效；缺失目录 exit 1。
- [ ] **AC5 环境**：restore repo PASS；空项目 blocking exit 1；检查项不减少，新增 cryptography。
- [ ] **AC6 注册面**：清单/`toolchain.py`/`build-player.ps1`/工具索引/现行文档全 Python；旧 PS 删除；无现行旧引用。
- [ ] **AC7 门禁**：Python 测试全绿；L1 1801；Doc 0/0；toolchain check；BuildHash 不变。
- [ ] **AC8 范围**：仅 scripts/Docs/AGENTS/构建器命令；不碰 Unity/共享源集/协议/表/真实 CDN/SSH。

## 风险与回滚

| 风险 | 监控/发现 | 回滚 |
|---|---|---|
| 规范 JSON 字节与 PS/运行时不一致 | 测试重算 canonical + 独立公钥验签；必要时用现有信封对照 | 保留 PS 一版/回退 gen-candidate |
| RSA XML 解析/签名差异 | 临时密钥往返 + 自校验；真实信封比对 | 回退 PS；Python 修复 |
| `cryptography` 缺失 | restore 检查 + 文档依赖说明 | 安装包或回退 PS |
| publish 清理/复制语义差异 | 临时目录布局断言；bundle 镜像 | 回退 PS；修复 |
| serve 端口/终止 | 空闲端口 + HTTP GET + terminate | 回退 PS |
| restore 检查项漂移 | repo 基线 + 空项目 blocking 断言 | 回退 PS；逐项修 |
| 外部引用遗漏 | grep 旧路径（排除历史）/toolchain check | 恢复旧 PS 一版；补引用 |

**回滚**：恢复五个 `.ps1` 与清单/`toolchain.py`/`build-player`/文档命令；删除新增 Python/测试即可回到 P1 状态；无数据迁移。
