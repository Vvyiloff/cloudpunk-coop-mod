# Optional read-only integrity check. Does not install or modify anything.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$count = 0
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'CHECKSUMS.sha256') -Encoding UTF8) {
    if (-not $line.Trim()) { continue }
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid manifest record.' }
    $expected = $Matches[1]
    $relative = $Matches[2].Replace('/', '\')
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped package.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) {
        throw "File checksum mismatch: $relative"
    }
    $count++
}
Write-Output "PASS: $count package files match SHA256."
