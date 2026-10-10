# PostToolUse hook:共享源集 / 表 / Unity 序列化资产 改动即时提醒(客户端中立)
# 挂载:ZCode → .zcode/config.json;Claude Code → .claude/settings.json(配置入口各挂各的,脚本单源)
# 输入:stdin 的 hook 事件 JSON(取 tool_input.file_path / notebook_path)
# 输出:命中时按客户端 schema 注入提醒——ZCode 读 ZCODE_PROJECT_DIR → 平铺 {"additionalContext":...};
#       Claude Code 读 CLAUDE_PROJECT_DIR → 嵌套 {"hookSpecificOutput":{hookEventName,additionalContext}};
#       未命中或任何异常静默退出 0,绝不阻塞会话。
# 源集清单权威 = scripts/codegen/gen-build-hash.py 的 TARGETS/DATA_TARGETS(单源);
# 本脚本是提醒面不是守卫面,守卫单源在该脚本的 --check(出包门禁步),改 TARGETS 时同步此处路径前缀。
# 覆盖红线:AGENTS.md 红线 1(BuildHash 源集)、红线 4(Unity 序列化资产)。
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $event = $raw | ConvertFrom-Json
    $path = $null
    if ($event.tool_input) {
        $path = $event.tool_input.file_path
        if (-not $path) { $path = $event.tool_input.notebook_path }
    }
    if (-not $path) { exit 0 }

    # 客户端判别与工程根:两家 hook 各自注入自己的 env 变量
    $isClaude = $false
    $root = $env:ZCODE_PROJECT_DIR
    if (-not $root -and $env:CLAUDE_PROJECT_DIR) { $root = $env:CLAUDE_PROJECT_DIR; $isClaude = $true }

    $p = ($path -replace '/', '\').ToLowerInvariant()
    # file_path 通常是绝对路径;剥掉工程根前缀得到仓库相对路径,供目录前缀匹配
    if ($root) {
        $r = ($root -replace '/', '\').TrimEnd('\').ToLowerInvariant()
        if ($p.StartsWith($r + '\')) { $p = $p.Substring($r.Length + 1) }
        elseif ($p -eq $r) { exit 0 }
    }

    $msg = $null
    $buildHash = 'python scripts/codegen/gen-build-hash.py'
    if ($p -like '*buildhash.g.cs') {
        $msg = '[红线1] BuildHash.g.cs 是生成物,勿手改——由 gen-build-hash.py 从共享源重生成。'
    }
    elseif ($p -like '*.proto') {
        $msg = "[红线1] 改了 .proto:先 scripts/codegen/gen-proto.ps1 重生成,再 $buildHash 。Proto/Generated 生成物勿手改。"
    }
    elseif ($p -like 'assets\litenet\proto\generated\*') {
        $msg = '[红线1] Proto/Generated 是 gen-proto.ps1 的产物,勿手改;改行为去改 .proto 源再重生成。'
    }
    elseif ($p -like 'assets\litesim\core\scripts\*' -or $p -like 'assets\litesim\core\systems\*' -or
        $p -like 'assets\litenet\protocol\*' -or $p -like 'assets\litenet\transport\security\*') {
        $msg = "[红线1] 触碰 BuildHash 共享源集,收尾必跑: $buildHash ——漏跑=出包门禁被拦/两端常量不同步时 Join 拒进房。详见 skill: build-hash-sync"
    }
    elseif ($p -like 'luban\data\*' -or $p -like 'luban\defines\*' -or
        $p -like 'assets\gamedata\config\*' -or $p -like 'assets\litegame\lua\cfg\*') {
        $msg = "[红线1] 触碰表数据/表生成物:表源改动跑 Luban/gen.bat 双端重生成 + $buildHash ;.bytes 与 Lua/Cfg 是生成物勿手改。"
    }
    elseif (($p -like 'assets\*') -and ($p -match '\.(unity|prefab|mat|asset|controller|anim|meta)$')) {
        $msg = '[红线4] Unity 序列化资产/.meta 禁直改文件,一律走 unity-pipeline CLI。详见 skill: unity-asset-pipeline'
    }

    if ($msg) {
        if ($isClaude) {
            @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'; additionalContext = $msg } } | ConvertTo-Json -Compress -Depth 3
        }
        else {
            @{ additionalContext = $msg } | ConvertTo-Json -Compress
        }
    }
    exit 0
}
catch { exit 0 }
