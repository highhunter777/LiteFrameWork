# Sync code generator (LiteSim <-> wire)
#
# Why this exists: adding one field to EntitySlot used to mean editing SIX places
# (struct + two checksum profiles + snapshot entry + proto + codec). Miss one and
# nothing fails to compile -- you get a silent divergence instead. That is not
# hypothetical: CorpseFrames shipped with "public" in its comment, "public" in the
# public checksum, and NO wire field, which made every reconcile in the 180-frame
# corpse window a false one.
#
# The single source of truth is the [StateLayer] attribute on each field. This
# script parses EntitySlot.cs SOURCE (not the compiled assembly) and emits the
# mechanical half of the sync plumbing as a PARTIAL struct, so hand-written files
# keep their shape and the field->wire mapping lives in exactly one place.
#
# WHY SOURCE AND NOT REFLECTION: SimChecksum calls the generated mixers, and the
# generated mixers live in EntitySlot -- so the assembly cannot build before the
# file is generated. Reflecting over the assembly is a chicken-and-egg deadlock.
# Parsing source breaks it and has no build-order coupling.
#
# Usage:  pwsh -NoProfile -File scripts/codegen/gen-sync-code.ps1
# Output: Assets/LiteSim/Core/Diagnostics/Generated/EntitySlot.Sync.g.cs
#         (checked in -- same convention as Battle.cs / BuildHash.g.cs)
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$srcFile = Join-Path $repoRoot 'Assets/LiteSim/Core/Scripts/EntitySlot.cs'
$outDir = Join-Path $repoRoot 'Assets/LiteSim/Core/Diagnostics/Generated'
$outFile = Join-Path $outDir 'EntitySlot.Sync.g.cs'

# Structs covered by generation. Slot-index layering (see below) applies to
# ActionRuntime, whose public/private split depends on the SUB-SLOT INDEX:
# slot 0 is the public action summary, slots 1..3 are the private skill ledger.
$targets = @(
    @{ Name = 'EntitySlot';     Src = 'Assets/LiteSim/Core/Scripts/EntitySlot.cs' },
    @{ Name = 'WeaponRuntime';  Src = 'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs' },
    @{ Name = 'ActionRuntime';  Src = 'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs' },
    @{ Name = 'StatusSlotData'; Src = 'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs' },
    @{ Name = 'MatchBagSlot';   Src = 'Assets/LiteSim/Core/Scripts/SimMatchRuntime.cs' }
)

function Parse-StateLayer {
    param([string]$text)

    $layer = $null
    if ($text -match 'StateLayer\.(Public|Private|Internal)') { $layer = $Matches[1] }
    if (-not $layer) { throw "cannot parse layer from: $text" }

    $flat = $null
    if ($text -match 'flatten:\s*new\[\]\s*\{([^}]*)\}') {
        $flat = @($Matches[1].Split(',') | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
    }

    $wire = $null
    if ($text -match 'wireType:\s*"([^"]+)"') { $wire = $Matches[1] }

    # Slot-index condition: some structs are layered BY ARRAY POSITION (ActionRuntime:
    # slot 0 is the public action summary, slots 1..3 are private skill ledgers).
    # That cannot be inferred from the type -- it must be declared.
    $onlySlot = -1
    if ($text -match 'onlySlotIndex:\s*(-?\d+)') { $onlySlot = [int]$Matches[1] }

    $otherwise = 'Private'
    if ($text -match 'otherwiseLayer:\s*StateLayer\.(\w+)') { $otherwise = $Matches[1] }

    return [pscustomobject]@{ Layer = $layer; Flatten = $flat; Wire = $wire
                              OnlySlot = $onlySlot; Otherwise = $otherwise }
}
# Collect [StateLayer]-decorated fields of one struct, in DECLARATION order.
function Get-StructFields {
    param([string]$file, [string]$structName)

    if (-not (Test-Path $file)) { throw "source not found: $file" }
    $src = [System.IO.File]::ReadAllLines($file)

    $inside = $false
    $pending = $null
    $result = New-Object System.Collections.ArrayList

    foreach ($raw in $src) {
        $line = $raw.Trim()

        if (-not $inside) {
            if ($line -match "^(public|internal)\s+(partial\s+)?struct\s+$structName\b") { $inside = $true }
            continue
        }

        if ($line -eq '}') { break }        # end of struct body

        if ($line.StartsWith('[StateLayer(')) {
            $pending = Parse-StateLayer $line
            continue
        }
        if ($null -eq $pending) { continue }

        if ($line -match '^\s*public\s+([A-Za-z_][\w\.]*)\s+([A-Za-z_]\w*)\s*;') {
            [void]$result.Add([pscustomobject]@{
                Type      = $Matches[1]
                Name      = $Matches[2]
                Layer     = $pending.Layer
                Flatten   = $pending.Flatten
                Wire      = $pending.Wire
                OnlySlot  = $pending.OnlySlot
                Otherwise = $pending.Otherwise
            })
        }
        $pending = $null
    }

    if ($result.Count -eq 0) { throw "no [StateLayer] fields found in struct '$structName' ($file)" }
    return $result
}

function Get-MixCall {
    param([string]$typeName, [string]$valueExpr, [string]$fieldName)

    switch ($typeName) {
        # C# keyword spellings (source-parsed)
        'long'       { return "h = SimChecksum.MixInt64(h, $valueExpr);" }
        'int'        { return "h = SimChecksum.MixInt32(h, $valueExpr);" }
        'uint'       { return "h = SimChecksum.MixUInt32(h, $valueExpr);" }
        'byte'       { return "h = SimChecksum.MixByte(h, $valueExpr);" }
        'ulong'      { return "h = SimChecksum.MixUInt64(h, $valueExpr);" }
        # BCL spellings (kept for flattened components / explicit wire types)
        'Int64'      { return "h = SimChecksum.MixInt64(h, $valueExpr);" }
        'Int32'      { return "h = SimChecksum.MixInt32(h, $valueExpr);" }
        'UInt32'     { return "h = SimChecksum.MixUInt32(h, $valueExpr);" }
        'Byte'       { return "h = SimChecksum.MixByte(h, $valueExpr);" }
        'Single'     { return "h = SimChecksum.MixFloat(h, $valueExpr);" }
        'float'      { return "h = SimChecksum.MixFloat(h, $valueExpr);" }
        'int32'      { return "h = SimChecksum.MixInt32(h, $valueExpr);" }
        'uint32'     { return "h = SimChecksum.MixUInt32(h, $valueExpr);" }
        'int64'      { return "h = SimChecksum.MixInt64(h, $valueExpr);" }
        'uint64'     { return "h = SimChecksum.MixUInt64(h, $valueExpr);" }
        default {
            # enum names end with 'State' / 'Phase' / 'Kind' in this codebase; treat
            # anything not a known scalar as an enum carried as a byte
            if ($typeName -match '(State|Phase|Kind|Flags)$') { return "h = SimChecksum.MixByte(h, (byte)$valueExpr);" }
            throw "no mixer for type '$typeName' on field '$fieldName' -- add a wireType to its [StateLayer]"
        }
    }
}
# Emit one fold method for one struct.
#   $valueExprPrefix : how to reach the value, e.g. 'e' (slot) or 'a' (array item)
#   $slotExpr        : expression for the sub-slot index, or $null when the struct
#                      has no slot-indexed layering (most of them)
function Write-Profile {
    param($sb, [string]$structName, [string]$methodName, [string]$doc,
          [string[]]$layers, $structFields, [string]$valueExprPrefix = 'e',
          [string]$slotExpr = $null, [string]$slotParm = $null)

    [void]$sb.AppendLine("        /// <summary>$doc</summary>")
    $sig = if ($slotParm) { "internal static uint $methodName(uint h, in $structName $valueExprPrefix, int $slotParm)" }
           else           { "internal static uint $methodName(uint h, in $structName $valueExprPrefix)" }
    [void]$sb.AppendLine("        $sig")
    [void]$sb.AppendLine('        {')

    $needsBranch = $false
    foreach ($f in $structFields) { if ($f.OnlySlot -ge 0) { $needsBranch = $true; break } }

    foreach ($f in $structFields) {
        $effective = $f.Layer

        if ($f.OnlySlot -ge 0 -and $slotExpr) {
            # Slot-indexed layering: the SAME field belongs to different layers
            # depending on the sub-slot index.
            #   public profile -> fold only for the designated slot (else it would
            #                     leak private skill data into the reconcile anchor)
            #   full profile   -> fold unconditionally for every slot (it is a
            #                     superset; skipping here would drop deterministic state)
            $inLayers = $layers -contains $f.Layer
            $outLayers = $layers -contains $f.Otherwise

            if ($inLayers -and $outLayers) {
                # full profile reaches this field through both layers -> no branch needed
                [void]$sb.AppendLine('            ' + (Get-MixCall -typeName $f.Type -valueExpr "$valueExprPrefix.$($f.Name)" -fieldName $f.Name))
                continue
            }
            if (-not $inLayers -and -not $outLayers) { continue }

            # NOTE: slotExpr is a C# expression (e.g. `slot`). Build the condition with
            # the C# variable name, not a PowerShell-expanded value.
            $csharpSlot = $slotExpr.TrimStart('$')
            $cond = "$csharpSlot == $($f.OnlySlot)"
            $negCond = "$csharpSlot != $($f.OnlySlot)"
            $use = if ($inLayers) { $cond } else { $negCond }
            [void]$sb.AppendLine("            if ($use)")
            [void]$sb.AppendLine('            {')
            [void]$sb.AppendLine('                ' + (Get-MixCall -typeName $f.Type -valueExpr "$valueExprPrefix.$($f.Name)" -fieldName $f.Name))
            [void]$sb.AppendLine('            }')
            continue
        }

        if ($layers -notcontains $effective) { continue }

        if ($f.Flatten -and $f.Flatten.Count -gt 0) {
            $comp = Get-ComponentType -compositeType $f.Type -fieldName $f.Name
            foreach ($part in $f.Flatten) {
                [void]$sb.AppendLine('            ' + (Get-MixCall -typeName $comp -valueExpr "$valueExprPrefix.$($f.Name).$part" -fieldName $f.Name))
            }
            continue
        }

        # NOTE: checksum folds by the DECLARED type, not by wireType. wireType only
        # describes how proto carries the value (byte -> uint32); folding by it would
        # change every existing hash for no reason.
        [void]$sb.AppendLine('            ' + (Get-MixCall -typeName $f.Type -valueExpr "$valueExprPrefix.$($f.Name)" -fieldName $f.Name))
    }

    [void]$sb.AppendLine('            return h;')
    [void]$sb.AppendLine('        }')
    [void]$sb.AppendLine()
}
function Get-ComponentType {
    param([string]$compositeType, [string]$fieldName)
    if ($compositeType -eq 'SimVector3') { return 'Single' }
    throw "no flatten rule for composite '$compositeType' on field '$fieldName'"
}

$lines = [System.IO.File]::ReadAllLines($srcFile)

# -- scan (defined above) -- collect fields for every target struct
$structFields = @{}
foreach ($t in $targets) {
    $structFields[$t.Name] = Get-StructFields -file (Join-Path $repoRoot $t.Src) -structName $t.Name
}


# -- map a field (or one flattened component) to its mixer call --
# Resolution order: explicit scalar type > attribute wireType > declared type.

# Scalar type of a flattened component (SimVector3 -> three Single values).

# -- emit one profile --

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// <auto-generated>')
[void]$sb.AppendLine('//     Generated by scripts/codegen/gen-sync-code.ps1 -- DO NOT EDIT.')
[void]$sb.AppendLine('//     Source of truth: [StateLayer] attributes on the fields themselves.')
[void]$sb.AppendLine('//     The field declarations stay hand-written; this is the mechanical half of')
[void]$sb.AppendLine('//     the checksum plumbing. Adding a field = edit ONE place.')
[void]$sb.AppendLine('// </auto-generated>')
[void]$sb.AppendLine()
[void]$sb.AppendLine('namespace LiteSim')
[void]$sb.AppendLine('{')

foreach ($t in $targets) {
    $name = $t.Name
    $flds = $structFields[$name]

    [void]$sb.AppendLine("    public partial struct $name")
    [void]$sb.AppendLine('    {')

    # EntitySlot is a single value (no sub-slot); the array-backed runtimes pass
    # the sub-slot index in so slot-indexed layering can be expressed.
    switch ($name) {
        'EntitySlot' {
            Write-Profile -sb $sb -structName $name -methodName 'MixPublic' `
                -doc 'Public-profile checksum fold (order = field declaration order).' `
                -layers @('Public') -structFields $flds
            Write-Profile -sb $sb -structName $name -methodName 'MixFull' `
                -doc 'Full-profile fold (ordered by enum value; iterates all sub-slots).' `
                -layers @('Public', 'Private', 'Internal') -structFields $flds
        }
        'ActionRuntime' {
            # slot-indexed: sub-slot 0 is the public summary, 1..3 are private.
            # The loop covers all sub-slots; the branch picks public vs private per slot.
            Write-Profile -sb $sb -structName $name -methodName 'MixPublic' `
                -doc 'Public-profile fold for one sub-slot (0 = action summary; 1..3 contribute nothing).' `
                -layers @('Public') -structFields $flds -valueExprPrefix 'a' -slotExpr '$slot' -slotParm 'slot'
            Write-Profile -sb $sb -structName $name -methodName 'MixFull' `
                -doc 'Full-profile fold for one sub-slot (all deterministic fields).' `
                -layers @('Public', 'Private', 'Internal') -structFields $flds -valueExprPrefix 'a' -slotExpr '$slot' -slotParm 'slot'
        }
        default {
            Write-Profile -sb $sb -structName $name -methodName 'MixFull' `
                -doc 'Full-profile fold for one element (all deterministic fields).' `
                -layers @('Public', 'Private', 'Internal') -structFields $flds -valueExprPrefix 'a'
        }
    }

    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine()
}
[void]$sb.AppendLine('}')

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$new = $sb.ToString()
$old = if (Test-Path $outFile) { [System.IO.File]::ReadAllText($outFile) } else { '' }
if ($new -eq $old) {
    Write-Host "up to date: $outFile" -ForegroundColor Green
}
else {
    [System.IO.File]::WriteAllText($outFile, $new)
    Write-Host "generated: $outFile" -ForegroundColor Green
}
