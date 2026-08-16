<#
.SYNOPSIS
    一键构建 DeepSeek Harness 免安装便携版（Windows）。

.DESCRIPTION
    从官方渠道获取全部组件并组装出可独立运行的便携目录：
      1) 下载官方 Node.js（node.exe 运行时）
      2) 用 npm 安装官方发布包 @deepseek-ai/dsh（扁平 node_modules，零链接）
      3) 用 Windows 自带 csc 编译启动器（launcher.cs）
      4) 复制文档，组装为便携目录；可选打包 zip

    构建产物可直接拷贝到任意 64 位 Windows 10/11 电脑使用，
    双击「启动 DeepSeek Harness.exe」即可运行（默认端口 3081）。

.PARAMETER NodeVersion
    Node.js 版本，默认 v24.19.0。

.PARAMETER DshVersion
    官方 dsh 包版本，默认 0.1.0-rc.6。

.PARAMETER Registry
    npm 镜像源。国内网络可传 https://registry.npmmirror.com/。

.PARAMETER NodeMirror
    Node.js 下载镜像。国内网络可传 https://npmmirror.com/mirrors/node/。

.PARAMETER OutDir
    输出目录，默认 <脚本目录>\dist。

.PARAMETER Zip
    组装完成后打包为 zip（使用系统自带 tar.exe）。

.EXAMPLE
    # 默认构建（官方源）
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip

.EXAMPLE
    # 国内网络加速构建
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip `
        -Registry "https://registry.npmmirror.com/" `
        -NodeMirror "https://npmmirror.com/mirrors/node/"
#>
param(
    [string]$NodeVersion = "v24.19.0",
    [string]$DshVersion = "0.1.0-rc.6",
    [string]$Registry = "https://registry.npmjs.org/",
    [string]$NodeMirror = "https://nodejs.org/dist/",
    [string]$OutDir = "",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $scriptDir "dist" }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$work = Join-Path $env:TEMP "dsh-build-work"

function Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Ok([string]$msg)   { Write-Host "    $msg" -ForegroundColor Green }

# ── 0. 检查系统要求 ────────────────────────────────────────────────────────
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Host "未找到 Windows 自带 C# 编译器（$csc）。需要 64 位 Windows 10 及以上系统。" -ForegroundColor Red
    exit 1
}
Step "系统检查"
Ok "csc 编译器: $csc"

# ── 1. 下载并解压官方 Node.js ──────────────────────────────────────────────
$nodeZip = Join-Path $work "node.zip"
$nodeRoot = Join-Path $work "node-root"
if (-not (Test-Path (Join-Path $nodeRoot "node.exe"))) {
    Step "下载 Node.js $NodeVersion"
    $nodeUrl = "$NodeMirror$NodeVersion/node-$NodeVersion-win-x64.zip"
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Write-Host "    下载: $nodeUrl"
    curl.exe -L --fail --retry 3 -o $nodeZip $nodeUrl
    if ($LASTEXITCODE -ne 0) { Write-Host "下载 Node.js 失败，请检查网络或 -NodeMirror 参数" -ForegroundColor Red; exit 1 }
    Expand-Archive -Path $nodeZip -DestinationPath $work -Force
    # zip 内是 node-vX.Y.Z-win-x64 子目录，整目录作为 Node 根（含 node.exe 与 npm）
    $inner = Get-ChildItem $work -Directory | Where-Object { $_.Name -like "node-v*-win-x64" } | Select-Object -First 1
    if (-not $inner) { Write-Host "Node.js 解压后未找到可执行文件" -ForegroundColor Red; exit 1 }
    Remove-Item $nodeRoot -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item $inner.FullName $nodeRoot
}
$nodeExeSrc = Join-Path $nodeRoot "node.exe"
Ok "Node.js: $(& $nodeExeSrc --version)"

# ── 2. npm 安装官方 dsh 运行时（扁平 node_modules）──────────────────────────
$nmDir = Join-Path $OutDir "node_modules"
if (-not (Test-Path (Join-Path $nmDir "@deepseek-ai\dsh\lib\bin.js"))) {
    Step "安装官方包 @deepseek-ai/dsh@$DshVersion（npm，扁平安装，可能需要几分钟）"
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    $manifest = @{
        name = "dsh-portable-runtime"
        private = $true
        dependencies = @{ "@deepseek-ai/dsh" = $DshVersion }
    } | ConvertTo-Json
    [System.IO.File]::WriteAllText(
        (Join-Path $OutDir "package.json"),
        $manifest,
        (New-Object System.Text.UTF8Encoding $false)
    )
    # 用刚下载的 Node 自带 npm（node 目录加入 PATH）
    $npm = Join-Path $nodeRoot "npm.cmd"
    $env:PATH = "$nodeRoot;$env:PATH"
    $env:npm_config_registry = $Registry
    $oldCwd = (Get-Location).Path
    Set-Location $OutDir
    try {
        & $npm install --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { Write-Host "npm install 失败，请检查网络或 -Registry 参数" -ForegroundColor Red; exit 1 }
    } finally { Set-Location $oldCwd }
    if (-not (Test-Path (Join-Path $nmDir "@deepseek-ai\dsh\lib\bin.js"))) {
        Write-Host "安装后未找到 @deepseek-ai/dsh 入口，构建中止" -ForegroundColor Red
        exit 1
    }
}
Ok "运行时: $((Get-ChildItem $nmDir -Directory -Force | Measure-Object).Count) 个顶层包（扁平，零链接）"

# ── 3. csc 编译启动器 ──────────────────────────────────────────────────────
$launcherExe = Join-Path $OutDir "启动 DeepSeek Harness.exe"
if (-not (Test-Path $launcherExe)) {
    Step "编译启动器（csc）"
    & $csc /nologo /target:winexe /r:System.Windows.Forms.dll `
        /out:$launcherExe (Join-Path $scriptDir "launcher.cs")
    if ($LASTEXITCODE -ne 0) { Write-Host "启动器编译失败" -ForegroundColor Red; exit 1 }
}
Ok "启动器: $launcherExe"

# ── 4. 组装 ────────────────────────────────────────────────────────────────
Step "组装便携目录"
Copy-Item $nodeExeSrc (Join-Path $OutDir "node.exe") -Force
Copy-Item (Join-Path $scriptDir "LICENSE") $OutDir -Force
Copy-Item (Join-Path $scriptDir "docs\使用说明.txt") $OutDir -Force
if (Test-Path (Join-Path $scriptDir "THIRD_PARTY_NOTICES.md")) {
    Copy-Item (Join-Path $scriptDir "THIRD_PARTY_NOTICES.md") $OutDir -Force
}
Ok "便携目录: $OutDir"
Ok "大小: $([math]::Round((Get-ChildItem $OutDir -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum / 1MB)) MB"

# ── 5. 可选：打包 zip ──────────────────────────────────────────────────────
if ($Zip) {
    Step "打包 zip"
    $zipPath = Join-Path $scriptDir "DeepSeek-Harness-Portable-$DshVersion.zip"
    $parent = Split-Path $OutDir -Parent
    $name = Split-Path $OutDir -Leaf
    tar.exe -a -c -f $zipPath -C $parent $name
    if ($LASTEXITCODE -ne 0) { Write-Host "打包失败" -ForegroundColor Red; exit 1 }
    Ok "zip: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB)) MB)"
}

Write-Host "`n构建完成。把整个输出目录拷贝到任意 64 位 Windows 10/11 电脑，双击「启动 DeepSeek Harness.exe」即可使用（默认端口 3081）。" -ForegroundColor Green
