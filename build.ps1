# ============================================================
#  Сборка Luxe Launcher из исходников
#
#  Обычная сборка:        build.ps1
#  ПОДАРОЧНЫЙ билд:      build.ps1 -Gift
#                         (надпись в окне "Артемка с др",
#                          при запуске экран поздравления)
#
#  Либо просто запусти сборка_подарок.bat
# ============================================================

param([switch]$Gift)

$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $proj "Luxe Launcher.exe"
if ($Gift) { $exe = Join-Path $proj "Luxe Launcher (подарок Артему).exe" }
$ico  = Join-Path $proj "assets\art\logo_v3.ico"
$png  = Join-Path $proj "assets\art\logo_v3.png"
$fontsDir = Join-Path $proj "fonts"
$photo = Join-Path $proj "assets\artem.jpg"

$sources = @("Platform.cs", "Launcher.cs", "Theme.cs", "Chrome.cs", "Artem.cs", "ArtemFlow.cs", "Auth.cs", "Skin.cs", "Skin3D.cs", "Social.cs", "Together.cs", "CapeMod.cs", "PlayerSkin.cs", "SitFigure.cs", "Settings.cs", "AiClient.cs", "WebSearch.cs", "Assistant.cs")
foreach ($s in $sources) {
    if (-not (Test-Path (Join-Path $proj $s))) {
        Write-Host "НЕТ ФАЙЛА ИСХОДНИКА: $s" -ForegroundColor Red; exit 1
    }
}

# --- шрифты Inter (OFL 1.1) вшиваются в exe, внешних файлов не нужно ---
$need = @("Inter-Regular.ttf","Inter-Medium.ttf","Inter-SemiBold.ttf","Inter-Bold.ttf")
$missing = @()
foreach ($n in $need) { if (-not (Test-Path (Join-Path $fontsDir $n))) { $missing += $n } }
if ($missing.Count -gt 0) {
    Write-Host "НЕТ ШРИФТОВ в папке fonts\: $($missing -join ', ')" -ForegroundColor Red
    Write-Host "Скачай Inter 4.1 и положи ttf в .\fonts\" -ForegroundColor Yellow
    exit 1
}
if ($Gift -and -not (Test-Path $photo)) {
    Write-Host "НЕТ ФОТО: .\assets\artem.jpg" -ForegroundColor Red
    exit 1
}

# --- если лаунчер запущен, exe не перезапишется ---
# имя процесса у подарочной сборки другое, поэтому ищем оба
$running = @()
foreach ($n in @("Luxe Launcher", "Luxe Launcher (подарок Артему)")) {
    $running += Get-Process -Name $n -ErrorAction SilentlyContinue
}
if ($running.Count -gt 0) {
    Write-Host "Лаунчер сейчас запущен - закрой его и запусти сборку снова." -ForegroundColor Red
    exit 1
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = (Get-ChildItem "C:\Windows\Microsoft.NET\Framework*\v4.0.30319\csc.exe" |
            Select-Object -First 1).FullName
}

$tmp = Join-Path $env:TEMP ("luxebuild_" + [Guid]::NewGuid().ToString("N").Substring(0,6))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

# csc не понимает кириллицу в пути - собираем во временной латинской папке
foreach ($s in $sources) { Copy-Item (Join-Path $proj $s) (Join-Path $tmp $s) -Force }
Copy-Item $ico (Join-Path $tmp "logo.ico") -Force
Copy-Item $png (Join-Path $tmp "logo.png") -Force
# фото-пейзажи (assets\art\p_*.jpg) вшиваются ресурсами:
# имена без расширения — так их ищет Art.Photo().
$artDir = Join-Path $proj "assets\art"
if (Test-Path $artDir) {
    foreach ($f in Get-ChildItem (Join-Path $artDir "p_*.jpg")) {
        Copy-Item $f.FullName (Join-Path $tmp $f.Name) -Force
    }
}
if ($Gift) { Copy-Item $photo (Join-Path $tmp "artem.jpg") -Force }

$defs = @()
if ($Gift) { $defs = @("/define:ARTYOM") }

$cscArgs = @(
  "/nologo", "/t:winexe"
) + $defs + @(
  "/out:`"$tmp\Luxe.exe`"",
  "/win32icon:`"$tmp\logo.ico`"",
  "/resource:`"$tmp\logo.png`",logo.png",
  "/resource:`"$tmp\logo.ico`",logo.ico",
  "/resource:`"$fontsDir\Inter-Regular.ttf`",Inter-Regular.ttf",
  "/resource:`"$fontsDir\Inter-Medium.ttf`",Inter-Medium.ttf",
  "/resource:`"$fontsDir\Inter-SemiBold.ttf`",Inter-SemiBold.ttf",
  "/resource:`"$fontsDir\Inter-Bold.ttf`",Inter-Bold.ttf",
  "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll",
  "/r:System.Web.Extensions.dll", "/r:System.IO.Compression.dll",
  "/r:System.IO.Compression.FileSystem.dll"
)
# ресурсы фото: имя ресурса = имя файла (копии лежат в $tmp —
# csc не понимает кириллицу в пути)
if (Test-Path $artDir) {
    foreach ($f in Get-ChildItem (Join-Path $artDir "p_*.jpg")) {
        $cscArgs += "/resource:`"$(Join-Path $tmp $f.Name)`",$($f.Name)"
    }
}
if ($Gift) {
    $cscArgs += "/resource:`"$tmp\artem.jpg`",artem.jpg"
} 
# --- звуки подарка (необязательные) ---
# gift1 = поздравление, gift2 = после «буду пользоваться», gift3 = запуск версии.
# Достаточно положить файл в assets\gift1.wav (или .mp3) — он вшивается в exe.
# Файла нет — звук просто молчит, сборка от этого не падает.
$soundAliases = @(
    @("gift1", "greeting"),
    @("gift2", "thanks"),
    @("gift3", "launch")
)
$sounds = @()
if ($Gift) {
    foreach ($names in $soundAliases) {
        $hit = $null
        foreach ($base in $names) {
            if ($hit) { break }
            foreach ($ext in @(".wav", ".mp3")) {
                $f = Join-Path $proj ("assets\" + $base + $ext)
                if (Test-Path $f) { $hit = @($f, ($base + $ext)); break }
            }
        }
        if ($hit) { $sounds += ,$hit }
    }
    foreach ($s in $sounds) {
        $kb = [math]::Round((Get-Item $s[0]).Length / 1KB)
        Write-Host ("Звук подарка: " + (Split-Path -Leaf $s[0]) + "  ($kb KB)") -ForegroundColor DarkGray
    }
    foreach ($s in $sounds) { $cscArgs += "/resource:`"$($s[0])`",$($s[1])" }
}
foreach ($s in $sources) { $cscArgs += "`"$tmp\$s`"" }

Write-Host "Компиляция..." -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "ОШИБКА СБОРКИ (код $LASTEXITCODE)" -ForegroundColor Red
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

Copy-Item (Join-Path $tmp "Luxe.exe") $exe -Force
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

$sz = [math]::Round((Get-Item $exe).Length / 1KB)
Write-Host ""
if ($Gift) {
    Write-Host "ПОДАРОЧНЫЙ БИЛД: $exe  ($sz KB)" -ForegroundColor Green
    Write-Host "Надпись в окне: Артемка с др" -ForegroundColor DarkGray
} else {
    Write-Host "Готово: $exe  ($sz KB)" -ForegroundColor Green
    Write-Host "Проверка:  &`"$exe`" --check" -ForegroundColor DarkGray
}