<#
.SYNOPSIS
    Display Dimmer 中文补丁 —— 一键构建脚本（Windows）

.DESCRIPTION
    读取 orig/ 下你自己的官方原版 DLL，依次做两件事：
      1) localize：把 WPF 界面资源（BAML）里的字符串按 dict/dict.tsv 替换
      2) usx      ：把代码里的字符串常量按合并后的三遍词典替换，
                    dict/us_scope.tsv 提供「按方法限定」的替换规则
    产物输出到 out/Display Dimmer.dll

.PARAMETER Rebuild
    强制重新编译 BamlTool（默认只在找不到编译产物时才编译）

.PARAMETER InPlace
    构建完成后，直接把产物覆盖到 app\ 目录（你的绿色版程序目录）

.PARAMETER Dotnet
    手动指定 dotnet.exe 的路径，用于本机没把 dotnet 加进 PATH 的情况

.EXAMPLE
    .\build.ps1
    .\build.ps1 -InPlace
    .\build.ps1 -Rebuild -Dotnet "D:\dotnet8sdk\dotnet.exe"
#>
param(
    [switch]$Rebuild,
    [switch]$InPlace,
    [string]$Dotnet = ""
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
Set-Location $root

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}
function Write-Ok([string]$msg) {
    Write-Host "    $msg" -ForegroundColor Green
}
function Write-Warn([string]$msg) {
    Write-Host "    $msg" -ForegroundColor Yellow
}
function Fail([string]$msg) {
    Write-Host ""
    Write-Host "[出错] $msg" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Display Dimmer 中文补丁 —— 构建工具" -ForegroundColor White
Write-Host "  仓库根目录：$root" -ForegroundColor DarkGray
Write-Host ""

# ---------------------------------------------------------------- 1. 检查原版 DLL
Write-Step "1/5 检查官方原版文件"

$origDll = Join-Path $root 'orig\Display Dimmer.dll'
if (-not (Test-Path -LiteralPath $origDll)) {
    Fail @"
没找到 orig\Display Dimmer.dll

这个仓库不自带原软件，需要你自己放一份官方原版进来。
请先以管理员身份运行 fetch_original.ps1，或者手动从商店安装目录复制：
    C:\Program Files\WindowsApps\NathanLabs.DisplayDimmer_*\Display Dimmer.dll
"@
}
Write-Ok "找到原版 DLL：$origDll"

# BAML 里引用的是程序自己的类型（例如 local:SettingsWindow），BamlLocalizer
# 解析时必须能把程序集真正加载起来，所以要有这份原版基准，并通过 DD_REFDIR 告知工具。
$env:DD_REFDIR = (Resolve-Path -LiteralPath (Join-Path $root 'orig')).Path
Write-Ok "DD_REFDIR = $env:DD_REFDIR"

# ---------------------------------------------------------------- 2. 找 dotnet
Write-Step "2/5 定位 .NET 8 SDK"

if ($Dotnet -and (Test-Path -LiteralPath $Dotnet)) {
    $dotnetExe = (Resolve-Path -LiteralPath $Dotnet).Path
} else {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        $dotnetExe = $cmd.Source
    } else {
        # 常见的免安装 SDK 摆放位置，顺手找一下
        $cands = @(
            "$env:ProgramFiles\dotnet\dotnet.exe",
            'D:\dotnet8sdk\dotnet.exe',
            'C:\dotnet8sdk\dotnet.exe'
        )
        $dotnetExe = $null
        foreach ($c in $cands) {
            if (Test-Path -LiteralPath $c) { $dotnetExe = $c; break }
        }
    }
}

if (-not $dotnetExe) {
    Fail @"
没找到 dotnet。

BamlTool 是 .NET 8 程序，运行它需要 .NET 8 运行时；
如果想现场编译源码，则需要 .NET 8 SDK（用到 WPF，只能在 Windows 上编译）。

两种办法：
  1) 安装 .NET 8 SDK（含运行时）：https://dotnet.microsoft.com/download/dotnet/8.0
  2) 用免安装版，然后用 -Dotnet 参数指过来：
         .\build.ps1 -Dotnet "D:\dotnet8sdk\dotnet.exe"
"@
}

# 免安装 SDK 需要 DOTNET_ROOT，否则找不到运行时
$sdkDir = Split-Path -Parent $dotnetExe
if ($sdkDir) { $env:DOTNET_ROOT = $sdkDir; $env:PATH = "$sdkDir;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$v = & $dotnetExe --version 2>$null
Write-Ok "dotnet = $dotnetExe"
Write-Ok "SDK 版本 = $v"

# ---------------------------------------------------------------- 3. 编译 BamlTool
Write-Step "3/5 准备 BamlTool"

# 两条路：
#   A. 用预编译包 —— 从 Releases 下载 BamlTool-v1.zip，解压到 tools\bin\
#      这样不用装 .NET SDK，有 .NET 8 运行时就够了
#   B. 现场编译 —— 需要 .NET 8 SDK（首次编译要联网拉 NuGet 包 Mono.Cecil）
$prebuilt = Join-Path $root 'tools\bin\BamlTool.dll'
$built    = Join-Path $root 'tools\BamlTool\bin\Release\net8.0-windows\BamlTool.dll'

$btDll = $null

if ((Test-Path -LiteralPath $prebuilt) -and -not $Rebuild) {
    $btDll = $prebuilt
    Write-Ok "使用预编译包：tools\bin\BamlTool.dll"
} elseif ((Test-Path -LiteralPath $built) -and -not $Rebuild) {
    $btDll = $built
    Write-Ok "使用已编译产物（-Rebuild 可强制重编译）"
} else {
    # 编译需要 SDK，运行只需要运行时，这里先确认确实有 SDK
    $sdks = & $dotnetExe --list-sdks 2>$null
    if (-not $sdks) {
        Fail @"
本机只有 .NET 运行时，没有 SDK，无法现场编译 BamlTool。

两个办法：
  1) 从本仓库 Releases 下载 BamlTool-v1.zip，解压到 tools\bin\ 再跑本脚本
  2) 安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0
"@
    }
    Write-Warn "正在编译（首次编译需要联网下载 NuGet 包 Mono.Cecil，请稍候）..."
    $proj = Join-Path $root 'tools\BamlTool\BamlTool.csproj'
    & $dotnetExe build $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Fail "编译失败，退出码 $LASTEXITCODE" }
    $btDll = $built
    Write-Ok "编译完成"
}

if (-not $btDll -or -not (Test-Path -LiteralPath $btDll)) { Fail "找不到 $btDll" }

# ---------------------------------------------------------------- 4. 合并词典
Write-Step "4/5 合并三遍词典"

# 三遍的分工（详见 README）：
#   第一遍 us_dict.tsv   —— 完整的句子 / 标签
#   第二遍 us_dict2.tsv  —— 拼接碎片（"Connected to: " 这种带首尾空格的）
#   第三遍 us_dict3.tsv  —— 第一遍类白名单漏掉的界面类
# 三遍按顺序合并，后面的覆盖前面的。
$enc = New-Object System.Text.UTF8Encoding($false)
$sb  = New-Object System.Text.StringBuilder

foreach ($rel in @('dict\us_dict.tsv', 'dict\us_dict2.tsv', 'dict\us_dict3.tsv')) {
    $p = Join-Path $root $rel
    if (-not (Test-Path -LiteralPath $p)) { Fail "缺少词典文件：$rel" }
    $text = [System.IO.File]::ReadAllText($p, $enc)
    [void]$sb.Append($text)
    # 文件末尾没有换行的话，直接拼接会把两行粘到一起
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { [void]$sb.Append("`n") }
}

$merged = Join-Path $root 'dict\_merged.tsv'
[System.IO.File]::WriteAllText($merged, $sb.ToString(), $enc)
$lines = ($sb.ToString() -split "`n" | Where-Object { $_.Trim().Length -gt 0 }).Count
Write-Ok "合并完成 -> dict\_merged.tsv（$lines 行）"

# ---------------------------------------------------------------- 5. 打补丁
Write-Step "5/5 生成中文版 DLL"

$outDir = Join-Path $root 'out'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$step1 = Join-Path $outDir '_step1.dll'
$final = Join-Path $outDir 'Display Dimmer.dll'

Remove-Item -LiteralPath $step1, $final -Force -ErrorAction SilentlyContinue

Write-Host "    [5.1] BAML 界面资源翻译..." -ForegroundColor DarkGray
& $dotnetExe $btDll localize $origDll (Join-Path $root 'dict\dict.tsv') $step1
if ($LASTEXITCODE -ne 0) { Fail "BAML 翻译失败，退出码 $LASTEXITCODE" }

Write-Host "    [5.2] 代码字符串翻译（含按方法限定替换）..." -ForegroundColor DarkGray
& $dotnetExe $btDll usx $step1 $merged $final (Join-Path $root 'dict\us_scope.tsv')
if ($LASTEXITCODE -ne 0) { Fail "字符串翻译失败，退出码 $LASTEXITCODE" }

Remove-Item -LiteralPath $step1 -Force -ErrorAction SilentlyContinue

if (-not (Test-Path -LiteralPath $final)) { Fail "产物没有生成：$final" }
$size = (Get-Item -LiteralPath $final).Length
Write-Ok "产物：$final（$([math]::Round($size/1KB)) KB）"

# ---------------------------------------------------------------- 可选：原地覆盖
if ($InPlace) {
    $appDll = Join-Path $root 'app\Display Dimmer.dll'
    if (-not (Test-Path -LiteralPath $appDll)) {
        Write-Warn "没找到 app\Display Dimmer.dll，跳过覆盖。"
        Write-Warn "请先把官方整个程序目录复制到 app\ （或运行 fetch_original.ps1）"
    } else {
        $bak = $appDll + '.bak'
        if (-not (Test-Path -LiteralPath $bak)) { Copy-Item -LiteralPath $appDll $bak }
        Copy-Item -LiteralPath $final $appDll -Force
        Write-Ok "已覆盖 app\Display Dimmer.dll（原版备份在 Display Dimmer.dll.bak）"
    }
}

Write-Host ""
Write-Host "  完成！" -ForegroundColor Green
Write-Host ""
if (-not $InPlace) {
    Write-Host "  下一步：把 out\Display Dimmer.dll 覆盖到你自己的程序目录里" -ForegroundColor White
    Write-Host "  （如果已经准备好绿色版目录 app\，直接跑 .\\build.ps1 -InPlace 更省事）" -ForegroundColor DarkGray
    Write-Host ""
}
