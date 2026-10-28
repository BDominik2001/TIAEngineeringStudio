<#
.SYNOPSIS
    Verziónkénti integrációs teszt VALÓDI TIA Portallal (spec 11.2 tesztmátrix).
.DESCRIPTION
    Az ismert jó projektről friss sandbox-másolatot készít (az eredeti nem módosul), majd a
    worker-folyamaton keresztül: felderítés, képességek, száraz futás, jóváhagyás nélküli import
    elutasítása, import + offline fordítás + mentés, ütközés- és hashhiba-elutasítás.
    A sikeres eredményt kézzel kell rögzíteni a docs\COMPATIBILITY_MATRIX.md-ben, és csak utána
    vehető fel a verzió az appsettings.json integration_tested_versions listájába.
.EXAMPLE
    .\test-tia-integration.ps1 -Version V18 -KnownGoodProject D:\TIA\Known\Empty1500\Empty1500.ap18
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('V18', 'V19', 'V20')][string]$Version,
    [Parameter(Mandatory = $true)][string]$KnownGoodProject,
    [string]$PlcName = 'PLC_1',
    [string]$WorkersDir = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio\workers')
)
. "$PSScriptRoot\_common.ps1"

$expectedExtension = ".ap$(Get-TiaMajor $Version)"
if ([System.IO.Path]::GetExtension($KnownGoodProject) -ne $expectedExtension) {
    throw "A projekt kiterjesztése nem $expectedExtension – $Version teszthez $Version projekt kell."
}
if (-not (Get-OpennessDllPath $Version)) { throw "TIA Portal $Version Openness nincs telepítve (TIA_NOT_INSTALLED)." }
if (-not (Test-OpennessGroupMembership)) { Write-Warning "A felhasználó nem tagja a 'Siemens TIA Openness' csoportnak." }

$env:TES_TIA_INTEGRATION = $Version
$env:TES_TIA_KNOWN_GOOD_PROJECT = (Resolve-Path $KnownGoodProject).Path
$env:TES_TIA_PLC_NAME = $PlcName
$env:TES_WORKERS_DIR = (Resolve-Path $WorkersDir).Path
try {
    $results = Join-Path $RepoRoot "artifacts\tia-integration-$Version-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Invoke-Checked 'dotnet' @('test', (Join-Path $RepoRoot 'tests\Integration\TiaStudio.IntegrationTests\TiaStudio.IntegrationTests.csproj'),
        '--filter', 'FullyQualifiedName~TiaOpennessIntegrationTests', '--logger', 'trx', '--results-directory', $results)
    Write-Host "`n$Version integrációs teszt sikeres. TRX: $results" -ForegroundColor Green
    Write-Host 'Rögzítsd a docs\COMPATIBILITY_MATRIX.md-ben, és ellenőrizd kézzel a sandboxban létrejött PLC-objektumokat (spec 11.4).'
} finally {
    Remove-Item Env:TES_TIA_INTEGRATION, Env:TES_TIA_KNOWN_GOOD_PROJECT, Env:TES_TIA_PLC_NAME, Env:TES_WORKERS_DIR -ErrorAction SilentlyContinue
}
