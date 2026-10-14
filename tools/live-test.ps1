# Launches Cloudpunk, drives it through the splash screen and the main menu into the
# loaded save, then performs scripted key actions and takes screenshots.
#
# This exists because the game will NOT reach the world on its own: it waits for a key on
# the "press any key" splash and then needs the first main-menu entry (Continue). Verification
# runs that skip those two steps sit on the splash screen and measure nothing.
#
# Input is injected with keybd_event *including the scan code*. Without the scan code the
# game ignores the synthetic key entirely, which cost real debugging time to find.
#
# -Actions is a comma separated list of:
#     tap:F6          press and release a key
#     hold:W:6000     press and hold for 6000 ms
#     shot:name.png   capture the screen
#     wait:5000       pause
#
# Example:
#   .\live-test.ps1 -Actions "shot:world.png,wait:2000,tap:F6,wait:30000,shot:override.png"

param(
    [string]$GameDir = "C:\GOG Games\Cloudpunk",
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cloudpunk-coop-tests'),
    [string]$Actions = "shot:world.png",
    [int]$StartupSeconds = 14,
    [int]$MenuSettleSeconds = 5,
    [int]$WorldLoadSeconds = 45,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
[DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
'@ -Name Win -Namespace CloudpunkCoop

$script:Vk = @{
    "SPACE" = @(0x20, 0x39); "W" = @(0x57, 0x11); "A" = @(0x41, 0x1E); "S" = @(0x53, 0x1F)
    "D" = @(0x44, 0x20); "Q" = @(0x51, 0x10); "E" = @(0x45, 0x12); "F" = @(0x46, 0x21)
    "SHIFT" = @(0x10, 0x2A); "ENTER" = @(0x0D, 0x1C); "ESC" = @(0x1B, 0x01)
    "F1" = @(0x70, 0x3B); "F2" = @(0x71, 0x3C); "F3" = @(0x72, 0x3D); "F4" = @(0x73, 0x3E)
    "F5" = @(0x74, 0x3F); "F6" = @(0x75, 0x40); "F7" = @(0x76, 0x41); "F8" = @(0x77, 0x42)
}

function Write-Step([string]$message) {
    Write-Host ("{0}  {1}" -f ([DateTime]::Now.ToString("HH:mm:ss")), $message)
}

function Get-KeyVk([string]$name) {
    $key = $name.ToUpperInvariant()
    if (-not $script:Vk.ContainsKey($key)) {
        throw "unknown key '$name' - add it to the Vk table in this script"
    }
    return $script:Vk[$key]
}

function Invoke-Tap([string]$name) {
    $vk = Get-KeyVk $name
    [CloudpunkCoop.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [CloudpunkCoop.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 2, [UIntPtr]::Zero)
}

function Invoke-Hold([string]$name, [int]$milliseconds) {
    $vk = Get-KeyVk $name
    [CloudpunkCoop.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $milliseconds
    [CloudpunkCoop.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 2, [UIntPtr]::Zero)
}

function Save-Screenshot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $path = Join-Path $OutDir $name
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Step "screenshot $name ($($bounds.Width)x$($bounds.Height))"
}

# A background process is not allowed to take the foreground window, and without focus the
# game never receives the injected keys. This is the standard recipe that does work.
function Set-GameFocus([IntPtr]$handle) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ([CloudpunkCoop.Win]::GetForegroundWindow() -eq $handle) {
            return $true
        }

        [CloudpunkCoop.Win]::ShowWindow($handle, 9) | Out-Null
        [CloudpunkCoop.Win]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null

        $foreground = [CloudpunkCoop.Win]::GetForegroundWindow()
        $foregroundThread = [CloudpunkCoop.Win]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
        $myThread = [CloudpunkCoop.Win]::GetCurrentThreadId()

        [CloudpunkCoop.Win]::AttachThreadInput($myThread, $foregroundThread, $true) | Out-Null
        [CloudpunkCoop.Win]::BringWindowToTop($handle) | Out-Null
        [CloudpunkCoop.Win]::SetForegroundWindow($handle) | Out-Null
        [CloudpunkCoop.Win]::SetFocus($handle) | Out-Null
        [CloudpunkCoop.Win]::AttachThreadInput($myThread, $foregroundThread, $false) | Out-Null

        [CloudpunkCoop.Win]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    return ([CloudpunkCoop.Win]::GetForegroundWindow() -eq $handle)
}

function Invoke-Click([int]$x, [int]$y) {
    [CloudpunkCoop.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 350
    [CloudpunkCoop.Win]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [CloudpunkCoop.Win]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Get-Process Cloudpunk -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$log = Join-Path $GameDir "BepInEx\LogOutput.log"
if (Test-Path $log) {
    Remove-Item $log -Force
}

$process = Start-Process (Join-Path $GameDir "Cloudpunk.exe") -WorkingDirectory $GameDir -PassThru
Write-Step "launched pid=$($process.Id)"
Start-Sleep -Seconds $StartupSeconds

$process.Refresh()
$handle = $process.MainWindowHandle
if (-not (Set-GameFocus $handle)) {
    Write-Step "WARNING: could not take focus; injected keys will probably be ignored"
} else {
    Write-Step "focus acquired"
}

Write-Step "dismissing the press-any-key splash"
Invoke-Tap "SPACE"
Start-Sleep -Seconds $MenuSettleSeconds

# The Continue entry sits at a fixed fraction of the screen in the main menu.
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$continueX = [int]($screen.Width * 0.118)
$continueY = [int]($screen.Height * 0.767)
Write-Step "clicking the Continue entry at ($continueX,$continueY)"
Invoke-Click $continueX $continueY

Write-Step "waiting $WorldLoadSeconds s for the save to load"
Start-Sleep -Seconds $WorldLoadSeconds
Set-GameFocus $handle | Out-Null

foreach ($action in ($Actions -split ",")) {
    $trimmed = $action.Trim()
    if ($trimmed -eq "") { continue }

    $parts = $trimmed -split ":"
    switch ($parts[0]) {
        "tap"  { Write-Step "tap $($parts[1])";  Invoke-Tap $parts[1] }
        "hold" { Write-Step "hold $($parts[1]) for $($parts[2]) ms"; Invoke-Hold $parts[1] ([int]$parts[2]) }
        "shot" { Save-Screenshot $parts[1] }
        "wait" { Start-Sleep -Milliseconds ([int]$parts[1]) }
        default { Write-Step "unknown action '$trimmed'" }
    }
}

Write-Step "--- plugin log ---"
if (Test-Path $log) {
    Get-Content $log |
        Where-Object { $_ -match "car telemetry|car input|input override|seat |EnterVehicle|scripted demo|driving actions|Harmony" } |
        ForEach-Object { $_ -replace "^\s*\[Info\s*:Cloudpunk Coop\]\s*", "" }
}

if (-not $KeepRunning) {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Seconds 2
    Get-Process Cloudpunk -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Step "closed"
} else {
    Write-Step "left running for manual inspection (pid=$($process.Id))"
}
