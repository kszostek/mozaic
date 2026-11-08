<#
  Tests the keyboard hook and mouse dragging with synthetic input, in an isolated instance that
  only manages "MZTEST*" windows. NOTE: this moves your mouse cursor and sends key presses for a
  few seconds — don't touch the mouse/keyboard while it runs. Uses unusual hotkeys (Alt+Shift+F9..F11)
  and Alt+` (cheat sheet). -KeysOnly skips the mouse dragging (use it while another Mozaic manages your
  desktop: a drag could land on one of its windows).
#>
param([switch]$Published, [switch]$KeysOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$exe = if ($Published) { "$root\publish\Mozaic.exe" } else { "$root\src\Mozaic\bin\Debug\net10.0-windows\Mozaic.exe" }
$msg = if ($Published) { "$root\publish\mozaic-msg.exe" } else { "$root\src\MozaicMsg\bin\Debug\net10.0-windows\mozaic-msg.exe" }

Add-Type -Namespace MzIn -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
delegate bool EnumProc(IntPtr h, IntPtr l);
public static System.Collections.Generic.List<IntPtr> Find(uint pid, string title) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true;
        var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == title) list.Add(h); return true; }, IntPtr.Zero);
    return list;
}
'@
function Key([byte[]]$mods, [byte]$key) {
    foreach ($m in $mods) { [MzIn.U]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
    [MzIn.U]::keybd_event($key, 0, 0, [UIntPtr]::Zero); [MzIn.U]::keybd_event($key, 0, 2, [UIntPtr]::Zero)
    [array]::Reverse($mods); foreach ($m in $mods) { [MzIn.U]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 400
}
function Drag($x1, $y1, $x2, $y2, [switch]$WithAlt) {
    if ($WithAlt) { [MzIn.U]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero) }
    [MzIn.U]::SetCursorPos($x1, $y1) | Out-Null; Start-Sleep -Milliseconds 150
    [MzIn.U]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le 20; $i++) {
        [MzIn.U]::SetCursorPos([int]($x1 + ($x2 - $x1) * $i / 20), [int]($y1 + ($y2 - $y1) * $i / 20)) | Out-Null
        [MzIn.U]::mouse_event(0x1, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 150
    [MzIn.U]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
    if ($WithAlt) { [MzIn.U]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 700
}
$script:fail = 0; $script:pass = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" -ForegroundColor Green } else { $script:fail++; Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}
function Q([string]$type) { (& $msg --instance input -r -t $type | Out-String | ConvertFrom-Json) }
function Win([string]$t) { @(Q get_windows) | Where-Object title -eq $t }
function Layout { $ws = (Q get_tree).nodes.nodes | Where-Object { $_.visible -and $_.nodes.Count } | Select-Object -First 1; D $ws }
function D($n) { if ($null -eq $n) { return '<none>' }; if ($n.type -eq 'window') { return $n.name -replace '^MZTEST ', '' }; ($n.layout.Substring(5, 1).ToUpper()) + '[' + (($n.nodes | ForEach-Object { D $_ }) -join ' ') + ']' }

$cfg = Join-Path $env:TEMP 'mozaic-input-test.config'
@'
set $mod Alt
floating_modifier $mod
tiling_drag on
gaps inner 10
bindsym $mod+Shift+F8 workspace 1
bindsym $mod+Shift+F9 focus right
bindsym $mod+Shift+F10 focus left
bindsym $mod+Shift+F11 workspace 7
'@ | Set-Content -Encoding UTF8 $cfg
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Mozaic\instances\input" -ErrorAction SilentlyContinue
$wins = @()
try {
    Start-Process $exe -ArgumentList '--instance', 'input', '--config', "`"$cfg`"", '--manage-only', '^MZTEST', '--no-tray', '--debug' | Out-Null
    Start-Sleep 2
    foreach ($n in 'A', 'B') { $wins += Start-Process $exe -ArgumentList '--test-window', "`"MZTEST $n`"" -PassThru; Start-Sleep -Milliseconds 900 }
    Start-Sleep 1
    Check 'B focused after opening' ((Win 'MZTEST B').focused)

    Write-Host 'Keyboard hook:'
    Key @(0x12, 0x10) 0x79   # Alt+Shift+F10
    Check 'Alt+Shift+F10 -> focus left' ((Win 'MZTEST A').focused)
    Check 'OS foreground follows' ([MzIn.U]::GetForegroundWindow() -eq [IntPtr](Win 'MZTEST A').window)
    Key @(0x12, 0x10) 0x78   # Alt+Shift+F9
    Check 'Alt+Shift+F9 -> focus right' ((Win 'MZTEST B').focused)
    Key @(0x12, 0x10) 0x7A   # Alt+Shift+F11
    Check 'Alt+Shift+F11 -> workspace 7' ((@(Q get_workspaces) | Where-Object focused).name -eq '7')
    & $msg --instance input 'workspace 1' | Out-Null; Start-Sleep -Milliseconds 500

    Write-Host 'Cheat sheet while Alt+` is held:'
    $inst = (Get-CimInstance Win32_Process -Filter "Name='Mozaic.exe'" | Where-Object { $_.CommandLine -like '*--instance input*' } | Select-Object -First 1).ProcessId
    function Sheet { @([MzIn.U]::Find($inst, 'Mozaic cheat sheet') | Where-Object { [MzIn.U]::IsWindowVisible($_) }).Count }
    [MzIn.U]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)          # Alt down
    [MzIn.U]::keybd_event(0xC0, 0, 0, [UIntPtr]::Zero)          # ` down
    Start-Sleep -Milliseconds 300
    Check 'shown while held' ((Sheet) -eq 1)
    [MzIn.U]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200   # Shift: other layer
    Check 'still shown with Shift' ((Sheet) -eq 1)
    [MzIn.U]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 100
    [MzIn.U]::keybd_event(0xC0, 0, 2, [UIntPtr]::Zero)          # ` up
    Start-Sleep -Milliseconds 150
    Check 'hidden when ` is released' ((Sheet) -eq 0)
    [MzIn.U]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)          # Alt up
    Key @(0x12) 0xC0
    Check 'a short tap does not leave it open' ((Sheet) -eq 0)
    [MzIn.U]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [MzIn.U]::keybd_event(0xC0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    [MzIn.U]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)          # Alt released first
    Start-Sleep -Milliseconds 150
    Check 'hidden when Alt is released' ((Sheet) -eq 0)
    [MzIn.U]::keybd_event(0xC0, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    if (-not $KeysOnly) {

    Write-Host 'Dragging a tiled window by its title bar:'
    $a = (Win 'MZTEST A').rect; $b = (Win 'MZTEST B').rect
    Drag ($b.x + 200) ($b.y + 15) ($a.x + 40) ($a.y + $a.height / 2)
    Check 'dropped on left edge of A -> H[B A]' ((Layout) -eq 'H[B A]') ((Layout) + ' ' + ((Q get_windows | ForEach-Object { "$($_.title)@$($_.workspace) vis=$($_.visible) $($_.rect.x),$($_.rect.y)" }) -join '; '))
    $w = Win 'MZTEST B'
    Check 'window snapped back into its tile' ([math]::Abs($w.rect.x - $w.actual_rect.x) -le 3 -and [math]::Abs($w.rect.width - $w.actual_rect.width) -le 3)

    Write-Host 'Dragging with $mod held:'
    $a = (Win 'MZTEST A').rect; $b = (Win 'MZTEST B').rect
    Drag ($a.x + $a.width / 2) ($a.y + $a.height / 2) ($b.x + $b.width / 2) ($b.y + $b.height - 30) -WithAlt
    Check 'Alt+drag A onto bottom of B -> V[B A]' ((Layout) -eq 'V[B A]') (Layout)

    Write-Host 'Resizing a tiled window by dragging its edge:'
    $b = (Win 'MZTEST B').rect
    $before = $b.height
    Drag ($b.x + $b.width / 2) ($b.y + $b.height + 2) ($b.x + $b.width / 2) ($b.y + $b.height + 300)
    $after = (Win 'MZTEST B').rect.height
    Check 'edge drag resized the split' ($after -gt $before + 200) "$before -> $after"
    }
}
finally {
    & $msg --instance input exit 2>$null | Out-Null
    Start-Sleep 1
    foreach ($p in $wins) { if (-not $p.HasExited) { $p.Kill() } }
    Write-Host "$($script:pass) passed, $($script:fail) failed"
}
exit $script:fail
