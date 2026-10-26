# Builds bin\ScreenBreak.exe and the installer bin\ScreenBreak-Setup.exe with the
# C# compiler that ships with Windows (.NET Framework 4.x). No SDK or downloads needed.
#   .\build.ps1          build app + installer
#   .\build.ps1 -Icon    also regenerate assets\ScreenBreak.ico from tools\IconGen.cs
param([switch]$Icon)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$ico = Join-Path $root 'assets\ScreenBreak.ico'
$manifest = Join-Path $root 'src\app.manifest'
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
New-Item -ItemType Directory -Force $bin | Out-Null

if ($Icon) {
    & $csc /nologo "/out:$bin\IconGen.exe" /r:System.Drawing.dll (Join-Path $root 'tools\IconGen.cs')
    if ($LASTEXITCODE -ne 0) { throw "IconGen build failed" }
    & "$bin\IconGen.exe" $ico
}

$sources = (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
& $csc /nologo /target:winexe /optimize+ "/out:$bin\ScreenBreak.exe" "/win32manifest:$manifest" "/win32icon:$ico" "/resource:$ico,ScreenBreak.ico" @refs $sources
if ($LASTEXITCODE -ne 0) { throw "App build failed" }

# The installer embeds the app exe; its manifest (asInvoker) stops Windows from forcing a UAC prompt on "Setup".
& $csc /nologo /target:winexe /optimize+ "/out:$bin\ScreenBreak-Setup.exe" "/win32manifest:$manifest" "/win32icon:$ico" "/resource:$bin\ScreenBreak.exe,ScreenBreak.exe" @refs (Join-Path $root 'installer\Setup.cs')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }

Write-Host "Built $bin\ScreenBreak.exe"
Write-Host "Built $bin\ScreenBreak-Setup.exe"
