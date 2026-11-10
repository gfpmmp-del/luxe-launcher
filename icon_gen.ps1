# Генератор логотипа Luxe: «аметистовый кристалл» — та же векторная
# графика, что и в шапке лаунчера (Theme.cs, Art.Logomark).
# Пишет logo.ico (16/32/48/64/256) и logo.png (256) рядом с собой.
Add-Type -AssemblyName System.Drawing

function RR([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2.0
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    if ($d -lt 1.0) { $p.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h)); return $p }
    $p.AddArc($x, $y, $d, $d, 180.0, 90.0)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270.0, 90.0)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0.0, 90.0)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90.0, 90.0)
    $p.CloseFigure()
    return $p
}

function Draw-Gem([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $u = $s / 24.0
    $rf = [System.Drawing.RectangleF]::new(0, 0, $s, $s)
    $rad = $s * 0.30

    # основа: сине-фиолетовый градиент
    $c1 = [System.Drawing.Color]::FromArgb(255, 96, 70, 220)
    $c2 = [System.Drawing.Color]::FromArgb(255, 77, 124, 255)
    $base = RR 0 0 $s $s $rad
    $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rf, $c1, $c2, 118.0)
    $g.FillPath($gb, $base)
    $gb.Dispose()

    # верхний блик
    $hf = RR ($s*0.06) ($s*0.05) ($s*0.88) ($s*0.44) ($rad*0.8)
    $hrf = [System.Drawing.RectangleF]::new(0, 0, $s, $s*0.5)
    $hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush($hrf, [System.Drawing.Color]::FromArgb(84,255,255,255), [System.Drawing.Color]::FromArgb(0,255,255,255), 90.0)
    $g.FillPath($hb, $hf)
    $hb.Dispose(); $hf.Dispose()

    # огранённый камень: подушка-октаэдр
    $cx = 12.0*$u; $cy = 12.0*$u
    $c = [System.Drawing.PointF]::new($cx, $cy)
    $top = [System.Drawing.PointF]::new($cx, 4.4*$u)
    $rgt = [System.Drawing.PointF]::new(19.6*$u, $cy)
    $bot = [System.Drawing.PointF]::new($cx, 19.6*$u)
    $lft = [System.Drawing.PointF]::new(4.4*$u, $cy)

    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(225,64,46,170))
    $g.FillPolygon($dark, @($lft, $c, $bot))
    $g.FillPolygon($dark, @($bot, $c, $rgt))
    $dark.Dispose()
    $light = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235,168,140,255))
    $g.FillPolygon($light, @($lft, $c, $top))
    $g.FillPolygon($light, @($top, $c, $rgt))
    $light.Dispose()
    # блик на верхней левой грани (только на крупных размерах)
    if ($s -ge 48) {
        $t2 = [System.Drawing.PointF]::new($cx - 3.4*$u, $cy - 3.4*$u)
        $hl = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120,255,255,255))
        $g.FillPolygon($hl, @($top, $t2, $lft))
        $hl.Dispose()
    }
    # контур камня
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150,235,230,255), [single](1.1*$u))
    $g.DrawPolygon($pen, @($top, $rgt, $bot, $lft))
    $pen.Dispose()
    # центральная искра
    $sp = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240,255,255,255))
    $g.FillPolygon($sp, @(
        [System.Drawing.PointF]::new($cx, $cy - 3.0*$u),
        [System.Drawing.PointF]::new($cx + 1.1*$u, $cy - 1.1*$u),
        [System.Drawing.PointF]::new($cx + 3.0*$u, $cy),
        [System.Drawing.PointF]::new($cx + 1.1*$u, $cy + 1.1*$u),
        [System.Drawing.PointF]::new($cx, $cy + 3.0*$u),
        [System.Drawing.PointF]::new($cx - 1.1*$u, $cy + 1.1*$u),
        [System.Drawing.PointF]::new($cx - 3.0*$u, $cy),
        [System.Drawing.PointF]::new($cx - 1.1*$u, $cy - 1.1*$u)))
    $sp.Dispose()
    $base.Dispose()
    $g.Dispose()
    return $bmp
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 16, 32, 48, 64, 256

# logo.png — 256px
$p256 = Draw-Gem 256
$p256.Save((Join-Path $here 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$p256.Dispose()

# logo.ico — PNG-записи в ICO-контейнере (Vista+, csc поддерживает)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
$datas = @()
foreach ($s in $sizes) {
    $bmp = Draw-Gem $s
    $pm = New-Object System.IO.MemoryStream
    $bmp.Save($pm, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $pm.ToArray(); $pm.Dispose(); $bmp.Dispose()
    $w = 0; if ($s -lt 256) { $w = $s }
    $bw.Write([byte]$w); $bw.Write([byte]$w); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
    $offset += $bytes.Length
    $datas += , $bytes
}
foreach ($b in $datas) { $bw.Write($b) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $here 'logo.ico'), $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Output 'icons written'
