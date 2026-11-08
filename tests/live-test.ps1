<#
  Live end-to-end test of Mozaic against real windows, in an isolated instance.
  Only windows titled "MZTEST*" (spawned by this script) are managed; hotkeys and tray are off,
  so your other windows are never touched. Test windows will appear on screen for ~30 seconds.

  Usage:  powershell -ExecutionPolicy Bypass -File tests\live-test.ps1 [-Configuration Debug]
#>
param([string]$Configuration = 'Debug', [switch]$Published, [switch]$KeepOpen)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$exe = Join-Path $root "src\Mozaic\bin\$Configuration\net10.0-windows\Mozaic.exe"
$msg = Join-Path $root "src\MozaicMsg\bin\$Configuration\net10.0-windows\mozaic-msg.exe"
if ($Published) {
    # Test the NativeAOT build produced by build.ps1 / dotnet publish
    $exe = Join-Path $root 'publish\Mozaic.exe'
    $msg = Join-Path $root 'publish\mozaic-msg.exe'
}
if (-not (Test-Path $exe) -or -not (Test-Path $msg)) { throw "Build first: dotnet build -c $Configuration" }

Add-Type -Namespace MzTest -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@

$script:fail = 0; $script:pass = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}
function Msg([string]$type, [string]$payload = '') {
    $out = if ($type -eq 'command') { & $msg --instance test -r $payload } else { & $msg --instance test -r -t $type $payload }
    if ($out) { return ($out | Out-String | ConvertFrom-Json) }
}
function Cmd([string]$c) { $r = Msg command $c; Start-Sleep -Milliseconds 350; if ($r -and -not $r[0].success) { Write-Host "    ($c -> $($r[0].error))" -ForegroundColor Yellow }; return $r }
function Wins {
    $all = @(Msg get_windows)
    # Safety tripwire: this test must never manage anything but its own windows.
    $foreign = @($all | Where-Object { $_.title -notlike 'MZTEST*' })
    if ($foreign.Count -gt 0) {
        & $msg --instance test exit 2>$null | Out-Null
        throw "ABORT: a non-test window was managed: $(($foreign | ForEach-Object title) -join ', ')"
    }
    $all | Sort-Object { $_.rect.x }, { $_.rect.y }
}
function Win([string]$title) { Wins | Where-Object title -eq $title }
function Near($a, $b, [int]$tol = 3) {
    [math]::Abs($a.x - $b.x) -le $tol -and [math]::Abs($a.y - $b.y) -le $tol -and
    [math]::Abs($a.width - $b.width) -le $tol -and [math]::Abs($a.height - $b.height) -le $tol
}
function Layout { $t = Msg get_tree; $ws = $t.nodes | ForEach-Object { $_.nodes } | Where-Object { $_.visible -and ($_.nodes.Count -gt 0) } | Select-Object -First 1; Describe $ws }
function Describe($n) {
    if ($n.type -eq 'window') { return $n.name -replace '^MZTEST ', '' }
    $l = switch ($n.layout) { 'splith' { 'H' } 'splitv' { 'V' } 'tabbed' { 'T' } default { 'S' } }
    return $l + '[' + (($n.nodes | ForEach-Object { Describe $_ }) -join ' ') + ']'
}

$tmp = Join-Path $env:TEMP "mozaic-live-test"
New-Item -ItemType Directory -Force $tmp | Out-Null
$cfg = Join-Path $tmp 'config'
@'
set $mod Alt
gaps inner 10
gaps outer 0
remember_layout yes
bar {
    position top
    status_blocks "cpu memory datetime"
}
'@ | Set-Content -Encoding UTF8 $cfg
Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'Mozaic\instances\test') -ErrorAction SilentlyContinue

$testWindows = @()
$wm = $null
try {
    Write-Host "Starting isolated Mozaic instance..."
    $wm = Start-Process $exe -ArgumentList '--instance', 'test', '--config', "`"$cfg`"", '--manage-only', '^MZTEST', '--no-hotkeys', '--no-tray', '--debug' -PassThru
    Start-Sleep -Seconds 2
    $v = Msg get_version
    Check 'IPC answers get_version' ($null -ne $v -and $v.human_readable -like 'Mozaic*') "$v"

    foreach ($n in 'A', 'B', 'C') {
        $testWindows += Start-Process $exe -ArgumentList '--test-window', "`"MZTEST $n`"" -PassThru
        Start-Sleep -Milliseconds 900
    }
    Start-Sleep -Seconds 1

    Write-Host "Tiling:"
    $w = Wins
    Check 'three windows managed' ($w.Count -eq 3) "got $($w.Count)"
    Check 'opened left to right' ((($w | ForEach-Object title) -join ',') -eq 'MZTEST A,MZTEST B,MZTEST C') (($w | ForEach-Object title) -join ',')
    $ok = $true; foreach ($x in $w) { if (-not (Near $x.rect $x.actual_rect)) { $ok = $false; Write-Host "    $($x.title): want $($x.rect | ConvertTo-Json -Compress) got $($x.actual_rect | ConvertTo-Json -Compress)" } }
    Check 'real window frames match the layout' $ok
    Check 'gap between windows is 10px' ([math]::Abs(($w[1].rect.x - ($w[0].rect.x + $w[0].rect.width)) - 10) -le 1)
    $out = (Msg get_outputs)[0]
    Check 'bar reserves space at the top' ($out.layout_area.y -gt $out.work_area.y) "layout_area.y=$($out.layout_area.y) work_area.y=$($out.work_area.y)"
    Check 'last opened window is focused' ((Win 'MZTEST C').focused)
    Check 'OS foreground matches focus' ([MzTest.Win]::GetForegroundWindow() -eq [IntPtr](Win 'MZTEST C').window)

    Write-Host "i3 tree operations:"
    Cmd 'focus left' | Out-Null
    Check 'focus left' ((Win 'MZTEST B').focused)
    Check 'OS focus follows' ([MzTest.Win]::GetForegroundWindow() -eq [IntPtr](Win 'MZTEST B').window)
    Cmd 'split v' | Out-Null
    $testWindows += Start-Process $exe -ArgumentList '--test-window', '"MZTEST D"' -PassThru
    Start-Sleep -Milliseconds 1500
    Check 'split v + open nests the new window' ((Layout) -eq 'H[A V[B D] C]') (Layout)
    $b = Win 'MZTEST B'; $d = Win 'MZTEST D'
    Check 'nested windows stacked vertically' ($d.rect.y -gt $b.rect.y -and $d.rect.x -eq $b.rect.x -and (Near $d.rect $d.actual_rect))
    Cmd 'move right' | Out-Null
    Check 'move right leaves the container' ((Layout) -eq 'H[A V[B] D C]' -or (Layout) -eq 'H[A B D C]') (Layout)
    Cmd 'move up' | Out-Null
    Check 'move up forces vertical workspace' ((Layout).StartsWith('V[D H[')) (Layout)
    Cmd 'layout tabbed' | Out-Null
    Start-Sleep -Milliseconds 300
    $w = Wins
    $visible = @($w | Where-Object { [MzTest.Win]::IsWindowVisible([IntPtr]$_.window) })
    Check 'tabbed: only one window visible' ($visible.Count -eq 1) "visible: $(($visible | ForEach-Object title) -join ',')"
    Cmd 'layout toggle split' | Out-Null
    Start-Sleep -Milliseconds 300
    $visible = @(Wins | Where-Object { [MzTest.Win]::IsWindowVisible([IntPtr]$_.window) })
    Check 'toggle split: all windows visible again' ($visible.Count -eq 4) "$($visible.Count) visible"

    Write-Host "Workspaces:"
    Cmd 'workspace 2' | Out-Null
    $visible = @(Wins | Where-Object { [MzTest.Win]::IsWindowVisible([IntPtr]$_.window) })
    Check 'workspace 2 hides workspace 1 windows' ($visible.Count -eq 0) "$($visible.Count) still visible"
    Check 'workspace 2 is focused' ((@(Msg get_workspaces) | Where-Object focused).name -eq '2')
    Cmd 'workspace 1' | Out-Null
    $visible = @(Wins | Where-Object { [MzTest.Win]::IsWindowVisible([IntPtr]$_.window) })
    Check 'workspace 1 shows them again' ($visible.Count -eq 4) "$($visible.Count) visible"
    Check 'empty workspace 2 was destroyed' ($null -eq (@(Msg get_workspaces) | Where-Object name -eq '2'))
    Cmd "[title='MZTEST A'] move container to workspace 3" | Out-Null
    $a = Win 'MZTEST A'
    Check 'criteria + move to workspace' ($a.workspace -eq '3' -and -not [MzTest.Win]::IsWindowVisible([IntPtr]$a.window))
    Cmd "[title='MZTEST A'] focus" | Out-Null
    Check 'focus by criteria switches workspace' ((Win 'MZTEST A').focused -and [MzTest.Win]::IsWindowVisible([IntPtr]$a.window))
    Cmd 'move container to workspace 1; workspace 1' | Out-Null

    Write-Host "Floating, fullscreen, resize, scratchpad:"
    Cmd "[title='MZTEST A'] focus" | Out-Null
    Cmd 'floating toggle' | Out-Null
    $a = Win 'MZTEST A'
    Check 'floating toggle' ($a.floating -and (Near $a.rect $a.actual_rect 4)) "$($a.rect | ConvertTo-Json -Compress) vs $($a.actual_rect | ConvertTo-Json -Compress)"
    Cmd 'move position center' | Out-Null
    Cmd 'floating toggle' | Out-Null
    Check 'back to tiling' (-not (Win 'MZTEST A').floating)
    Cmd 'fullscreen toggle' | Out-Null
    $a = Win 'MZTEST A'
    Check 'fullscreen covers the monitor' ([math]::Abs($a.actual_rect.width - $out.rect.width) -le 2 -and [math]::Abs($a.actual_rect.height - $out.rect.height) -le 2) "$($a.actual_rect | ConvertTo-Json -Compress)"
    Cmd 'fullscreen toggle' | Out-Null
    # A may span a whole row or column, so grow in both directions and compare the area.
    $r = (Win 'MZTEST A').rect; $before = $r.width * $r.height
    Cmd 'resize grow width 10 ppt' | Out-Null
    Cmd 'resize grow height 10 ppt' | Out-Null
    $r = (Win 'MZTEST A').rect; $after = $r.width * $r.height
    Check 'resize grow' ($after -gt $before) "$before -> $after"
    $a = Win 'MZTEST A'
    Check 'resized frame matches layout' (Near $a.rect $a.actual_rect)
    Cmd 'balance' | Out-Null
    Cmd 'move scratchpad' | Out-Null
    $a = Win 'MZTEST A'
    Check 'move scratchpad hides it' ($a.scratchpad -and -not [MzTest.Win]::IsWindowVisible([IntPtr]$a.window))
    Cmd 'scratchpad show' | Out-Null
    $a = Win 'MZTEST A'
    Check 'scratchpad show' ($a.floating -and $a.focused -and [MzTest.Win]::IsWindowVisible([IntPtr]$a.window))
    Cmd 'floating disable' | Out-Null
    Cmd 'mark foo' | Out-Null
    Check 'mark' (@(Msg get_marks) -contains 'foo')
    $r = Msg command 'frobnicate'
    Check 'unknown command reports an error' (-not $r[0].success)

    Write-Host "New features:"
    Cmd "[title='MZTEST B'] focus" | Out-Null
    Cmd "[title='MZTEST C'] focus" | Out-Null
    Cmd 'focus last' | Out-Null
    Check 'focus last jumps back' ((Win 'MZTEST B').focused)
    $cands = @(Msg get_candidates)
    $mine = @($cands | Where-Object { $_.title -like 'MZTEST*' })
    Check 'get_candidates lists managed windows' ($mine.Count -ge 3 -and ($mine | Where-Object verdict -eq 'tiled').Count -ge 2)
    Check 'get_candidates explains ignored windows' (@($cands | Where-Object { $_.reason -eq 'excluded by --manage-only' }).Count -gt 0)
    $r = Msg command 'hotkeys off'
    Check 'hotkeys off' ($r[0].success)
    Msg command 'hotkeys on' | Out-Null
    $v = Msg get_version
    Check 'get_version reports the executable' ($v.executable -like '*Mozaic.exe')

    Write-Host "External changes:"
    $c = Win 'MZTEST C'
    Add-Type -Namespace MzTest -Name Mv -MemberDefinition '[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);'
    [MzTest.Mv]::MoveWindow([IntPtr]$c.window, 50, 50, 300, 200, $true) | Out-Null
    Start-Sleep -Milliseconds 600
    $c = Win 'MZTEST C'
    Check 'window that moves itself is put back' (Near $c.rect $c.actual_rect 3) "$($c.actual_rect | ConvertTo-Json -Compress)"

    Write-Host "Restart keeps the layout:"
    $layoutBefore = Layout
    Msg command 'restart' | Out-Null
    Start-Sleep -Seconds 3
    $layoutAfter = Layout
    Check 'layout restored after restart' ($layoutBefore -eq $layoutAfter) "$layoutBefore -> $layoutAfter"
    Check 'all windows still managed' (@(Wins).Count -eq 4)

    Write-Host "Closing:"
    Cmd "[title='MZTEST D'] kill" | Out-Null
    Start-Sleep -Milliseconds 500
    Check 'kill closes the window' (@(Wins).Count -eq 3)
    Cmd 'workspace 5' | Out-Null
}
finally {
    if (-not $KeepOpen) {
        try { & $msg --instance test exit 2>$null | Out-Null } catch {}
        Start-Sleep -Seconds 1
        $hidden = @($testWindows | Where-Object { -not $_.HasExited -and $_.MainWindowHandle -ne 0 -and -not [MzTest.Win]::IsWindowVisible($_.MainWindowHandle) })
        Check 'exit shows hidden windows again' ($hidden.Count -eq 0) "$($hidden.Count) hidden"
        foreach ($p in $testWindows) { if (-not $p.HasExited) { $p.Kill() } }
        Get-Process Mozaic -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -like 'MZTEST*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Write-Host ""
    Write-Host "$($script:pass) passed, $($script:fail) failed" -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
    Write-Host "Log: $(Join-Path $env:LOCALAPPDATA 'Mozaic\instances\test\mozaic.log')"
}
exit $script:fail
