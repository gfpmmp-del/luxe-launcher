param(
    [string]$Size = "1024x680",
    [string]$ViewList = "0,1,2,3,4,5"
)
$ErrorActionPreference = "SilentlyContinue"
$exe = Join-Path (Get-Location) "Luxe Launcher.exe"
$views = $ViewList.Split(",") | ForEach-Object { [int]$_.Trim() }
$total = 0
foreach ($v in $views) {
    Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' } | Stop-Process -Force
    do { Start-Sleep -Milliseconds 300 } while ((Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' }).Count -gt 0)
    Remove-Item controls.txt -Force
    # порядок аргументов: --shot-run <вкладка главной> <view навигации>
    $p = Start-Process -FilePath $exe -ArgumentList @("--shot-run", "0", "$v", $Size) -PassThru -WorkingDirectory (Get-Location)
    Start-Sleep -Seconds 15
    Write-Output "################ view=$v  $Size"
    Write-Output "-- геометрия --"
    powershell -ExecutionPolicy Bypass -File check_layout.ps1 2>&1 | Select-Object -Skip 1
    Write-Output "-- наложения --"
    powershell -ExecutionPolicy Bypass -File check_overlap.ps1 2>&1
    try { $p | Stop-Process -Force } catch { }
}