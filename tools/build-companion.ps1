# Build the DSH Pet Host (Windows, no .NET SDK required).
#   pwsh -File tools/build-companion.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$fx   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf  = Join-Path $fx 'WPF'
$csc  = Join-Path $fx 'csc.exe'

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc (Windows + .NET Framework 4.x required)" }

Get-Process -Name PetHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 200
$src = Join-Path $root 'companion\src\PetHost.cs'
$bin = Join-Path $root 'companion\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$out = Join-Path $bin 'PetHost.exe'

& $csc /nologo /target:winexe /optimize+ /out:$out `
  /reference:"$wpf\PresentationFramework.dll" `
  /reference:"$wpf\PresentationCore.dll" `
  /reference:"$wpf\WindowsBase.dll" `
  /reference:"$fx\System.Xaml.dll" `
  /reference:"$fx\System.Web.Extensions.dll" `
  $src

if ($LASTEXITCODE -ne 0) { throw "compile failed with exit code $LASTEXITCODE" }
Write-Host "built: $out"

