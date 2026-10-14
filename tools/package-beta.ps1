<#
.SYNOPSIS
Packages a beta using the supported deploy/build workflow and an explicit file allowlist.
.DESCRIPTION
By default invokes tools/deploy.ps1 (which builds and deploys; close the game first).
Use -UseExistingBuild to avoid deployment and package an already deployed matching build.
No game binaries, loader binaries, logs, probes or saves are included.
#>
[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\GOG Games\Cloudpunk',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputPath,
    [switch]$UseExistingBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot ('build\CloudpunkCoop-beta-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ([System.IO.Path]::GetExtension($OutputPath) -ne '.zip') { throw 'OutputPath must end in .zip.' }
if (Test-Path -LiteralPath $OutputPath) { throw "Refusing to overwrite existing archive: $OutputPath" }

if (-not $UseExistingBuild) {
    & (Join-Path $PSScriptRoot 'deploy.ps1') -GameRoot $GameRoot -Configuration $Configuration
}

$outDir = Join-Path $repoRoot "src\CloudpunkCoop.Plugin\bin\$Configuration"
$dllNames = @('CloudpunkCoop.Plugin.dll', 'CloudpunkCoop.Protocol.dll')
# Catch stale output after edits, including newly added source and project files.
$inputs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src\CloudpunkCoop.Plugin'),
    (Join-Path $repoRoot 'src\CloudpunkCoop.Protocol') -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.csproj', '.props', '.targets') -and
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
foreach ($name in $dllNames) {
    $relevantInputs = $inputs
    if ($name -eq 'CloudpunkCoop.Protocol.dll') {
        $relevantInputs = @($inputs | Where-Object { $_.FullName.StartsWith(
            (Join-Path $repoRoot 'src\CloudpunkCoop.Protocol') + '\', [System.StringComparison]::OrdinalIgnoreCase) })
    }
    $newestInput = ($relevantInputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    $built = Join-Path $outDir $name
    $deployed = Join-Path $GameRoot "BepInEx\plugins\$name"
    if (-not (Test-Path -LiteralPath $built -PathType Leaf) -or
        -not (Test-Path -LiteralPath $deployed -PathType Leaf)) {
        throw "Missing supported build/deployment for $name. Run tools\deploy.ps1 first."
    }
    if ((Get-Item -LiteralPath $built).LastWriteTimeUtc -lt $newestInput) {
        throw "Build is older than source: $name. Run tools\deploy.ps1 first."
    }
    if ((Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $deployed -Algorithm SHA256).Hash) {
        throw "Build and deployed DLL differ: $name. Run tools\deploy.ps1 first."
    }
}

$notes = Join-Path $repoRoot 'docs\BETA-INSTALL.md'
if (-not (Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Install notes missing: $notes" }
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('cloudpunk-coop-beta-' + [System.Guid]::NewGuid().ToString('N'))
try {
    $plugins = Join-Path $stage 'BepInEx\plugins'
    $configDir = Join-Path $stage 'BepInEx\config'
    New-Item -ItemType Directory -Path $plugins, $configDir -Force | Out-Null
    foreach ($name in $dllNames) { Copy-Item -LiteralPath (Join-Path $outDir $name) -Destination $plugins }
    Copy-Item -LiteralPath $notes -Destination (Join-Path $stage 'BETA-INSTALL.md')
    @'
# Example only. Rename to dev.andre.cloudpunk-coop.cfg after reviewing settings.
[Developer]
EnableDiagnostics = false

[Automation]
RunScriptedDemo = false

[Network]
Role = Off
Port = 27050
Address = 127.0.0.1
IndependentCars = true

[Campaign]
Id = campaign-1
'@ | Set-Content -LiteralPath (Join-Path $configDir 'dev.andre.cloudpunk-coop.cfg.example') -Encoding ASCII

    $expected = @('BETA-INSTALL.md', 'BepInEx\plugins\CloudpunkCoop.Plugin.dll',
        'BepInEx\plugins\CloudpunkCoop.Protocol.dll', 'BepInEx\config\dev.andre.cloudpunk-coop.cfg.example')
    $actual = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
        ForEach-Object { $_.FullName.Substring($stage.Length + 1) })
    if (@(Compare-Object $expected $actual).Count -ne 0) { throw 'Unexpected package contents.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputPath
    Write-Host "Beta archive: $OutputPath"
    Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash)
}
finally {
    # Delete only this invocation's uniquely named staging folder under the temp root.
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedStage = [System.IO.Path]::GetFullPath($stage)
    if (-not $resolvedStage.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolvedStage) -notmatch '^cloudpunk-coop-beta-[0-9a-f]{32}$') {
        throw 'Unsafe staging cleanup path.'
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
