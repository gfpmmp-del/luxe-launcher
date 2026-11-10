# Снимок одного раздела в отдельный файл: shots/v<view>_<size>.png
# Порядок аргументов: --shot-run <вкладка главной> <view навигации> [WxH]
param(
    [int]$View = 0,
    [string]$Size = "1280x940",
    [int]$Tab = 0,
    [int]$WaitSec = 16
)
$ErrorActionPreference = "SilentlyContinue"
$exe = Join-Path (Get-Location) "Luxe Launcher.exe"
if (-not (Test-Path "shots")) { New-Item -ItemType Directory -Path "shots" | Out-Null }
Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' } | Stop-Process -Force
do { Start-Sleep -Milliseconds 300 } while ((Get-Process | Where-Object { $_.ProcessName -like '*Luxe*' }).Count -gt 0)
Remove-Item shot_test.png -Force
$p = Start-Process -FilePath $exe -ArgumentList @("--shot-run", "$Tab", "$View", $Size) -PassThru -WorkingDirectory (Get-Location)
Start-Sleep -Seconds $WaitSec
$dst = Join-Path "shots" ("v{0}_{1}.png" -f $View, $Size)
if (Test-Path shot_test.png) {
    Copy-Item shot_test.png $dst -Force
    Write-Output ("{0}  <- view={1} tab={2} {3}" -f $dst, $View, $Tab, $Size)
    (Get-Content shot_args.txt -Encoding UTF8 | Select-Object -Skip 1) | ForEach-Object { Write-Output ("  " + $_) }
} else {
    Write-Output "shot_test.png НЕ создан (view=$View)"
}
try { $p | Stop-Process -Force } catch { }