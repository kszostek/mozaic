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
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
delegate bool EnumProc(IntPtr h, IntPtr l);
public struct RECT { public int Left, Top, Right, Bottom; }
// Top-level windows of a process with the given title (e.g. Mozaic's group lines or cheat sheet).
public static System.Collections.Generic.List<IntPtr> Find(uint pid, string title) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows((h, l) => {
        uint p; GetWindowThreadProcessId(h, out p);
        if (p != pid) return true;
        var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256);
        if (sb.ToString() == title) list.Add(h);
        return true;
    }, IntPtr.Zero);
    return list;
}
// 255 = opaque (not layered)
public static int Alpha(IntPtr h) {
    if ((GetWindowLong(h, -20) & 0x80000) == 0) return 255;
    uint k, f; byte a;
    return GetLayeredWindowAttributes(h, out k, out a, out f) ? a : -1;
}
'@
function Visible([uint32]$procId, [string]$title) { @([MzTest.Win]::Find($procId, $title) | Where-Object { [MzTest.Win]::IsWindowVisible($_) }) }
function Alpha($w) { [MzTest.Win]::Alpha([IntPtr]$w.window) }
function TestPid { (Get-CimInstance Win32_Process -Filter "Name='Mozaic.exe'" | Where-Object { $_.CommandLine -like '*--instance test*' } | Select-Object -First 1).ProcessId }
# Number of gaps between neighbours inside the tiles of the visible workspace (= expected group lines). The
# workspace's own split, and a container that is its only child, separate tiles: no lines there.
function SplitGaps($n, [bool]$top = $true) {
    $c = 0
    if ($n.type -eq 'window' -or $n.layout -notin 'splith', 'splitv') { return 0 }
    if (-not $top -and $n.nodes.Count -ge 2) { $c += $n.nodes.Count - 1 }
    foreach ($k in $n.nodes) { $c += SplitGaps $k ($top -and $n.nodes.Count -eq 1) }
    $c
}

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

    Write-Host "Opacity, group lines, cheat sheet:"
    Cmd "[title='MZTEST C'] focus" | Out-Null
    Cmd "[title='MZTEST B'] focus" | Out-Null
    Start-Sleep -Milliseconds 300
    $a = @{}; foreach ($n in 'A', 'B', 'C', 'D') { $a[$n] = Alpha (Win "MZTEST $n") }
    Check 'active window is opaque' ($a.B -eq 255) "B=$($a.B)"
    Check 'previously used window is 90%' ($a.C -eq 230) "C=$($a.C)"
    Check 'older windows fade, not below 70%' ($a.A -lt 230 -and $a.A -ge 178 -and $a.D -lt 230 -and $a.D -ge 178 -and $a.A -ne $a.D) "A=$($a.A) D=$($a.D)"
    $instance = TestPid
    $ws = (Msg get_tree).nodes | ForEach-Object { $_.nodes } | Where-Object { $_.visible -and $_.nodes.Count -gt 0 } | Select-Object -First 1
    $expected = SplitGaps $ws
    $lines = Visible $instance 'Mozaic group line'
    Check 'one group line per gap between neighbours' ($expected -gt 0 -and $lines.Count -eq $expected) "expected $expected, got $($lines.Count) ($(Layout))"
    $thin = $true
    foreach ($h in $lines) { $r = New-Object MzTest.Win+RECT; [MzTest.Win]::GetWindowRect($h, [ref]$r) | Out-Null; if ([math]::Min($r.Right - $r.Left, $r.Bottom - $r.Top) -ne 10) { $thin = $false } }
    Check 'group lines fill the 10px gaps' $thin
    Cmd 'layout tabbed' | Out-Null
    Start-Sleep -Milliseconds 300
    $ws = (Msg get_tree).nodes | ForEach-Object { $_.nodes } | Where-Object { $_.visible -and $_.nodes.Count -gt 0 } | Select-Object -First 1
    Check 'tabs have no group lines' ((Visible $instance 'Mozaic group line').Count -eq (SplitGaps $ws)) "$((Visible $instance 'Mozaic group line').Count) vs $(SplitGaps $ws)"
    Cmd 'layout toggle split' | Out-Null
    Cmd 'cheatsheet show' | Out-Null
    $sheet = Visible $instance 'Mozaic cheat sheet'
    Check 'cheatsheet show' ($sheet.Count -eq 1)
    if ($sheet.Count -eq 1) {
        $r = New-Object MzTest.Win+RECT; [MzTest.Win]::GetWindowRect($sheet[0], [ref]$r) | Out-Null
        Add-Type -AssemblyName System.Drawing
        $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size); $g.Dispose()
        $bmp.Save((Join-Path $tmp 'cheatsheet.png')); $bmp.Dispose()
    }
    Cmd 'cheatsheet hide' | Out-Null
    Check 'cheatsheet hide' ((Visible $instance 'Mozaic cheat sheet').Count -eq 0)
    $r = Msg command 'cheatsheet sideways'
    Check 'cheatsheet rejects bad arguments' (-not $r[0].success)

    Write-Host "Crash leaves no translucent windows:"
    Cmd "[title='MZTEST C'] focus" | Out-Null
    Start-Sleep -Milliseconds 300
    Check 'some windows are translucent before the crash' (@(Wins | Where-Object { (Alpha $_) -lt 255 }).Count -ge 2)
    Stop-Process -Id $instance -Force
    Start-Sleep -Milliseconds 500
    # Start again with the effect off: whatever the killed instance left translucent must become opaque.
    (Get-Content $cfg) + 'inactive_opacity off' | Set-Content -Encoding UTF8 $cfg
    $wm = Start-Process $exe -ArgumentList '--instance', 'test', '--config', "`"$cfg`"", '--manage-only', '^MZTEST', '--no-hotkeys', '--no-tray', '--debug' -PassThru
    Start-Sleep -Seconds 2
    $left = @(Wins | Where-Object { (Alpha $_) -ne 255 })
    Check 'next start makes them opaque again' ($left.Count -eq 0) "$(($left | ForEach-Object { "$($_.title)=$(Alpha $_)" }) -join ', ')"
    Check 'layout survives the crash' (@(Wins).Count -eq 4) "$(@(Wins).Count) windows"
    @(Get-Content $cfg | Where-Object { $_ -ne 'inactive_opacity off' }) | Set-Content -Encoding UTF8 $cfg
    Cmd 'reload' | Out-Null
    Start-Sleep -Milliseconds 300
    Check 'reload turns the effect back on' (@(Wins | Where-Object { (Alpha $_) -lt 255 }).Count -eq 3)

    Write-Host "Title bars:"
    $instance = TestPid
    (Get-Content $cfg) + 'titlebars yes' | Set-Content -Encoding UTF8 $cfg
    Cmd 'reload' | Out-Null
    Start-Sleep -Milliseconds 500
    $tiled = @(Wins | Where-Object { -not $_.floating -and [MzTest.Win]::IsWindowVisible([IntPtr]$_.window) })
    $headers = @(Visible $instance 'Mozaic tabs')
    $ws = (Msg get_tree).nodes | ForEach-Object { $_.nodes } | Where-Object { $_.visible -and $_.nodes.Count -gt 0 } | Select-Object -First 1
    Check 'a title bar above every tiled window' ($headers.Count -eq $tiled.Count) "$($headers.Count) title bars, $($tiled.Count) tiled windows ($(Layout))"
    function HeaderOf($w) {
        foreach ($h in Visible $instance 'Mozaic tabs') {
            $r = New-Object MzTest.Win+RECT; [MzTest.Win]::GetWindowRect($h, [ref]$r) | Out-Null
            if ([math]::Abs($r.Left - $w.actual_rect.x) -le 2 -and $r.Bottom -le $w.actual_rect.y -and $w.actual_rect.y - $r.Bottom -le 6) { return $h }
        }
    }
    $b = Win 'MZTEST B'
    $hb = HeaderOf $b
    Check 'title bar sits on top of its window' ($null -ne $hb) "$($b.actual_rect | ConvertTo-Json -Compress)"
    Add-Type -Namespace MzTest -Name Post -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
    # Clicks a title bar button (counted from the right: 0 = close) with window messages, not the real mouse.
    function ClickButton($h, [int]$fromRight) {
        $r = New-Object MzTest.Win+RECT; [MzTest.Win]::GetWindowRect($h, [ref]$r) | Out-Null
        $height = $r.Bottom - $r.Top; $s = $height / 24.0
        $size = [math]::Min($height - [math]::Round(4 * $s), [math]::Round(26 * $s)); $bw = $size + [math]::Round(6 * $s)
        $x = [int](($r.Right - $r.Left) - [math]::Round(3 * $s) - $bw * $fromRight - $bw / 2); $y = [int]($height / 2)
        $lp = [IntPtr](($y -shl 16) -bor $x)
        [MzTest.Post]::PostMessage($h, 0x201, [IntPtr]1, $lp) | Out-Null
        [MzTest.Post]::PostMessage($h, 0x202, [IntPtr]0, $lp) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    if ($hb) {
        ClickButton $hb 2
        Check 'floating button makes the window float' ((Win 'MZTEST B').floating)
        Cmd "[title='MZTEST B'] floating disable" | Out-Null
        Start-Sleep -Milliseconds 300
        $hb = HeaderOf (Win 'MZTEST B')
        if ($hb) { ClickButton $hb 1 }
        Check 'fullscreen button' ((Win 'MZTEST B').fullscreen -eq $true)
        Check 'no title bars over a fullscreen window' ((Visible $instance 'Mozaic tabs').Count -eq 0)
        Cmd "[title='MZTEST B'] fullscreen disable" | Out-Null
    }
    @(Get-Content $cfg | Where-Object { $_ -ne 'titlebars yes' }) | Set-Content -Encoding UTF8 $cfg
    Cmd 'reload' | Out-Null
    Start-Sleep -Milliseconds 400
    Check 'title bars off again' ((Visible $instance 'Mozaic tabs').Count -eq 0)
    $ok = $true; foreach ($x in Wins) { if (-not $x.floating -and -not (Near $x.rect $x.actual_rect)) { $ok = $false } }
    Check 'windows take the space back' $ok

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

    Write-Host "Exit never leaves the process behind:"
    # A second instance (its own windows: MZHANG*) whose exit gets stuck on purpose.
    $hcfg = Join-Path $tmp 'hang-config'
    "set `$mod Alt`nwelcome no" | Set-Content -Encoding UTF8 $hcfg
    Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'Mozaic\instances\hang') -ErrorAction SilentlyContinue
    $hang = Start-Process $exe -ArgumentList '--instance', 'hang', '--config', "`"$hcfg`"", '--manage-only', '^MZHANG', '--no-hotkeys', '--no-tray', '--test-hang-on-exit' -PassThru
    Start-Sleep -Seconds 2
    $hw = Start-Process $exe -ArgumentList '--test-window', '"MZHANG A"' -PassThru
    $testWindows += $hw
    Start-Sleep -Seconds 1
    $hw.Refresh(); $hh = $hw.MainWindowHandle
    & $msg --instance hang 'move container to workspace 9' | Out-Null
    Start-Sleep -Milliseconds 500
    Check 'its window on another workspace is hidden' ($hh -ne 0 -and -not [MzTest.Win]::IsWindowVisible($hh))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $msg --instance hang exit | Out-Null
    $ended = $hang.WaitForExit(15000)
    Check 'a stuck exit still ends the process' $ended "after $([int]$sw.Elapsed.TotalSeconds) s"
    Check 'and shows the hidden window' ([MzTest.Win]::IsWindowVisible($hh))
    $hlog = Get-Content (Join-Path $env:LOCALAPPDATA 'Mozaic\instances\hang\mozaic.log') -Raw
    Check 'the log names the step that got stuck' ($hlog -match 'stuck at: --test-hang-on-exit')
    if (-not $hang.HasExited) { $hang.Kill() }

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
        $translucent = @($testWindows | Where-Object { -not $_.HasExited -and $_.MainWindowHandle -ne 0 -and [MzTest.Win]::Alpha($_.MainWindowHandle) -ne 255 })
        Check 'exit leaves every window opaque' ($translucent.Count -eq 0) "$($translucent.Count) translucent"
        foreach ($p in $testWindows) { if (-not $p.HasExited) { $p.Kill() } }
        Get-Process Mozaic -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -like 'MZTEST*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Write-Host ""
    Write-Host "$($script:pass) passed, $($script:fail) failed" -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
    Write-Host "Log: $(Join-Path $env:LOCALAPPDATA 'Mozaic\instances\test\mozaic.log')"
}
exit $script:fail
