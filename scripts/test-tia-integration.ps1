<#
.SYNOPSIS
    Verziónkénti integrációs teszt VALÓDI TIA Portallal (spec 11.2 tesztmátrix).
.DESCRIPTION
    Az ismert jó projektről friss sandbox-másolatot készít (az eredeti nem módosul), majd a
    worker-folyamaton keresztül: felderítés, képességek, száraz futás, jóváhagyás nélküli import
    elutasítása, import + offline fordítás + mentés, ütközés- és hashhiba-elutasítás.
    A sikeres eredményt kézzel kell rögzíteni a docs\COMPATIBILITY.md-ben, és csak utána
    vehető fel a verzió az appsettings.json integration_tested_versions listájába.

    Siker CSAK akkor, ha a TRX szerint legalább -MinimumTests teszt ténylegesen lefutott és mind
    sikeres: kihagyott (Skipped/NotExecuted) teszt = sikertelen futás. Eredmény: passed | failed | not-run
    a -StatusFile JSON-ba (ha megadva) és a kimenetre.
.EXAMPLE
    .\test-tia-integration.ps1 -Version V18 -KnownGoodProject D:\TIA\Known\Empty1500\Empty1500.ap18
.EXAMPLE
    .\test-tia-integration.ps1 -Version V20 -KnownGoodProject D:\TIA\Known\Empty1200\Empty1200.ap20 -CpuFamily S7-1200
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('V18', 'V19', 'V20')][string]$Version,
    [Parameter(Mandatory = $true)][string]$KnownGoodProject,
    [string]$PlcName = 'PLC_1',
    # A mintaprojekt PLC-jének CPU-családja; üresen az examples\machine-vXX.json értéke.
    [ValidatePattern('^(|S7-1200|S7-1500)$')][string]$CpuFamily = '',
    [string]$WorkersDir = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio\workers'),
    # Ennyi TIA-tesztnek kell ténylegesen lefutnia (TiaOpennessIntegrationTests).
    [int]$MinimumTests = 4,
    [string]$StatusFile
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
$env:TES_TIA_CPU_FAMILY = $CpuFamily
$env:TES_WORKERS_DIR = (Resolve-Path $WorkersDir).Path
$results = Join-Path $RepoRoot "artifacts\tia-integration-$Version-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$trxPath = Join-Path $results 'tia-integration.trx'
try {
    & dotnet test (Join-Path $RepoRoot 'tests\tia\TiaStudio.TiaIntegrationTests\TiaStudio.TiaIntegrationTests.csproj') `
        --filter 'FullyQualifiedName~TiaOpennessIntegrationTests' --logger 'trx;LogFileName=tia-integration.trx' --results-directory $results
    $testExit = $LASTEXITCODE
} finally {
    Remove-Item Env:TES_TIA_INTEGRATION, Env:TES_TIA_KNOWN_GOOD_PROJECT, Env:TES_TIA_PLC_NAME, Env:TES_TIA_CPU_FAMILY, Env:TES_WORKERS_DIR -ErrorAction SilentlyContinue
}

# Audit A-09: a dotnet test kilépési kódja kihagyott teszteknél is 0, ezért a TRX dönt.
$outcomes = @()
if (Test-Path $trxPath) {
    [xml]$trx = Get-Content -Raw -LiteralPath $trxPath
    $outcomes = @($trx.TestRun.Results.UnitTestResult | ForEach-Object { [pscustomobject]@{ Name = $_.testName; Outcome = $_.outcome } })
}
$passed = @($outcomes | Where-Object Outcome -eq 'Passed').Count
$failed = @($outcomes | Where-Object { $_.Outcome -eq 'Failed' -or $_.Outcome -eq 'Error' -or $_.Outcome -eq 'Timeout' -or $_.Outcome -eq 'Aborted' }).Count
$notRun = $outcomes.Count - $passed - $failed
$status = if ($failed -gt 0 -or $testExit -ne 0) { 'failed' } elseif ($passed -ge $MinimumTests -and $notRun -eq 0) { 'passed' } else { 'not-run' }
$outcomes | ForEach-Object { Write-Host ("    {0,-12} {1}" -f $_.Outcome, $_.Name) }
Write-Host "    passed=$passed failed=$failed skipped/not-run=$notRun (elvárt: legalább $MinimumTests lefutott, 0 kihagyott) -> $status"
if ($StatusFile) {
    [ordered]@{ tia = $Version; integration = $status; passed = $passed; failed = $failed; notRun = $notRun; minimumTests = $MinimumTests; trx = $trxPath } |
        ConvertTo-Json | Set-Content -LiteralPath $StatusFile -Encoding UTF8
}
if ($status -ne 'passed') {
    throw "$Version TIA-integrációs teszt: $status (passed=$passed, failed=$failed, kihagyott=$notRun, dotnet test exit=$testExit). TRX: $trxPath"
}
Write-Host "`n$Version integrációs teszt sikeres ($passed teszt, 0 kihagyott). TRX: $trxPath" -ForegroundColor Green
Write-Host 'Rögzítsd a docs\COMPATIBILITY.md-ben, és ellenőrizd kézzel a sandboxban létrejött PLC-objektumokat (spec 11.4).'
