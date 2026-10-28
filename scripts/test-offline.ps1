<#
.SYNOPSIS
    TIA nélkül futtatható tesztek: Python, Rust, C# unit és folyamatközi integráció.
#>
param([string]$Configuration = 'Debug')
. "$PSScriptRoot\_common.ps1"

Write-Step 'Python unit + golden SCL tesztek'
$env:PYTHONPATH = Join-Path $RepoRoot 'src\python'
try {
    $pyTests = Join-Path $RepoRoot 'tests\unit\python'
    Invoke-Checked 'python' @('-m', 'unittest', 'discover', '-s', $pyTests, '-t', $pyTests, '-v')
    Invoke-Checked 'python' @('-m', 'generator.template_hashes')
} finally { Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue }

& "$PSScriptRoot\build-rust.ps1"

Write-Step 'C# unit és integrációs tesztek (TIA-tesztek Skip, ha nincs beállítva)'
Invoke-Checked 'dotnet' @('test', (Join-Path $RepoRoot 'TIA-Engineering-Studio.sln'), '-c', $Configuration)
Write-Host "`nMinden offline teszt lefutott." -ForegroundColor Green
