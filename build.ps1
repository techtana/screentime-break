# Builds bin\ScreenBreak.exe and the installer bin\ScreenBreak-Setup.exe with the
# C# compiler that ships with Windows (.NET Framework 4.x). No SDK or downloads needed.
#   .\build.ps1                         build app + installer
#   .\build.ps1 -Icon                   also regenerate assets\ScreenBreak.ico from tools\IconGen.cs
#   .\build.ps1 -CertThumbprint <hash>  also code-sign both exes (needs signtool from the Windows SDK
#                                       and a code-signing certificate in your certificate store)
param([switch]$Icon, [string]$CertThumbprint)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$ico = Join-Path $root 'assets\ScreenBreak.ico'
$manifest = Join-Path $root 'src\app.manifest'
$defaults = Join-Path $root 'config\defaults.ini'   # default settings, embedded in the exe
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
New-Item -ItemType Directory -Force $bin | Out-Null

function Sign([string]$file) {
    if (-not $CertThumbprint) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK." }
    & $signtool.FullName sign /sha1 $CertThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw "Signing $file failed" }
}

if ($Icon) {
    & $csc /nologo "/out:$bin\IconGen.exe" /r:System.Drawing.dll (Join-Path $root 'tools\IconGen.cs')
    if ($LASTEXITCODE -ne 0) { throw "IconGen build failed" }
    & "$bin\IconGen.exe" $ico
    Remove-Item "$bin\IconGen.exe"
}

$sources = (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
& $csc /nologo /target:winexe /optimize+ "/out:$bin\ScreenBreak.exe" "/win32manifest:$manifest" "/win32icon:$ico" "/resource:$ico,ScreenBreak.ico" "/resource:$defaults,defaults.ini" @refs $sources
if ($LASTEXITCODE -ne 0) { throw "App build failed" }
# Sign the app before it is embedded, so the copy the installer writes out is signed too.
Sign "$bin\ScreenBreak.exe"

# The installer embeds the app exe; its manifest (asInvoker) stops Windows from forcing a UAC prompt on "Setup".
& $csc /nologo /target:winexe /optimize+ "/out:$bin\ScreenBreak-Setup.exe" "/win32manifest:$manifest" "/win32icon:$ico" "/resource:$bin\ScreenBreak.exe,ScreenBreak.exe" @refs (Join-Path $root 'installer\Setup.cs')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Sign "$bin\ScreenBreak-Setup.exe"

# Checksums let people confirm a download wasn't tampered with.
Get-FileHash "$bin\ScreenBreak.exe", "$bin\ScreenBreak-Setup.exe" -Algorithm SHA256 |
    ForEach-Object { "{0}  {1}" -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) } |
    Set-Content "$bin\SHA256SUMS.txt" -Encoding ascii

Write-Host "Built $bin\ScreenBreak.exe"
Write-Host "Built $bin\ScreenBreak-Setup.exe"
if (-not $CertThumbprint) { Write-Host "Not signed (pass -CertThumbprint to sign)." }
