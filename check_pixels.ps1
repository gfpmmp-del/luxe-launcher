param(
    [string]$Png = "shot_test.png",
    [string]$Size = "1280x940"
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png))
"image: $($bmp.Width)x$($bmp.Height)"

function Lum($p) { return ($p.R * 0.2126 + $p.G * 0.7152 + $p.B * 0.0722) }

# 1) вертикальные швы: резкий перепад между соседними столбцами
function ScanVertical($x0, $y0, $w, $h, $name) {
    $out = @()
    for ($x = $x0 + 1; $x -lt $x0 + $w; $x++) {
        $sum = 0.0; $n = 0
        for ($y = $y0; $y -lt $y0 + $h; $y += 2) {
            $a = Lum $bmp.GetPixel($x - 1, $y)
            $b = Lum $bmp.GetPixel($x, $y)
            $sum += [Math]::Abs($b - $a); $n++
        }
        $m = $sum / $n
        if ($m -gt 12) { $out += ("    x={0} mean-step={1:N1}" -f $x, $m) }
    }
    "  [$name] вертикальные скачки: " + $(if ($out.Count -eq 0) { "нет" } else { "" })
    $out | Select-Object -First 8 | ForEach-Object { $_ }
}

function ScanHorizontal($x0, $y0, $w, $h, $name) {
    $out = @()
    for ($y = $y0 + 1; $y -lt $y0 + $h; $y++) {
        $sum = 0.0; $n = 0
        for ($x = $x0; $x -lt $x0 + $w; $x += 2) {
            $a = Lum $bmp.GetPixel($x, $y - 1)
            $b = Lum $bmp.GetPixel($x, $y)
            $sum += [Math]::Abs($b - $a); $n++
        }
        $m = $sum / $n
        if ($m -gt 12) { $out += ("    y={0} mean-step={1:N1}" -f $y, $m) }
    }
    "  [$name] горизонтальные скачки: " + $(if ($out.Count -eq 0) { "нет" } else { "" })
    $out | Select-Object -First 8 | ForEach-Object { $_ }
}

"--- витрина (баннер) ---"
ScanVertical 252 80 700 220 "hero"
ScanHorizontal 252 80 700 220 "hero"

"--- сайдбар ---"
ScanVertical 0 66 232 600 "side"
ScanHorizontal 0 66 232 600 "side"

# 2) заполненность: пустых (совсем тёмных) зон быть не должно
"--- средняя яркость областей ---"
$regions = @(
    @{n = "hero"; x = 252; y = 78; w = 700; h = 226 },
    @{n = "news"; x = 972; y = 78; w = 292; h = 234 },
    @{n = "premium"; x = 972; y = 324; w = 292; h = 116 },
    @{n = "quick"; x = 972; y = 452; w = 292; h = 276 },
    @{n = "mods"; x = 252; y = 620; w = 700; h = 104 }
)
foreach ($r in $regions) {
    $sum = 0.0; $n = 0; $dark = 0
    for ($y = $r.y; $y -lt [Math]::Min($r.y + $r.h, $bmp.Height); $y += 3) {
        for ($x = $r.x; $x -lt [Math]::Min($r.x + $r.w, $bmp.Width); $x += 3) {
            $l = Lum $bmp.GetPixel($x, $y)
            $sum += $l; $n++
            if ($l -lt 6) { $dark++ }
        }
    }
    "  {0,-9} avg={1,6:N1}  доля чёрных={2:P1}" -f $r.n, ($sum / $n), ($dark / $n)
}
$bmp.Dispose()