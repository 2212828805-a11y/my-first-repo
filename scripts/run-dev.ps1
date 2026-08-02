$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\LooyWindowsController\LooyWindowsController.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
}

dotnet run --project $ProjectFile
