# ─────────────────────────────────────────────────────────────────────────────
# LiteGame 夜间门禁（Windows 计划任务入口；2026-09-26）
#
# 为什么要独立脚本而不是直接注册 l2-unity-gate.ps1：
#   计划任务需要「跑 → 落日志 → 写退出码 → 可诊断」。l2 脚本自身不落盘日志，
#   任务计划程序的历史记录又只保留有限条，所以这里包一层。
#
# 跑什么：
#   默认 L2（Unity 门禁：meta 扫描 + 编译状态 + EditMode/PlayMode）。
#   桌面会话中编辑器在跑 → 经 Unity Pipeline；没有编辑器 → batchmode。
#   **batchmode 分支目前只跑 EditMode**（PlayMode 仅 Pipeline 分支覆盖）——
#   本脚本不掩盖这一点：执行结束后会核对日志里有没有 PlayMode 段，没有就如实标注。
#
# 退出码：0 = 通过；非 0 = 失败（任务计划程序据此显示"上次运行结果"）。
# 日志：<repo>\TestResults\nightly\L2-<日期>.log（UTF-8）
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = 'E:\unityProject\Test',
    [switch]$SkipL1
)

$ErrorActionPreference = 'Stop'

$stamp   = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$logDir  = Join-Path $ProjectPath 'TestResults\nightly'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logFile = Join-Path $logDir "L2-$stamp.log"

function Write-Log([string]$text) {
    # 同时进控制台与文件（任务计划程序会捕获控制台，但历史条目有限，文件才是长期证据）
    Write-Host $text
    Add-Content -LiteralPath $logFile -Value $text -Encoding UTF8
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

    # ── 如实标注 PlayMode 是否真的跑了（batchmode 分支不含它）──
    $ranPlayMode = ($l2 | Out-String) -match 'run_tests\(PlayMode\)'
    Write-Log ''
    if ($ranPlayMode) { Write-Log 'PlayMode：已执行（见上方 run_tests(PlayMode) 行）' }
    else { Write-Log 'PlayMode：**本次未执行**——batchmode 分支只跑 EditMode；要覆盖 PlayMode 需编辑器在跑（Pipeline 模式）' }
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
