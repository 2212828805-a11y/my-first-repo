$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\LooyWindowsController\LooyWindowsController.csproj"
$PublishDirectory = Join-Path $ProjectRoot "dist\win-x64"
$ZipPath = Join-Path $ProjectRoot "dist\LooyWindowsController-win-x64.zip"
$LogPath = Join-Path $ProjectRoot "build.log"
$BuildSucceeded = $false

function Resolve-DotNet {
    $Existing = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($Existing) {
        return $Existing.Source
    }

    $DefaultPath = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $DefaultPath) {
        return $DefaultPath
    }

    $Winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $Winget) {
        throw "未找到 .NET SDK，也无法使用 winget 自动安装。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    Write-Host "未检测到 .NET 8 SDK，正在通过 winget 安装……" -ForegroundColor Yellow
    & winget install --id Microsoft.DotNet.SDK.8 --exact --source winget --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "安装 .NET 8 SDK 失败，请手动安装后重试。"
    }
    if (-not (Test-Path $DefaultPath)) {
        throw "已执行安装，但仍未找到 dotnet.exe。请关闭窗口后重新运行构建脚本。"
    }
    return $DefaultPath
}

try {
    Start-Transcript -Path $LogPath -Force | Out-Null
    Write-Host "开始时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host "项目目录：$ProjectRoot"

    if (-not (Test-Path $ProjectFile)) {
        throw "没有找到项目文件。请确认已经完整解压整个压缩包，而不是在压缩包预览中运行。"
    }

    $DotNet = Resolve-DotNet
    Write-Host "dotnet 路径：$DotNet"
    & $DotNet --info
    Write-Host "正在构建路遥智控……" -ForegroundColor Cyan

    if (Test-Path $PublishDirectory) {
        Remove-Item $PublishDirectory -Recurse -Force
    }
    New-Item $PublishDirectory -ItemType Directory -Force | Out-Null

    & $DotNet publish $ProjectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $PublishDirectory `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 构建失败，错误代码：$LASTEXITCODE"
    }

    $ExecutablePath = Join-Path $PublishDirectory "LooyWindowsController.exe"
    if (-not (Test-Path $ExecutablePath)) {
        throw "构建命令已结束，但没有找到 LooyWindowsController.exe。"
    }

    $ReadmeSource = Join-Path $ProjectRoot "README_CN.md"
    Copy-Item $ReadmeSource (Join-Path $PublishDirectory "使用说明.md") -Force

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }
    Compress-Archive -Path (Join-Path $PublishDirectory "*") -DestinationPath $ZipPath -CompressionLevel Optimal

    Write-Host ""
    Write-Host "构建完成。" -ForegroundColor Green
    Write-Host "可直接运行：$ExecutablePath"
    Write-Host "可发送给用户：$ZipPath"
    $BuildSucceeded = $true
    Start-Process explorer.exe -ArgumentList (Split-Path -Parent $ZipPath)
}
catch {
    Write-Host ""
    Write-Host "构建失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "完整日志：$LogPath" -ForegroundColor Yellow
}
finally {
    try {
        Stop-Transcript | Out-Null
    }
    catch {
        # Transcript may not have started.
    }
}

if (-not $BuildSucceeded) {
    exit 1
}
