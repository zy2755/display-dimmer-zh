<#
.SYNOPSIS
    从本机已安装的 Microsoft Store 版 Display Dimmer 中取出官方文件

.DESCRIPTION
    做两件事：
      1) 把整个程序目录复制到 app\   —— 这是「绿色版」的运行目录
      2) 把主程序集复制到 orig\      —— 这是构建时用的原版基准（BAML 解析需要）

    本脚本只在你自己的电脑本地复制你已经合法获取的文件，不上传任何东西。
    需要管理员权限（WindowsApps 目录默认不给普通用户访问）。

.EXAMPLE
    .\fetch_original.ps1
#>

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

# ---------------------------------------------------------------- 自动提权
# WindowsApps 目录需要管理员才能读。不是管理员就重新以管理员身份启动自己。
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "需要管理员权限才能读取商店安装目录，正在重新以管理员身份启动..." -ForegroundColor Yellow
    $args = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell -ArgumentList $args -Verb RunAs
    exit
}

Set-Location $root
Write-Host ""
Write-Host "  取回官方原版文件（以管理员身份运行）" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------- 定位商店安装目录
$pkg = Get-AppxPackage -Name 'NathanLabs.DisplayDimmer' -ErrorAction SilentlyContinue

if (-not $pkg) {
    Write-Host "[出错] 本机没有找到 Display Dimmer 的商店安装。" -ForegroundColor Red
    Write-Host ""
    Write-Host "请先在 Microsoft Store 安装官方版（免费）：" -ForegroundColor White
    Write-Host "  https://apps.microsoft.com/detail/9nbwhfcln6cm" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "如果你已经装了但这里没找到，可能是装在了别的账户下。" -ForegroundColor DarkGray
    Write-Host ""
    pause
    exit 1
}

$src = $pkg.InstallLocation
Write-Host "  安装包  ：$($pkg.PackageFullName)" -ForegroundColor DarkGray
Write-Host "  安装位置：$src" -ForegroundColor DarkGray
Write-Host ""

if (-not (Test-Path -LiteralPath $src)) {
    Write-Host "[出错] 安装目录不存在：$src" -ForegroundColor Red
    pause
    exit 1
}

# 主程序集可能在包根目录，也可能在 DisplayDimmer.App\ 子目录（取决于版本）
$dllRel = $null
foreach ($cand in @('Display Dimmer.dll', 'DisplayDimmer.App\Display Dimmer.dll')) {
    if (Test-Path -LiteralPath (Join-Path $src $cand)) { $dllRel = $cand; break }
}

if (-not $dllRel) {
    Write-Host "[出错] 在 $src 下没找到 Display Dimmer.dll" -ForegroundColor Red
    Write-Host "       （这个包的目录结构可能变了，请手动复制到 orig\）" -ForegroundColor DarkGray
    pause
    exit 1
}

# 程序本体所在目录（exe 和 dll 应该在一起）
$appSrc = Split-Path -Parent (Join-Path $src $dllRel)
Write-Host "  程序目录：$appSrc" -ForegroundColor DarkGray
Write-Host ""

# ---------------------------------------------------------------- 1. 复制到 app\
Write-Host "==> 1/2 复制整个程序目录到 app\" -ForegroundColor Cyan

$appDst = Join-Path $root 'app'
if (Test-Path -LiteralPath $appDst) {
    Write-Host "    app\ 已存在，先清空..." -ForegroundColor Yellow
    Remove-Item -LiteralPath $appDst -Recurse -Force
}
New-Item -ItemType Directory -Path $appDst | Out-Null

$copied = 0
$failed = 0
Get-ChildItem -LiteralPath $appSrc -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    $rel = $_.FullName.Substring($appSrc.Length).TrimStart('\')
    $dst = Join-Path $appDst $rel
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) {
        New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    }
    try {
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force -ErrorAction Stop
        $copied++
    } catch {
        $failed++
    }
}

Write-Host "    成功 $copied 个文件" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
if ($failed -gt 0) {
    Write-Host "    跳过 $failed 个（无权限或受保护，一般不影响运行）" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 2. 复制到 orig\
Write-Host ""
Write-Host "==> 2/2 复制原版基准到 orig\" -ForegroundColor Cyan

$origDst = Join-Path $root 'orig'
if (-not (Test-Path -LiteralPath $origDst)) {
    New-Item -ItemType Directory -Path $origDst | Out-Null
}

foreach ($name in @('Display Dimmer.dll', 'Display Dimmer.runtimeconfig.json')) {
    $s = Join-Path $appSrc $name
    if (Test-Path -LiteralPath $s) {
        Copy-Item -LiteralPath $s -Destination (Join-Path $origDst $name) -Force
        Write-Host "    $name" -ForegroundColor Green
    } else {
        Write-Host "    $name（原包里没有，跳过）" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------- 收尾
Write-Host ""
Write-Host "  完成！目录现在是这个样子：" -ForegroundColor Green
Write-Host ""
Write-Host "    orig\  原版基准（构建时读取）"
Write-Host "    app\   绿色版程序目录（打完补丁后从这里启动）"
Write-Host ""
Write-Host "  接下来运行：  .\build.ps1 -InPlace" -ForegroundColor White
Write-Host "  然后双击：    app\Display Dimmer.exe" -ForegroundColor White
Write-Host ""

pause
