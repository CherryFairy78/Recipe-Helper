[CmdletBinding()]
param(
    [string]$XivLauncherRoot = (Join-Path $env:APPDATA "XIVLauncher"),
    [switch]$RemoveDevPlugin,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$devPluginRoot = Join-Path $XivLauncherRoot "devPlugins\\RecipeHelper"
$backupRoot = Join-Path $XivLauncherRoot "backups\\RecipeHelper"

function Get-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile
    )

    if (!(Test-Path -LiteralPath $ProjectFile)) {
        return $null
    }

    [xml]$projectXml = Get-Content -LiteralPath $ProjectFile
    return $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
}

function Get-ManifestVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestFile
    )

    if (!(Test-Path -LiteralPath $ManifestFile)) {
        return $null
    }

    return (Get-Content -LiteralPath $ManifestFile -Raw | ConvertFrom-Json).AssemblyVersion
}

function Assert-VersionParity {
    $projectVersion = Get-ProjectVersion (Join-Path $projectRoot "DalamudRecipeHelper.csproj")
    $rootManifestVersion = Get-ManifestVersion (Join-Path $projectRoot "DalamudRecipeHelper.json")
    $debugManifestVersion = Get-ManifestVersion (Join-Path $projectRoot "bin\\Debug\\DalamudRecipeHelper.json")
    $releaseManifestVersion = Get-ManifestVersion (Join-Path $projectRoot "bin\\Release\\DalamudRecipeHelper.json")
    $devManifestVersion = Get-ManifestVersion (Join-Path $devPluginRoot "Debug\\DalamudRecipeHelper.json")

    Write-Host "Version parity:"
    Write-Host "  Project:        $projectVersion"
    Write-Host "  Root manifest:  $rootManifestVersion"
    Write-Host "  Debug manifest: $debugManifestVersion"
    Write-Host "  Release manifest: $releaseManifestVersion"
    if ($null -ne $devManifestVersion) {
        Write-Host "  Dev plugin:     $devManifestVersion"
    }

    $mismatches = @()

    if ($null -eq $projectVersion) {
        $mismatches += "project version is unavailable"
    }

    foreach ($pair in @(
            @{ Label = "root manifest"; Value = $rootManifestVersion },
            @{ Label = "Debug manifest"; Value = $debugManifestVersion },
            @{ Label = "Release manifest"; Value = $releaseManifestVersion })) {
        if ($null -eq $pair.Value) {
            $mismatches += "$($pair.Label) is unavailable"
        } elseif ($pair.Value -ne $projectVersion) {
            $mismatches += "$($pair.Label) is $($pair.Value) but project version is $projectVersion"
        }
    }

    if (($null -ne $devManifestVersion) -and ($devManifestVersion -ne $projectVersion)) {
        $mismatches += "dev plugin is $devManifestVersion but project version is $projectVersion"
    }

    if ($mismatches.Count -gt 0) {
        Write-Warning "Version mismatch detected:"
        $mismatches | ForEach-Object { Write-Warning " - $_" }
        throw "Recipe Helper version parity check failed."
    }
}

Write-Host "Preparing Recipe Helper release safety checks..."
Assert-VersionParity

if (Test-Path -LiteralPath $devPluginRoot) {
    Get-ChildItem -LiteralPath $devPluginRoot -Force -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            if ($_.Attributes -band [System.IO.FileAttributes]::ReadOnly) {
                $_.Attributes = $_.Attributes -bxor [System.IO.FileAttributes]::ReadOnly
            }
        }

    $files = Get-ChildItem -LiteralPath $devPluginRoot -File -Force -Recurse -ErrorAction SilentlyContinue

    if (($null -eq $files) -or ($files.Count -eq 0)) {
        if (-not $WhatIf) {
            Remove-Item -LiteralPath $devPluginRoot -Recurse -Force
        }

        Write-Host "Removed empty stale dev plugin folders."
        Write-Host "Release safety checks complete."
        exit 0
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $backupRoot $timestamp

    Write-Warning "Found local dev plugin at '$devPluginRoot'."
    Write-Warning "That can override or conflict with the installed release plugin."

    if ($RemoveDevPlugin) {
        if ($WhatIf) {
            Write-Host "Would move dev plugin backup to '$backupPath'."
        } else {
            New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
            Move-Item -LiteralPath $devPluginRoot -Destination $backupPath
            Write-Host "Moved dev plugin backup to '$backupPath'."
        }
    } else {
        Write-Warning "Re-run with -RemoveDevPlugin to move the dev plugin out of XIVLauncher before publishing."
        exit 1
    }
} else {
    Write-Host "No stale dev plugin found."
}

Write-Host "Release safety checks complete."
