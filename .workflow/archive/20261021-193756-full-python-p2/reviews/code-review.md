# 代码评审：全量 Python P2（内容/环境）

> 运行 full-python-p2 · 创建于 2026-10-21

## 变更范围

- 覆盖 P2 五个脚本：`_local_cdn`、gen-candidate、publish-candidate、serve-cdn、restore-check；P3–P5 未做。
- 新增五个 Python 入口 + `_rsa_fixture` + 六组测试；删除五个 `.ps1`。
- 修改清单四 command、`toolchain.py` restore 子命令、`build-player.ps1` restore 调用、工具索引、AGENTS/现行文档/skills。
- 范围审计：仅 scripts/Docs/AGENTS/CI/构建器命令；无 Assets/Server/Luban/Tests/ProjectSettings 改动。

## 正确性与边界

- **配置**：缺文件/缺字段 fail loudly；相对目录按仓库根解析；serve/publish 共用。
- **签名**：规范 manifest 手写序列化，字段序/大小写/空值/转义对齐 PS；`.NET XML` 私钥解析为 `cryptography` 数字；PKCS1v15+SHA256；内置锚公钥自校验；临时 RSA 测试独立验签。
- **发布**：bundle 镜像、ReleaseId 目录清理、contents 复制、candidate.json；失败透传非 0；测试 key/anchors 为可选参数，默认行为不变。
- **服务**：保留 `python -m http.server` 子进程；端口/目录覆盖；缺目录 exit 1；测试用空闲端口 + HTTP GET + terminate。
- **环境**：检查项与 PS 对齐（Unity/ProjectSettings/manifest/lock/vendored/.NET/python/openpyxl+cryptography/git/插件/字体/URP/场景/JDK/toolchain check）；blocking exit 1；.NET 9 仍 WARN。
- **边界**：未使用真实私钥、未真实发布/CDN、未跑 Unity/SSH；测试只证明临时夹具与本地 HTTP。

## 测试充分性

- 红测 14 个断言失败；最终 Python 75/75。
- 覆盖：配置正反例、签名规范字节/独立验签/空目录、发布布局/bundle/失败、serve HTTP/缺目录、restore repo PASS/空项目 blocking、P2 清单/旧文件断言。
- 未覆盖：真实签名机 XML 私钥、真实 CDN 消费端、Windows CI 首次 Python restore；L2/Unity 不在本批。

## 通用性与扩展性

- `_common`/`_local_cdn` 复用；签名逻辑单处，不抽通用序列化器（保留跨实现证据）。
- 发布/服务测试夹具可复用于后续流程；`--config-path/--key-path/--anchors-cs` 为测试/自动化扩展点，不改变默认。
- 无第二份 ReleaseLayout 前缀表；前缀仍与运行时/设计**独立实现**对照（这是有意保留的验签证据，不是重复实现债）。

## 安全与性能

- 安全：不读取/打印真实私钥；测试用临时 key；不真实发布/云侧；restore 不改变阻塞规则。
- 性能：gen/publish 对候选目录做一次扫描+哈希；serve 为静态 HTTP；restore 为秒级检查。
- 残余：`cryptography` 成为内容签名硬依赖，已在 restore 与文档声明；CI 首次运行需确认安装。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 信息 | `gen-candidate.py` | 锚校验指数固定 65537（与 PS 的 AQAB/运行时锚契约一致） | 保持与 PS/运行时单源，不新增锚字段解析 | 接受 |
| C-2 | 信息 | `publish-candidate.py` | 新增可选 key/anchors/key-id 参数，PS 无 | 仅测试/自动化显式使用，默认行为与 PS 一致；文档未扩写为常规用法 | 接受 |
| C-3 | 信息 | `restore-check.py` | openpyxl/cryptography 用当前解释器检测，PS 用 `python` 子进程 | 正常调用同一解释器；文档要求 Python 3.10+；未做跨解释器发现 | 接受 |
| C-4 | 信息 | 真实面 | 真实私钥/真实 CDN/远端 CI restore 未执行 | 本批只做临时夹具；上线前按发布流程再验 | 遗留（非阻塞） |

无阻塞问题。

## 复测记录

- `workflow.py verify` → PASS（L1 1801、Doc 0/0、restore PASS、L0 测试、Python 75/75、toolchain check 0 error/6 存量 warning、docs current）。
- 附加：临时 RSA 信封独立验签；publish 布局/签名；serve HTTP GET；restore 空项目 blocking；CI YAML/JSON/PS AST OK；BuildHash `--check-shared` OK。
- 验证证据：`evidence/verify.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder（agent）
- 评审时间: 2026-10-21
