# Actuator for the in-game autopilot.
#
# The plugin owns the policy: it has the car's transform, the raycast probes and the
# waypoint. It cannot, however, make the car move. A Harmony prefix on InControl's value
# getter does replace the returned number, but the vehicle never consumes it - with the
# override reporting full lift for 35 seconds the car did not rise at all, while a real
# Space key lifted it 17 m in two. Synthetic keystrokes with scan codes do drive the car.
#
# So this script closes the loop from the outside: it reads the decision the plugin
# publishes and holds the matching real keys.
#
#   .\autopilot-driver.ps1
#
# It waits for the plugin to report enabled=1, then actuates. It releases every key and
# exits when the plugin disengages, when the command file goes stale, or after -MaxSeconds.

param(
    [string]$CommandFile = "C:\GOG Games\Cloudpunk\BepInEx\config\autopilot-command.txt",
    [int]$PollMs = 30,
    [int]$MaxSeconds = 300,
    [int]$StaleSeconds = 3,
    [switch]$NoFocus
)

$ErrorActionPreference = "Stop"

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
[DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
'@ -Name Win -Namespace CoopDriver

# vk + scan code. The scan code is not optional: without it the game ignores the key.
$Keys = @{
    "W"     = @(0x57, 0x11)
    "A"     = @(0x41, 0x1E)
    "D"     = @(0x44, 0x20)
    "S"     = @(0x53, 0x1F)
    "SPACE" = @(0x20, 0x39)
    "SHIFT" = @(0x10, 0x2A)
}

$held = @{}

function Set-Key([string]$name, [bool]$down) {
    if ($down -eq $held.ContainsKey($name)) {
        return
    }

    $code = $Keys[$name]
    if ($down) {
        [CoopDriver.Win]::keybd_event([byte]$code[0], [byte]$code[1], 0, [UIntPtr]::Zero)
        $held[$name] = $true
    } else {
        [CoopDriver.Win]::keybd_event([byte]$code[0], [byte]$code[1], 2, [UIntPtr]::Zero)
        $held.Remove($name)
    }
}

function Clear-Keys {
    foreach ($name in @($held.Keys)) {
        Set-Key $name $false
    }
}

function Focus-Game {
    $proc = Get-Process Cloudpunk -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $proc) { return $false }
    $handle = $proc.MainWindowHandle
    if ([CoopDriver.Win]::GetForegroundWindow() -eq $handle) { return $true }

    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        [CoopDriver.Win]::ShowWindow($handle, 9) | Out-Null
        [CoopDriver.Win]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
        $foreground = [CoopDriver.Win]::GetForegroundWindow()
        $foregroundThread = [CoopDriver.Win]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
        $myThread = [CoopDriver.Win]::GetCurrentThreadId()
        [CoopDriver.Win]::AttachThreadInput($myThread, $foregroundThread, $true) | Out-Null
        [CoopDriver.Win]::BringWindowToTop($handle) | Out-Null
        [CoopDriver.Win]::SetForegroundWindow($handle) | Out-Null
        [CoopDriver.Win]::SetFocus($handle) | Out-Null
        [CoopDriver.Win]::AttachThreadInput($myThread, $foregroundThread, $false) | Out-Null
        [CoopDriver.Win]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
        if ([CoopDriver.Win]::GetForegroundWindow() -eq $handle) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Read-Decision([string]$path) {
    try {
        $text = [System.IO.File]::ReadAllText($path)
    } catch {
        return $null
    }

    $result = @{}
    foreach ($pair in ($text -split ";")) {
        $parts = $pair -split "=", 2
        if ($parts.Count -eq 2) { $result[$parts[0].Trim()] = $parts[1].Trim() }
    }
    return $result
}

function Get-Number($decision, [string]$key) {
    if (-not $decision.ContainsKey($key)) { return 0.0 }
    $value = 0.0
    if ([double]::TryParse($decision[$key], [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }
    return 0.0
}

Write-Host "autopilot driver: waiting for the plugin to publish $CommandFile"
$startedAt = Get-Date
$lastSeen = $null
$lastLog = Get-Date

while ($true) {
    $elapsed = ((Get-Date) - $startedAt).TotalSeconds
    if ($elapsed -gt $MaxSeconds) {
        Write-Host "autopilot driver: reached -MaxSeconds, stopping"
        break
    }

    $decision = Read-Decision $CommandFile

    if ($null -eq $decision -or -not $decision.ContainsKey("enabled")) {
        Start-Sleep -Milliseconds 200
        continue
    }

    $stamp = (Get-Item $CommandFile -ErrorAction SilentlyContinue).LastWriteTime
    if ($null -ne $stamp) {
        if ($null -ne $lastSeen -and ($stamp -eq $lastSeen)) {
            $age = ((Get-Date) - $stamp).TotalSeconds
            if ($age -gt $StaleSeconds) {
                Write-Host "autopilot driver: command file stale for $([int]$age)s, stopping"
                break
            }
        } else {
            $lastSeen = $stamp
        }
    }

    if ($decision["enabled"] -ne "1") {
        Clear-Keys
        Start-Sleep -Milliseconds 150
        continue
    }

    if (-not $NoFocus -and (((Get-Date) - $lastLog).TotalSeconds -gt 5)) {
        Focus-Game | Out-Null
    }

    $throttle = Get-Number $decision "throttle"
    $brake    = Get-Number $decision "brake"
    $steer    = Get-Number $decision "steer"
    $ascend   = Get-Number $decision "ascend"
    $sink     = Get-Number $decision "sink"

    Set-Key "W"     ($throttle -ge 0.2)
    Set-Key "S"     ($brake -ge 0.2)
    Set-Key "A"     ($steer -le -0.2)
    Set-Key "D"     ($steer -ge 0.2)
    Set-Key "SPACE" ($ascend -ge 0.2)
    Set-Key "SHIFT" ($sink -ge 0.2)

    if (((Get-Date) - $lastLog).TotalSeconds -gt 1) {
        $lastLog = Get-Date
        $state = ($held.Keys | Sort-Object) -join "+"
        if ($state -eq "") { $state = "none" }
        Write-Host ("{0}  keys={1}  throttle={2:0.00} brake={3:0.00} steer={4:0.00} ascend={5:0.00} wp={6}  {7}" -f `
            (Get-Date).ToString("HH:mm:ss"), $state, $throttle, $brake, $steer, $ascend, $decision["wp"], $decision["decision"])
    }

    Start-Sleep -Milliseconds $PollMs
}

Clear-Keys
Write-Host "autopilot driver: stopped, all keys released"
