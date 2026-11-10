# Ищем наложения соседей внутри одного родителя: два контрола с текстом,
# которые перекрываются заметно — почти всегда дефект раскладки.
param(
    [string]$Dump = "controls.txt",
    [double]$MinOverlap = 0.25
)
$lines = Get-Content $Dump -Encoding UTF8
$rx = [regex]'^(?<ind>\s*)(?<type>[A-Za-z0-9_]+) \[(?<name>[^\]]*)\] \{X=(?<x>-?\d+),Y=(?<y>-?\d+),Width=(?<w>\d+),Height=(?<h>\d+)\} vis=(?<vis>\w+) hwnd=(?<hwnd>\S+) par=(?<par>\S+)(?<rest>.*)$'

$rec = New-Object System.Collections.ArrayList
$stack = @{}
foreach ($l in $lines) {
    $m = $rx.Match($l)
    if (-not $m.Success) { continue }
    $ind = [int]($m.Groups['ind'].Value.Length / 2)
    $parIx = -1
    $d = $ind - 1
    while ($d -ge 0) { if ($stack.ContainsKey($d)) { $parIx = $stack[$d]; break }; $d-- }
    $ix = $rec.Count
    [void]$rec.Add([pscustomobject]@{
            Ix = $ix; ParIx = $parIx; Depth = $ind
            Type = $m.Groups['type'].Value
            X = [int]$m.Groups['x'].Value; Y = [int]$m.Groups['y'].Value
            W = [int]$m.Groups['w'].Value; H = [int]$m.Groups['h'].Value
            Vis = $m.Groups['vis'].Value
            Text = $m.Groups['rest'].Value.Trim()
        })
    $keys = @($stack.Keys | Where-Object { $_ -ge $ind })
    foreach ($k in $keys) { $stack.Remove($k) }
    $stack[$ind] = $ix
}

# наложения только между «текстовыми» контролами: иконки/плитки/бейджи
# по замыслу лежат поверх других, их пропускаем
$texty = @('Label', 'PixelLabel', 'RoundedButton', 'NavButton', 'TextLink', 'TextBox', 'Dropdown', 'IconView', 'CardHeader', 'ModernProgress')

$found = 0
for ($i = 0; $i -lt $rec.Count; $i++) {
    $a = $rec[$i]
    if ($a.Vis -ne 'True') { continue }
    if ($texty -notcontains $a.Type) { continue }
        if ($a.Text -eq '' -or $b.Text -eq '') { continue }
    if ($a.ParIx -lt 0) { continue }
    for ($j = $i + 1; $j -lt $rec.Count; $j++) {
        $b = $rec[$j]
        if ($b.Vis -ne 'True') { continue }
        if ($b.ParIx -ne $a.ParIx) { continue }
        if ($texty -notcontains $b.Type) { continue }
        # один внутри другого (иконка внутри кнопки) — не дефект
        $aInB = ($a.X -ge $b.X -and $a.Y -ge $b.Y -and ($a.X + $a.W) -le ($b.X + $b.W) -and ($a.Y + $a.H) -le ($b.Y + $b.H))
        $bInA = ($b.X -ge $a.X -and $b.Y -ge $a.Y -and ($b.X + $b.W) -le ($a.X + $a.W) -and ($b.Y + $b.H) -le ($a.Y + $a.H))
        if ($aInB -or $bInA) { continue }

        $ox = [Math]::Min($a.X + $a.W, $b.X + $b.W) - [Math]::Max($a.X, $b.X)
        $oy = [Math]::Min($a.Y + $a.H, $b.Y + $b.H) - [Math]::Max($a.Y, $b.Y)
        if ($ox -le 0 -or $oy -le 0) { continue }
        $ov = $ox * $oy
        $small = [Math]::Min($a.W * $a.H, $b.W * $b.H)
        if ($small -le 0) { continue }
        $ratio = $ov / $small
        if ($ratio -ge $MinOverlap) {
            $par = if ($a.ParIx -ge 0) { $rec[$a.ParIx].Type } else { "form" }
            Write-Output ("  {0:P0}  {1} [{2},{3} {4}x{5}] '{6}'" -f $ratio, $a.Type, $a.X, $a.Y, $a.W, $a.H, $a.Text)
            Write-Output ("         x {0} [{1},{2} {3}x{4}] '{5}'   par={6}" -f $b.Type, $b.X, $b.Y, $b.W, $b.H, $b.Text, $par)
            $found++
        }
    }
}
if ($found -eq 0) { Write-Output "  наложений нет" } else { Write-Output ("ВСЕГО наложений: {0}" -f $found) }