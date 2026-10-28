<#
.SYNOPSIS
    Teljes kiadási elrendezés építése a dist\TiaEngineeringStudio mappába:
    Desktop (WPF, net48 x64) + tes_validator.dll + python\generator + workers\V18..V20 + appsettings.json.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Dist = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio'),
    [switch]$RequireOpenness
)
. "$PSScriptRoot\_common.ps1"

if (Test-Path $Dist) { throw "A kimeneti mappa már létezik: $Dist (automatikus felülírás tiltott)" }

& "$PSScriptRoot\build-rust.ps1" -SkipTests
Write-Step 'Desktop'
Invoke-Checked 'dotnet' @('build', (Join-Path $RepoRoot 'src\TiaStudio.Desktop\TiaStudio.Desktop.csproj'), '-c', $Configuration, '-o', $Dist)
Copy-Item (Join-Path $RepoRoot 'rust\tes_validator\target\release\tes_validator.dll') $Dist

Write-Step 'Python generátor'
$pyTarget = Join-Path $Dist 'python\generator'
New-Item -ItemType Directory -Force $pyTarget | Out-Null
Copy-Item (Join-Path $RepoRoot 'python\generator\*') $pyTarget -Recurse -Exclude '__pycache__'

& "$PSScriptRoot\build-tia-workers.ps1" -Configuration $Configuration -OutputRoot (Join-Path $Dist 'workers') -RequireOpenness:$RequireOpenness

Write-Step 'Integritás'
Push-Location (Join-Path $Dist 'python')
try { Invoke-Checked 'python' @('-m', 'generator.template_hashes') } finally { Pop-Location }
Write-Host "`nKész: $Dist" -ForegroundColor Green
