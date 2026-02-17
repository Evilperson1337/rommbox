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

# Clean output directory before build
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

# Build the project
dotnet build $project -c Release -o $outputRoot

# Validate output assembly
$assemblyPath = Join-Path $outputRoot "RomMbox.dll"
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Build did not produce RomMbox.dll at: $assemblyPath"
}

# Create system directories
New-Item -ItemType Directory -Path $systemDir -Force | Out-Null
New-Item -ItemType Directory -Path $assetsTargetDir -Force | Out-Null

# Copy the default mapping file from a single source
Copy-RequiredFile -Source $mappingSource -Destination $mappingTarget -Label "default-mapping.yaml"

# Copy required assets from the repo assets folder
foreach ($assetFile in $assetFiles) {
    $assetSource = Join-Path $assetsSourceDir $assetFile
    Copy-RequiredFile -Source $assetSource -Destination (Join-Path $assetsTargetDir $assetFile) -Label "asset $assetFile"
}

# Create settings file
"{`"logLevel`": `"Info`"}" | Out-File -FilePath $settingsTarget -Encoding UTF8

# Ensure plugin root output has only expected files
$pluginOutputFiles = @(
    "RomMbox.dll"
)

Get-ChildItem -Path $outputRoot -File | Where-Object { $pluginOutputFiles -notcontains $_.Name } | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
}

# Remove any top-level assets folder from the build output
$outputAssetsDir = Join-Path $outputRoot "assets"
if (Test-Path -LiteralPath $outputAssetsDir) {
    Remove-Item -LiteralPath $outputAssetsDir -Recurse -Force
}

# Clean up unnecessary files
$unnecessaryFiles = @(
    "*.pdb",          # Debug symbols
    "*.deps.json",    # Dependencies file
    "*.xml",          # Documentation files
    "*.log"           # Log files
)

foreach ($pattern in $unnecessaryFiles) {
    $files = Get-ChildItem -Path $outputRoot -Filter $pattern -Recurse -ErrorAction SilentlyContinue
    if ($files) {
        $files | Remove-Item -Force
    }
}

Write-Host "Build completed successfully. Essential files copied to $outputRoot"
Write-Host "Files included:"
Write-Host "  - RomMbox.dll (main plugin assembly)"
Write-Host "  - system/default-mapping.yaml (platform mapping configuration)"
Write-Host "  - system/settings.json (plugin settings)"
Write-Host "  - system/assets/romm.png (Plugin badge)"
Write-Host "  - system/assets/upload.png (Upload Save icon)"
Write-Host "  - system/assets/gaming.png (Play on RomM icon)"
