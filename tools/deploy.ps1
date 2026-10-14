<#
.SYNOPSIS
Builds the plugin and copies it into the game's BepInEx plugins folder.

.DESCRIPTION
Iteration 1+ use this after every code change. It is the only supported way to get a
build into the game, so the deployed DLL can never drift from a verified build.

.EXAMPLE
.\tools\deploy.ps1
.\tools\deploy.ps1 -GameRoot 'D:\Games\Cloudpunk'
#>
[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\GOG Games\Cloudpunk',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\CloudpunkCoop.Plugin\CloudpunkCoop.Plugin.csproj'
$outDir = Join-Path $repoRoot "src\CloudpunkCoop.Plugin\bin\$Configuration"
$dll = Join-Path $outDir 'CloudpunkCoop.Plugin.dll'
$protocolDll = Join-Path $outDir 'CloudpunkCoop.Protocol.dll'
$plugins = Join-Path $GameRoot 'BepInEx\plugins'

if (-not (Test-Path (Join-Path $GameRoot 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw "BepInEx interop assemblies not found under $GameRoot. Install the loader and launch the game once first."
}

if(Get-Process Cloudpunk -ErrorAction SilentlyContinue) { throw 'Close Cloudpunk before building and deploying the mod.' }

Write-Host "building $Configuration..."
dotnet build $project -c $Configuration -v minimal "-p:GameRoot=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }

if (-not (Test-Path $dll)) { throw "expected output not found: $dll" }

New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $dll $plugins -Force

# The protocol library is a dependency of the plugin; without it BepInEx fails to load the
# plugin with a FileNotFoundException that looks unrelated to networking.
if (Test-Path $protocolDll) {
    Copy-Item $protocolDll $plugins -Force
} else {
    throw "expected protocol library not found: $protocolDll"
}

$stale = Join-Path $plugins 'CloudpunkCoop.Plugin.pdb'
if (Test-Path $stale) { Remove-Item $stale -Force }

$deployed = Get-Item (Join-Path $plugins 'CloudpunkCoop.Plugin.dll')
Write-Host ("deployed {0} ({1} bytes, {2})" -f $deployed.FullName, $deployed.Length, $deployed.LastWriteTime)
$deployedProtocol = Get-Item (Join-Path $plugins 'CloudpunkCoop.Protocol.dll')
Write-Host ("deployed {0} ({1} bytes)" -f $deployedProtocol.FullName, $deployedProtocol.Length)
