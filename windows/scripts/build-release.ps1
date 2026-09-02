# Build IntelByte Windows portable release (IntelByte.exe + bundled Node).
# Run from the windows/ folder:  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1

$ErrorActionPreference = 'Stop'
$WindowsRoot = Split-Path $PSScriptRoot -Parent
$DistRoot = Join-Path $WindowsRoot 'dist\IntelByte'
$AppDir = Join-Path $DistRoot 'app'
$NodeDir = Join-Path $DistRoot 'node'
$ExePath = Join-Path $DistRoot 'IntelByte.exe'
$ZipPath = Join-Path $WindowsRoot 'dist\IntelByte-Windows.zip'

Write-Host 'Building IntelByte Windows release...'

if (Test-Path $DistRoot) { Remove-Item $DistRoot -Recurse -Force }
New-Item -ItemType Directory -Path $AppDir -Force | Out-Null
New-Item -ItemType Directory -Path $NodeDir -Force | Out-Null

# Copy app (no dev junk)
$copyItems = @('bin', 'src', 'scripts', 'package.json', 'package-lock.json', 'README.md', 'LICENSE')
foreach ($item in $copyItems) {
  $src = Join-Path $WindowsRoot $item
  if (Test-Path $src) {
    Copy-Item $src (Join-Path $AppDir $item) -Recurse -Force
  }
}

Push-Location $AppDir
npm ci --omit=dev 2>&1 | Out-Host
Pop-Location

# Download portable Node LTS if missing locally
$NodeVersion = 'v22.14.0'
$NodeZip = Join-Path $env:TEMP "node-$NodeVersion-win-x64.zip"
$NodeUrl = "https://nodejs.org/dist/$NodeVersion/node-$NodeVersion-win-x64.zip"
if (-not (Test-Path $NodeZip)) {
  Write-Host "Downloading Node $NodeVersion..."
  Invoke-WebRequest -Uri $NodeUrl -OutFile $NodeZip -UseBasicParsing
}
$NodeExtract = Join-Path $env:TEMP "node-$NodeVersion-win-x64"
$bundledNode = Join-Path $NodeExtract 'node.exe'
if (-not (Test-Path $bundledNode)) {
  Expand-Archive -Path $NodeZip -DestinationPath $env:TEMP -Force
}
if (-not (Test-Path $bundledNode)) {
  $found = Get-ChildItem $NodeExtract -Recurse -Filter node.exe -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($found) { $bundledNode = $found.FullName }
}
if (-not (Test-Path $bundledNode)) { throw "node.exe not found after extracting $NodeZip" }
Copy-Item $bundledNode (Join-Path $NodeDir 'node.exe') -Force

# Package launcher as IntelByte.exe (C# — no Node/pkg needed on build machine)
& (Join-Path $WindowsRoot 'scripts\prepare-logo.ps1')
$Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) {
  $Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $Csc)) {
  throw 'csc.exe not found. Install .NET Framework SDK or Visual Studio Build Tools.'
}
$LauncherIco = Join-Path $WindowsRoot 'launcher\intelbyte.ico'
$LauncherPng = Join-Path $WindowsRoot 'launcher\intelbyte-logo.png'
$LauncherFont = Join-Path $WindowsRoot 'launcher\SpecialElite-Regular.ttf'
$LauncherManifest = Join-Path $WindowsRoot 'launcher\intelbyte.manifest'
$LauncherSrc = @(Get-ChildItem (Join-Path $WindowsRoot 'launcher\*.cs') | ForEach-Object { $_.FullName })
& $Csc /nologo /optimize /target:winexe /platform:anycpu /out:$ExePath `
  /reference:System.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Core.dll `
  "/win32icon:$LauncherIco" `
  "/win32manifest:$LauncherManifest" `
  "/resource:$LauncherIco,intelbyte.ico" `
  "/resource:$LauncherPng,intelbyte-logo.png" `
  "/resource:$LauncherFont,SpecialElite-Regular.ttf" `
  $LauncherSrc
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile IntelByte.exe' }

# Zip for distribution
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $DistRoot -DestinationPath $ZipPath -Force

Write-Host ''
Write-Host 'Done!'
Write-Host "  Folder: $DistRoot"
Write-Host "  Zip:    $ZipPath"
Write-Host ''
Write-Host 'Usage:'
Write-Host '  IntelByte.exe help'
Write-Host '  IntelByte.exe protect-mail you@example.com'
Write-Host '  IntelByte.exe setup'
Write-Host '  IntelByte.exe install'
