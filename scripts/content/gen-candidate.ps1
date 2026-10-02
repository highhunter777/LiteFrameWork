# ─────────────────────────────────────────────────────────────────────────────
# 候选信封生成器（发布端工具；《热更与内容发布专项设计》§6）
#
# 用途：把一份候选内容目录打成**已签名候选信封**，供运行时 FileSystemCandidateProvider
# 读取（约定路径 content/candidate.json）。
#
# 用法：
#   powershell -NoProfile -File scripts/content/gen-candidate.ps1 `
#       -ContentDir <候选目录> -ReleaseId <id> -Revision <n> [-OutFile <路径>] [-ExpiresInDays <n>]
#
# 私钥：默认 ~/.unitylib-content-signing/release-key-2026-09-26.xml（**仓库外，绝不入库**）。
#
# ── 编码契约（与运行时逐字节对齐，改动前必读）────────────────────────────────
# 运行时 SignedManifestEnvelope.Parse（Assets/LiteClient/Runtime/Resource/ContentRuntimeAdapters.cs:249）
# 这样取被签名字节：
#     manifestToken.ToString(Newtonsoft.Json.Formatting.None)
# 即**紧凑 JSON**（无空白/无换行、字段按插入序）。随后 ToObject<ReleaseManifest>() 反序列化。
# 故签发端必须让「签名覆盖的字节」==「该紧凑序列化结果」，否则验签必失败。
#
# 本脚本由**独立实现**产出该字节（Write-CanonicalManifest，手写序化）——不复用
# SignedManifestEnvelope。理由：复用会把"序列化契约不一致"这类失效掩盖掉
# （契约一旦漂移，两端用同一份错误实现仍会"自洽通过"）；跨实现一致才是真证据。
# 字段名与序由 ReleaseManifest 的**字段声明序**决定，见该文件。
#
# ── 退出码 ──────────────────────────────────────────────────────────────────
#   0 = 生成成功且**已用内置锚点公钥验签通过**（自校验）
#   非 0 = 失败（私钥缺失/候选目录为空/自校验不过——详情见输出）
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ContentDir,
    [Parameter(Mandatory = $true)][string]$ReleaseId,
    [Parameter(Mandatory = $true)][int]$Revision,
    [string]$OutFile = 'content/candidate.json',
    [int]$ExpiresInDays = 30,
    [string]$Platform = '',
    [string]$AnchorsCs = 'Assets/LiteClient/Runtime/Resource/ContentTrustAnchors.cs',
    [string]$KeyPath = "$env:USERPROFILE\.unitylib-content-signing\release-key-2026-09-26.xml",
    [string]$KeyId = 'release-key-2026-09-26'
)

$ErrorActionPreference = 'Stop'

function Fail([string]$msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red; exit 1 }
function Ok([string]$msg)   { Write-Host "  [PASS] $msg" -ForegroundColor Green }

if (-not (Test-Path -LiteralPath $ContentDir)) { Fail "候选目录不存在：$ContentDir" }
if (-not (Test-Path -LiteralPath $KeyPath)) {
    Fail "私钥不存在：$KeyPath`n        私钥由 provisioning 阶段生成，存签名机用户目录，绝不入库。"
}
if (-not (Test-Path -LiteralPath $AnchorsCs)) { Fail "锚表文件不存在：$AnchorsCs（用于自校验）" }

# ── ① 扫候选目录：只收受控前缀（与 ReleaseLayout 同源约定）────────────────────
# ReleaseLayout：config/**（Luban 表字节）+ lua/**.lua（热更脚本）+ bundle/**（资产包根镜像：
# YooAsset 包根的版本/清单/Bundle 文件，运行时以该根初始化资源包）。
# 其余文件一律跳过并报告——静默收录会让"没被打包的内容"看起来已被签名。
$root = (Resolve-Path -LiteralPath $ContentDir).Path
$entries = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[string]

foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $isConfig = $rel.StartsWith('config/')
    $isLua = $rel.StartsWith('lua/') -and $rel.EndsWith('.lua')
    $isBundle = $rel.StartsWith('bundle/')
    if (-not ($isConfig -or $isLua -or $isBundle)) { $skipped.Add($rel); continue }

    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hex = ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '' }
    finally { $sha.Dispose() }
    $entries.Add([pscustomobject]@{ Path = $rel; Length = [long]$bytes.Length; Sha256 = $hex })
}
$entries = $entries | Sort-Object Path        # 稳定序（同输入同输出，便于比对）

if ($entries.Count -eq 0) {
    Fail "候选目录内无受控内容——需 config/**、lua/**.lua 或 bundle/**（ReleaseLayout 约定）。跳过 $($skipped.Count) 个非受控文件。"
}
Write-Host "候选条目 $($entries.Count) 个（跳过非受控 $($skipped.Count) 个）"

# ── ② 组 manifest 并产出**规范紧凑字节**（与运行时 Formatting.None 对齐）──────
# 字段序 == ReleaseManifest 声明序：schemaVersion, releaseId, revision, platform, channel,
# compatibility, dependencies, files, createdAtUnix, expiresAtUnix, keyId, signature,
# effectWindow, revoked, note。**signature 由运行时的 signing 流程回填，此处留空串**
# （字段存在且为 ""，与 Newtonsoft 序列化 null/空字符串的行为对齐）。
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$expires = $now + ([long]$ExpiresInDays * 86400)

function Esc([string]$s) {
    if ($null -eq $s) { return '""' }
    return '"' + ($s -replace '\\', '\\\\' -replace '"', '\"' -replace "`n", '\n' -replace "`r", '\r' -replace "`t", '\t') + '"'
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{"SchemaVersion":1')
[void]$sb.Append(',"ReleaseId":').Append((Esc $ReleaseId))
[void]$sb.Append(',"Revision":').Append($Revision)
[void]$sb.Append(',"Platform":').Append((Esc $Platform))
[void]$sb.Append(',"Channel":""')
# ReleaseCompatibility：空对象（不限任何下限）——字段全为默认值
[void]$sb.Append(',"Compatibility":{}')
[void]$sb.Append(',"Dependencies":[]')
[void]$sb.Append(',"Files":[')
for ($i = 0; $i -lt $entries.Count; $i++) {
    if ($i -gt 0) { [void]$sb.Append(',') }
    $e = $entries[$i]
    [void]$sb.Append('{"Path":').Append((Esc $e.Path))
    [void]$sb.Append(',"Length":').Append($e.Length)
    [void]$sb.Append(',"Sha256":').Append((Esc $e.Sha256)).Append('}')
}
[void]$sb.Append(']')
[void]$sb.Append(',"CreatedAtUnix":').Append($now)
[void]$sb.Append(',"ExpiresAtUnix":').Append($expires)
[void]$sb.Append(',"KeyId":').Append((Esc $KeyId))
[void]$sb.Append(',"Signature":""')
[void]$sb.Append(',"EffectWindow":0')
[void]$sb.Append(',"Revoked":false')
[void]$sb.Append(',"Note":""')
[void]$sb.Append('}')

$canonical = $sb.ToString()
$canonicalBytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)

# ── ③ 用发布私钥签名（RSA PKCS#1 v1.5 + SHA-256，与 RsaSignatureVerifier 同款）──
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.FromXmlString([System.IO.File]::ReadAllText($KeyPath))
    $sig = $rsa.SignData($canonicalBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
} finally { $rsa.Dispose() }

# ── ④ 写信封（manifest 段 = 上面那份**规范字节**，逐字节一致）────────────────
# 信封里的 manifest 用同一份 $canonical 字符串，运行时 Parse 会对它再跑一次
# Formatting.None —— 若 $canonical 已是最紧凑形式，二者逐字节相同，验签通过。
$envelope = '{"manifest":' + $canonical + ',"signature":"' + [Convert]::ToBase64String($sig) + '"}'

$outFull = if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile }
           else { Join-Path (Get-Location) $OutFile }
$outDir = Split-Path -Parent $outFull
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
[System.IO.File]::WriteAllText($outFull, $envelope, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "信封已写入：$outFull"

# ── ⑤ 自校验：用**内置锚点公钥**（从锚表 .cs 取，即真正编进包的那份）验签 ──────
$cs = [System.IO.File]::ReadAllText($AnchorsCs)
$m = [regex]::Match($cs, 'FromBase64String\("([^"]{100,})"\)')
if (-not $m.Success) { Fail "未能从锚表提取公钥 Modulus：$AnchorsCs" }
$modB64 = $m.Groups[1].Value

$pub = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $pub.FromXmlString("<RSAKeyValue><Modulus>$modB64</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>")
    $verified = $pub.VerifyData($canonicalBytes, $sig,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
} finally { $pub.Dispose() }

if (-not $verified) { Fail "自校验失败：内置锚点公钥验不过本签名——信封不可用（勿分发）" }
Ok "自校验通过（内置锚点 $KeyId 验签成功）"

$skippedMsg = if ($skipped.Count -gt 0) { "；跳过非受控文件 $($skipped.Count) 个（未签名）" } else { '' }
Write-Host "完成：releaseId=$ReleaseId revision=$Revision 条目=$($entries.Count)$skippedMsg" -ForegroundColor Cyan
exit 0
