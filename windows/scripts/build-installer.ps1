# Build a single-file IntelByte-Setup.exe installer (embedded payload + GUI).
# Run: powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1

$ErrorActionPreference = 'Stop'
$WindowsRoot = Split-Path $PSScriptRoot -Parent

# 1) Build portable app payload first
& (Join-Path $WindowsRoot 'scripts\build-release.ps1')

$DistRoot = Join-Path $WindowsRoot 'dist\IntelByte'
$PayloadZip = Join-Path $WindowsRoot 'dist\payload.zip'
$InstallerExe = Join-Path $WindowsRoot 'dist\IntelByte-Setup.exe'
$DesktopExe = Join-Path ([Environment]::GetFolderPath('Desktop')) 'IntelByte-Setup.exe'

if (Test-Path $PayloadZip) { Remove-Item $PayloadZip -Force }
# Flat zip: IntelByte.exe, app/, node/ at archive root (no extra folder)
Compress-Archive -Path (Join-Path $DistRoot '*') -DestinationPath $PayloadZip -Force

$Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) {
  $Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $Csc)) {
  throw 'csc.exe not found. Install .NET Framework.'
}

$refs = @(
  '/reference:System.dll',
  '/reference:System.Drawing.dll',
  '/reference:System.Windows.Forms.dll'
)

$src = @(
  (Join-Path $WindowsRoot 'installer\Program.cs'),
  (Join-Path $WindowsRoot 'installer\InstallerForm.cs')
)

& $Csc /nologo /target:winexe /platform:anycpu /out:$InstallerExe `
  $refs `
  "/resource:$PayloadZip,IntelByteSetup.Payload.zip" `
  $src

if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed' }

Copy-Item $InstallerExe $DesktopExe -Force
$sizeMb = [math]::Round((Get-Item $DesktopExe).Length / 1MB, 1)

Write-Host ''
Write-Host 'Installer ready!'
Write-Host "  $InstallerExe"
Write-Host "  $DesktopExe  ($sizeMb MB)"
Write-Host ''
Write-Host 'Double-click IntelByte-Setup.exe on your desktop to install.'
