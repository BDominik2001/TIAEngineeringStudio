<#
.SYNOPSIS
    TIA nélkül futtatható tesztek: Python, Rust, C# unit és folyamatközi integráció.
#>
param([string]$Configuration = 'Debug')
. "$PSScriptRoot\_common.ps1"

Write-Step 'Python unit + golden SCL tesztek'
Push-Location (Join-Path $RepoRoot 'python')
try {
    Invoke-Checked 'python' @('-m', 'unittest', 'discover', '-s', 'tests', '-v')
    Invoke-Checked 'python' @('-m', 'generator.template_hashes')
} finally { Pop-Location }

& "$PSScriptRoot\build-rust.ps1"

Write-Step 'C# unit és integrációs tesztek (TIA-tesztek Skip, ha nincs beállítva)'
Invoke-Checked 'dotnet' @('test', (Join-Path $RepoRoot 'TIA-Engineering-Studio.sln'), '-c', $Configuration)
Write-Host "`nMinden offline teszt lefutott." -ForegroundColor Green
