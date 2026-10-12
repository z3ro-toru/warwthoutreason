#!/usr/bin/env pwsh

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $scriptDir
try {
    Write-Host "Building War Without Reason in $Configuration configuration..." -ForegroundColor Cyan
    dotnet run --project ZZCakeBuild/CakeBuild.csproj -- --configuration=$Configuration
}
finally {
    Pop-Location
}