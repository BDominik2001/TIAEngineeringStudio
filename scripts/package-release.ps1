<#
.SYNOPSIS
    Egy TIA-célverzióhoz tartozó kiadási csomag: TIAStudio_<appVersion>_TIA<NN>_win-x64.zip
    (docs/RELEASE_PROCESS.md). A csomag a Desktopot és CSAK az adott verzió workerét tartalmazza.
.DESCRIPTION
    Kiadáshoz -RequireOpenness kötelező: a worker a gépen telepített TIA Portal saját
    Siemens.Engineering.dll-jével fordul (a DLL nem kerül a csomagba). Meglévő csomagot nem ír felül.
.EXAMPLE
    .\package-release.ps1 -Tia V18 -RequireOpenness
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('V18', 'V19', 'V20')][string]$Tia,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\release'),
    [string]$Configuration = 'Release',
    [switch]$RequireOpenness
)
. "$PSScriptRoot\_common.ps1"

$info = & python (Join-Path $RepoRoot 'scripts\tools\build_info.py') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'build_info.py failed' }
$name = "TIAStudio_$($info.appVersion)_TIA$(Get-TiaMajor $Tia)_win-x64"
$layout = Join-Path $OutputDir $name
$zip = "$layout.zip"
if (Test-Path $zip) { throw "A csomag már létezik: $zip (kiadott csomag nem írható felül)" }

New-Item -ItemType Directory -Force $OutputDir | Out-Null
& "$PSScriptRoot\build-desktop.ps1" -Configuration $Configuration -Dist $layout -TiaVersions @($Tia) -RequireOpenness:$RequireOpenness

Write-Step "Csomag: $zip"
Compress-Archive -Path (Join-Path $layout '*') -DestinationPath $zip
Write-Host "`nKész: $zip" -ForegroundColor Green
