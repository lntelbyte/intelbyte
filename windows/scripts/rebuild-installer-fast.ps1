# Fast rebuild: recompile launcher + installer from current source, reusing the
# already-populated dist\IntelByte (node_modules + node). No npm ci / no download.
$ErrorActionPreference = 'Stop'
$WindowsRoot = Split-Path $PSScriptRoot -Parent
$DistRoot    = Join-Path $WindowsRoot 'dist\IntelByte'
$ExePath     = Join-Path $DistRoot 'IntelByte.exe'
$PayloadZip  = Join-Path $WindowsRoot 'dist\payload.zip'
$InstallerExe= Join-Path $WindowsRoot 'dist\IntelByte-Setup.exe'
$DesktopExe  = Join-Path ([Environment]::GetFolderPath('Desktop')) 'IntelByte-Setup.exe'

$Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) { $Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $Csc)) { throw 'csc.exe not found.' }

Write-Host '1/5  Preparing logo assets...'
& (Join-Path $WindowsRoot 'scripts\prepare-logo.ps1')
$Font = Join-Path $WindowsRoot 'launcher\SpecialElite-Regular.ttf'
if (-not (Test-Path $Font)) {
  Write-Host '      Downloading Special Elite font...'
  curl.exe -L -o $Font 'https://github.com/google/fonts/raw/main/apache/specialelite/SpecialElite-Regular.ttf'
}
if (-not (Test-Path $Font)) { throw 'Missing detective font: launcher\SpecialElite-Regular.ttf' }
$Man = Join-Path $WindowsRoot 'launcher\intelbyte.manifest'
Write-Host '2/5  Compiling launcher (IntelByte.exe) from current source...'
$Ico = Join-Path $WindowsRoot 'launcher\intelbyte.ico'
$Png = Join-Path $WindowsRoot 'launcher\intelbyte-logo.png'
$LauncherSrc = @(Get-ChildItem (Join-Path $WindowsRoot 'launcher\*.cs') | ForEach-Object { $_.FullName })
& $Csc /nologo /optimize /target:winexe /platform:anycpu /out:$ExePath `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  /reference:System.Core.dll `
  "/win32icon:$Ico" `
  "/win32manifest:$Man" `
  "/resource:$Ico,intelbyte.ico" `
  "/resource:$Png,intelbyte-logo.png" `
  "/resource:$Font,SpecialElite-Regular.ttf" `
  $LauncherSrc
if ($LASTEXITCODE -ne 0) { throw 'launcher compile failed' }
Write-Host ("      launcher size: {0} bytes" -f (Get-Item $ExePath).Length)

Write-Host '3/5  Rebuilding payload.zip (flat: IntelByte.exe, app\, node\ at root)...'
if (Test-Path $PayloadZip) { Remove-Item $PayloadZip -Force }
Compress-Archive -Path (Join-Path $DistRoot '*') -DestinationPath $PayloadZip -Force

Write-Host '4/5  Compiling IntelByte-Setup.exe (embedding new payload)...'
& $Csc /nologo /optimize /target:winexe /platform:anycpu /out:$InstallerExe `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  "/win32icon:$Ico" `
  "/resource:$PayloadZip,IntelByteSetup.Payload.zip" `
  (Join-Path $WindowsRoot 'installer\Program.cs') `
  (Join-Path $WindowsRoot 'installer\InstallerForm.cs') `
  (Join-Path $WindowsRoot 'installer\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'installer compile failed' }

Write-Host '5/5  Copying to Desktop...'
Copy-Item $InstallerExe $DesktopExe -Force
$sizeMb = [math]::Round((Get-Item $DesktopExe).Length / 1MB, 1)
Write-Host ''
Write-Host ("Done. IntelByte-Setup.exe  ({0} MB)  ->  {1}" -f $sizeMb, $DesktopExe)
