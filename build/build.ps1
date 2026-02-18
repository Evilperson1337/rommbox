$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$ProgressPreference = "SilentlyContinue"

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required $Label not found at: $Source"
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\RomM.LaunchBoxPlugin\RomMbox.csproj"
$outputRoot = Join-Path $root "output\RomMbox"
$buildRoot = Join-Path $root "output\.build"
$systemDir = Join-Path $outputRoot "system"
$assetsTargetDir = Join-Path $systemDir "assets"
$settingsTarget = Join-Path $systemDir "settings.json"
$mappingTarget = Join-Path $systemDir "default-mapping.yaml"

$mappingSource = Join-Path $root "assets\default-mapping.yaml"
$assetsSourceDir = Join-Path $root "assets\images"
$assetFiles = @(
    "romm.png",
    "upload.png",
    "gaming.png"
)

# Clean output directories before build
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}

# Build the project into a staging directory
dotnet build $project -c Release -o $buildRoot

# Validate output assembly
$assemblyPath = Join-Path $buildRoot "RomMbox.dll"
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Build did not produce RomMbox.dll at: $assemblyPath"
}

# Create output directories
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# Create system directories
New-Item -ItemType Directory -Path $systemDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsTargetDir -Force | Out-Null

# Copy the plugin assembly only
Copy-RequiredFile -Source $assemblyPath -Destination (Join-Path $outputRoot "RomMbox.dll") -Label "RomMbox.dll"

# Copy the default mapping file from a single source
Copy-RequiredFile -Source $mappingSource -Destination $mappingTarget -Label "default-mapping.yaml"

# Copy required assets from the repo assets folder
foreach ($assetFile in $assetFiles) {
    $assetSource = Join-Path $assetsSourceDir $assetFile
    Copy-RequiredFile -Source $assetSource -Destination (Join-Path $assetsTargetDir $assetFile) -Label "asset $assetFile"
}

# Create settings file
"{`"logLevel`": `"Debug`"}" | Out-File -FilePath $settingsTarget -Encoding UTF8

Write-Host "Build completed successfully. Essential files copied to $outputRoot"
Write-Host "Files included:"
Write-Host "  - RomMbox.dll (main plugin assembly)"
Write-Host "  - system/default-mapping.yaml (platform mapping configuration)"
Write-Host "  - system/settings.json (plugin settings)"
Write-Host "  - system/assets/romm.png (Plugin badge)"
Write-Host "  - system/assets/upload.png (Upload Save icon)"
Write-Host "  - system/assets/gaming.png (Play on RomM icon)"

# Cleanup staging directory
if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
