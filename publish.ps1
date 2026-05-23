# ============================================================================
# StudioLog - Build & Package Script
# ============================================================================
# Usage:
#   .\publish.ps1              # Release build (no console window)
#   .\publish.ps1 -Debug       # Debug build (with console window)
#   .\publish.ps1 -Version 2.1 # Set version string
# ============================================================================

param(
    [switch]$Debug,
    [string]$Version = "2.0.1",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

# Paths
$ProjectDir   = $PSScriptRoot
$ProjectFile  = Join-Path $ProjectDir "StudioLog.csproj"
$OutputDir    = Join-Path $ProjectDir "publish"
$PackageDir   = Join-Path $ProjectDir "package"
$AppName      = "StudioLog"
$Configuration = if ($Debug) { "Debug" } else { "Release" }

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  $AppName Build & Package Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Configuration : $Configuration"
Write-Host "  Runtime       : $Runtime"
Write-Host "  Version       : $Version"
Write-Host ""

# ---- Step 1: Clean ----
Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $OutputDir)  { Remove-Item $OutputDir  -Recurse -Force }
if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }

# ---- Step 2: Restore ----
Write-Host "[2/5] Restoring packages..." -ForegroundColor Yellow
dotnet restore $ProjectFile --runtime $Runtime
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# ---- Step 3: Publish ----
Write-Host "[3/5] Publishing $Configuration build..." -ForegroundColor Yellow
$publishArgs = @(
    "publish", $ProjectFile,
    "--configuration", $Configuration,
    "--runtime", $Runtime,
    "--self-contained", "true",
    "--output", $OutputDir,
    "-p:PublishSingleFile=false",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version",
    "-p:FileVersion=$Version"
)

# Trim unused assemblies in Release mode
if (-not $Debug) {
    $publishArgs += "-p:PublishTrimmed=false"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# ---- Step 4: Verify critical files ----
Write-Host "[4/5] Verifying output..." -ForegroundColor Yellow
$requiredFiles = @(
    "StudioLog.exe",
    "libltc.dll",
    "icon.ico",
    "colorbars.png"
)

$missing = @()
foreach ($file in $requiredFiles) {
    $path = Join-Path $OutputDir $file
    if (-not (Test-Path $path)) {
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    Write-Host "WARNING: Missing files: $($missing -join ', ')" -ForegroundColor Red
} else {
    Write-Host "  All required files present" -ForegroundColor Green
}

# Count output files
$fileCount = (Get-ChildItem $OutputDir -File).Count
$folderSize = "{0:N1} MB" -f ((Get-ChildItem $OutputDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "  Files: $fileCount | Size: $folderSize"

# ---- Step 5: Package as ZIP ----
Write-Host "[5/6] Creating portable ZIP..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

$zipName = "$AppName-v$Version-$Runtime.zip"
$zipPath = Join-Path $PackageDir $zipName

Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -Force

$zipSize = "{0:N1} MB" -f ((Get-Item $zipPath).Length / 1MB)
Write-Host "  Portable ZIP: $zipName ($zipSize)"

# ---- Step 6: Build Installer (if Inno Setup is available) ----
Write-Host "[6/6] Building installer..." -ForegroundColor Yellow

$issFile = Join-Path $ProjectDir "installer.iss"
$iscc = $null

# Search for Inno Setup compiler
$searchPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 5\ISCC.exe"
)

foreach ($path in $searchPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if ($iscc -and (Test-Path $issFile)) {
    # Create installer output directory
    $installerDir = Join-Path $ProjectDir "installer"
    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
    
    & $iscc "/DMyAppVersion=$Version" $issFile
    
    if ($LASTEXITCODE -eq 0) {
        $installerFile = Get-ChildItem $installerDir -Filter "*.exe" | Select-Object -First 1
        if ($installerFile) {
            $installerSize = "{0:N1} MB" -f ($installerFile.Length / 1MB)
            Write-Host "  Installer: $($installerFile.Name) ($installerSize)" -ForegroundColor Green
            
            # Copy installer to package folder too
            Copy-Item $installerFile.FullName $PackageDir -Force
        }
    } else {
        Write-Host "  Installer build failed (exit code $LASTEXITCODE)" -ForegroundColor Red
    }
} else {
    if (-not $iscc) {
        Write-Host "  Inno Setup not found — skipping installer" -ForegroundColor DarkYellow
        Write-Host "  Install from: https://jrsoftware.org/isinfo.php" -ForegroundColor DarkYellow
    }
    if (-not (Test-Path $issFile)) {
        Write-Host "  installer.iss not found — skipping installer" -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Publish folder : $OutputDir"
Write-Host "  Portable ZIP   : $zipPath"
Write-Host "  Package folder : $PackageDir"
Write-Host ""
Write-Host "  To run directly: .\publish\StudioLog.exe"
Write-Host ""

# ---- Optional: Show debug console reminder ----
if ($Debug) {
    Write-Host "  NOTE: Debug console will appear at runtime" -ForegroundColor Yellow
    Write-Host ""
}
