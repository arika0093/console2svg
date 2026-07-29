#Requires -Version 5.1
<#
.SYNOPSIS
    Install console2svg on Windows from GitHub releases.
.DESCRIPTION
    Downloads a release binary (or zip archive for v0.8+) and adds it to PATH.
.EXAMPLE
    irm https://raw.githubusercontent.com/arika0093/console2svg/main/install.ps1 | iex
.EXAMPLE
    $env:CONSOLE2SVG_VERSION = "0.8.0"; .\install.ps1
.EXAMPLE
    $env:CONSOLE2SVG_INSTALL_DIR = "C:\tools\console2svg"; .\install.ps1
#>

$ErrorActionPreference = "Stop"

$Repo = "arika0093/console2svg"
$Version = if ($env:CONSOLE2SVG_VERSION) { $env:CONSOLE2SVG_VERSION } else { "latest" }

# RuntimeInformation.OSArchitecture is unavailable in some Windows PowerShell
# 5.1/.NET Framework combinations. Use the OS architecture (rather than the
# PowerShell process architecture) and retain a compatible fallback.
$Arch = $null
try {
    $RuntimeInformationType = [type]::GetType(
        "System.Runtime.InteropServices.RuntimeInformation, System.Runtime.InteropServices.RuntimeInformation",
        $false
    )
    if ($null -ne $RuntimeInformationType) {
        $Arch = $RuntimeInformationType::OSArchitecture.ToString()
    }
} catch {
    # Fall back below for older .NET Framework installations.
}

if ([string]::IsNullOrWhiteSpace($Arch)) {
    $Arch = if ([Environment]::Is64BitOperatingSystem) { "X64" } else { "X86" }
}

switch ($Arch.ToUpperInvariant()) {
    "X64"   { $RID = "win-x64" }
    "AMD64" { $RID = "win-x64" }
    "ARM64" { $RID = "win-arm64" }
    default { Write-Error "Unsupported architecture: $Arch"; exit 1 }
}

if ($env:CONSOLE2SVG_INSTALL_DIR) {
    $InstallDir = $env:CONSOLE2SVG_INSTALL_DIR
} else {
    $InstallDir = Join-Path $env:LOCALAPPDATA "console2svg"
}

Write-Host "Installing console2svg ($RID) ..."

$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "console2svg-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

try {
    # Try new format (v0.8+): zip archive
    $ArchiveNew = "console2svg-$RID.zip"
    if ($Version -eq "latest") {
        $UrlNew = "https://github.com/$Repo/releases/latest/download/$ArchiveNew"
    } else {
        $UrlNew = "https://github.com/$Repo/releases/download/v$Version/$ArchiveNew"
    }

    $Downloaded = $false
    try {
        Write-Host "Trying new format (v0.8+): $UrlNew ..."
        Invoke-WebRequest -Uri $UrlNew -OutFile (Join-Path $TmpDir $ArchiveNew) -UseBasicParsing
        Expand-Archive -Path (Join-Path $TmpDir $ArchiveNew) -DestinationPath $InstallDir -Force
        $Downloaded = $true
    } catch {
        Write-Host "New format not available, trying old format ..."
    }

    if (-not $Downloaded) {
        # Fallback to old format (v0.7): raw binary
        $BinaryOld = "console2svg-$RID.exe"
        if ($Version -eq "latest") {
            $UrlOld = "https://github.com/$Repo/releases/latest/download/$BinaryOld"
        } else {
            $UrlOld = "https://github.com/$Repo/releases/download/v$Version/$BinaryOld"
        }
        Write-Host "Falling back to old format (v0.7): $UrlOld ..."
        Invoke-WebRequest -Uri $UrlOld -OutFile (Join-Path $InstallDir "console2svg.exe") -UseBasicParsing
    }

    # Add to PATH for current user if not already present
    $UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($UserPath -notlike "*$InstallDir*") {
        [Environment]::SetEnvironmentVariable("Path", "$UserPath;$InstallDir", "User")
        Write-Host "Added $InstallDir to user PATH"
    }
    $env:Path = "$env:Path;$InstallDir"

    Write-Host "console2svg installed to $InstallDir\console2svg.exe"
} finally {
    Remove-Item -Recurse -Force $TmpDir -ErrorAction SilentlyContinue
}
