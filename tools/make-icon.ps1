<#
.SYNOPSIS
  Vẽ lại icon của Toolbox (src/Toolbox/Assets/toolbox.ico) từ hình vector trong script này.

.DESCRIPTION
  Icon là một hộp đồ nghề trắng trên nền bo góc chuyển màu tím (cùng dải màu với logo trong app). Mọi kích cỡ được vẽ riêng
  từ vector (không thu nhỏ ảnh lớn) rồi đóng thành một file .ico chứa các ảnh PNG. Chạy lại script khi muốn đổi hình:

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-icon.ps1
#>
[CmdletBinding()]
param([string]$Out)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Toolbox\Assets\toolbox.ico' }
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $s = $size / 64.0   # hình gốc vẽ trên lưới 64

    # Nền bo góc chuyển màu.
    $bg = New-RoundRect (2 * $s) (2 * $s) (60 * $s) (60 * $s) (14 * $s)
    $rect = New-Object Drawing.RectangleF 0, 0, $size, $size
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.ColorTranslator]::FromHtml('#818CF8')), ([Drawing.ColorTranslator]::FromHtml('#4338CA')), 45
    $g.FillPath($brush, $bg)

    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    # Quai xách: khung bo góc rỗng phía trên thân hộp.
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([single][Math]::Max(1.6, 4.2 * $s))
    $pen.LineJoin = 'Round'
    $handle = New-RoundRect (24 * $s) (15 * $s) (16 * $s) (14 * $s) (4 * $s)
    $g.DrawPath($pen, $handle)
    # Thân hộp.
    $body = New-RoundRect (12 * $s) (24 * $s) (40 * $s) (26 * $s) (5 * $s)
    $g.FillPath($white, $body)
    # Đường chia nắp và khoá (màu nền) để nhận ra là hộp đồ nghề; bỏ ở cỡ rất nhỏ cho đỡ rối.
    if ($size -ge 24) {
        $accent = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#4F46E5'))
        $g.FillRectangle($accent, [single](12 * $s), [single](34 * $s), [single](40 * $s), [single][Math]::Max(1, 2.6 * $s))
        $latch = New-RoundRect (28 * $s) (31 * $s) (8 * $s) (9 * $s) (2 * $s)
        $g.FillPath($accent, $latch)
        $accent.Dispose()
    }

    $g.Dispose()
    $stream = New-Object IO.MemoryStream
    $bmp.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) { , (New-IconPng $size) }

$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Flush()
[IO.File]::WriteAllBytes($Out, $file.ToArray())
"Đã ghi $Out ($([int]($file.Length / 1KB)) KB, cỡ: $($sizes -join ', '))"
