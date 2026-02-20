param(
    [string]$SourceDir = "D:\Projects\github.com\rommbox\output\RomMbox",
    [string]$TargetDir = "D:\temp\launchbox\Plugins"
)

$pluginName = "RomMbox"
$pluginRoot = Join-Path -Path $TargetDir -ChildPath $pluginName

$preserveFiles = @(
    "system\romm.db",
    "system\settings.json"
)
$preserveBackupRoot = Join-Path -Path $TargetDir -ChildPath "${pluginName}_preserve"

if (Test-Path -LiteralPath $pluginRoot) {
    foreach ($file in $preserveFiles) {
        $existingPath = Join-Path -Path $pluginRoot -ChildPath $file
        if (Test-Path -LiteralPath $existingPath) {
            $backupPath = Join-Path -Path $preserveBackupRoot -ChildPath $file
            $backupDir = Split-Path -Parent $backupPath
            if (-not (Test-Path -LiteralPath $backupDir)) {
                New-Item -ItemType Directory -Path $backupDir | Out-Null
            }
            Copy-Item -LiteralPath $existingPath -Destination $backupPath -Force
        }
    }

    Write-Host "Removing existing LaunchBox plugin folder at $pluginRoot"
    Remove-Item -LiteralPath $pluginRoot -Recurse -Force
}

$files = @(
    "system\default-mapping.yaml",
    "system\settings.json",
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

foreach ($file in $preserveFiles) {
    $backupPath = Join-Path -Path $preserveBackupRoot -ChildPath $file
    if (Test-Path -LiteralPath $backupPath) {
        $restorePath = Join-Path -Path $pluginRoot -ChildPath $file
        $restoreDir = Split-Path -Parent $restorePath
        if (-not (Test-Path -LiteralPath $restoreDir)) {
            New-Item -ItemType Directory -Path $restoreDir | Out-Null
        }
        Copy-Item -LiteralPath $backupPath -Destination $restorePath -Force
    }
}

if (Test-Path -LiteralPath $preserveBackupRoot) {
    Remove-Item -LiteralPath $preserveBackupRoot -Recurse -Force
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
