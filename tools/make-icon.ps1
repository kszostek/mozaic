# Generates src/Mozaic/mozaic.ico (the Mozaic logo: one tall tile + two stacked tiles).
# Usage (Windows PowerShell 5.1 or PowerShell 7 on Windows):  .\tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Mozaic\mozaic.ico'))

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 32.0
    $tiles = @(
        @(2, 2, 13, 28, '#2E9EF4'),
        @(17, 2, 13, 13, '#8CCBFF'),
        @(17, 17, 13, 13, '#1C6FB5')
    )
    foreach ($t in $tiles) {
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($t[4]))
        $path = New-RoundedRect ($t[0] * $k) ($t[1] * $k) ($t[2] * $k) ($t[3] * $k) ([Math]::Max(1, 3 * $k))
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @($s, $ms.ToArray())
    $bmp.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $data = $img[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Out)).Path + '\' + (Split-Path $Out -Leaf), $ico.ToArray())
Write-Output "wrote $Out ($($ico.Length) bytes)"
