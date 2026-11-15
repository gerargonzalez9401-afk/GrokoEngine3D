[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$Publish,
    [switch]$Smoke,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Step([string]$name, [scriptblock]$action) {
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        throw "$name fallo con codigo $LASTEXITCODE."
    }
}

function Remove-BuildArtifacts {
    Get-ChildItem -Path $root -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force -ErrorAction Stop
}

Write-Host 'GrokoEngine PRO Verify' -ForegroundColor Green

Step '.NET SDK' { dotnet --info }

if (-not $KeepArtifacts) {
    Write-Host "`n=== Clean artifacts ===" -ForegroundColor Cyan
    Remove-BuildArtifacts
}

if (Test-Path "$root\verify_static.py") {
    Step 'Static quality gate' { python "$root\verify_static.py" }
}

Step 'Restore dependencies' { dotnet restore GrokoEngine.sln --force-evaluate }
Step 'Build Debug' { dotnet build GrokoEngine.sln -c Debug --no-restore -p:ContinuousIntegrationBuild=true }
Step 'Tests Debug' { dotnet run --project GrokoEngine.Tests/GrokoEngine.Tests.csproj -c Debug --no-build }

if ($Release) {
    Step 'Build Release' { dotnet build GrokoEngine.sln -c Release --no-restore -p:ContinuousIntegrationBuild=true }
    Step 'Tests Release' { dotnet run --project GrokoEngine.Tests/GrokoEngine.Tests.csproj -c Release --no-build }
}

if ($Publish) {
    if (-not (Test-Path "$root\publish_editor.bat")) {
        throw "No existe publish_editor.bat."
    }
    Step 'Publish Editor' { & "$root\publish_editor.bat" }
}

if ($Smoke) {
    if (-not (Test-Path "$root\smoke_editor.bat")) {
        throw "No existe smoke_editor.bat."
    }
    Step 'Smoke Editor' { & "$root\smoke_editor.bat" }
}

Write-Host "`nVERIFY PRO OK" -ForegroundColor Green
