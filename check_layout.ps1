# Проверка геометрии по дампу controls.txt (одним проходом, без рекурсии).
# 1) контролы, уходящие за клиентскую область окна
# 2) дети, выходящие за границы своего родителя
param(
    [string]$Dump = "controls.txt"
)

$lines = Get-Content $Dump -Encoding UTF8
$rx = [regex]'^(?<ind>\s*)(?<type>[A-Za-z0-9_]+) \[(?<name>[^\]]*)\] \{X=(?<x>-?\d+),Y=(?<y>-?\d+),Width=(?<w>\d+),Height=(?<h>\d+)\} vis=(?<vis>\w+) hwnd=(?<hwnd>\S+) par=(?<par>\S+)(?<rest>.*)$'

$types   = New-Object System.Collections.ArrayList   # тип
$depthA  = New-Object System.Collections.ArrayList   # отступ
$xs      = New-Object System.Collections.ArrayList
$ys      = New-Object System.Collections.ArrayList
$ws      = New-Object System.Collections.ArrayList
$hs      = New-Object System.Collections.ArrayList
$visA    = New-Object System.Collections.ArrayList
$parA    = New-Object System.Collections.ArrayList
$textA   = New-Object System.Collections.ArrayList
$absX    = New-Object System.Collections.ArrayList
$absY    = New-Object System.Collections.ArrayList
$parIdx  = New-Object System.Collections.ArrayList

# стек индексов родителей по уровням отступа
$stack = @{}
$formW = 0; $formH = 0

foreach ($l in $lines) {
    $m = $rx.Match($l)
    if (-not $m.Success) { continue }
    $ind = [int]($m.Groups['ind'].Value.Length / 2)
    $t = $m.Groups['type'].Value
    $px = [int]$m.Groups['x'].Value
    $py = [int]$m.Groups['y'].Value
    $pw = [int]$m.Groups['w'].Value
    $ph = [int]$m.Groups['h'].Value
    $pv = $m.Groups['vis'].Value
    $pp = $m.Groups['par'].Value
    $pt = $m.Groups['rest'].Value.Trim()

    # родитель = последний записанный элемент с меньшим отступом
    $parentIx = -1
    $d = $ind - 1
    while ($d -ge 0) {
        if ($stack.ContainsKey($d)) { $parentIx = $stack[$d]; break }
        $d--
    }
    $ax = $px; $ay = $py
    if ($parentIx -ge 0) {
        # координаты детей считаются от клиентской области формы,
        # а сама форма в дампе имеет экранные Bounds (X=0,Y=18) — их
        # прибавлять нельзя, иначе всё «съезжает» на высоту заголовка
        if ($parA[$parentIx] -eq 'null') { $ax = $px; $ay = $py }
        else { $ax = $absX[$parentIx] + $px; $ay = $absY[$parentIx] + $py }
    }

    $ix = $types.Count
    [void]$types.Add($t); [void]$depthA.Add($ind)
    [void]$xs.Add($px); [void]$ys.Add($py); [void]$ws.Add($pw); [void]$hs.Add($ph)
    [void]$visA.Add($pv); [void]$parA.Add($pp); [void]$textA.Add($pt)
    [void]$absX.Add($ax); [void]$absY.Add($ay); [void]$parIdx.Add($parentIx)

    if ($pp -eq 'null') { $formW = $pw; $formH = $ph }
    # сбросить более глубокие уровни
    $keys = @($stack.Keys | Where-Object { $_ -ge $ind })
    foreach ($k in $keys) { $stack.Remove($k) }
    $stack[$ind] = $ix
}

Write-Output ("=== FORM {0}x{1}  controls={2} ===" -f $formW, $formH, $types.Count)

# поддерево, отрисованное в скроллящемся контейнере (DarkFlowPanel),
# по определению выходит за его пределы и за окно — это не дефект
$clipped = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $types.Count; $i++) {
    $isClip = $false
    $p = $parIdx[$i]
    while ($p -ge 0) {
        if ($types[$p] -eq 'DarkFlowPanel' -or $types[$p] -eq 'ScrollPanel') { $isClip = $true; break }
        $p = $parIdx[$p]
    }
    [void]$clipped.Add($isClip)
}

$bad = 0
Write-Output "--- 1) за пределы окна (без содержимого скроллов) ---"
for ($i = 0; $i -lt $types.Count; $i++) {
    if ($visA[$i] -ne 'True') { continue }
    if ($parA[$i] -eq 'null') { continue }
    if ($clipped[$i]) { continue }
    $x2 = $absX[$i] + $ws[$i]
    $y2 = $absY[$i] + $hs[$i]
    if ($absX[$i] -lt 0 -or $absY[$i] -lt 0 -or $x2 -gt $formW -or $y2 -gt $formH) {
        $pi = $parIdx[$i]
        $pname = if ($pi -ge 0) { $types[$pi] + "@" + $absX[$pi] + "," + $absY[$pi] + " " + $ws[$pi] + "x" + $hs[$pi] } else { "form" }
        Write-Output ("  {0,-13} abs={1},{2} {3}x{4} right={5} bottom={6} par={7}  {8}" -f $types[$i], $absX[$i], $absY[$i], $ws[$i], $hs[$i], $x2, $y2, $pname, $textA[$i])
        $bad++
    }
}
if ($bad -eq 0) { Write-Output "  нет" }

Write-Output "--- 2) за границы родителя ---"
$bad2 = 0
for ($i = 0; $i -lt $types.Count; $i++) {
    if ($visA[$i] -ne 'True') { continue }
    $p = $parIdx[$i]
    if ($p -lt 0) { continue }
    if ($types[$i] -eq 'LauncherForm') { continue }
# содержимое прокручиваемых панелей законно больше родителя
    if ($types[$p] -eq 'DarkFlowPanel' -or $types[$p] -eq 'ScrollPanel') { continue }
    if ($xs[$i] -lt 0 -or $ys[$i] -lt 0 -or
        ($xs[$i] + $ws[$i]) -gt $ws[$p] -or ($ys[$i] + $hs[$i]) -gt ($hs[$p] + 2)) {
        Write-Output ("  {0,-13} in {1}({2}x{3}) at {4},{5} {6}x{7} right={8} bottom={9}  {10}" -f `
            $types[$i], $types[$p], $ws[$p], $hs[$p], $xs[$i], $ys[$i], $ws[$i], $hs[$i], `
            ($xs[$i] + $ws[$i]), ($ys[$i] + $hs[$i]), $textA[$i])
        $bad2++
    }
}
if ($bad2 -eq 0) { Write-Output "  нет" }
Write-Output ("ИТОГО: за окном={0}, за родителем={1}" -f $bad, $bad2)