# Generates DynamicIsland.ico (16-256 px, PNG-compressed entries) for build.bat's /win32icon.
# The mark: a dark rounded tile holding the island pill with a soft rim and the green "live" dot.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'DynamicIsland.ico'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Draw([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $small = $s -le 24
    # tile (skipped at 16/24 px, where the pill alone reads better)
    if (-not $small) {
        $m = $s * 0.06; $tile = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
        $bg = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 0)), ([Drawing.PointF]::new($s, $s)), ([Drawing.Color]::FromArgb(255, 44, 44, 50)), ([Drawing.Color]::FromArgb(255, 14, 14, 17))
        $g.FillPath($bg, $tile)
        $edge = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(40, 255, 255, 255)), ([Math]::Max(1, $s / 128))
        $g.DrawPath($edge, $tile)
    }
    # pill
    # the island sits at the top of the tile like on a phone screen; alone and centred at small sizes
    $pw = if ($small) { $s * 0.94 } else { $s * 0.54 }
    $ph = if ($small) { $pw * 0.40 } else { $pw * 0.30 }
    $px = ($s - $pw) / 2; $py = if ($small) { ($s - $ph) / 2 } else { $s * 0.20 }
    if (-not $small) {
        # faint screen light under the island
        $lp = New-Object Drawing.Drawing2D.GraphicsPath; $lw = $s * 0.9; $lh = $s * 0.55
        $lp.AddEllipse(($s - $lw) / 2, $py + $ph * 0.2, $lw, $lh)
        $lb = New-Object Drawing.Drawing2D.PathGradientBrush $lp
        $lb.CenterColor = [Drawing.Color]::FromArgb(36, 120, 200, 255); $lb.SurroundColors = @([Drawing.Color]::FromArgb(0, 120, 200, 255))
        $g.SetClip($tile); $g.FillPath($lb, $lp); $g.ResetClip()
    }
    $pill = RoundRect $px $py $pw $ph ($ph / 2)
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::Black)), $pill)
    $rimW = if ($small) { [Math]::Max(1, $s / 16) } else { [Math]::Max(1, $s / 96) }
    $rimA = if ($small) { 150 } else { 70 }
    $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb($rimA, 255, 255, 255)), $rimW), $pill)
    # green dot, right end of the pill
    $dd = $ph * 0.36; $dx = $px + $pw - $ph / 2 - $dd / 2; $dy = $py + ($ph - $dd) / 2
    if (-not $small) {
        $glow = New-Object Drawing.Drawing2D.GraphicsPath; $gd = $dd * 2.2
        $glow.AddEllipse($dx + $dd / 2 - $gd / 2, $dy + $dd / 2 - $gd / 2, $gd, $gd)
        $pg = New-Object Drawing.Drawing2D.PathGradientBrush $glow
        $pg.CenterColor = [Drawing.Color]::FromArgb(110, 48, 209, 88); $pg.SurroundColors = @([Drawing.Color]::FromArgb(0, 48, 209, 88))
        $g.FillPath($pg, $glow)
    }
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 48, 209, 88))), $dx, $dy, $dd, $dd)
    $g.Dispose(); return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $b = Draw $s; $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }

$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$off)
    $off += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
# preview of the 256 px image next to the icon, for a quick look
$pv = Draw 256; $pv.Save((Join-Path $env:TEMP 'di-icon-preview.png')); $pv.Dispose()
"wrote $out"
