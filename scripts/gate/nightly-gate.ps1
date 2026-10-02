# ─────────────────────────────────────────────────────────────────────────────
# LiteGame 夜间门禁（Windows 计划任务入口）
#
# 为什么要独立脚本而不是直接注册 l2-unity-gate.ps1：
#   计划任务需要「跑 → 落日志 → 写退出码 → 可诊断」。l2 脚本自身不落盘日志，
#   任务计划程序的历史记录又只保留有限条，所以这里包一层。
#
# 跑什么：
#   默认 L2（Unity 门禁：meta 扫描 + 编译状态 + EditMode/PlayMode）。
#   桌面会话中编辑器在跑 → 经 Unity Pipeline；没有编辑器 → batchmode。
#   本脚本不掩盖：执行结束后会核对日志里有没有 PlayMode 段，没有就如实标注。
#
# 退出码：0 = 通过；非 0 = 失败（任务计划程序据此显示"上次运行结果"）。
# 日志：<repo>\TestResults\nightly\L2-<日期>.log（UTF-8）
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [switch]$SkipL1
)

$ErrorActionPreference = 'Stop'

# project root: derived from this script's location (was a machine-specific hardcoded path)
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = (Resolve-Path (Join-Path $here '..\..')).Path }

# 编码链：PowerShell 5.1 默认按控制台代码页（本机 GBK/936）解码子进程 stdout。
# 子进程（l2-unity-gate.ps1 / Unity CLI）输出含中文时会被解成乱码，再经 Add-Content 落盘
# 就得到无效 UTF-8——日志作为"证据"直接废掉。故显式统一下行到 UTF-8。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$stamp   = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$logDir  = Join-Path $ProjectPath 'TestResults\nightly'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logFile = Join-Path $logDir "L2-$stamp.log"

function Write-Log([string]$text) {
    # 同时进控制台与文件（任务计划程序会捕获控制台，但历史条目有限，文件才是长期证据）。
    # 用 .NET 显式 UTF-8（无 BOM）而非 Add-Content —— 后者在 5.1 下行为随 $PSDefaultParameterValues 漂移。
    Write-Host $text
    [System.IO.File]::AppendAllText($logFile, $text + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
}

$exitCode = 0
Write-Log "==== LiteGame 夜间门禁 $stamp ===="
Write-Log "工程：$ProjectPath"
Write-Log "主机：$env:COMPUTERNAME  用户：$env:USERNAME"
Write-Log "编辑器进程：$((Get-Process -Name Unity -ErrorAction SilentlyContinue | Measure-Object).Count) 个"

try {
    # ── L1（dotnet 侧，秒级；默认一并跑，便于「夜间一把过」）──
    if (-not $SkipL1) {
        Write-Log ''
        Write-Log '---- L1 ----'
        $l1 = & (Join-Path $ProjectPath 'scripts\test.ps1') -Lane L1 -Profile Nightly *>&1
        $l1 | ForEach-Object { Write-Log "  $_" }
        if ($LASTEXITCODE -ne 0) { Write-Log "  [FAIL] L1 退出码 $LASTEXITCODE"; $exitCode = 1 }
        else { Write-Log '  [PASS] L1' }
    }

    # ── L2（Unity 门禁）──
    Write-Log ''
    Write-Log '---- L2 ----'
    $l2Script = Join-Path $ProjectPath 'scripts\l2-unity-gate.ps1'
    $l2 = & powershell -NoProfile -File $l2Script -ProjectPath $ProjectPath *>&1
    $l2 | ForEach-Object { Write-Log "  $_" }
    if ($LASTEXITCODE -ne 0) { Write-Log "  [FAIL] L2 退出码 $LASTEXITCODE"; $exitCode = 1 }
    else { Write-Log '  [PASS] L2' }

    # ── 如实标注 PlayMode 是否真的跑了（覆盖两种模式的不同输出形态）──
    #   Pipeline 模式：打印 "run_tests(PlayMode): Total=… Passed=…"
    #   batchmode 模式：打印 "PlayMode 测试通过/未通过，报告见 …"
    # 只匹配一种会让另一种模式下误报"未执行"。
    $l2Text = $l2 | Out-String
    $ranPlayMode = ($l2Text -match 'run_tests\(PlayMode\)') -or ($l2Text -match 'PlayMode 测试(通过|未通过)')
    Write-Log ''
    if ($ranPlayMode) { Write-Log 'PlayMode：已执行' }
    else { Write-Log 'PlayMode：**本次未执行**——请检查 L2 是否因「编辑器在跑但管线不可用」提前失败' }
}
catch {
    Write-Log "[FAIL] 夜间门禁异常：$($_.Exception.Message)"
    Write-Log $_.ScriptStackTrace
    $exitCode = 1
}
finally {
    Write-Log ''
    Write-Log "==== 结束：退出码 $exitCode ===="
    Write-Log "日志：$logFile"
}

exit $exitCode
