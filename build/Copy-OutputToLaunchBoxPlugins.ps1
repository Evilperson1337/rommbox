param(
    [string]$SourceDir = "D:\Projects\github.com\rommbox-1\output",
    [string]$TargetDir = "D:\temp\launchbox\Plugins"
)

$pluginName = "RomMbox"
$pluginRoot = Join-Path -Path $TargetDir -ChildPath $pluginName

$files = @(
    "system\default-mapping.yaml",
    "system\assets\upload.png",
    "system\assets\gaming.png",
    "system\assets\romm.png",
    "RomMbox.dll"
)

if (-not (Test-Path -LiteralPath $SourceDir)) {
    throw "Source directory does not exist: $SourceDir"
}

if (-not (Test-Path -LiteralPath $pluginRoot)) {
    New-Item -ItemType Directory -Path $pluginRoot | Out-Null
}

foreach ($file in $files) {
    $sourcePath = Join-Path -Path $SourceDir -ChildPath $file
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing source file: $sourcePath"
    }

    $destinationPath = Join-Path -Path $pluginRoot -ChildPath $file
    $destinationDir = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationDir)) {
        New-Item -ItemType Directory -Path $destinationDir | Out-Null
    }

    try {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }
    catch [System.IO.IOException] {
        Write-Warning "Failed to copy $file because it is in use. Close LaunchBox or unload the plugin and re-run the script."
    }
}

Write-Host "Copied $($files.Count) files to $pluginRoot" -ForegroundColor Green
