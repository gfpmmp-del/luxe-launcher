# Minecraft Offline Launcher - PowerShell only, NO Python needed
# Portable Java auto-download, all files inside this folder
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Windows.Forms | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$BaseDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($BaseDir)) { $BaseDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrEmpty($BaseDir)) { $BaseDir = (Get-Location).Path }
$McDir = Join-Path $BaseDir ".minecraft"
$JdkDir = Join-Path $BaseDir ".jdk"
$ConfigPath = Join-Path $BaseDir "config.json"
$ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
$JavaApiJre = "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse"
$JavaApiJdk = "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jdk/hotspot/normal/eclipse"

function Load-Config {
  $cfg = @{ username = "Steve"; version = ""; ram = "2" }
  if (Test-Path $ConfigPath) {
    try {
      $j = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
      if ($j.username) { $cfg.username = $j.username }
      if ($j.version) { $cfg.version = $j.version }
      if ($j.ram) { $cfg.ram = [string]$j.ram }
    } catch {}
  }
  return $cfg
}

function Save-Config($nick, $ver, $ram) {
  $o = @{ username = $nick; version = $ver; ram = [string]$ram }
  ($o | ConvertTo-Json) | Set-Content $ConfigPath -Encoding UTF8
}

function Get-OfflineUuid($nick) {
  $bytes = [Text.Encoding]::UTF8.GetBytes("OfflinePlayer:" + $nick)
  $md5 = [Security.Cryptography.MD5]::Create()
  $h = $md5.ComputeHash($bytes)
  $md5.Dispose()
  # version 3, variant RFC4122
  $h[6] = ($h[6] -band 0x0F) -bor 0x30
  $h[8] = ($h[8] -band 0x3F) -bor 0x80
  $hex = ($h | ForEach-Object { $_.ToString("x2") }) -join ""
  return $hex.Substring(0,8) + "-" + $hex.Substring(8,4) + "-" + $hex.Substring(12,4) + "-" + $hex.Substring(16,4) + "-" + $hex.Substring(20,12)
}

function Find-LocalJava {
  # 1. portable .jdk in this folder
  if (Test-Path $JdkDir) {
    try {
      $j = Get-ChildItem $JdkDir -Recurse -Filter "java.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($j) { return $j.FullName }
    } catch {}
  }
  # 2. PATH
  try {
    $c = Get-Command java -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
  } catch {}
  # 3. common install paths
  $paths = @(
    "C:\Program Files\Eclipse Adoptium\jdk-21-hotspot\bin\java.exe",
    "C:\Program Files\Eclipse Foundation\jdk-21-hotspot\bin\java.exe",
    "C:\Program Files\Microsoft\jdk-21\bin\java.exe",
    "C:\Program Files\Eclipse Adoptium\jdk-17-hotspot\bin\java.exe",
    "C:\Program Files\Java\jdk-17\bin\java.exe",
    "C:\Program Files\Java\jdk-21\bin\java.exe"
  )
  foreach ($p in $paths) { if (Test-Path $p) { return $p } }
  foreach ($base in @("C:\Program Files\Eclipse Adoptium", "C:\Program Files\Java", "C:\Program Files\Microsoft")) {
    if (Test-Path $base) {
      try {
        $f = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName "bin\java.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($f) { return $f }
      } catch {}
    }
  }
  return $null
}

function Ensure-Java($statusCb) {
  $j = Find-LocalJava
  if ($j) { return $j }
  & $statusCb "Java ne naydena. Kachayu portable Java 21 (~190 MB)..."
  New-Item -ItemType Directory -Force -Path $JdkDir | Out-Null
  $zip = Join-Path $BaseDir ".jdk_download.zip"
  foreach ($url in @($JavaApiJre, $JavaApiJdk)) {
    try {
      & $statusCb "Skachivanie Java: $url"
      (New-Object Net.WebClient).DownloadFile($url, $zip)
      if ((Test-Path $zip) -and ((Get-Item $zip).Length -gt 10MB)) { break }
    } catch {
      & $statusCb "Povtor... $($_.Exception.Message)"
    }
  }
  if (-not (Test-Path $zip)) { throw "Ne udalos skachat Java. Prover internet." }
  & $statusCb "Raspakovka Java..."
  if (Test-Path $JdkDir) { Remove-Item $JdkDir -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory -Force -Path $JdkDir | Out-Null
  # Expand-Archive needs PS5.1 - ok
  Expand-Archive -Path $zip -DestinationPath (Join-Path $BaseDir ".jdk_tmp") -Force
  # find bin/java.exe inside tmp and move up
  $found = Get-ChildItem (Join-Path $BaseDir ".jdk_tmp") -Recurse -Filter "java.exe" | Select-Object -First 1
  if (-not $found) { throw "Java zip raspakovan, no java.exe ne nayden" }
  $root = $found.Directory.Parent.FullName
  # root contains bin/ -> copy content to .jdk
  Get-ChildItem $root | ForEach-Object { Move-Item $_.FullName (Join-Path $JdkDir $_.Name) -Force }
  Remove-Item (Join-Path $BaseDir ".jdk_tmp") -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item $zip -Force -ErrorAction SilentlyContinue
  $j2 = Find-LocalJava
  if (-not $j2) { throw "Java ustanovlena, no java.exe ne nayden v .jdk" }
  return $j2
}

function Get-Json($url) {
  $wc = New-Object Net.WebClient
  $wc.Encoding = [Text.Encoding]::UTF8
  $txt = $wc.DownloadString($url)
  $wc.Dispose()
  return $txt | ConvertFrom-Json
}

function Test-Allowed($rules) {
  if (-not $rules -or $rules.Count -eq 0) { return $true }
  $allowed = $false
  foreach ($r in $rules) {
    $match = $true
    if ($r.os) {
      $match = $false
      $osname = $r.os.name
      if ([string]::IsNullOrEmpty($osname)) { $match = $true }
      elseif ($osname -eq "windows") { $match = $true }
    }
    if ($match) {
      $allowed = ($r.action -eq "allow")
    }
  }
  return $allowed
}

function Download-File($url, $dest) {
  $dir = Split-Path -Parent $dest
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  if (Test-Path $dest) { return }
  $wc = New-Object Net.WebClient
  try { $wc.DownloadFile($url, $dest) }
  finally { $wc.Dispose() }
}

function Get-InstalledVersions {
  $out = @()
  $vdir = Join-Path $McDir "versions"
  if (Test-Path $vdir) {
    foreach ($d in Get-ChildItem $vdir -Directory -ErrorAction SilentlyContinue) {
      $jar = Join-Path $d.FullName ($d.Name + ".jar")
      $json = Join-Path $d.FullName ($d.Name + ".json")
      if ((Test-Path $jar) -and (Test-Path $json)) { $out += $d.Name }
    }
  }
  return $out
}

function Install-Version($versionId, $statusCb, $progCb) {
  New-Item -ItemType Directory -Force -Path $McDir | Out-Null
  & $statusCb "Poluchayu spisok versiy..."
  $manifest = Get-Json $ManifestUrl
  $entry = $manifest.versions | Where-Object { $_.id -eq $versionId } | Select-Object -First 1
  if (-not $entry) { throw "Versiya $versionId ne naydena na Mojang" }
  & $statusCb "Kachayu opisanie $versionId..."
  $vjson = Get-Json $entry.url
  $vdir = Join-Path (Join-Path $McDir "versions") $versionId
  New-Item -ItemType Directory -Force -Path $vdir | Out-Null
  $vjsonPath = Join-Path $vdir ($versionId + ".json")
  ($vjson | ConvertTo-Json -Depth 20) | Set-Content $vjsonPath -Encoding UTF8

  # client jar
  $clientJar = Join-Path $vdir ($versionId + ".jar")
  if (-not (Test-Path $clientJar)) {
    & $statusCb "Kachayu client $versionId..."
    (New-Object Net.WebClient).DownloadFile($vjson.downloads.client.url, $clientJar)
  }

  # libraries
  $libDir = Join-Path $McDir "libraries"
  $nativesDir = Join-Path $vdir "natives"
  New-Item -ItemType Directory -Force -Path $nativesDir | Out-Null
  $libs = $vjson.libraries
  $total = $libs.Count
  $i = 0
  $cp = @()
  foreach ($lib in $libs) {
    $i++
    & $progCb $i $total
    [System.Windows.Forms.Application]::DoEvents()
    if (-not (Test-Allowed $lib.rules)) { continue }
    # natives?
    $nativeKey = $null
    if ($lib.natives -and $lib.natives.windows) { $nativeKey = $lib.natives.windows }
    if ($nativeKey -and $lib.downloads -and $lib.downloads.classifiers -and $lib.downloads.classifiers.$nativeKey) {
      $art = $lib.downloads.classifiers.$nativeKey
      $dest = Join-Path $libDir ($art.path -replace "/", "\")
      try { Download-File $art.url $dest } catch { & $statusCb "Warn native: $($_.Exception.Message)" }
      # extract dlls
      try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
        $zip = [IO.Compression.ZipFile]::OpenRead($dest)
        foreach ($e in $zip.Entries) {
          if ($e.FullName -like "META-INF*") { continue }
          $target = Join-Path $nativesDir ([IO.Path]::GetFileName($e.FullName))
          if ([string]::IsNullOrEmpty($target) -or $e.FullName.EndsWith("/")) { continue }
          [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
        }
        $zip.Dispose()
      } catch {}
      # also add non-native artifact to cp if exists
      if ($lib.downloads.artifact) {
        $a = $lib.downloads.artifact
        $p = Join-Path $libDir ($a.path -replace "/", "\")
        try { Download-File $a.url $p } catch {}
        $cp += $p
      }
      continue
    }
    if ($lib.downloads -and $lib.downloads.artifact) {
      $a = $lib.downloads.artifact
      $p = Join-Path $libDir ($a.path -replace "/", "\")
      if (-not (Test-Path $p)) {
        & $statusCb "Lib [$i/$total] $($a.path)"
        try { Download-File $a.url $p } catch { & $statusCb "Warn lib: $($_.Exception.Message)" }
      }
      $cp += $p
    }
  }
  $cp += $clientJar

  # logging config
  $logArg = $null
  if ($vjson.logging -and $vjson.logging.client -and $vjson.logging.client.file) {
    $lf = $vjson.logging.client.file
    $logDir = Join-Path (Join-Path $McDir "assets") "log_configs"
    $logPath = Join-Path $logDir $lf.id
    try {
      Download-File $lf.url $logPath
      $logArg = $vjson.logging.client.argument -replace '\$\{path\}', $logPath
    } catch {}
  }

  # assets
  $assetsDir = Join-Path $McDir "assets"
  $idxDir = Join-Path $assetsDir "indexes"
  New-Item -ItemType Directory -Force -Path $idxDir | Out-Null
  $assetIndex = $vjson.assetIndex
  $idxPath = Join-Path $idxDir ($assetIndex.id + ".json")
  if (-not (Test-Path $idxPath)) {
    & $statusCb "Kachayu asset index..."
    (New-Object Net.WebClient).DownloadFile($assetIndex.url, $idxPath)
  }
  $idxJson = Get-Content $idxPath -Raw -Encoding UTF8 | ConvertFrom-Json
  $objs = $idxJson.objects.PSObject.Properties
  $t2 = $objs.Count
  $k = 0
  $wc2 = New-Object Net.WebClient
  foreach ($prop in $objs) {
    $k++
    if (($k % 50) -eq 0) {
      & $statusCb "Assets $k/$t2..."
      & $progCb $k $t2
      [System.Windows.Forms.Application]::DoEvents()
    }
    $h = $prop.Value.hash
    $sub = $h.Substring(0,2)
    $objPath = Join-Path (Join-Path (Join-Path $assetsDir "objects") $sub) $h
    if (-not (Test-Path $objPath)) {
      $u = "https://resources.download.minecraft.net/$sub/$h"
      $d = Split-Path -Parent $objPath
      if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
      try { $wc2.DownloadFile($u, $objPath) } catch {}
    }
  }
  $wc2.Dispose()

  return @{ json = $vjson; classpath = $cp; natives = $nativesDir; logArg = $logArg }
}

function Resolve-Placeholders($s, $map) {
  $r = [string]$s
  foreach ($k in $map.Keys) { $r = $r.Replace($k, $map[$k]) }
  return $r
}

function Build-Args($inst, $versionId, $nick, $uuidStr, $ram, $javaExe) {
  $vjson = $inst.json
  $cpList = $inst.classpath
  $natives = $inst.natives
  $sep = ";"
  $cpStr = ($cpList -join $sep)
  $assetsDir = Join-Path $McDir "assets"
  $assetId = $vjson.assetIndex.id
  $map = @{
    '${natives_directory}'   = $natives
    '${launcher_name}'       = "OfflineLauncher"
    '${launcher_version}'    = "1.0"
    '${classpath}'           = $cpStr
    '${classpath_separator}' = $sep
    '${library_directory}'   = (Join-Path $McDir "libraries")
    '${version_name}'        = $versionId
    '${game_directory}'      = $McDir
    '${assets_root}'         = $assetsDir
    '${assets_index_name}'   = $assetId
    '${auth_uuid}'           = $uuidStr
    '${auth_access_token}'   = "0"
    '${auth_player_name}'    = $nick
    '${user_type}'           = "legacy"
    '${version_type}'        = $vjson.type
    '${game_assets}'         = $assetsDir
    '${auth_session}'        = "0"
    '${user_properties}'     = "{}"
  }
  $jvm = @()
  $jvm += "-Xmx${ram}G"
  $jvm += "-Xms1G"
  if ($inst.logArg) { $jvm += (Resolve-Placeholders $inst.logArg $map) }
  $jvm += "-Djava.library.path=`"$natives`""

  $game = @()
  if ($vjson.arguments) {
    foreach ($a in $vjson.arguments.jvm) {
      if ($a -is [string]) { $jvm += (Resolve-Placeholders $a $map) }
      elseif ($a.rules -and (Test-Allowed $a.rules)) {
        $v = $a.value
        if ($v -is [string]) { $jvm += (Resolve-Placeholders $v $map) }
        elseif ($v -is [array]) { foreach ($x in $v) { $jvm += (Resolve-Placeholders $x $map) } }
      }
    }
    foreach ($g in $vjson.arguments.game) {
      if ($g -is [string]) { $game += (Resolve-Placeholders $g $map) }
      elseif ($g.rules -and (Test-Allowed $g.rules)) {
        $v = $g.value
        if ($v -is [string]) { $game += (Resolve-Placeholders $v $map) }
        elseif ($v -is [array]) { foreach ($x in $v) { $game += (Resolve-Placeholders $x $map) } }
      }
    }
  } elseif ($vjson.minecraftArguments) {
    $parts = $vjson.minecraftArguments -split " "
    foreach ($p in $parts) { $game += (Resolve-Placeholders $p $map) }
  }
  $main = $vjson.mainClass
  return @{ jvm = $jvm; main = $main; game = $game; cp = $cpStr }
}

# ================= GUI =================
$cfg = Load-Config
$form = New-Object Windows.Forms.Form
$form.Text = "Minecraft Offline Launcher (bez Python)"
$form.Size = New-Object Drawing.Size(540, 480)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$y = 15
$l1 = New-Object Windows.Forms.Label
$l1.Text = "Minecraft Java - Offline vkhod (bez Python)"
$l1.Font = New-Object Drawing.Font("Segoe UI", 13, [Drawing.FontStyle]::Bold)
$l1.AutoSize = $true
$l1.Location = New-Object Drawing.Point(20, $y)
$form.Controls.Add($l1)
$y += 40

$ln = New-Object Windows.Forms.Label
$ln.Text = "Nik (3-16: a-z 0-9 _):"
$ln.AutoSize = $true
$ln.Location = New-Object Drawing.Point(20, $y)
$form.Controls.Add($ln)
$txtNick = New-Object Windows.Forms.TextBox
$txtNick.Text = $cfg.username
$txtNick.Font = New-Object Drawing.Font("Segoe UI", 11)
$txtNick.Location = New-Object Drawing.Point(200, ($y - 3))
$txtNick.Size = New-Object Drawing.Size(300, 28)
$form.Controls.Add($txtNick)
$y += 38

$lv = New-Object Windows.Forms.Label
$lv.Text = "Versiya:"
$lv.AutoSize = $true
$lv.Location = New-Object Drawing.Point(20, $y)
$form.Controls.Add($lv)
$cmbVer = New-Object Windows.Forms.ComboBox
$cmbVer.DropDownStyle = "DropDownList"
$cmbVer.Location = New-Object Drawing.Point(200, ($y - 3))
$cmbVer.Size = New-Object Drawing.Size(220, 28)
$form.Controls.Add($cmbVer)
$btnRef = New-Object Windows.Forms.Button
$btnRef.Text = "Obnovit"
$btnRef.Location = New-Object Drawing.Point(430, ($y - 4))
$btnRef.Size = New-Object Drawing.Size(70, 28)
$form.Controls.Add($btnRef)
$y += 38

$lr = New-Object Windows.Forms.Label
$lr.Text = "Pamyat (GB):"
$lr.AutoSize = $true
$lr.Location = New-Object Drawing.Point(20, $y)
$form.Controls.Add($lr)
$cmbRam = New-Object Windows.Forms.ComboBox
$cmbRam.DropDownStyle = "DropDownList"
$cmbRam.Items.AddRange(@("1", "2", "4", "6", "8"))
$cmbRam.SelectedItem = $cfg.ram
if (-not $cmbRam.SelectedItem) { $cmbRam.SelectedItem = "2" }
$cmbRam.Location = New-Object Drawing.Point(200, ($y - 3))
$cmbRam.Size = New-Object Drawing.Size(100, 28)
$form.Controls.Add($cmbRam)
$y += 38

$lblJava = New-Object Windows.Forms.Label
$lblJava.Text = "Poisk Java..."
$lblJava.ForeColor = [Drawing.Color]::Gray
$lblJava.AutoSize = $false
$lblJava.Location = New-Object Drawing.Point(20, $y)
$lblJava.Size = New-Object Drawing.Size(480, 34)
$form.Controls.Add($lblJava)
$y += 38

$lblStatus = New-Object Windows.Forms.Label
$lblStatus.Text = "Gotov"
$lblStatus.AutoSize = $false
$lblStatus.Location = New-Object Drawing.Point(20, $y)
$lblStatus.Size = New-Object Drawing.Size(480, 20)
$form.Controls.Add($lblStatus)
$y += 24

$bar = New-Object Windows.Forms.ProgressBar
$bar.Location = New-Object Drawing.Point(20, $y)
$bar.Size = New-Object Drawing.Size(480, 20)
$form.Controls.Add($bar)
$y += 34

$btnPlay = New-Object Windows.Forms.Button
$btnPlay.Text = "IGRAT"
$btnPlay.Font = New-Object Drawing.Font("Segoe UI", 14, [Drawing.FontStyle]::Bold)
$btnPlay.BackColor = [Drawing.Color]::FromArgb(68, 189, 50)
$btnPlay.ForeColor = [Drawing.Color]::White
$btnPlay.FlatStyle = "Flat"
$btnPlay.Location = New-Object Drawing.Point(20, $y)
$btnPlay.Size = New-Object Drawing.Size(480, 50)
$form.Controls.Add($btnPlay)
$y += 60

$lh = New-Object Windows.Forms.Label
$lh.Text = "Offline: parol ne nuzhen. Portable Java kachaetsya sama v papku .jdk.`r`nDlya servera: online-mode=false"
$lh.ForeColor = [Drawing.Color]::Gray
$lh.AutoSize = $false
$lh.Location = New-Object Drawing.Point(20, $y)
$lh.Size = New-Object Drawing.Size(480, 40)
$form.Controls.Add($lh)

$script:javaExe = $null

function Set-Status($s) {
  try { $lblStatus.Text = $s } catch {}
  [System.Windows.Forms.Application]::DoEvents()
}
function Set-Prog($v, $m) {
  try {
    if ($m -gt 0) {
      $bar.Maximum = $m
      if ($v -gt $m) { $v = $m }
      if ($v -lt 0) { $v = 0 }
      $bar.Value = $v
    }
  } catch {}
}

function Load-Versions {
  Set-Status "Poluchayu versii..."
  $installed = Get-InstalledVersions
  $releases = @()
  try {
    $m = Get-Json $ManifestUrl
    $rel = $m.versions | Where-Object { $_.type -eq "release" } | Select-Object -First 30
    $releases = $rel | ForEach-Object { $_.id }
  } catch {
    Set-Status "Net interneta, pokazyvayu ustanovlennye"
  }
  if ($releases.Count -eq 0) { $releases = $installed }
  if ($releases.Count -eq 0) { $releases = @("1.20.1") }
  $cmbVer.Items.Clear()
  foreach ($v in $releases) {
    if ($installed -contains $v) { $cmbVer.Items.Add("$v [ustanovlena]") | Out-Null }
    else { $cmbVer.Items.Add($v) | Out-Null }
  }
  $sel = 0
  if ($cfg.version) {
    for ($i = 0; $i -lt $cmbVer.Items.Count; $i++) {
      if ($cmbVer.Items[$i].ToString().StartsWith($cfg.version)) { $sel = $i; break }
    }
  }
  $cmbVer.SelectedIndex = $sel
  Set-Status "Gotov"
}

$btnRef.Add_Click({ Load-Versions })

# init java label + versions (sync, fast if java exists)
try {
  $j0 = Find-LocalJava
  if ($j0) {
    $script:javaExe = $j0
    $lblJava.Text = "Java: $j0"
    $lblJava.ForeColor = [Drawing.Color]::Green
  } else {
    $lblJava.Text = "Java skachaetsya sama pri pervom zapuske (portable, ustanovka ne nuzhna)"
    $lblJava.ForeColor = [Drawing.Color]::Orange
  }
} catch {}
try { Load-Versions } catch { Set-Status "Oshibka versiy: $($_.Exception.Message)" }

$btnPlay.Add_Click({
  $nick = $txtNick.Text.Trim()
  if ($nick -notmatch "^[a-zA-Z0-9_]{3,16}$") {
    [Windows.Forms.MessageBox]::Show("Nik: 3-16 simvolov a-z 0-9 _", "Oshibka") | Out-Null
    return
  }
  $vdisp = $cmbVer.SelectedItem
  if (-not $vdisp) {
    [Windows.Forms.MessageBox]::Show("Vyberi versiyu", "Oshibka") | Out-Null
    return
  }
  $ver = ([string]$vdisp).Split(" ")[0]
  $ram = $cmbRam.SelectedItem
  if (-not $ram) { $ram = "2" }
  Save-Config $nick $ver $ram
  $btnPlay.Enabled = $false
  $btnPlay.Text = "ZAPUSK..."
  try {
    if (-not $script:javaExe) { $script:javaExe = Find-LocalJava }
    if (-not $script:javaExe) {
      $script:javaExe = Ensure-Java ${function:Set-Status}
      $lblJava.Text = "Java: $($script:javaExe)"
      $lblJava.ForeColor = [Drawing.Color]::Green
    }
    # old versions need Java 8 - warn
    if ($ver -match "^1\.(8|9|10|11|12|13|14|15|16)\.") {
      Set-Status "Vnimanie: starye versii mogut trebovat Java 8/17. Probuyu Java 21..."
    }
    $uuidStr = Get-OfflineUuid $nick
    $inst = Install-Version $ver ${function:Set-Status} ${function:Set-Prog}
    Set-Status "Zapusk Minecraft..."
    $args = Build-Args $inst $ver $nick $uuidStr $ram $script:javaExe
    $cpQ = '"' + $args.cp + '"'
    $allArgs = @()
    $allArgs += $args.jvm
    $allArgs += $args.main
    $allArgs += $args.game
    # build single arg string with quoting
    $argStr = "-cp $cpQ "
    foreach ($a in $allArgs) {
      if ($a -match "\s") { $argStr += '"' + $a + '" ' }
      else { $argStr += $a + " " }
    }
    Set-Status "Minecraft zapushchen! Priyatnoy igry"
    Start-Process -FilePath $script:javaExe -ArgumentList $argStr.Trim() -WorkingDirectory $McDir
  } catch {
    Set-Status "Oshibka: $($_.Exception.Message)"
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, "Oshibka zapuska") | Out-Null
  } finally {
    $btnPlay.Enabled = $true
    $btnPlay.Text = "IGRAT"
    try { $bar.Value = 0 } catch {}
  }
})

[void]$form.ShowDialog()
