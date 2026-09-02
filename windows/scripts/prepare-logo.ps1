# Build crisp launcher assets from intelbyte-logo-source.png
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Src = Join-Path $Root 'launcher\intelbyte-logo-source.png'
$OutPng = Join-Path $Root 'launcher\intelbyte-logo.png'
$OutIco = Join-Path $Root 'launcher\intelbyte.ico'
$MakeIconCs = Join-Path $PSScriptRoot 'MakeIcon.cs'
$MakeIconExe = Join-Path $env:TEMP 'intelbyte-makeicon.exe'

if (-not (Test-Path $Src)) { throw "Missing $Src" }

$Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) { $Csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

& $Csc /nologo /out:$MakeIconExe $MakeIconCs /reference:System.Drawing.dll
if ($LASTEXITCODE -ne 0) { throw 'MakeIcon compile failed' }

& $MakeIconExe $Src $OutIco $OutPng
if ($LASTEXITCODE -ne 0) { throw 'MakeIcon run failed' }

Write-Host "Logo ready: $OutPng, $OutIco"
