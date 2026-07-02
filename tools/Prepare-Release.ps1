[CmdletBinding()]
param(
    [string]$XivLauncherRoot = (Join-Path $env:APPDATA "XIVLauncher"),
    [switch]$RemoveDevPlugin,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$devPluginRoot = Join-Path $XivLauncherRoot "devPlugins\\RecipeHelper"
$backupRoot = Join-Path $XivLauncherRoot "backups\\RecipeHelper"

Write-Host "Preparing Recipe Helper release safety checks..."

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
        if (-not $WhatIf) {
            New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
            Move-Item -LiteralPath $devPluginRoot -Destination $backupPath
        }

        Write-Host "Moved dev plugin backup to '$backupPath'."
    } else {
        Write-Warning "Re-run with -RemoveDevPlugin to move the dev plugin out of XIVLauncher before publishing."
        exit 1
    }
} else {
    Write-Host "No stale dev plugin found."
}

Write-Host "Release safety checks complete."
