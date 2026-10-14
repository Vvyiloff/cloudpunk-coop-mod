# Runs two copies of Cloudpunk on one machine: one host, one guest, connected over loopback.
#
# Two copies share a single BepInEx config, so the guest is started with
# CLOUDPUNK_COOP_ROLE=guest in its environment rather than a second config file.
#
# The save folder is backed up first and restored afterwards: two processes share one save and
# Easy Save 2 auto-saves, which is not something to gamble with.
#
# Each window is captured on its own with PrintWindow, because only one window can be in front
# and a desktop screenshot would only ever show that one.

param(
    [string]$GameDir = "C:\GOG Games\Cloudpunk",
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cloudpunk-coop-tests'),
    [int]$StartupSeconds = 14,
    [ValidateRange(30,600)][int]$WindowReadySeconds = 180,
    [int]$MenuSettleSeconds = 6,
    [int]$WorldLoadSeconds = 50,
    [int]$ConnectWaitSeconds = 8,
    [int]$HostDriveSeconds = 8,
    [switch]$AutopilotDrive,
    [switch]$TestDisconnect,
    [switch]$TestSeatExchange,
    [switch]$TestIndependentCars,
    [switch]$TestOwnedBoarding,
    [switch]$TestWalkingAnimation,
    [switch]$TestCarCollision,
    [switch]$TestParkingAuthority,
    [switch]$TestParkingGuestFirst,
    [switch]$TestCampaignState,
    [switch]$TestInventoryPersistence,
    [switch]$TestConsumables,
    [switch]$TestConsumableReconnect,
    [ValidateSet('use','resume','burst')][string]$ConsumablesMode='use',
    [ValidateSet('equip','resume')][string]$InventoryPersistenceMode='equip',
    [switch]$TestCampaignStory,
    [int]$TestCampaignScene = -1,
    [int]$TestIndependentRegion = -1,
    [switch]$TestRegionByLift,
    [ValidateSet('host','guest')][string]$TestRegionTraveller = 'guest',
    [ValidateRange(0,900)][int]$ConnectionMenuReviewSeconds = 0,
    [ValidateRange(0,300)][int]$HostMainMenuReviewSeconds = 0,
    [int]$Port = 27050
)

$ErrorActionPreference = "Stop"
if($TestCarCollision -and -not($TestOwnedBoarding -or $TestWalkingAnimation)) {
    throw 'TestCarCollision requires TestOwnedBoarding or TestWalkingAnimation; it must exercise actual parked cars.'
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
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
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hWnd, string text);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@ -Name Win -Namespace Duo

$script:Vk = @{
    "SPACE" = @(0x20, 0x39); "W" = @(0x57, 0x11); "A" = @(0x41, 0x1E); "S" = @(0x53, 0x1F)
    "D" = @(0x44, 0x20); "ENTER" = @(0x0D, 0x1C); "F6" = @(0x75, 0x40); "SHIFT" = @(0x10, 0x2A)
    "F8" = @(0x77, 0x42); "F9" = @(0x78, 0x43); "F11" = @(0x7A, 0x57)
    "E" = @(0x45, 0x12)
}

function Write-Step([string]$message) {
    Write-Host ("{0}  {1}" -f ([DateTime]::Now.ToString("HH:mm:ss")), $message)
}

function Invoke-Tap([string]$name) {
    $vk = $script:Vk[$name]
    [Duo.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [Duo.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 2, [UIntPtr]::Zero)
}

function Invoke-Hold([string]$name, [int]$milliseconds) {
    $vk = $script:Vk[$name]
    [Duo.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 0, [UIntPtr]::Zero)
    try { Start-Sleep -Milliseconds $milliseconds }
    finally { [Duo.Win]::keybd_event([byte]$vk[0], [byte]$vk[1], 2, [UIntPtr]::Zero) }
}

function Set-WindowFocus([IntPtr]$handle) {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        if ([Duo.Win]::GetForegroundWindow() -eq $handle) { return $true }
        [Duo.Win]::ShowWindow($handle, 9) | Out-Null
        [Duo.Win]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
        $foreground = [Duo.Win]::GetForegroundWindow()
        $foregroundThread = [Duo.Win]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
        $myThread = [Duo.Win]::GetCurrentThreadId()
        [Duo.Win]::AttachThreadInput($myThread, $foregroundThread, $true) | Out-Null
        [Duo.Win]::BringWindowToTop($handle) | Out-Null
        [Duo.Win]::SetForegroundWindow($handle) | Out-Null
        [Duo.Win]::SetFocus($handle) | Out-Null
        [Duo.Win]::AttachThreadInput($myThread, $foregroundThread, $false) | Out-Null
        [Duo.Win]::SetWindowPos($handle, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
        Start-Sleep -Milliseconds 400
    }
    return ([Duo.Win]::GetForegroundWindow() -eq $handle)
}

function Invoke-ClickInWindow([IntPtr]$handle, [double]$fx, [double]$fy) {
    $rect = New-Object Duo.Win+RECT
    [Duo.Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $x = [int]($rect.Left + ($rect.Right - $rect.Left) * $fx)
    $y = [int]($rect.Top + ($rect.Bottom - $rect.Top) * $fy)
    [Duo.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 350
    [Duo.Win]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [Duo.Win]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
    Write-Step "clicked at ($x,$y) inside the window"
}

function Save-WindowShot([IntPtr]$handle, [string]$name) {
    $rect = New-Object Duo.Win+RECT
    [Duo.Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { Write-Step "window has no size, skipping $name"; return }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [Duo.Win]::PrintWindow($handle, $hdc, 2) | Out-Null
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $path = Join-Path $OutDir $name
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Step "captured $name ($width x $height)"
}

function Get-ReadyWindow([System.Diagnostics.Process]$process, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { return [IntPtr]::Zero }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $class = New-Object System.Text.StringBuilder 256
            [Duo.Win]::GetClassName($process.MainWindowHandle,$class,$class.Capacity) | Out-Null
            # A cold IL2CPP installation shows the loader console before Unity.
            # Input there can select/pause the console and stall generation.
            if($class.ToString() -eq 'UnityWndClass'){ return $process.MainWindowHandle }
        }
        Start-Sleep -Milliseconds 500
    }
    return [IntPtr]::Zero
}

function Read-Status([string]$path) {
    for($attempt=0;$attempt -lt 30;$attempt++) {
        try {
            $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,
                [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            try { $reader=New-Object IO.StreamReader($stream); return $reader.ReadToEnd() }
            finally { $stream.Dispose() }
        } catch [IO.IOException] { Start-Sleep -Milliseconds 100 }
    }
    throw "Status unavailable after retries: $path"
}

function Get-ToWorld([System.Diagnostics.Process]$process, [string]$label) {
    $handle = Get-ReadyWindow $process $WindowReadySeconds
    if ($handle -eq [IntPtr]::Zero) { Write-Step "$label never produced a window"; return [IntPtr]::Zero }

    Start-Sleep -Seconds $StartupSeconds
    # Unity/preloader can replace the first startup window. Select the current
    # process window after startup before sending any input.
    $handle = Get-ReadyWindow $process $WindowReadySeconds
    if ($handle -eq [IntPtr]::Zero) { throw "$label has no window after startup" }
    Write-Step "$label current window=$handle title=$($process.MainWindowTitle) session=$($process.SessionId)"
    if (-not (Set-WindowFocus $handle)) { throw "Cannot focus $label" }
    Write-Step "${label}: dismissing the press-any-key splash"
    if (-not (Set-WindowFocus $handle)) { throw "Lost focus before $label splash input" }
    Invoke-Tap "SPACE"
    Start-Sleep -Seconds $MenuSettleSeconds
    if($label -eq 'host' -and $HostMainMenuReviewSeconds -gt 0) {
        Set-DuoPosition $handle 0 'Cloudpunk - Player 1 (HOST)'
        Save-WindowShot $handle 'startup-off-main-menu.png'
        Write-Step "Role Off main-menu native review window: $HostMainMenuReviewSeconds seconds"
        Start-Sleep -Seconds $HostMainMenuReviewSeconds
        Save-WindowShot $handle 'startup-menu-after-review.png'
    }
    Write-Step "${label}: clicking Continue"
    if (-not (Set-WindowFocus $handle)) { throw "Lost focus before $label Continue input" }
    Invoke-ClickInWindow $handle 0.118 0.767
    return $handle
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$windowWidth = [int]($workArea.Width / 2) - 12
$windowHeight = [Math]::Min(900, $workArea.Height - 100)

function Set-DuoPosition([IntPtr]$handle, [int]$index, [string]$title) {
    [Duo.Win]::SetWindowText($handle, $title) | Out-Null
    $left = $workArea.Left + $index * [int]($workArea.Width / 2)
    [Duo.Win]::ShowWindow($handle, 9) | Out-Null
    [Duo.Win]::SetWindowPos($handle, [IntPtr]::Zero, $left, $workArea.Top + 45,
        $windowWidth, $windowHeight, 0x14) | Out-Null
}

if (Get-Process Cloudpunk -ErrorAction SilentlyContinue) {
    throw "Close existing Cloudpunk sessions before running this harness."
}

# Back up the save folder: two processes share one save and the game auto-saves.
$saveDir = Join-Path $env:USERPROFILE "AppData\LocalLow\ION LANDS\Cloudpunk"
$saveBackup = Join-Path $OutDir ("save-backup-" + [DateTime]::Now.ToString("yyyyMMdd-HHmmss-fff"))
if (Test-Path $saveDir) {
    Copy-Item -LiteralPath $saveDir -Destination $saveBackup -Recurse -Force
    Write-Step "saved a copy of $saveDir to $saveBackup"
}

$saveHashes = @{}
Get-ChildItem -LiteralPath $saveDir -Filter *.sav -File | ForEach-Object {
    $saveHashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$saveInventory = @{}
Get-ChildItem -LiteralPath $saveBackup -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($saveBackup.Length + 1)
    $saveInventory[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$hostProcess = $null
$guestProcess = $null
$envNames = @("CLOUDPUNK_COOP_ROLE", "CLOUDPUNK_COOP_PORT", "CLOUDPUNK_COOP_ADDRESS", "CLOUDPUNK_COOP_DEMO", "CLOUDPUNK_COOP_DIAGNOSTICS", "CLOUDPUNK_COOP_TEST", "CLOUDPUNK_COOP_TEST_DIR", "CLOUDPUNK_COOP_TEST_PERSIST_INVENTORY")
$oldEnv = @{}
foreach ($name in $envNames) { $oldEnv[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }

try {

$configDir = Join-Path $GameDir "BepInEx\config"
foreach ($stale in @("coop-status-host.txt", "coop-status-guest.txt")) {
    $p = Join-Path $configDir $stale
    if (Test-Path $p) { Remove-Item $p -Force }
}

    $script:CampaignCommandId=0
    function Invoke-CampaignCommand([string]$role,[hashtable]$data) {
        $script:CampaignCommandId++
        $data.Id=$script:CampaignCommandId
        $path=Join-Path $OutDir "campaign-command-$role.json"
        $commandJson=$data | ConvertTo-Json -Depth 10
        [IO.File]::WriteAllText("$path.tmp",$commandJson,(New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath "$path.tmp" -Destination $path -Force
        $resultPath=Join-Path $OutDir "campaign-result-$role.json"
        $deadline=[DateTime]::Now.AddSeconds(15)
        do {
            try {
                if(Test-Path -LiteralPath $resultPath) {
                    $result=Read-Status $resultPath | ConvertFrom-Json
                    if($result.Id -eq $data.Id) {
                        if($result.Error){throw "Native campaign command failed: $($result.Error)"}
                        return $result
                    }
                }
            } catch [System.ArgumentException] { }
            Start-Sleep -Milliseconds 100
        } while([DateTime]::Now -lt $deadline)
        throw "Native campaign command timeout: $role $($data.Action)"
    }

# Host first: it must be listening before the guest connects.
Write-Step "launching HOST (fullscreen)"
$env:CLOUDPUNK_COOP_ROLE = if($HostMainMenuReviewSeconds -gt 0){'off'}else{'host'}
$env:CLOUDPUNK_COOP_PORT = "$Port"
$env:CLOUDPUNK_COOP_ADDRESS = "127.0.0.1"
$env:CLOUDPUNK_COOP_DEMO = "false"
$env:CLOUDPUNK_COOP_DIAGNOSTICS = "true"
$env:CLOUDPUNK_COOP_TEST = "true"
$env:CLOUDPUNK_COOP_TEST_DIR = $OutDir
$env:CLOUDPUNK_COOP_TEST_PERSIST_INVENTORY=if($TestInventoryPersistence -or $TestConsumables){'true'}else{'false'}
foreach($name in @('campaign-command-host.json','campaign-command-guest.json','campaign-result-host.json','campaign-result-guest.json')) {
    $stale=Join-Path $OutDir $name
    if(Test-Path -LiteralPath $stale){Remove-Item -LiteralPath $stale}
}
$hostProcess = Start-Process (Join-Path $GameDir "Cloudpunk.exe") -WorkingDirectory $GameDir `
    -ArgumentList "-screen-fullscreen", "0", "-screen-width", "$windowWidth", "-screen-height", "$windowHeight" -PassThru
$hostHandle = Get-ToWorld $hostProcess "host"
if ($hostHandle -eq [IntPtr]::Zero) { throw "Host has no window" }
Set-DuoPosition $hostHandle 0 "Cloudpunk - Player 1 (HOST)"
Write-Step "host window handle=$hostHandle, waiting $WorldLoadSeconds s for its save"
Start-Sleep -Seconds $WorldLoadSeconds
if($TestConsumables) {Invoke-CampaignCommand 'host' @{Action='prepare-consumables';Target='EnergyDrink'} | Out-Null}

Write-Step "launching GUEST (windowed, CLOUDPUNK_COOP_ROLE=guest)"
$env:CLOUDPUNK_COOP_ROLE = "guest"
$env:CLOUDPUNK_COOP_PORT = "$Port"
$guestProcess = Start-Process (Join-Path $GameDir "Cloudpunk.exe") `
    -WorkingDirectory $GameDir `
    -ArgumentList "-screen-fullscreen", "0", "-screen-width", "$windowWidth", "-screen-height", "$windowHeight" `
    -PassThru
Remove-Item Env:\CLOUDPUNK_COOP_ROLE -ErrorAction SilentlyContinue
Remove-Item Env:\CLOUDPUNK_COOP_PORT -ErrorAction SilentlyContinue

$guestHandle = Get-ToWorld $guestProcess "guest"
if ($guestHandle -eq [IntPtr]::Zero) { throw "Guest has no window" }
Set-DuoPosition $hostHandle 0 "Cloudpunk - Player 1 (HOST)"
Set-DuoPosition $guestHandle 1 "Cloudpunk - Player 2 (GUEST)"
Write-Step "guest window handle=$guestHandle, waiting $WorldLoadSeconds s for its save"
Start-Sleep -Seconds $WorldLoadSeconds

Write-Step "waiting $ConnectWaitSeconds s for the hand-shake"
Start-Sleep -Seconds $ConnectWaitSeconds

Write-Step "host drives for $HostDriveSeconds s so the guest receives moving coordinates"
Save-WindowShot $hostHandle "duo-before-host.png"
Save-WindowShot $guestHandle "duo-before-guest.png"
if (-not (Set-WindowFocus $hostHandle)) { throw "Cannot focus host for driving" }
if($TestCampaignState -or $TestInventoryPersistence -or $TestCampaignStory -or $TestIndependentRegion -ge 0) {
    $campaignDeadline=[DateTime]::Now.AddSeconds(30)
    do {
        $campaignHost=Read-Status (Join-Path $configDir 'coop-status-host.txt')
        $campaignGuest=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
        if($campaignHost.Contains(';campaign=shared campaign authoritative;') -and
            $campaignGuest -match ';campaignApplied=([0-9]+);' -and [long]$Matches[1] -gt 0){break}
        if($campaignHost -match 'campaign=[^;]*failed' -or $campaignGuest -match 'campaign=[^;]*(failed|Invalid)') {
            throw "Campaign state failed: host=$campaignHost guest=$campaignGuest"
        }
        Start-Sleep -Milliseconds 200
    } while([DateTime]::Now -lt $campaignDeadline)
    if($campaignGuest -notmatch ';campaignApplied=([0-9]+);' -or [long]$Matches[1] -le 0){throw 'Guest never applied shared campaign state'}
    foreach($role in @('host','guest')) {
        $handle=if($role -eq 'host'){$hostHandle}else{$guestHandle}
        if(-not(Set-WindowFocus $handle)){throw "Cannot focus $role for campaign inventory evidence"}
        Invoke-Tap 'F11'
        Start-Sleep -Milliseconds 500
        Copy-Item -LiteralPath (Join-Path $configDir "coop-campaign-$role.jsonl") -Destination (Join-Path $OutDir "campaign-initial-$role.jsonl")
    }
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot restore host focus after campaign probe'}
    Write-Step 'Guest applied an authoritative campaign snapshot; native inventory evidence captured (not dialogue acceptance)'
}
if (-not $TestOwnedBoarding -and -not $TestWalkingAnimation -and -not $TestParkingAuthority -and -not $TestParkingGuestFirst -and -not $TestCampaignStory -and $TestIndependentRegion -lt 0) { Invoke-Hold "S" 2000 }
if (-not $TestIndependentCars -and -not $TestOwnedBoarding -and -not $TestWalkingAnimation -and -not $TestParkingAuthority -and -not $TestParkingGuestFirst -and -not $TestCampaignStory -and $TestIndependentRegion -lt 0) { Invoke-Hold "SPACE" 3000 }
$driveEnd = [DateTime]::Now.AddSeconds($HostDriveSeconds)
$tracePath = Join-Path $OutDir "duo-status-trace.txt"
[System.IO.File]::WriteAllText($tracePath, "")
if ($TestParkingGuestFirst -or $TestCampaignStory -or ($TestIndependentRegion -ge 0 -and -not $TestRegionByLift)) {
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest for first parking'}
    Invoke-Hold 'E' 300
    Start-Sleep -Seconds 6
    $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
    if(-not $g.Contains(';p1Vehicle=1;p2Vehicle=0;car1Driver=1;car2Driver=0;')){throw "Guest did not park first: $g"}
    Save-WindowShot $guestHandle 'parking-guest-first.png'
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host for occupied guest bay'}
    Invoke-Hold 'E' 300
    Start-Sleep -Seconds 2
    $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
    if(-not $h.Contains(';parking=Parking space occupied;') -or -not $h.Contains(';parkingPromptHidden=True;') -or -not $h.Contains(';parkingUiActive=False;')) {
        throw "Host occupied-bay denial or native prompt suppression failed: $h"
    }
    Save-WindowShot $hostHandle 'parking-host-denied.png'
    Write-Step 'Guest-first parking: host denied occupied bay and native landing caption hidden'
} elseif ($TestParkingAuthority) {
    function Wait-ParkingOccupancy([string]$expected) {
        $deadline=[DateTime]::Now.AddSeconds(30)
        do {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($h.Contains($expected) -and $g.Contains($expected)){return}
            Start-Sleep -Milliseconds 100
        } while([DateTime]::Now -lt $deadline)
        throw "Parking occupancy mismatch: $expected host=$h guest=$g"
    }
    function Return-HostToBay {
        if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host for parking approach'}
        for($attempt=0;$attempt -lt 60;$attempt++) {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            if($h.Contains(';parkingVolumeAvailable=True;')){return}
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($g -notmatch ';peerCarPos=([-0-9.]+),([-0-9.]+),([-0-9.]+);'){throw 'Host pose missing during approach'}
            $z=[double]::Parse($Matches[3],[Globalization.CultureInfo]::InvariantCulture)
            $key=if($z -gt -29){'S'}else{'W'}
            Invoke-Hold $key 100
            Start-Sleep -Milliseconds 150
        }
        throw "Host failed to approach the native parking volume: $h"
    }
    foreach($role in @('host','guest')) {
        $id=if($role -eq 'host'){1}else{2}
        $text=Read-Status (Join-Path $configDir "coop-status-$role.txt")
        if($text.Contains(";p${id}Vehicle=0;")) {
            $handle=if($role -eq 'host'){$hostHandle}else{$guestHandle}
            if(-not(Set-WindowFocus $handle)){throw "Cannot focus $role to prepare parking test"}
            Invoke-Hold 'E' 300
            Start-Sleep -Seconds 3
        }
    }
    Wait-ParkingOccupancy ';p1Vehicle=1;p2Vehicle=2;car1Driver=1;car2Driver=2;'
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host for parking reservation'}
    Invoke-Hold 'E' 300
    Wait-ParkingOccupancy ';p1Vehicle=0;p2Vehicle=2;car1Driver=0;car2Driver=2;'
    Save-WindowShot $hostHandle 'parking-host-first.png'
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest for occupied bay'}
    Invoke-Hold 'E' 300
    Start-Sleep -Seconds 2
    Wait-ParkingOccupancy ';p1Vehicle=0;p2Vehicle=2;car1Driver=0;car2Driver=2;'
    $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
    if(-not $g.Contains(';parking=Parking space occupied;')){throw "Guest parking was not denied for occupied space: $g"}
    if(-not $g.Contains(';parkingPromptHidden=True;')){throw "Native guest parking prompt still available: $g"}
    Save-WindowShot $guestHandle 'parking-guest-denied.png'
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host to release bay'}
    Invoke-Hold 'E' 300
    Wait-ParkingOccupancy ';p1Vehicle=1;p2Vehicle=2;car1Driver=1;car2Driver=2;'
    Start-Sleep -Seconds 3
    # Fly clear of the approach before the second car descends. Otherwise the two
    # real collision hulls correctly keep the second hovering car above the first.
    Invoke-Hold 'S' 1500
    Start-Sleep -Seconds 1
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest to retry free bay'}
    Invoke-Hold 'SHIFT' 2000
    Start-Sleep -Seconds 1
    Invoke-Hold 'E' 300
    Wait-ParkingOccupancy ';p1Vehicle=1;p2Vehicle=0;car1Driver=1;car2Driver=0;'
    Save-WindowShot $guestHandle 'parking-guest-after-release.png'
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host for reverse denial'}
    Return-HostToBay
    Start-Sleep -Seconds 1
    Invoke-Hold 'E' 300
    Start-Sleep -Seconds 2
    $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
    if(-not $h.Contains(';parking=Parking space occupied;')){throw "Host did not respect guest parking: $h"}
    if(-not $h.Contains(';parkingPromptHidden=True;')){throw "Native host parking prompt still available: $h"}
    if(-not $h.Contains(';parkingUiActive=False;')){throw "Native host landing caption remained visible: $h"}
    Save-WindowShot $hostHandle 'parking-host-denied.png'
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest to release bay again'}
    Invoke-Hold 'E' 300
    Wait-ParkingOccupancy ';p1Vehicle=1;p2Vehicle=2;car1Driver=1;car2Driver=2;'
    Start-Sleep -Seconds 3
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus first competing request'}
    Invoke-Hold 'E' 300
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus second competing request'}
    Invoke-Hold 'E' 300
    Wait-ParkingOccupancy ';p1Vehicle=0;p2Vehicle=2;car1Driver=0;car2Driver=2;'
    Save-WindowShot $guestHandle 'parking-competing-guest-denied.png'
    Write-Step 'Occupied bay denied both directions, release allowed retry, competing requests had one winner'
} elseif ($TestOwnedBoarding -or $TestWalkingAnimation) {
    function Wait-Occupancy([int]$p1,[int]$p2,[int]$d1,[int]$d2) {
        $deadline=[DateTime]::Now.AddSeconds(120)
        do {
            $hostText=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $guestText=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            $expected=";p1Vehicle=$p1;p2Vehicle=$p2;car1Driver=$d1;car2Driver=$d2;"
            if($hostText.Contains($expected) -and $guestText.Contains($expected)){return}
            Start-Sleep -Milliseconds 100
        } while([DateTime]::Now -lt $deadline)
        throw "Occupancy mismatch: $expected; host=$hostText; guest=$guestText"
    }
    # A real user's save may already be on foot. Do not require a driving start.
    foreach($role in @('host','guest')) {
        $localId=if($role -eq 'host'){1}else{2}
        $text=Read-Status (Join-Path $configDir "coop-status-$role.txt")
        if($text.Contains(";p${localId}Vehicle=0;")){continue}
        $hostBays=Invoke-CampaignCommand 'host' @{Action='parking-bays'}
        $guestBays=Invoke-CampaignCommand 'guest' @{Action='parking-bays'}
        $common=@($hostBays.Candidates | Where-Object {
            $candidate=$_
            @($guestBays.Candidates | Where-Object {[Math]::Abs($_.X-$candidate.X)-lt 0.05 -and [Math]::Abs($_.Y-$candidate.Y)-lt 0.05 -and [Math]::Abs($_.Z-$candidate.Z)-lt 0.05}).Count -eq 1
        })
        if($common.Count -eq 0){throw 'No authored parking bay is free on both peers'}
        $actor=if($role -eq 'host'){$hostBays}else{$guestBays}
        $bay=$common | Sort-Object {[Math]::Pow($_.X-$actor.X,2)+[Math]::Pow($_.Y-$actor.Y,2)+[Math]::Pow($_.Z-$actor.Z,2)} | Select-Object -First 1
        Invoke-CampaignCommand $role @{Action='prepare-park';X=$bay.X;Y=$bay.Y;Z=$bay.Z} | Out-Null
        Start-Sleep -Seconds 3
        $handle=if($role -eq 'host'){$hostHandle}else{$guestHandle}
        if(-not(Set-WindowFocus $handle)){throw "Cannot focus $role for parking"}
        Invoke-Tap 'E'
        $deadline=[DateTime]::Now.AddSeconds(40)
        do {
            $parked=Read-Status (Join-Path $configDir "coop-status-$role.txt")
            if($parked.Contains(";p${localId}Vehicle=0;")){break}
            Start-Sleep -Milliseconds 200
        } while([DateTime]::Now -lt $deadline)
        if(-not $parked.Contains(";p${localId}Vehicle=0;")){throw "Native parking/exit did not complete on $role : $parked"}
    }
    Wait-Occupancy 0 0 0 0
    if($TestCarCollision) {
        Start-Sleep -Seconds 3
        foreach($role in @('host','guest')) {
            $text=Read-Status (Join-Path $configDir "coop-status-$role.txt")
            if($text -notmatch ';peerCarColliders=([0-9]+);' -or [int]$Matches[1] -lt 1){throw "No remote car collision shape: $text"}
            if($text -notmatch ';carPenetration=([-0-9.]+);' -or [double]::Parse($Matches[1],[Globalization.CultureInfo]::InvariantCulture) -gt 0.05){throw "Parked cars remain overlapped: $text"}
        }
        Save-WindowShot $hostHandle 'collision-parked-separated-host.png'
        Save-WindowShot $guestHandle 'collision-parked-separated-guest.png'
        Write-Step 'Both parked cars have collision shapes and no persistent penetration'
    }
    Save-WindowShot $hostHandle 'boarding-both-foot.png'
    if($TestWalkingAnimation) {
        foreach($role in @('host','guest')) {
            $handle=if($role -eq 'host'){$hostHandle}else{$guestHandle}
            $observer=if($role -eq 'host'){$guestHandle}else{$hostHandle}
            if(-not(Set-WindowFocus $handle)){throw "Cannot focus $role for walking"}
            [Duo.Win]::keybd_event(0x57,0x11,0,[UIntPtr]::Zero)
            try {
                Start-Sleep -Milliseconds 700
                Save-WindowShot $observer "walking-$role-observed-a.png"
                Start-Sleep -Milliseconds 700
                Save-WindowShot $observer "walking-$role-observed-b.png"
            } finally { [Duo.Win]::keybd_event(0x57,0x11,2,[UIntPtr]::Zero) }
            Start-Sleep -Seconds 2
        }
        Start-Sleep -Seconds 8
        foreach($role in @('host','guest')) {
            $text=Read-Status (Join-Path $configDir "coop-status-$role.txt")
            if($text -notmatch ';footAnimChanges=([0-9]+);' -or [int]$Matches[1] -lt 2){throw "Peer walking animation did not advance on $role : $text"}
            if($text -notmatch ';footAnimMissing=0;'){throw "Peer animation clip unavailable on $role : $text"}
            if($role -eq 'guest' -and ($text -notmatch ';npcAnimChanges=([0-9]+);' -or [int]$Matches[1] -lt 10)){throw "NPC animation did not advance: $text"}
            [IO.File]::AppendAllText($tracePath,$role+': '+$text+[Environment]::NewLine)
        }
        Write-Step 'Bidirectional peer walking frames and replicated NPC animation advanced'
    } else {
    Invoke-CampaignCommand 'host' @{Action='near-peer-car'} | Out-Null
    Start-Sleep -Seconds 2
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus host for guest-car boarding'}
    [Duo.Win]::keybd_event(0x10,0x2A,0,[UIntPtr]::Zero)
    try { Start-Sleep -Milliseconds 400; Invoke-Hold 'E' 400; Start-Sleep -Milliseconds 300 }
    finally { [Duo.Win]::keybd_event(0x10,0x2A,2,[UIntPtr]::Zero) }
    Wait-Occupancy 2 0 0 1
    Save-WindowShot $hostHandle 'boarding-host-first-car2.png'
    Save-WindowShot $guestHandle 'boarding-guest-stays-foot.png'
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest for passenger boarding'}
    Invoke-Tap 'E'
    Wait-Occupancy 2 2 0 1
    Save-WindowShot $guestHandle 'boarding-guest-passenger-car2.png'
    Write-Step 'First boarder is driver; native owner boards as passenger'
    function Car2-Position {
        $status=Read-Status (Join-Path $configDir 'coop-status-host.txt')
        if($status -notmatch ';peerCarPos=([-0-9.]+),([-0-9.]+),([-0-9.]+);'){throw 'Car2 pose missing'}
        return @([double]::Parse($Matches[1],[Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[3],[Globalization.CultureInfo]::InvariantCulture))
    }
    function Travel-Distance($a,$b) {
        return [Math]::Sqrt([Math]::Pow($b[0]-$a[0],2)+[Math]::Pow($b[1]-$a[1],2)+[Math]::Pow($b[2]-$a[2],2))
    }
    Start-Sleep -Seconds 3
    $before=Car2-Position
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus passenger for control denial'}
    Invoke-Hold 'S' 3000
    Start-Sleep -Seconds 2
    $deniedDistance=Travel-Distance $before (Car2-Position)
    Write-Step ("Passenger acceleration travel={0:N2} m" -f $deniedDistance)
    if($deniedDistance -gt 2){throw 'Passenger moved the shared car'}
    $before=Car2-Position
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus remote driver'}
    Invoke-Hold 'S' 3000
    Start-Sleep -Seconds 2
    $driverDistance=Travel-Distance $before (Car2-Position)
    Write-Step ("Remote driver travel={0:N2} m" -f $driverDistance)
    if($driverDistance -lt 1){throw 'Remote driver failed to move owner physics'}
    if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus passenger for swap'}
    Invoke-Hold 'F8' 300
    Start-Sleep -Seconds 1
    if(-not(Set-WindowFocus $hostHandle)){throw 'Cannot focus driver for swap confirmation'}
    Invoke-Hold 'F8' 300
    Wait-Occupancy 2 2 0 2
    Save-WindowShot $guestHandle 'boarding-owner-driver-after-swap.png'
    Write-Step 'Passenger control denied, remote driving and mutual F8 exchange passed'
    }
} elseif ($TestIndependentCars) {
    function Read-PeerPosition([string]$role) {
        $status = Read-Status (Join-Path $configDir "coop-status-$role.txt")
        if ($status -notmatch ";peerCarPos=([-0-9.]+),([-0-9.]+),([-0-9.]+);") { throw "Peer car pose missing: $role; $status" }
        return @([double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture))
    }
    $deadline = [DateTime]::Now.AddSeconds(120)
    do {
        $hostText = Read-Status (Join-Path $configDir "coop-status-host.txt")
        $guestText = Read-Status (Join-Path $configDir "coop-status-guest.txt")
        $ready = $hostText.Contains(';peerCar=True;') -and $guestText.Contains(';peerCar=True;') -and
            $hostText.Contains(';p1Vehicle=1;p2Vehicle=2;car1Driver=1;car2Driver=2;') -and
            $guestText.Contains(';p1Vehicle=1;p2Vehicle=2;car1Driver=1;car2Driver=2;')
        if (-not $ready) { Start-Sleep -Milliseconds 200 }
    } while (-not $ready -and [DateTime]::Now -lt $deadline)
    if (-not $ready) { throw "Independent occupancy did not converge: host=$hostText guest=$guestText" }
    foreach ($role in @('host','guest')) {
        $handle = if ($role -eq 'host') { $hostHandle } else { $guestHandle }
        $observer = if ($role -eq 'host') { 'guest' } else { 'host' }
        $before = Read-PeerPosition $observer
        if (-not (Set-WindowFocus $handle)) { throw "Cannot focus $role for independent driving" }
        Invoke-Hold 'S' 3000
        Start-Sleep -Seconds 2
        $after = Read-PeerPosition $observer
        $travel = [Math]::Sqrt([Math]::Pow($after[0]-$before[0],2)+[Math]::Pow($after[1]-$before[1],2)+[Math]::Pow($after[2]-$before[2],2))
        Write-Step ("Independent $role car moved {0:N2} m on $observer" -f $travel)
        if ($travel -lt 1) { throw "$role native car did not move on $observer" }
        Save-WindowShot $handle "independent-$role-driver.png"
        $observerHandle = if ($observer -eq 'host') { $hostHandle } else { $guestHandle }
        Save-WindowShot $observerHandle "independent-$observer-observer.png"
    }
    Write-Step 'Two owned vehicles, exclusive drivers and bidirectional movement passed'
} elseif ($TestSeatExchange) {
    function Wait-SeatState([int]$driver, [int]$requester) {
        $deadline = [DateTime]::Now.AddSeconds(6)
        do {
            $text = Read-Status (Join-Path $configDir "coop-status-guest.txt")
            if ($text -match ";driver=$driver;" -and $text -match ";seatRequester=$requester;") { return $text }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::Now -lt $deadline)
        throw "Seat state did not converge: driver=$driver requester=$requester; $text"
    }
    function Read-RemotePosition([string]$status) {
        if ($status -notmatch ";remotePos=([-0-9.]+),([-0-9.]+),([-0-9.]+);") { throw "Remote pose missing" }
        return @([double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture),
            [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture))
    }
    Invoke-Tap "F11"
    Invoke-Tap "F8"
    Wait-SeatState 1 1 | Out-Null
    Save-WindowShot $hostHandle "seat-request.png"
    if (-not (Set-WindowFocus $guestHandle)) { throw "Cannot focus guest for seat confirmation" }
    Invoke-Tap "F8"
    $initial = Wait-SeatState 2 0
    Save-WindowShot $guestHandle "seat-guest-driver.png"
    $before = Read-RemotePosition $initial
    Invoke-Tap "F11"
    Invoke-Hold "S" 3000
    Start-Sleep -Seconds 2
    $afterText = Wait-SeatState 2 0
    $after = Read-RemotePosition $afterText
    $distance = [Math]::Sqrt([Math]::Pow($after[0]-$before[0],2) + [Math]::Pow($after[1]-$before[1],2) + [Math]::Pow($after[2]-$before[2],2))
    Write-Step ("Guest drove real host car {0:N2} m; {1}" -f $distance,$afterText)
    if ($distance -lt 1) { throw "Guest driving did not move the authoritative car" }
    Invoke-Tap "F8"
    Wait-SeatState 2 2 | Out-Null
    if (-not (Set-WindowFocus $hostHandle)) { throw "Cannot focus host for reverse exchange" }
    Invoke-Tap "F8"
    Wait-SeatState 1 0 | Out-Null
    Save-WindowShot $hostHandle "seat-host-driver-again.png"
    Invoke-Tap "F8"
    Wait-SeatState 1 1 | Out-Null
    Start-Sleep -Seconds 11
    Wait-SeatState 1 0 | Out-Null
    Write-Step "Mutual exchange, guest driving, reverse exchange and timeout passed"
} elseif ($AutopilotDrive) {
    Invoke-Tap "F6"
    try {
        # Focus is already on the explicitly selected host, not an arbitrary Cloudpunk process.
        & (Join-Path $PSScriptRoot "autopilot-driver.ps1") -NoFocus -MaxSeconds $HostDriveSeconds
    } finally { Invoke-Tap "F6" }
} else {
[Duo.Win]::keybd_event(0x57, 0x11, 0, [UIntPtr]::Zero)
try {
    $nextShot = [DateTime]::Now.AddSeconds([Math]::Max(1, $HostDriveSeconds / 2))
    $nextSample = [DateTime]::Now
    while ([DateTime]::Now -lt $driveEnd) {
        if ([DateTime]::Now -ge $nextSample) {
            foreach ($role in @("host", "guest")) {
                $path = Join-Path $configDir "coop-status-$role.txt"
                if (Test-Path -LiteralPath $path) {
                    [System.IO.File]::AppendAllText($tracePath,
                        $role + ": " + (Read-Status $path) + [Environment]::NewLine)
                }
            }
            $nextSample = [DateTime]::Now.AddSeconds(1)
        }
        if ([DateTime]::Now -ge $nextShot) {
            Save-WindowShot $hostHandle "duo-mid-host.png"
            Save-WindowShot $guestHandle "duo-mid-guest.png"
            $nextShot = $driveEnd.AddSeconds(1)
        }
        Start-Sleep -Milliseconds 30
    }
} finally {
    [Duo.Win]::keybd_event(0x57, 0x11, 2, [UIntPtr]::Zero)
}
}
if($TestCampaignStory -or $TestInventoryPersistence -or $TestConsumables -or $TestIndependentRegion -ge 0) {
    if($TestConsumables) {
        Invoke-CampaignCommand 'host' @{Action='catalog'} | Out-Null
        $initialHost=Invoke-CampaignCommand 'host' @{Action='state';Target='EnergyDrink'}
        $initialGuest=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
        if($ConsumablesMode -eq 'resume') {
            if($initialHost.Item.Amount -ne 3 -or $initialGuest.Item.Amount -ne 1 -or
                $initialGuest.PersonalEffects.Speed -lt 300 -or $initialGuest.PersonalEffects.Speed -gt 356 -or
                $initialGuest.PersonalEffects.AudioPlays -ne 0 -or $initialHost.PersonalEffects.Speed -ne 0) {
                throw 'Persisted personal effect/quantity did not restore without sound or host contamination'
            }
            Save-WindowShot $hostHandle 'consumables-resumed-host.png'
            Save-WindowShot $guestHandle 'consumables-resumed-guest.png'
            Write-Step 'Persisted guest quantity/effect restored after process restart; host unchanged; no replay sound'
        } else {
        if($initialHost.Item.Amount -ne 3 -or $initialGuest.Item.Amount -ne 3){throw 'Prepared consumable baseline was not admitted'}
        function Wait-PersonalUse([uint32]$requestId,[int]$quantity,[bool]$accepted) {
            $deadline=[DateTime]::Now.AddSeconds(20)
            do {
                $state=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
                if($state.LastReply.RequestId -eq $requestId -and $state.LastReply.Accepted -eq $accepted -and $state.Item.Amount -eq $quantity){return $state}
                Start-Sleep -Milliseconds 200
            }while([DateTime]::Now -lt $deadline)
            throw "Consumable outcome/quantity did not converge: $($state | ConvertTo-Json -Depth 5 -Compress)"
        }
        if($ConsumablesMode -eq 'burst') {
            $request=Invoke-CampaignCommand 'guest' @{Action='request-burst';Kind=3;Target='EnergyDrink';Duplicate=$true}
        } else {
        $request=Invoke-CampaignCommand 'guest' @{Action='native-use';Target='EnergyDrink'}
        if($request.RequestId -eq 0){throw 'Native inventory UseItem did not route a guest request'}
        $first=Wait-PersonalUse $request.RequestId 2 $true
        if($first.PersonalEffects.Speed -lt 165 -or $first.PersonalEffects.Drink -lt 40){throw 'First native guest speed/cooldown effect was not projected'}
        $request=Invoke-CampaignCommand 'guest' @{Action='request';Kind=3;Target='EnergyDrink';Duplicate=$true}
        }
        $second=Wait-PersonalUse $request.RequestId 1 $true
        if($second.PersonalEffects.Speed -lt 330 -or $second.PersonalEffects.Drink -lt 80){throw 'Stacked personal effect or cooldown was not projected'}
        if($TestConsumableReconnect){Invoke-CampaignCommand 'guest' @{Action='remember-request'} | Out-Null}
        $request=Invoke-CampaignCommand 'guest' @{Action='request';Kind=3;Target='EnergyDrink';Duplicate=$true}
        $third=Wait-PersonalUse $request.RequestId 1 $false
        $finalHost=Invoke-CampaignCommand 'host' @{Action='state';Target='EnergyDrink'}
        if($finalHost.Item.Amount -ne 3){throw 'Guest use consumed the host item'}
        foreach($field in @('Food','Drink','Drug','Speed','Alcohol')) {
            if([Math]::Abs($finalHost.PersonalEffects.$field-$initialHost.PersonalEffects.$field) -gt 0.1){throw "Guest use changed host $field"}
        }
        if($third.PersonalEffects.AudioPlays -ne 2){throw "Consume sound replay/missing: $($third.PersonalEffects.AudioPlays)"}
        $pauseBefore=$third.PersonalEffects.LocalClock
        if(-not(Set-WindowFocus $guestHandle)){throw 'Cannot focus guest for native menu pause'}
        Invoke-Tap 'F9'
        Start-Sleep -Seconds 2
        Invoke-Tap 'F9'
        $afterPanel=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
        if($afterPanel.PersonalEffects.LocalClock -le $pauseBefore){throw 'Connection overlay incorrectly stopped the native effect clock'}
        Invoke-CampaignCommand 'guest' @{Action='menu-open'} | Out-Null
        Start-Sleep -Seconds 2
        $paused=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
        Start-Sleep -Seconds 3
        $pausedAgain=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
        if([Math]::Abs($pausedAgain.PersonalEffects.LocalClock-$paused.PersonalEffects.LocalClock) -gt 0.05 -or
            [Math]::Abs($pausedAgain.PersonalEffects.Speed-$paused.PersonalEffects.Speed) -gt 0.1){throw 'Native menu did not pause personal effects'}
        Save-WindowShot $guestHandle 'consumables-native-menu-paused.png'
        Invoke-CampaignCommand 'guest' @{Action='menu-close'} | Out-Null
        Start-Sleep -Seconds 3
        Save-WindowShot $hostHandle 'consumables-isolated-host.png'
        Save-WindowShot $guestHandle 'consumables-projected-guest.png'
        Write-Step 'Native UseItem and duplicated request consumed two guest items once each; third denied; host unchanged; two sounds; native menu pause'
        if($TestConsumableReconnect) {
            $beforeReconnect=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
            $hostBeforeReconnect=Invoke-CampaignCommand 'host' @{Action='state';Target='EnergyDrink'}
            Invoke-CampaignCommand 'guest' @{Action='drop-transport'} | Out-Null
            $deadline=[DateTime]::Now.AddSeconds(45)
            do {
                $rejoined=Invoke-CampaignCommand 'guest' @{Action='state';Target='EnergyDrink'}
                if($rejoined.PeerOnline -and $rejoined.Applied -gt $beforeReconnect.Applied -and
                    $rejoined.Status -eq 'shared campaign synchronized'){break}
                Start-Sleep -Milliseconds 200
            }while([DateTime]::Now -lt $deadline)
            if(-not $rejoined.PeerOnline -or $rejoined.Session -ne $beforeReconnect.Session -or
                $rejoined.Item.Amount -ne 1 -or $rejoined.PersonalEffects.Speed -le 0 -or
                $rejoined.PersonalEffects.AudioPlays -ne 2 -or $rejoined.PersonalEffects.Error){
                throw 'Transport rejoin did not retain personal quantity/effect/session without replay sound'
            }
            $replay=Invoke-CampaignCommand 'guest' @{Action='replay-request'}
            $replayed=Wait-PersonalUse $replay.RequestId 1 $true
            $hostRejoined=Invoke-CampaignCommand 'host' @{Action='state';Target='EnergyDrink'}
            if($replayed.PersonalEffects.AudioPlays -ne 2 -or
                $replayed.PersonalEffects.Speed -gt $rejoined.PersonalEffects.Speed+1 -or
                $hostRejoined.Session -ne $hostBeforeReconnect.Session -or $hostRejoined.Item.Amount -ne 3){
                throw 'Accepted request replay after reconnect repeated use or changed host identity/inventory'
            }
            Save-WindowShot $hostHandle 'consumables-reconnected-host.png'
            Save-WindowShot $guestHandle 'consumables-reconnected-guest.png'
            Write-Step 'Actual socket retirement/rejoin preserved campaign and personal effects; replay of accepted pre-disconnect request did not consume again or replay sound'
        }
        }
        $deadline=[DateTime]::Now.AddSeconds(30)
        do {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($h.Contains(';carPenetration=0.000;') -and $g.Contains(';carPenetration=0.000;')){break}
            Start-Sleep -Milliseconds 200
        }while([DateTime]::Now -lt $deadline)
        if(-not $h.Contains(';carPenetration=0.000;') -or -not $g.Contains(';carPenetration=0.000;')){throw 'Join placement failed to find a bay free on both clients'}
    }
    elseif($TestInventoryPersistence) {
        $before=Invoke-CampaignCommand 'host' @{Action='state'}
        if(-not $before.SaveIdentityReady -or -not $before.SaveIdentity){throw 'Host save identity is not bound'}
        if($InventoryPersistenceMode -eq 'equip') {
            Invoke-CampaignCommand 'guest' @{Action='request';Kind=4;Target='NeonTrail';Duplicate=$true} | Out-Null
        }
        $deadline=[DateTime]::Now.AddSeconds(15)
        do {
            $guestCapture=Invoke-CampaignCommand 'guest' @{Action='capture'}
            $rows=Get-Content -LiteralPath (Join-Path $OutDir "campaign-native-guest-$($guestCapture.Id).jsonl") | ForEach-Object {$_|ConvertFrom-Json}
            $gear=@($rows | Where-Object {$_.kind -eq 'item' -and $_.key -eq 'NeonTrail'})
            if($gear.Count -eq 1 -and $gear[0].isEquipped -and $gear[0].amount -eq 0){break}
            Start-Sleep -Milliseconds 400
        } while([DateTime]::Now -lt $deadline)
        if($gear.Count -ne 1 -or -not $gear[0].isEquipped -or $gear[0].amount -ne 0){throw 'Guest NeonTrail equipment did not persist/project'}
        $hostCapture=Invoke-CampaignCommand 'host' @{Action='capture'}
        $hostRows=Get-Content -LiteralPath (Join-Path $OutDir "campaign-native-host-$($hostCapture.Id).jsonl") | ForEach-Object {$_|ConvertFrom-Json}
        $hostGear=@($hostRows | Where-Object {$_.kind -eq 'item' -and $_.key -eq 'NeonTrail'})
        if($hostGear.Count -ne 1 -or $hostGear[0].isEquipped -or $hostGear[0].amount -ne 1){throw 'Guest equipment contaminated host personal inventory'}
        if(-not (Test-Path -LiteralPath $before.InventoryPath)){throw 'Inventory profile was not written'}
        $profile=Get-Content -LiteralPath $before.InventoryPath -Raw | ConvertFrom-Json
        $personal=@($profile.Guest.Items | Where-Object {$_.Id -eq 'NeonTrail'})
        if($personal.Count -ne 1 -or -not $personal[0].Equipped -or $personal[0].Amount -ne 0){throw 'Disk profile did not preserve guest equipment'}
        $saved=Invoke-CampaignCommand 'host' @{Action='save'}
        if($saved.CommittedSaves -le $before.CommittedSaves -or $saved.SaveIdentity -ne $before.SaveIdentity -or $saved.SaveIdentityError){throw 'Native save commit did not preserve campaign identity'}
        $aliases=Get-Content -LiteralPath (Join-Path $OutDir 'campaign-save-identities.json') -Raw | ConvertFrom-Json
        $nativeHash=(Get-FileHash -LiteralPath (Join-Path $saveDir 'Slot0.sav') -Algorithm SHA256).Hash
        $alias=@($aliases.Aliases | Where-Object {$_.RevisionHash -eq $nativeHash})
        if($alias.Count -ne 1 -or $alias[0].CampaignId -ne $before.SaveIdentity){throw 'Committed native revision was not registered to the loaded campaign'}
        Save-WindowShot $hostHandle 'persistent-inventory-host.png'
        Save-WindowShot $guestHandle 'persistent-inventory-guest.png'
        Write-Step "Personal inventory $InventoryPersistenceMode and native save identity passed; original saves will be restored"
    }
    elseif($TestIndependentRegion -ge 0) {
        $initial=Invoke-CampaignCommand 'host' @{Action='state'}
        $initialGuest=Invoke-CampaignCommand 'guest' @{Action='state'}
        if($initial.Scene -ne $initialGuest.Scene -or $initial.Scene -eq $TestIndependentRegion) {
            throw 'Independent region fixture requires both players initially together and a different target'
        }
        $regionAction=if($TestRegionByLift){'lift'}else{'scene'}
        $expectedHostRegion=if($TestRegionTraveller -eq 'host'){$TestIndependentRegion}else{$initial.Scene}
        $expectedGuestRegion=if($TestRegionTraveller -eq 'guest'){$TestIndependentRegion}else{$initial.Scene}
        Invoke-CampaignCommand $TestRegionTraveller @{Action=$regionAction;Amount=$TestIndependentRegion} | Out-Null
        $deadline=[DateTime]::Now.AddSeconds(90)
        $nextRegionReport=[DateTime]::Now.AddSeconds(8)
        do {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($h.Contains(";scene=$expectedHostRegion;") -and $g.Contains(";scene=$expectedGuestRegion;") -and
                $h.Contains(';sameRegion=False;') -and $g.Contains(';sameRegion=False;') -and
                $h.Contains(';regionReady=True;') -and $g.Contains(';regionReady=True;') -and
                $g.Contains(';campaign=shared campaign synchronized;')){break}
            if($g -match 'campaign=[^;]*(failed|Invalid)'){throw "Campaign failed after separate travel: $g"}
            if([DateTime]::Now -ge $nextRegionReport) {
                $loading=Invoke-CampaignCommand $TestRegionTraveller @{Action='loading'}
                [IO.File]::AppendAllText((Join-Path $OutDir 'region-loading.jsonl'),
                    ($loading | ConvertTo-Json -Depth 10 -Compress)+[Environment]::NewLine)
                Write-Step "$TestRegionTraveller region=$($loading.Scene) ready=$($loading.RegionReady) loader=$($loading.LoaderLoading)/$($loading.LoaderActive)/$($loading.LoaderFading) layer=$($loading.LayerScene) step=$($loading.SceneRoutineState)"
                if($loading.NativeException){throw "Native region transition exception: $($loading.NativeException)"}
                $nextRegionReport=[DateTime]::Now.AddSeconds(10)
            }
            Start-Sleep -Milliseconds 300
        } while([DateTime]::Now -lt $deadline)
        if(-not $g.Contains(";scene=$expectedGuestRegion;") -or -not $h.Contains(";scene=$expectedHostRegion;") -or
            -not $h.Contains(';sameRegion=False;') -or -not $g.Contains(';sameRegion=False;') -or
            -not $h.Contains(';regionReady=True;') -or -not $g.Contains(';regionReady=True;') -or
            -not $g.Contains(';campaign=shared campaign synchronized;')) {
            throw 'Independent native region transition did not complete'
        }
        Start-Sleep -Seconds 5
        Save-WindowShot $hostHandle 'regions-separate-host.png'
        Save-WindowShot $guestHandle 'regions-separate-guest.png'
        $separate=Invoke-CampaignCommand 'guest' @{Action='state'}
        if($separate.Applied -le $initialGuest.Applied -or $separate.Session -ne $initialGuest.Session) {
            throw 'Campaign did not stay synchronized across independent travel'
        }
        foreach($role in @('host','guest')) {
            [IO.File]::WriteAllText((Join-Path $OutDir "regions-separate-$role.txt"),
                (Read-Status (Join-Path $configDir "coop-status-$role.txt")))
        }
        Invoke-CampaignCommand $TestRegionTraveller @{Action=$regionAction;Amount=$initial.Scene} | Out-Null
        $deadline=[DateTime]::Now.AddSeconds(90)
        do {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($h.Contains(';sameRegion=True;') -and $g.Contains(';sameRegion=True;') -and
                $g.Contains(';npcs=replicating;') -and $g.Contains(';traffic=replicating;')){break}
            Start-Sleep -Milliseconds 300
        } while([DateTime]::Now -lt $deadline)
        if(-not $g.Contains(';sameRegion=True;') -or -not $g.Contains(';npcs=replicating;') -or
            -not $g.Contains(';traffic=replicating;')){throw 'Shared ambient replication did not resume after reunion'}
        Start-Sleep -Seconds 15
        foreach($role in @('host','guest')) {
            $status=Read-Status (Join-Path $configDir "coop-status-$role.txt")
            if(-not $status.Contains(';regionReady=True;') -or -not $status.Contains(';peerCar=True;') -or
                -not $status.Contains(';campaign=shared campaign synchronized;') -and $role -eq 'guest') {
                throw "Reunion did not restore gameplay/cars/campaign on $role"
            }
            [IO.File]::WriteAllText((Join-Path $OutDir "regions-reunion-$role.txt"),$status)
            $log=Join-Path $OutDir "native-$role.log"
            if(Test-Path -LiteralPath $log) {
                if(Select-String -LiteralPath $log -Pattern '\[Error|Exception in IL2CPP-to-Managed trampoline') {
                    throw "Native runtime errors occurred on $role; inspect the separate process log"
                }
            }
        }
        Save-WindowShot $hostHandle 'regions-reunion-host.png'
        Save-WindowShot $guestHandle 'regions-reunion-guest.png'
        Write-Step 'Independent regions and reunion passed; cross-region interactions/rewards are a separate gate'
    }
    if($TestCampaignStory) {
    if($TestCampaignScene -ge 0) {
        # Explicit fixture travel on BOTH peers; production campaign state never forces travel.
        Invoke-CampaignCommand 'host' @{Action='scene';Amount=$TestCampaignScene} | Out-Null
        Invoke-CampaignCommand 'guest' @{Action='scene';Amount=$TestCampaignScene} | Out-Null
        $sceneDeadline=[DateTime]::Now.AddSeconds(150)
        do {
            $h=Read-Status (Join-Path $configDir 'coop-status-host.txt')
            $g=Read-Status (Join-Path $configDir 'coop-status-guest.txt')
            if($h.Contains(";scene=$TestCampaignScene;") -and $g.Contains(";scene=$TestCampaignScene;") -and
                $g.Contains(';campaign=shared campaign synchronized;')){break}
            Start-Sleep -Milliseconds 500
        } while([DateTime]::Now -lt $sceneDeadline)
        if(-not $g.Contains(";scene=$TestCampaignScene;") -or -not $g.Contains(';campaign=shared campaign synchronized;')){throw "Explicit two-player fixture travel did not complete for scene $TestCampaignScene"}
        Write-Step "Both native clients loaded authoritative scene $TestCampaignScene"
        Start-Sleep -Seconds 3
    }
    $initialHost=Invoke-CampaignCommand 'host' @{Action='candidates'}
    $candidates=@($initialHost.Candidates | Sort-Object @{Expression={if($_.Name -eq 'ROTRIV'){0}elseif($_.Money -gt 0 -and $_.Quests.Count -gt 0){1}else{2}}})
    if($candidates.Count -eq 0){throw 'No naturally playable named native conversation in this loaded area'}
    $candidate=$candidates[0]
    $initialGuest=Invoke-CampaignCommand 'guest' @{Action='state'}
    Write-Step "Native story fixture: $($candidate.Name) $($candidate.Dialog), reward=$($candidate.Money), quests=$($candidate.Quests -join ',')"
    Invoke-CampaignCommand 'guest' @{Action='near';Target=$candidate.Guid;X=$candidate.X+1.5;Y=$candidate.Y;Z=$candidate.Z} | Out-Null
    Start-Sleep -Seconds 2
    Invoke-CampaignCommand 'guest' @{Action='request';Kind=1;Target=$candidate.Guid;Duplicate=$true} | Out-Null
    $storyDeadline=[DateTime]::Now.AddMinutes(5)
    $sawConversation=$false;$firstShot=$false;$lastLine=-1
    do {
        $state=Invoke-CampaignCommand 'guest' @{Action='state'}
        if($state.Conversation.Active) {
            $sawConversation=$true
            if(-not $firstShot -and $state.Conversation.Text.Length -gt 10) {
                Save-WindowShot $hostHandle 'campaign-dialog-host.png'
                Save-WindowShot $guestHandle 'campaign-dialog-guest.png'
                $firstShot=$true
            }
            if($state.Conversation.CanContinue -and $state.Conversation.LineIndex -ne $lastLine) {
                $lastLine=$state.Conversation.LineIndex
                Write-Step "Shared native dialogue line $lastLine ready; sending one duplicated continue request"
                Invoke-CampaignCommand 'guest' @{Action='continue';Duplicate=$true} | Out-Null
            }
        } elseif($sawConversation) {break}
        Start-Sleep -Milliseconds 500
    } while([DateTime]::Now -lt $storyDeadline)
    if(-not $sawConversation -or $state.Conversation.Active){throw "Shared native conversation did not complete: $($state.Status)"}
    Start-Sleep -Seconds 3
    $finalHost=Invoke-CampaignCommand 'host' @{Action='capture'}
    $finalGuest=Invoke-CampaignCommand 'guest' @{Action='capture'}
    if($finalHost.Money-$initialHost.Money -ne $candidate.Money -or $finalGuest.Money-$initialGuest.Money -ne $candidate.Money){throw 'Native conversation reward delta did not match on both personal wallets'}
    foreach($role in @('host','guest')) {
        $captureId=if($role -eq 'host'){$finalHost.Id}else{$finalGuest.Id}
        $rows=Get-Content -LiteralPath (Join-Path $OutDir "campaign-native-$role-$captureId.jsonl") | ForEach-Object {$_|ConvertFrom-Json}
        $dialog=@($rows|Where-Object { $_.kind -eq 'dialog' -and $_.id -eq $candidate.Dialog })
        if($dialog.Count -ne 1 -or -not $dialog[0].completed){throw "Native dialog completion missing on $role"}
        foreach($quest in $candidate.Quests) {
            $q=@($rows|Where-Object { $_.kind -eq 'quest' -and $_.id -eq $quest })
            if($q.Count -ne 1 -or -not $q[0].enabled){throw "Native quest $quest missing on $role"}
        }
    }
    Save-WindowShot $hostHandle 'campaign-completed-host.png'
    Save-WindowShot $guestHandle 'campaign-completed-guest.png'
    Write-Step "Native shared conversation/reward/objectives passed; expected reward=$($candidate.Money). Duplicate packets sent for interaction and each continue."
    }
}
Start-Sleep -Seconds 1

if($ConnectionMenuReviewSeconds -gt 0) {
    if(-not (Set-WindowFocus $hostHandle)){throw 'Could not focus host for connection menu review'}
    Invoke-Tap 'F9'
    Start-Sleep -Seconds 1
    Save-WindowShot $hostHandle 'connection-menu-host.png'
    if(-not (Set-WindowFocus $guestHandle)){throw 'Could not focus guest for connection menu review'}
    Invoke-Tap 'F9'
    Start-Sleep -Seconds 1
    Save-WindowShot $guestHandle 'connection-menu-guest.png'
    Write-Step "Connection menu native review window: $ConnectionMenuReviewSeconds seconds"
    Start-Sleep -Seconds $ConnectionMenuReviewSeconds
}

Save-WindowShot $hostHandle "duo-01-host.png"
Save-WindowShot $guestHandle "duo-02-guest.png"

Write-Step "--- per-process status files ---"
foreach ($role in @("host", "guest")) {
    $p = Join-Path $configDir "coop-status-$role.txt"
    if (Test-Path $p) {
        Write-Host ("{0}: {1}" -f $role, (Read-Status $p).Trim())
    } else {
        Write-Host ("{0}: <no status file - the plugin did not reach a session>" -f $role)
    }
}

Write-Step "--- process state ---"
Write-Host ("host exited={0}  guest exited={1}" -f $hostProcess.HasExited, $guestProcess.HasExited)

if ($TestDisconnect) {
    Stop-Process -Id $hostProcess.Id -Force
    Start-Sleep -Seconds 4
    $guestStatus = Join-Path $configDir "coop-status-guest.txt"
    Write-Host ("disconnected: " + (Read-Status $guestStatus))
    Save-WindowShot $guestHandle "duo-disconnected-guest.png"
}

} finally {

foreach ($key in @("W", "S", "A", "D", "SPACE", "SHIFT")) {
    $keyCodes = $script:Vk[$key]
    [Duo.Win]::keybd_event([byte]$keyCodes[0], [byte]$keyCodes[1], 2, [UIntPtr]::Zero)
}
foreach ($p in @($hostProcess, $guestProcess)) {
    if ($null -ne $p) {
        $p.Refresh()
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        $p.WaitForExit(10000) | Out-Null
    }
}
Start-Sleep -Seconds 3
foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $oldEnv[$name], "Process") }

if (Test-Path $saveBackup) {
    # Keep run-created files in a separate quarantine instead of leaving new saves behind.
    $quarantine = $saveBackup + "-run-created"
    $resolvedSaveRoot = [System.IO.Path]::GetFullPath($saveDir).TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $saveDir -Recurse -File | ForEach-Object {
        $absolute = [System.IO.Path]::GetFullPath($_.FullName)
        if (-not $absolute.StartsWith($resolvedSaveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Restoration target escaped the save directory"
        }
        $relative = $absolute.Substring($resolvedSaveRoot.Length)
        if (-not $saveInventory.ContainsKey($relative)) {
            $destination = Join-Path $quarantine $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Move-Item -LiteralPath $absolute -Destination $destination
        }
    }
    Get-ChildItem -LiteralPath $saveBackup -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $saveDir -Recurse -Force
    }
    foreach ($name in $saveInventory.Keys) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $saveDir $name) -Algorithm SHA256).Hash
        if ($actual -ne $saveInventory[$name]) { throw "Save restoration mismatch: $name" }
    }
    Write-Step "restored the save folder from the backup"
    Write-Step "SHA256 matches for all $($saveInventory.Count) original files ($($saveHashes.Count) saves)"
}
}

Write-Step "done"
