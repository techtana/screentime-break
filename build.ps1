# Builds bin\ScreenBreak.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK or downloads needed.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$root = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    "/out:$root\bin\ScreenBreak.exe" `
    "/win32manifest:$root\src\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $root\bin\ScreenBreak.exe"
