<#
.SYNOPSIS
    Decides whether the current commit is a new release: reads <Version> from the app's project file and
    compares it with the existing v* tags.

.DESCRIPTION
    Used by the CI workflow (pull requests into main) and the release workflow (pushes to main).
    Returns an object with:
      Version        the project version, e.g. 0.2.0 or 0.3.0-beta.1
      Tag            the release tag, e.g. v0.2.0
      IsPrerelease   true if the version has a pre-release part (-beta.1 etc.)
      LatestRelease  the highest existing v* tag's version, or '' if there is none
      Status         'release'           the tag doesn't exist yet and the version is higher than every release
                     'already-released'  the tag already exists, so there is nothing to release
                     'older'             the tag doesn't exist, but the version is lower than the latest release
    In GitHub Actions (GITHUB_OUTPUT is set) the values are also written as step outputs: version, tag,
    prerelease, latest, status.

    Runs on PowerShell 7 (GitHub runners) and Windows PowerShell 5.1, so it doesn't use the [semver] type.

.PARAMETER ProjectPath
    The project file that holds <Version>. Defaults to the app's project.

.PARAMETER Tags
    The existing tags. Read from git when omitted; pass them to test the logic without a repository.
#>
[CmdletBinding()]
param(
    [string] $ProjectPath = (Join-Path $PSScriptRoot '..\..\src\InstantVolumeReducer\InstantVolumeReducer.csproj'),
    [string[]] $Tags
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# SemVer 2.0.0 without build metadata (a +suffix isn't part of a release version).
$SemVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$'

function ConvertTo-SemVer {
    param([string] $Text)

    $match = [regex]::Match($Text, $SemVerPattern)
    if (-not $match.Success) {
        return $null
    }

    $prerelease = [string[]]@()
    if ($match.Groups[4].Success) {
        $prerelease = $match.Groups[4].Value.Split('.')
    }

    [pscustomobject]@{
        Text       = $Text
        Major      = [long]$match.Groups[1].Value
        Minor      = [long]$match.Groups[2].Value
        Patch      = [long]$match.Groups[3].Value
        Prerelease = $prerelease
    }
}

# Returns -1, 0 or 1, following SemVer 2.0.0 precedence rules.
function Compare-SemVer {
    param($A, $B)

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($A.$part -ne $B.$part) {
            return [Math]::Sign($A.$part - $B.$part)
        }
    }

    $preA = $A.Prerelease
    $preB = $B.Prerelease
    if ($preA.Count -eq 0 -and $preB.Count -eq 0) { return 0 }
    if ($preA.Count -eq 0) { return 1 }   # 1.0.0 is higher than 1.0.0-beta.1
    if ($preB.Count -eq 0) { return -1 }

    for ($i = 0; $i -lt [Math]::Min($preA.Count, $preB.Count); $i++) {
        $x = $preA[$i]
        $y = $preB[$i]
        $xIsNumber = $x -match '^\d+$'
        $yIsNumber = $y -match '^\d+$'
        if ($xIsNumber -and $yIsNumber) { $order = ([long]$x).CompareTo([long]$y) }
        elseif ($xIsNumber) { $order = -1 }   # numeric identifiers are lower than alphanumeric ones
        elseif ($yIsNumber) { $order = 1 }
        else { $order = [string]::CompareOrdinal($x, $y) }

        if ($order -ne 0) {
            return [Math]::Sign($order)
        }
    }

    return [Math]::Sign($preA.Count - $preB.Count)
}

# ---- Version from the project file ----
[xml] $project = Get-Content -LiteralPath $ProjectPath -Raw
$versionNodes = @($project.SelectNodes('/Project/PropertyGroup/Version'))
if ($versionNodes.Count -ne 1) {
    throw "Expected exactly one <Version> in $ProjectPath, found $($versionNodes.Count)."
}

$version = $versionNodes[0].InnerText.Trim()
$semVer = ConvertTo-SemVer $version
if ($null -eq $semVer) {
    throw "<Version>$version</Version> in $ProjectPath isn't a release version like 1.2.3 or 1.2.3-beta.1."
}

# ---- Existing releases ----
if (-not $PSBoundParameters.ContainsKey('Tags')) {
    $Tags = @(git tag --list 'v*')
    if ($LASTEXITCODE -ne 0) {
        throw 'git tag failed. Check out the repository with its tags (fetch-depth: 0).'
    }
}

$latest = $null
foreach ($existing in @($Tags)) {
    if ($existing -cnotlike 'v*') { continue }
    $released = ConvertTo-SemVer $existing.Substring(1)
    if ($null -ne $released -and ($null -eq $latest -or (Compare-SemVer $released $latest) -gt 0)) {
        $latest = $released
    }
}

$tag = "v$version"
$status =
    if (@($Tags) -ccontains $tag) { 'already-released' }
    elseif ($null -eq $latest -or (Compare-SemVer $semVer $latest) -gt 0) { 'release' }
    else { 'older' }

$result = [pscustomobject][ordered]@{
    Version       = $version
    Tag           = $tag
    IsPrerelease  = $semVer.Prerelease.Count -gt 0
    LatestRelease = if ($null -ne $latest) { $latest.Text } else { '' }
    Status        = $status
}

if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value @(
        "version=$($result.Version)"
        "tag=$($result.Tag)"
        "prerelease=$($result.IsPrerelease.ToString().ToLowerInvariant())"
        "latest=$($result.LatestRelease)"
        "status=$($result.Status)"
    )
}

$result
