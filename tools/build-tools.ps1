# 编译 tools/ 下的 QA 小工具（Windows 自带 .NET Framework 编译器，不需要 SDK）。
#   pwsh tools/build-tools.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$fx   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fx 'csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc (Windows + .NET Framework 4.x required)"
}

$tools = @(
    @{ Name = 'screenshot';  Ref = @() },   # DPI 感知截屏
    @{ Name = 'nchittest';   Ref = @() },   # 逐像素命中测试
    @{ Name = 'setcursor';   Ref = @() },   # 移动/读取光标
    @{ Name = 'mouse-drag';  Ref = @() }    # 真实鼠标拖拽 / 单击
)

foreach ($tool in $tools) {
    $src = Join-Path $PSScriptRoot ($tool.Name + '.cs')
    $out = Join-Path $PSScriptRoot ($tool.Name + '.exe')
    if (-not (Test-Path $src)) { Write-Warning "skip missing $($tool.Name).cs"; continue }
    & $csc /nologo /target:exe /optimize+ "/out:$out" @($tool.Ref) $src
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $($tool.Name)" }
    Write-Host "built: $out"
}
