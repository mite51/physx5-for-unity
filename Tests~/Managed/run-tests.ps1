<#
.SYNOPSIS
    Runs the UNDPWR managed tests without opening Unity.

.DESCRIPTION
    Locates a Unity installation's managed assemblies, then builds and runs
    UNDPWR.ManagedTests against them. Exits non-zero if anything fails, so it can be used
    as a verification gate.

.PARAMETER UnityManagedDir
    A Unity installation's Editor\Data\Managed\UnityEngine directory. Discovered from the
    Unity Hub install root when omitted, preferring the newest version.

.PARAMETER Filter
    A VSTest --filter expression, for running a subset.

.EXAMPLE
    .\run-tests.ps1
.EXAMPLE
    .\run-tests.ps1 -Filter "FullyQualifiedName~SimTimingTests"
#>
[CmdletBinding()]
param(
    [string] $UnityManagedDir,
    [string] $Filter,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Get-UnityVersionKey {
    param([string] $Name)

    # Unity names installs like 2022.3.4f1 and 6000.5.6f1. Sorting the strings puts 6000
    # before 2022 only by accident of the first character, so compare the numbers.
    $match = [regex]::Match($Name, '^(\d+)\.(\d+)\.(\d+)')
    if (-not $match.Success) {
        return [version]'0.0.0'
    }
    return [version]::new(
        [int]$match.Groups[1].Value,
        [int]$match.Groups[2].Value,
        [int]$match.Groups[3].Value)
}

function Find-UnityManagedDir {
    $roots = @(
        $env:UNITY_HUB_EDITOR_DIR,
        (Join-Path ${env:ProgramFiles} 'Unity\Hub\Editor'),
        (Join-Path ${env:ProgramFiles(x86)} 'Unity\Hub\Editor'),
        '/Applications/Unity/Hub/Editor'
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

    foreach ($root in $roots) {
        $found =
            Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                foreach ($relative in @(
                    'Editor\Data\Managed\UnityEngine',
                    'Unity.app/Contents/Managed/UnityEngine')) {
                    $managed = Join-Path $_.FullName $relative
                    if (Test-Path (Join-Path $managed 'UnityEngine.CoreModule.dll')) {
                        [pscustomobject]@{ Version = $_.Name; Path = $managed }
                        break
                    }
                }
            } |
            Sort-Object { Get-UnityVersionKey $_.Version }

        if ($found) {
            return ($found | Select-Object -Last 1)
        }
    }

    return $null
}

if (-not $UnityManagedDir) {
    $unity = Find-UnityManagedDir
    if (-not $unity) {
        throw "No Unity installation found. Pass -UnityManagedDir with a path to an installation's Editor\Data\Managed\UnityEngine directory, or set UNITY_HUB_EDITOR_DIR."
    }
    $UnityManagedDir = $unity.Path
    Write-Host "Unity $($unity.Version): $UnityManagedDir" -ForegroundColor DarkGray
}

$project = Join-Path $PSScriptRoot 'UNDPWR.ManagedTests.csproj'

$dotnetArgs = @(
    'test', $project,
    '--configuration', $Configuration,
    "-p:UnityManagedDir=$UnityManagedDir",
    '--logger', 'console;verbosity=normal'
)
if ($Filter) {
    $dotnetArgs += @('--filter', $Filter)
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
