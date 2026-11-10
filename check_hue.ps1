# Гистограмма оттенков кадра: показывает, насколько палитра держится
# в фирменном коридоре (индиго-фиолетово-синий) и сколько «случайных»
# насыщенных цветов (зелёный, жёлтый, красный) осталось на экране.
param(
    [string]$Png = "shot_test.png",
    [int]$Sample = 2
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png))
Write-Output ("image {0}x{1}, шаг выборки {2}" -f $bmp.Width, $bmp.Height, $Sample)

$bins = @{}
for ($h = 0; $h -lt 360; $h += 15) { $bins[$h] = 0 }

$sat = 0        # насыщенные (цветные) пиксели
$corridor = 0   # из них в фирменном коридоре
$outside = @()
$total = 0

for ($y = 0; $y -lt $bmp.Height; $y += $Sample) {
    for ($x = 0; $x -lt $bmp.Width; $x += $Sample) {
        $c = $bmp.GetPixel($x, $y)
        $total++
        $r = $c.R / 255.0; $g = $c.G / 255.0; $b = $c.B / 255.0
        $max = [Math]::Max($r, [Math]::Max($g, $b))
        $min = [Math]::Min($r, [Math]::Min($g, $b))
        $d = $max - $min
        if ($max -le 0.01) { continue }
        $s = $d / $max
        if ($s -lt 0.18) { continue }   # серый/тёмный — пропускаем
        $sat++
        $hue = 0.0
        if ($d -gt 0) {
            if ($max -eq $r) { $hue = 60 * ((($g - $b) / $d) % 6) }
            elseif ($max -eq $g) { $hue = 60 * ((($b - $r) / $d) + 2) }
            else { $hue = 60 * ((($r - $g) / $d) + 4) }
            if ($hue -lt 0) { $hue += 360 }
        }
        $bin = [int]([Math]::Floor($hue / 15)) * 15
        $bins[$bin] = $bins[$bin] + 1

        # коридор: индиго/фиолет/синий + допуск на сдвиг в акцент
        if ($hue -ge 215 -and $hue -le 305) { $corridor++ }
        else { $outside += $hue }
    }
}

Write-Output ("насыщенных пикселей: {0:N0} из {1:N0} ({2:P1})" -f $sat, $total, ($sat / $total))
Write-Output ("в фирменном коридоре 215..305: {0:N0} ({1:P1} от насыщенных)" -f $corridor, ($corridor / $sat))
Write-Output "--- распределение по оттенкам (только заметные) ---"
foreach ($k in ($bins.Keys | Sort-Object)) {
    if ($bins[$k] -lt ($sat * 0.005)) { continue }
    $bar = "#" * [Math]::Min(50, [int]($bins[$k] * 50 / ($sat + 1)))
    Write-Output ("  {0,3}..{1,3}: {2,8} {3}" -f $k, ($k + 14), $bins[$k], $bar)
}
if ($outside.Count -gt 0) {
    $avg = ($outside | Measure-Object -Average).Average
    Write-Output ("вне коридора: {0:N0} пикс., средний оттенок {1:N0}" -f $outside.Count, $avg)
}
$bmp.Dispose()