param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true)]
    [string]$MarkerPath
)

$ErrorActionPreference = "Stop"
$resolvedAssembly = [System.IO.Path]::GetFullPath($AssemblyPath)
$resolvedMarker = [System.IO.Path]::GetFullPath($MarkerPath)
$inputDirectory = [System.IO.Path]::GetDirectoryName($resolvedAssembly)
$assemblyName = [System.IO.Path]::GetFileName($resolvedAssembly)
$workDirectory = Join-Path $inputDirectory "looy-obfuscation"
$outputDirectory = Join-Path $workDirectory "protected"
$configPath = Join-Path $workDirectory "obfuscar.xml"

if (-not (Test-Path -LiteralPath $resolvedAssembly -PathType Leaf)) {
    throw "Release assembly does not exist: $resolvedAssembly"
}

if (Test-Path -LiteralPath $workDirectory) {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Escape-XmlAttribute([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

$escapedInput = Escape-XmlAttribute $inputDirectory
$escapedOutput = Escape-XmlAttribute $outputDirectory
$escapedAssembly = Escape-XmlAttribute $resolvedAssembly

$configuration = @"
<?xml version="1.0" encoding="utf-8"?>
<Obfuscator>
  <Var name="InPath" value="$escapedInput" />
  <Var name="OutPath" value="$escapedOutput" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="HideStrings" value="true" />
  <Var name="OptimizeMethods" value="true" />
  <Var name="RenameProperties" value="false" />
  <Var name="RenameEvents" value="false" />
  <Var name="ReuseNames" value="false" />
  <Var name="SuppressIldasm" value="true" />
  <Var name="SkipGenerated" value="true" />
  <AssemblySearchPath path="$escapedInput" />
  <Module file="$escapedAssembly">
    <SkipType name="Looy.WindowsController.ControllerSettings" skipMethods="true" skipFields="true" skipProperties="true" skipEvents="true" />
    <SkipType name="Looy.WindowsController.AppEntry" skipMethods="true" skipFields="true" skipProperties="true" skipEvents="true" />
  </Module>
</Obfuscator>
"@

[System.IO.File]::WriteAllText(
    $configPath,
    $configuration,
    [System.Text.UTF8Encoding]::new($false))

dotnet tool run obfuscar.console -- $configPath
if ($LASTEXITCODE -ne 0) {
    throw "Obfuscar failed with exit code $LASTEXITCODE"
}

$protectedAssembly = Join-Path $outputDirectory $assemblyName
if (-not (Test-Path -LiteralPath $protectedAssembly -PathType Leaf)) {
    throw "Obfuscar did not create the protected assembly: $protectedAssembly"
}

Copy-Item -LiteralPath $protectedAssembly -Destination $resolvedAssembly -Force
$markerDirectory = [System.IO.Path]::GetDirectoryName($resolvedMarker)
if (-not [string]::IsNullOrWhiteSpace($markerDirectory)) {
    [System.IO.Directory]::CreateDirectory($markerDirectory) | Out-Null
}
[System.IO.File]::WriteAllText(
    $resolvedMarker,
    "Obfuscar 2.2.50`n$([DateTimeOffset]::UtcNow.ToString('O'))`n",
    [System.Text.UTF8Encoding]::new($false))
Write-Host "Protected release assembly: $assemblyName"
