<#
.SYNOPSIS
    A Rust validátor tesztelése és x64 release DLL építése (rust\tes_validator\target\release\tes_validator.dll).
#>
param([switch]$SkipTests)
. "$PSScriptRoot\_common.ps1"

Push-Location (Join-Path $RepoRoot 'rust\tes_validator')
try {
    if (-not $SkipTests) {
        Write-Step 'cargo test'
        Invoke-Checked 'cargo' @('test')
        if (Get-Command cargo-clippy -ErrorAction SilentlyContinue) {
            Write-Step 'cargo clippy'
            Invoke-Checked 'cargo' @('clippy', '--all-targets', '--', '-D', 'warnings')
        }
    }
    Write-Step 'cargo build --release'
    Invoke-Checked 'cargo' @('build', '--release')
    $dll = Join-Path (Get-Location) 'target\release\tes_validator.dll'
    if (Test-Path $dll) { Write-Host "    $dll" -ForegroundColor Green }
} finally {
    Pop-Location
}
