param(
    [string]$Size = "1024x680",
    [string]$ViewList = "0,1,2,3,5"
)
$ErrorActionPreference = "SilentlyContinue"
$exe = Join-Path (Get-Location) "Luxe Launcher.exe"
$views = $ViewList.Split(",") | ForEach-Object { [int]$_.Trim() }
foreach ($v in $views) {
    Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' } | Stop-Process -Force
    do { Start-Sleep -Milliseconds 400 } while ((Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' }).Count -gt 0)
    Remove-Item controls.txt, shot_args.txt -Force
    # порядок аргументов: --shot-run <вкладка главной> <view навигации>. Раньше
    # было "$v","0" — то есть вкладка=$v, а раздел всегда 0 (главная).
    $p = Start-Process -FilePath $exe -ArgumentList @("--shot-run", "0", "$v", $Size) -PassThru -WorkingDirectory (Get-Location)
    Start-Sleep -Seconds 15
    "################ view=$v  $Size   args=$(Get-Content shot_args.txt -Encoding UTF8 | Select-Object -First 1)"
    if (Test-Path controls.txt) {
        powershell -ExecutionPolicy Bypass -File check_layout.ps1 2>&1 | Select-Object -Skip 1
    } else { "  controls.txt НЕ создан" }
    try { $p | Stop-Process -Force } catch { }
}