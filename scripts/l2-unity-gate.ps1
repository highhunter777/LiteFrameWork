# ─────────────────────────────────────────────────────────────────────────────
# L2 Unity 侧门禁
#
# 为什么需要 L2：L1（dotnet 单测）按路径 glob 编译、不读 .meta，也不经 Unity 编译器——
#   非法 GUID 的 .meta 事故里 L1 全绿而 Unity 编译是坏的。
# L2 补这一层：① 非法 meta/GUID 扫描（纯文件，秒级）② Unity 侧编译/诊断状态。
#
# 两种模式（自动选择）：
#   A. 编辑器正在运行（检测 Library/Pipeline/.unity-pipeline-port）→ 经 Unity Pipeline
#      查询 recompile_status / console_status —— **无需关闭编辑器**。
#   B. 编辑器未运行 → 退回 batchmode `unity test --mode EditMode`（需已激活授权；
#      且必须没有其它实例占用该工程）。
#
# 依赖：Unity CLI（`unity`）在 PATH；本机 Unity 编辑器路径见 -UnityExe。
# 用法（Windows PowerShell 5.1 亦可，本机未装 pwsh）：
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1                  # 全量（默认）
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly    # 只跑①（无 Unity 环境也能用）
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1 -RunEditModeTests # 强制走 B（batchmode 测试）
#
# 两种模式跑**同样的两段测试**（EditMode + PlayMode）：
#   A. 编辑器在跑 → 经 Unity Pipeline（异步轮询 test_status），无需关闭编辑器
#   B. 编辑器未跑 → batchmode `unity test --mode <EditMode|PlayMode>`
# A、B 两模式都必须覆盖 EditMode 与 PlayMode，不得漏掉任何一半。
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$UnityExe = 'E:\unity3d\2022.3.55f1c1\Editor\Unity.exe',
    [string]$TestOutput = 'TestResults\editmode-results.xml',
    [switch]$MetaScanOnly,
    [switch]$RunEditModeTests
)

# batchmode 的 PlayMode 结果文件（与 EditMode 分开落盘，失败时能分辨是哪一段红的）
$PlayModeOutput = 'TestResults\playmode-results.xml'

# 工程路径：未显式给出时按脚本位置推导（$PSScriptRoot 在参数默认值阶段可能为空，故放体内）
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrWhiteSpace($here)) { $here = (Get-Location).Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..')).Path
}

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

function Write-Step($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "  [PASS] $t" -ForegroundColor Green }
function Write-Bad($t)  { Write-Host "  [FAIL] $t" -ForegroundColor Red; $failures.Add($t) }
function Write-Note($t) { Write-Host "  [INFO] $t" -ForegroundColor Yellow; $notes.Add($t) }

Write-Host "L2 Unity 侧门禁 | 工程: $ProjectPath" -ForegroundColor White

# ── ① 非法 meta/GUID 扫描（无需 Unity；防"Unity 拒绝导入资源"类故障）────────────
Write-Step '① 非法 meta GUID 扫描'
$scanRoots = @('Assets', 'Packages')
$guidRegex = [regex]'^guid:\s*([0-9a-fA-F]{32})\s*$'
$metaCount = 0
$badMetas = New-Object System.Collections.Generic.List[string]
foreach ($root in $scanRoots) {
    $rootPath = Join-Path $ProjectPath $root
    if (-not (Test-Path $rootPath)) { continue }
    $files = Get-ChildItem -Path $rootPath -Filter '*.meta' -Recurse -File -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        $metaCount++
        $reader = New-Object System.IO.StreamReader($f.FullName)
        try {
            $guid = $null; $line = $null; $i = 0
            while ($i -lt 8 -and ($line = $reader.ReadLine()) -ne $null) {
                $i++
                if ($line.StartsWith('guid:')) { $guid = $line.Trim(); break }
            }
        } finally { $reader.Close() }
        if ($null -eq $guid) { $badMetas.Add("$($f.FullName)  (缺少 guid 行)"); continue }
        if (-not $guidRegex.IsMatch($guid)) { $badMetas.Add("$($f.FullName)  ($guid)") }
    }
}
if ($badMetas.Count -eq 0) { Write-Ok "扫描 $metaCount 个 .meta：GUID 全部为合法 32 位 hex" }
else {
    Write-Bad "发现 $($badMetas.Count) 个非法 GUID 的 .meta（Unity 会拒绝导入 → 类型在编译中消失）"
    $badMetas | Select-Object -First 20 | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkRed }
    Write-Host "        修复：重写为 uuid4().hex（32 位 hex），旧 GUID 因非法从未被引用，改后无副作用" -ForegroundColor DarkGray
}

if ($MetaScanOnly) {
    Write-Step '汇总（MetaScanOnly）'
    if ($failures.Count -eq 0) { Write-Host '  L2(meta) 通过' -ForegroundColor Green; exit 0 }
    Write-Host "  L2(meta) 失败：$($failures.Count) 项" -ForegroundColor Red; exit 1
}

# ── ② Unity 侧：编译/诊断状态 ────────────────────────────────────────────────
$descriptor = Join-Path $ProjectPath 'Library\Pipeline\.unity-pipeline-port'
$unityAvailable = $null -ne (Get-Command 'unity' -ErrorAction SilentlyContinue)

function Invoke-PipelineCommand([string]$command, [string[]]$cmdArgs) {
    # 命令名与参数必须分开传：`unity command run_tests --mode editor`
    # 全局 ErrorActionPreference=Stop 会把 native stderr 提前升级成异常，导致真正的 CLI
    # 诊断被截断；这里先完整捕获 stdout/stderr，再按退出码统一抛出。
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        if ($cmdArgs -and $cmdArgs.Count -gt 0) {
            $out = & unity command --timeout 10 $command @cmdArgs --project-path $ProjectPath 2>&1
        }
        else {
            $out = & unity command --timeout 10 $command --project-path $ProjectPath 2>&1
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($exitCode -ne 0) { throw "unity command $command $($cmdArgs -join ' ') 失败（退出码 $exitCode）：$($out -join ' ')" }
    return ($out -join "`n")
}

# Unity 测试套件异步执行：同步 run_tests 走 `--timeout 10`，套件涨过阈值即超时失败。
# 异步触发 + 轮询 test_status 到终态——与 unity-pipeline 技能记载的
# 长跑形态一致（`--async_tests true` → 轮询 `test_status`）。返回含 Total/Passed/Failed 的结果文本。
# $mode：editor（EditMode）/ playmode（PlayMode）。两者共用同一入口，不另起旁路。
function Invoke-UnityTestsAsync([string]$mode, [int]$timeoutSeconds = 900) {
    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $null = & unity command --timeout 60 run_tests --mode $mode --async_tests true --project-path $ProjectPath 2>&1

        $deadline = (Get-Date).AddSeconds($timeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 5
            $statusText = & unity command --timeout 30 test_status --project-path $ProjectPath 2>&1
            $joined = $statusText -join "`n"
            if ($joined -match '"status"\s*:\s*"running"') { continue }
            if ($joined -match '"status"\s*:\s*"(completed|failed|idle)') { return $joined }
        }
        throw "$mode 用例超时（${timeoutSeconds}s 未到终态）"
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
}

# 从 Pipeline 的 JSON 结果里取整数字段（取不到返回 -1，便于区分"0 条"与"解析失败"）
function Get-JsonInt([string]$text, [string]$key) {
    $m = [regex]::Match($text, '(?i)"' + $key + '"\s*:\s*(\d+)')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return -1
}

# 新鲜度守卫用：Assets 下最新 .cs 的写入时间 / Library/ScriptAssemblies 下最新 .dll 的写入时间
#
# **必须排除点目录**：Directory.Build.props 把 dotnet 侧构建输出重定向到
# `Assets/<模块>/.dotnet/`，其中 `obj/Release/**/*.cs` 是 MSBuild 生成的 AssemblyInfo。点目录被
# Unity 忽略、**从不参与编译**，但其时间戳会被 L1 刷新 → "源码新于程序集" 恒真，
# `Refresh` 又不可能把它推进程序集 → 守卫永久 FAIL、L2 永远红。判据为
# "Unity 会编译的文件多新"。gitignore 的 `.dotnet/` 与此处排除的是同一样东西。
function Get-NewestSourceTime {
    $files = Get-ChildItem -Path (Join-Path $ProjectPath 'Assets') -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]\.' }
    if (-not $files) { return [datetime]::MinValue }
    return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
}

function Get-NewestAssemblyTime {
    $dir = Join-Path $ProjectPath 'Library/ScriptAssemblies'
    if (-not (Test-Path $dir)) { return [datetime]::MinValue }
    $dlls = Get-ChildItem -Path $dir -Filter *.dll -File -ErrorAction SilentlyContinue
    if (-not $dlls) { return [datetime]::MinValue }
    return ($dlls | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
}

if ((Test-Path $descriptor) -and -not $RunEditModeTests) {
    Write-Step '② Unity 侧状态（编辑器在跑 → 经 Pipeline，无需关闭编辑器）'
    if (-not $unityAvailable) {
        Write-Note '未找到 unity CLI（PATH），跳过 Unity 侧检查'
    }
    else {
        $desc = Get-Content $descriptor -Raw | ConvertFrom-Json
        Write-Note "发现编辑器描述符：port $($desc.port) / pid $($desc.pid) / $($desc.unityVersion)（连通性由首条 Pipeline 命令确认）"
        try {
            # Pipeline 规范：后台编译/测试前必须保持 Editor tick，避免失焦或最小化后挂起。
            Invoke-PipelineCommand 'set_autotick' @('--enable', 'true') | Out-Null

            # ③ 新鲜度守卫：**先把源码刷进程序集，再谈编译状态**
            #     外部改 .cs 后 Unity 不会自动导入（AssetDatabase 未 Refresh）→
            #     recompile_status 仍报 up_to_date、EditMode 用例跑的是**旧程序集** → L2 假绿。
            #     对策：无条件 Refresh(ForceSynchronousImport) + RequestScriptCompilation 并等编译收敛
            #     （幂等：无改动时几秒内返回）。
            $before = Get-NewestSourceTime
            $refreshDone = $true
            try {
                # 先清控制台缓冲：否则"上一次失败编译"的 error 会留在缓冲里被判成本次失败
                Invoke-PipelineCommand 'clear_console' | Out-Null

                # 代码走临时文件（eval_file）而不是内联字符串：脚本宿主传内联代码会被 native 参数引号规则弄坏
                $reqFile = Join-Path $ProjectPath 'Temp/l2-freshness-request.cs'
                $stFile = Join-Path $ProjectPath 'Temp/l2-freshness-status.cs'
                # 用 .NET 写无 BOM 的 UTF-8：Set-Content -Encoding UTF8 在 PS 5.1 下带 BOM，
                # eval_file 的编译器会拒
                $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
                [System.IO.File]::WriteAllText($reqFile, @"
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
"@, $utf8NoBom)
                [System.IO.File]::WriteAllText($stFile,
                    'return UnityEditor.EditorApplication.isCompiling ? 1 : 0;', $utf8NoBom)

                # `--timeout 30000` 是命令级超时：CodeEvalCommand 默认只等 5000ms 主线程操作，
                # 编辑器忙（导入/编译收尾）时报 "Main thread operation timed out after 5000ms" 并落
                # Console error 级条目——会被"Console 无新增 error"检查记成噪声。
                Invoke-PipelineCommand 'eval_file' @('--timeout', '30000', 'Temp/l2-freshness-request.cs') | Out-Null
                $waited = 0
                while ($waited -lt 90) {
                    Start-Sleep -Seconds 3
                    $waited += 3
                    $busy = Invoke-PipelineCommand 'eval_file' @('--timeout', '30000', 'Temp/l2-freshness-status.cs')
                    if ($busy -notmatch '"result":\s*"?1') { break }
                }
            }
            catch {
                # 刷新是"尽力而为"：编辑器正忙（编译中/域重载）时会失败——不中断，但要**记住没刷成**，
                # 因为此时"源码新于程序集"无法判定（见下）
                $refreshDone = $false
                Write-Note "新鲜度守卫：Refresh/编译请求未完成（$($_.Exception.Message)）——按现状校验"
            }
            $rec = Invoke-PipelineCommand 'recompile_status'
            Write-Host "  recompile_status: $($rec.Trim())" -ForegroundColor DarkGray
            $after = Get-NewestAssemblyTime

            # 新鲜度结论（**以 Unity 的判定为准，mtime 只作证据**）：
            #  - 强制 Refresh 之后：Unity 说 up_to_date = 内容没变（生成物同内容重写 → mtime 变、内容不变）；
            #    说 completed = 编译已跑完且无错。两者都意味着"程序集与源码一致"，**不去声称"这次重建了"**；
            #  - 只有"刷新没能执行"时，才用 mtime 保守判红（此时无法确认 Unity 是否看见了改动）。
            # 权威信号始终是：强制刷新 + 编译无失败 + 控制台无 CS 错误（下面两条）。
            if ($before -gt $after) {
                if (-not $refreshDone) {
                    Write-Bad "源码新于程序集（$($before.ToString('HH:mm:ss')) > $($after.ToString('HH:mm:ss'))）且刷新未完成——无法确认已重建，EditMode 用例可能跑旧代码"
                }
                else {
                    Write-Ok "程序集新鲜（以 Unity 判定为准；源码 mtime $($before.ToString('HH:mm:ss')) ＞ 程序集 $($after.ToString('HH:mm:ss')) 属内容未变的重写）"
                }
            }
            else {
                Write-Ok "程序集新鲜（源码 ≤ 程序集：$($before.ToString('HH:mm:ss')) ≤ $($after.ToString('HH:mm:ss'))）"
            }

            if ($rec -match '"compilationFailed"\s*:\s*true' -or $rec -match '"failed"\s*:\s*true') { Write-Bad 'Unity 编译失败（recompile_status.failed/compilationFailed=true）' }
            else { Write-Ok 'Unity 编译状态正常（无编译失败）' }

            # 精确判定：只有消息里出现 "error CS####" 才算编译错误；
            # 其它 error 级条目（如第三方程序集加载告警）仅提示，不判失败（否则误报）
            $con = Invoke-PipelineCommand 'console'
            if ($con -match 'error CS\d+') {
                $m = [regex]::Match($con, 'error CS\d+[^"\\]*')
                Write-Bad "控制台存在编译错误：$($m.Value.Trim())"
            }
            else {
                $errCount = ([regex]::Matches($con, '"level":"error"')).Count
                Write-Ok "控制台无编译错误（error 级条目 $errCount 条，均非 CS 编译错误）"
                if ($errCount -gt 0) { Write-Note "有 $errCount 条非编译类 error（如程序集加载告警）；明细：unity command console --project-path `"$ProjectPath`"" }
            }

            # ③ Unity EditMode 用例：经 Pipeline 直接跑，编辑器无需关闭。
            # 用例在 Assets/Tests/EditMode（IEEE 基线逐位对账 + UI 模板/资源完整性）；
            # Total=0 视为失败——否则"测试程序集没编进来"会静默通过。
            # 走异步轮询（同步调用在套件涨过 CLI 阈值后必超时，见 Invoke-UnityTestsAsync）。
            $rt = Invoke-UnityTestsAsync 'editor'
            $total  = Get-JsonInt $rt 'Total'
            $passed = Get-JsonInt $rt 'Passed'
            $failed = Get-JsonInt $rt 'Failed'
            Write-Host "  run_tests(EditMode): Total=$total Passed=$passed Failed=$failed" -ForegroundColor DarkGray
            if ($total -le 0) { Write-Bad 'EditMode 用例数 = 0（测试程序集未编入？检查 Assets/Tests/EditMode 的 asmdef 与 UNITY_INCLUDE_TESTS）' }
            elseif ($failed -gt 0) { Write-Bad "EditMode 用例失败 $failed 项（Total=$total）——明细：unity command run_tests --mode editor --project-path `"$ProjectPath`"" }
            else { Write-Ok "EditMode 用例全绿（$passed/$total）" }

            # ④ Unity PlayMode 用例：真 UIService + 真 prefab + 真转场链路。
            # 《UI测试开发专项设计》§6"仅验证转场 Runner 或模板结构不能替代此项"；用例在
            # Assets/Tests/UI/PlayMode。§8.1 要求接入**同一门禁**，不得另起旁路入口。
            # Total=0 同判失败——PlayMode 程序集未编入不能静默通过。
            $rtp = Invoke-UnityTestsAsync 'playmode'
            $ptotal  = Get-JsonInt $rtp 'Total'
            $ppassed = Get-JsonInt $rtp 'Passed'
            $pfailed = Get-JsonInt $rtp 'Failed'
            Write-Host "  run_tests(PlayMode): Total=$ptotal Passed=$ppassed Failed=$pfailed" -ForegroundColor DarkGray
            if ($ptotal -le 0) { Write-Bad 'PlayMode 用例数 = 0（测试程序集未编入？检查 Assets/Tests/UI/PlayMode 的 asmdef）' }
            elseif ($pfailed -gt 0) { Write-Bad "PlayMode 用例失败 $pfailed 项（Total=$ptotal）——明细：unity command run_tests --mode playmode --project-path `"$ProjectPath`"" }
            else { Write-Ok "PlayMode 用例全绿（$ppassed/$ptotal）" }
        }
        catch { Write-Bad "Pipeline 命令执行失败：$($_.Exception.Message)" }
    }
}
elseif ($RunEditModeTests -or -not (Test-Path $descriptor)) {
    Write-Step '② Unity 侧测试（batchmode：EditMode + PlayMode）'
    if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
        Write-Bad '检测到 Unity 进程在运行：batchmode 无法与已打开的编辑器共用同一工程。请关闭编辑器后重跑（或直接跑默认模式走 Pipeline）'
    }
    elseif (-not (Test-Path $UnityExe)) {
        Write-Bad "未找到编辑器：$UnityExe（用 -UnityExe 指定）"
    }
    elseif (-not $unityAvailable) {
        Write-Bad '未找到 unity CLI（PATH）'
    }
    else {
        $outDir = Join-Path $ProjectPath (Split-Path $TestOutput -Parent)
        if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

        # 两段各自独立判定：一段红不代表另一段不跑——两段的失败原因完全不同
        # （EditMode 偏资产/契约，PlayMode 偏真实 PlayerLoop/转场/资源），只跑一段等于漏一半。
        foreach ($mode in @('EditMode', 'PlayMode')) {
            $outFile = if ($mode -eq 'EditMode') { $TestOutput } else { $PlayModeOutput }
            Write-Note "执行 unity test --mode $mode --output $outFile"
            # 工程是**位置参数**，不是 --project-path（`unity test --help` 的 Arguments 段）。
            # -e/--editor-path：CLI 默认去 Unity Hub 标准位置找编辑器，**找不到 Unity 中国版**
            # （报「编辑器 2022.3.55f1c1 未安装」）——必须显式传 $UnityExe。
            # --timeout：batchmode 冷启动要导包/编译，默认无超时会让任务计划程序挂到天荒地老
            & unity test $ProjectPath -e $UnityExe --mode $mode --output $outFile --timeout 1800 2>&1 |
                ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            if ($LASTEXITCODE -ne 0) { Write-Bad "$mode 测试未通过（退出码 $LASTEXITCODE），报告见 $outFile" }
            else { Write-Ok "$mode 测试通过，报告见 $outFile" }
        }
    }
}

# ── ③ 汇总 ───────────────────────────────────────────────────────────────────
Write-Step '汇总'
# 注意：遍历时不得再调用 Write-Note（它会写 $notes 本身）——用只读遍历
if ($notes.Count -gt 0) { foreach ($n in @($notes)) { Write-Host "  [INFO] $n" -ForegroundColor Yellow } }
if ($failures.Count -eq 0) { Write-Host '  L2 通过 ✅' -ForegroundColor Green; exit 0 }
Write-Host "  L2 失败：$($failures.Count) 项" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
exit 1
