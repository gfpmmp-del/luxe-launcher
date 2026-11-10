param(
    [string]$Png = "shot_test.png",
    [int]$Hx = 252, [int]$Hy = 78, [int]$Hw = 700, [int]$Hh = 226
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Png))
function Lum($p) { return ($p.R * 0.2126 + $p.G * 0.7152 + $p.B * 0.0722) }

$xs = $Hx + [int]($Hw * 0.55)
Write-Output "banner right half: x from $xs to $($Hx + $Hw)"

Write-Output "-- vertical seams (columns) --"
$worst = @()
for ($x = $xs + 1; $x -lt $Hx + $Hw; $x++) {
    $sum = 0.0; $n = 0; $max = 0.0
    for ($y = $Hy + 2; $y -lt $Hy + $Hh - 2; $y++) {
        $a = Lum $bmp.GetPixel($x - 1, $y)
        $b = Lum $bmp.GetPixel($x, $y)
        $d = [Math]::Abs($b - $a)
        $sum += $d; $n++
        if ($d -gt $max) { $max = $d }
    }
    $worst += [pscustomobject]@{ X = $x; Mean = $sum / $n; Max = $max }
}
$bad = $worst | Where-Object { $_.Mean -gt 4 }
if ($bad.Count -eq 0) { Write-Output "  none below 4" }
else {
    Write-Output "  suspicious columns (mean > 4):"
    $bad | Sort-Object -Property Mean -Descending | Select-Object -First 10 |
        ForEach-Object { Write-Output ("    x={0} mean={1:N2} max={2:N1}" -f $_.X, $_.Mean, $_.Max) }
}

Write-Output "-- horizontal seams (rows) --"
$hb = @()
for ($y = $Hy + 1; $y -lt $Hy + $Hh; $y++) {
    $sum = 0.0; $n = 0
    for ($x = $xs; $x -lt $Hx + $Hw; $x++) {
        $a = Lum $bmp.GetPixel($x, $y - 1)
        $b = Lum $bmp.GetPixel($x, $y)
        $sum += [Math]::Abs($b - $a); $n++
    }
    $hb += [pscustomobject]@{ Y = $y; Mean = $sum / $n }
}
$hbad = $hb | Where-Object { $_.Mean -gt 6 }
if ($hbad.Count -eq 0) { Write-Output "  none below 6" }
else { $hbad | Sort-Object -Property Mean -Descending | Select-Object -First 8 | ForEach-Object { Write-Output ("    y={0} mean={1:N2}" -f $_.Y, $_.Mean) } }

Write-Output "-- background around the banner --"
$pts = @(
    @{ n = "left"; x = $Hx - 6; y = $Hy + 100 },
    @{ n = "right"; x = $Hx + $Hw + 6; y = $Hy + 100 },
    @{ n = "above"; x = $Hx + 300; y = $Hy - 6 }
)
foreach ($pt in $pts) {
    $p = $bmp.GetPixel($pt.x, $pt.y)
    Write-Output ("  {0,-6} rgb=({1},{2},{3})" -f $pt.n, $p.R, $p.G, $p.B)
}
$bmp.Dispose()