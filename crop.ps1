# Кропы снятого кадра по областям: сохраняем увеличенные фрагменты,
# чтобы искать дефекты точечно (швы, обрезку, наложения) без просмотра всего окна.
param(
    [string]$Png = "shot_test.png",
    [string]$OutDir = "crops",
    [string]$Region = "0,0,0,0",
    [int]$Scale = 2
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png))
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$list = @()
if ($Region -ne "0,0,0,0") {
    $p = $Region.Split(",") | ForEach-Object { [int]$_.Trim() }
    $list += , @($p[0], $p[1], $p[2], $p[3])
} else {
    $list = @(
        @(0, 0, 240, 100),
        @(232, 0, 400, 64),
        @(0, 60, 232, 300),
        @(0, 380, 232, 400),
        @(232, 64, 700, 230),
        @(940, 64, 300, 240)
    )
}
$i = 0
foreach ($r in $list) {
    $x = $r[0]; $y = $r[1]; $w = $r[2]; $h = $r[3]
    if ($x + $w -gt $bmp.Width) { $w = $bmp.Width - $x }
    if ($y + $h -gt $bmp.Height) { $h = $bmp.Height - $y }
    if ($w -le 0 -or $h -le 0) { continue }
    $crop = New-Object System.Drawing.Bitmap ($w * $Scale), ($h * $Scale)
    $g = [System.Drawing.Graphics]::FromImage($crop)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, ($w * $Scale), ($h * $Scale)), (New-Object System.Drawing.Rectangle $x, $y, $w, $h), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $name = Join-Path $OutDir ("crop_{0}_{1}_{2}_{3}x{4}.png" -f $i, $x, $y, $w, $h)
    $crop.Save($name, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose()
    Write-Output $name
    $i++
}
$bmp.Dispose()