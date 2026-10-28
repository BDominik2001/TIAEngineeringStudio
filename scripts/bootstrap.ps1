<#
.SYNOPSIS
    Fejlesztői környezet előkészítése: előfeltételek ellenőrzése, NuGet restore, Rust célplatform.
#>
. "$PSScriptRoot\_common.ps1"

Write-Step 'Előfeltételek'
foreach ($tool in @('python', 'cargo', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool nem található. Telepítsd (Python 3.9+, Rust stable x86_64-pc-windows-msvc, .NET SDK 8 + .NET Framework 4.8 Developer Pack)."
    }
}
$pyVersion = & python -c "import sys; print('%d.%d' % sys.version_info[:2])"
if ([version]$pyVersion -lt [version]'3.9') { throw "Python 3.9+ szükséges (talált: $pyVersion)" }

Write-Step 'Rust célplatform'
if (Get-Command rustup -ErrorAction SilentlyContinue) {
    Invoke-Checked 'rustup' @('target', 'add', 'x86_64-pc-windows-msvc')
}

Write-Step 'NuGet restore'
Invoke-Checked 'dotnet' @('restore', (Join-Path $RepoRoot 'TIA-Engineering-Studio.sln'))

Write-Step 'Sablonhash-ek ellenőrzése'
Push-Location (Join-Path $RepoRoot 'src\python')
try { Invoke-Checked 'python' @('-m', 'generator.template_hashes') } finally { Pop-Location }

Write-Host "`nKész. Következő lépés: scripts\check-environment.ps1, majd scripts\test-offline.ps1" -ForegroundColor Green
