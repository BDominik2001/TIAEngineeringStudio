<#
.SYNOPSIS
    Teljes kiadási elrendezés építése a dist\TiaEngineeringStudio mappába:
    Desktop (WPF, net48 x64) + tes_validator.dll + python\generator + templates\scl + workers\V18..V20 + appsettings.json.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Dist = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio'),
    [string[]]$TiaVersions = @('V18', 'V19', 'V20'),
    [switch]$RequireOpenness
)
. "$PSScriptRoot\_common.ps1"

if (Test-Path $Dist) { throw "A kimeneti mappa már létezik: $Dist (automatikus felülírás tiltott)" }

& "$PSScriptRoot\build-rust.ps1" -SkipTests
Write-Step 'Desktop'
Invoke-Checked 'dotnet' @('build', (Join-Path $RepoRoot 'src\csharp\Desktop\TiaStudio.Desktop.csproj'), '-c', $Configuration, '-o', $Dist)
Copy-Item (Join-Path $RepoRoot 'src\rust\validator\target\release\tes_validator.dll') $Dist

Write-Step 'Python generátor'
$pyTarget = Join-Path $Dist 'python\generator'
New-Item -ItemType Directory -Force $pyTarget | Out-Null
Copy-Item (Join-Path $RepoRoot 'src\python\generator\*') $pyTarget -Recurse -Exclude '__pycache__'
# A verziózott sablonkészlet a generátor fölötti templates\scl mappában (render.py keresési sorrendje)
$tplTarget = Join-Path $Dist 'templates\scl'
New-Item -ItemType Directory -Force $tplTarget | Out-Null
Copy-Item (Join-Path $RepoRoot 'templates\scl\*') $tplTarget
# Mintamodellek (Kezdőlap / Gépmodell: Mintamodell betöltése)
$examplesTarget = Join-Path $Dist 'examples'
New-Item -ItemType Directory -Force $examplesTarget | Out-Null
Copy-Item (Join-Path $RepoRoot 'examples\*.json') $examplesTarget

& "$PSScriptRoot\build-tia-workers.ps1" -Configuration $Configuration -Versions $TiaVersions -OutputRoot (Join-Path $Dist 'workers') -RequireOpenness:$RequireOpenness

Write-Step 'Buildazonosító (build-info.json, docs/VERSIONING.md)'
$buildInfo = Join-Path $RepoRoot 'scripts\tools\build_info.py'
Invoke-Checked 'python' @($buildInfo, '--output', (Join-Path $Dist 'build-info.json'))
foreach ($tia in @('V18', 'V19', 'V20')) {
    $workerDir = Join-Path $Dist "workers\$tia"
    if (Test-Path $workerDir) {
        Invoke-Checked 'python' @($buildInfo, '--tia', $tia, '--output', (Join-Path $workerDir 'build-info.json'))
    }
}

Write-Step 'Integritás'
Push-Location (Join-Path $Dist 'python')
try { Invoke-Checked 'python' @('-m', 'generator.template_hashes') } finally { Pop-Location }
Write-Host "`nKész: $Dist" -ForegroundColor Green
